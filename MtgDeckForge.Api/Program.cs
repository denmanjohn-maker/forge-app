using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MtgDeckForge.Api.Data;
using MtgDeckForge.Api.Models;
using MtgDeckForge.Api.Observability;
using MtgDeckForge.Api.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Events;

// ── Serilog bootstrap (captures startup errors) ──
var logStore = new InMemoryLogStore(1000);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Sink(new InMemoryLogSink(logStore))
    .WriteTo.OpenTelemetry(options =>
    {
        options.Endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? "http://localhost:4317";
        options.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc;
        options.ResourceAttributes = new Dictionary<string, object>
        {
            ["service.name"] = "MtgDeckForge"
        };
    })
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// Railway (and other PaaS) inject PORT at runtime — honour it
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://+:{port}");

// Kestrel keep-alive for long AI requests (prevents iOS Safari "Load Failed")
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2);
});

// MongoDB
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDb"));
builder.Services.AddSingleton<DeckService>();

// Claude API
builder.Services.Configure<ClaudeApiSettings>(
    builder.Configuration.GetSection("ClaudeApi"));
builder.Services.AddHttpClient<ClaudeService>();

// Scryfall
builder.Services.AddHttpClient<ScryfallService>();

// MTGJSON settings
builder.Services.Configure<MtgJsonSettings>(
    builder.Configuration.GetSection("MtgJson"));

// SQL storage (Identity + pricing — PostgreSQL)
// Railway injects DATABASE_URL in URI format; convert to Npgsql key-value format.
builder.Services.Configure<SqlStorageSettings>(
    builder.Configuration.GetSection("SqlStorage"));

var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
string sqlConnectionString;

if (!string.IsNullOrEmpty(databaseUrl))
{
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':');
    sqlConnectionString = $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";
}
else
{
    sqlConnectionString = builder.Configuration["SqlStorage:ConnectionString"]
        ?? throw new InvalidOperationException(
            "No SQL connection string found. Set DATABASE_URL or SqlStorage:ConnectionString.");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(sqlConnectionString));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// JWT Settings
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("JwtSettings"));

// Auth services
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddScoped<PricingService>();
builder.Services.AddHttpClient<MtgJsonPricingImportService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddHostedService<PricingRefreshHostedService>();

// ── Observability ──
builder.Services.AddSingleton(logStore);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("MtgDeckForge"))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();
var jwtSecret = builder.Configuration["JwtSettings:Secret"]
    ?? Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? jwtSettings?.Secret
    ?? string.Empty;
var jwtIssuer = jwtSettings?.Issuer ?? "MtgDeckForge";
var jwtAudience = jwtSettings?.Audience ?? "MtgDeckForge";

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "smart";
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    })
    .AddPolicyScheme("smart", "JWT or Cookie", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var auth = context.Request.Headers.Authorization.ToString();
            return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : IdentityConstants.ApplicationScheme;
        };
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

// Configure the Identity cookie (already registered by AddIdentity, don't re-add)
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
});

builder.Services.AddAuthorization();

// Rate limiting: 20 deck generations per user per 24 hours
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("deck-generation", context =>
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(userId, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromHours(24),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
    options.RejectionStatusCode = 429;
    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync(
            "{\"error\":\"Generation limit reached. You may generate up to 20 decks per 24 hours.\"}", token);
    };
});

// Controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MtgDeckForge API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// CORS — restrict in production, wide open in development
var allowedOrigins = builder.Configuration["Cors:AllowedOrigins"]
    ?? Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment() || string.IsNullOrEmpty(allowedOrigins))
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            var origins = allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
        }
    });
});

var app = builder.Build();

// Seed admin user — password from env var, falls back to default only in development
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Safety check: if migrations reported "up to date" but tables don't exist
    // (stale __EFMigrationsHistory from a previous failed deploy), reset and re-migrate.
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'AspNetRoles')";
        var exists = (bool)(await cmd.ExecuteScalarAsync())!;
        if (!exists)
        {
            using var dropCmd = conn.CreateCommand();
            dropCmd.CommandText = "DROP TABLE IF EXISTS \"__EFMigrationsHistory\"";
            await dropCmd.ExecuteNonQueryAsync();
            await conn.CloseAsync();
            await db.Database.MigrateAsync();
        }
        else
        {
            await conn.CloseAsync();
        }
    }

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    if (!await roleManager.RoleExistsAsync("Admin"))
        await roleManager.CreateAsync(new IdentityRole("Admin"));
    if (!await roleManager.RoleExistsAsync("User"))
        await roleManager.CreateAsync(new IdentityRole("User"));

    var userService = scope.ServiceProvider.GetRequiredService<UserService>();
    var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
    await userService.EnsureIndexesAsync();

    var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
    var adminUsername = builder.Configuration["AdminUsername"] ?? "admin";
    var adminDisplayName = builder.Configuration["AdminDisplayName"] ?? "Administrator";
    if (string.IsNullOrEmpty(adminPassword))
        adminPassword = builder.Configuration["AdminPassword"];
    if (string.IsNullOrEmpty(adminPassword))
        adminPassword = app.Environment.IsDevelopment() ? "Blakd@l3k" : null;

    if (!string.IsNullOrEmpty(adminPassword))
    {
        await userService.SeedAdminUserAsync(authService.HashPassword(adminPassword), adminUsername, adminDisplayName);

        var identityAdmin = await userManager.FindByNameAsync(adminUsername);
        if (identityAdmin is null)
        {
            identityAdmin = new ApplicationUser
            {
                UserName = adminUsername,
                DisplayName = adminDisplayName
            };
            var createResult = await userManager.CreateAsync(identityAdmin, adminPassword);
            if (createResult.Succeeded)
                await userManager.AddToRoleAsync(identityAdmin, "Admin");
        }
    }
}

app.UseSwagger();
app.UseSwaggerUI();

// Restrict /metrics and /logging to Docker-internal IPs only
app.UseInternalOnly("/metrics", "/logging");

app.UseSerilogRequestLogging();

app.UseCors();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

// Prometheus metrics endpoint (scraped by Prometheus container)
app.MapPrometheusScrapingEndpoint("/metrics");

// Recent structured logs endpoint (consumed by Grafana/internal tools)
app.MapGet("/logging", (InMemoryLogStore store, HttpContext ctx) =>
{
    var count = 200;
    if (ctx.Request.Query.TryGetValue("count", out var countStr) && int.TryParse(countStr, out var c))
        count = Math.Clamp(c, 1, 1000);
    return Results.Ok(store.GetRecent(count));
});

// Health check endpoint for ECS/ALB
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

app.Run();
