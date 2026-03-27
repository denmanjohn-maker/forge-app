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

// Controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

// Health check endpoint for ECS/ALB
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

app.Run();
