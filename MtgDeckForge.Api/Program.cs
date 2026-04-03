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
using MtgDeckForge.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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

// SQL storage (Identity + pricing LocalDB)
builder.Services.Configure<SqlStorageSettings>(
    builder.Configuration.GetSection("SqlStorage"));
var sqlConnectionString = builder.Configuration["SqlStorage:ConnectionString"]
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=MtgDeckForgeLocal;Trusted_Connection=True;MultipleActiveResultSets=true";
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(sqlConnectionString));

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
builder.Services.AddHttpClient<MtgJsonPricingImportService>();
builder.Services.AddHostedService<PricingRefreshHostedService>();

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
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
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

// Controllers + Razor + Swagger
builder.Services.AddControllers();
builder.Services.AddRazorPages();
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

app.UseCors();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapRazorPages();

// Health check endpoint for ECS/ALB
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

app.Run();
