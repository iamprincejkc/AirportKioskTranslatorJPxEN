using AirportKiosk.Services;
using AirportKiosk.API.HealthChecks;
using Microsoft.OpenApi.Models;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Configure CORS for kiosk app
builder.Services.AddCors(options =>
{
    options.AddPolicy("KioskPolicy", policy =>
    {
        policy.WithOrigins("https://localhost", "http://localhost")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Add HTTP Client for translation service
builder.Services.AddHttpClient<ITranslationService, MyMemoryTranslationService>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "AirportKiosk/1.0");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Register translation service
builder.Services.AddScoped<ITranslationService, MyMemoryTranslationService>();

// Add API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Airport Translation Kiosk API",
        Version = "v1",
        Description = "Translation API for airport kiosk supporting English and Japanese",
        Contact = new OpenApiContact
        {
            Name = "Airport Kiosk System",
            Email = "support@airportkiosk.com"
        }
    });

    // Include XML comments
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<TranslationHealthCheck>("translation_service");

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add configuration
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true);
builder.Configuration.AddEnvironmentVariables();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Airport Kiosk API v1");
        c.RoutePrefix = "swagger";
    });
}

// Enable CORS
app.UseCors("KioskPolicy");

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    await next();
});

app.UseHttpsRedirection();

// Add request logging middleware
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Request: {Method} {Path} from {RemoteIP}",
        context.Request.Method,
        context.Request.Path,
        context.Connection.RemoteIpAddress);

    await next();
});

app.UseAuthorization();

// Map controllers
app.MapControllers();

// Add health check endpoint
app.MapHealthChecks("/health");

// Add basic info endpoint
app.MapGet("/", () => new
{
    Service = "Airport Translation Kiosk API",
    Version = "1.0.0",
    Status = "Running",
    Timestamp = DateTime.UtcNow,
    Endpoints = new[]
    {
        "/api/translation/translate",
        "/api/translation/quick",
        "/api/translation/health",
        "/api/translation/languages",
        "/api/translation/detect",
        "/health",
        "/swagger"
    }
});

// Global exception handler
app.UseExceptionHandler("/error");
app.Map("/error", (HttpContext context) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();

    if (exceptionFeature != null)
    {
        logger.LogError(exceptionFeature.Error, "Unhandled exception occurred");
    }

    return Results.Problem(
        title: "An error occurred",
        statusCode: 500,
        detail: app.Environment.IsDevelopment() ? exceptionFeature?.Error?.Message : "Internal server error"
    );
});

app.Run();