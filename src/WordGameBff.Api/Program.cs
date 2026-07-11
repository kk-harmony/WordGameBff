using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WordGameBff.Api.Configuration;
using WordGameBff.Application;
using WordGameBff.Application.Configuration;
using WordGameBff.Api.Cors;
using WordGameBff.Api.Endpoints;
using WordGameBff.Api.Extensions;
using WordGameBff.Api.Middleware;
using WordGameBff.Infrastructure;
using WordGameBff.Infrastructure.Realtime.SignalR;
using WordGameBff.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Warning);

builder.Services.AddWordGameBffConfigurationValidation();
builder.Services.AddWordGameBffApplication();
builder.Services.AddWordGameBffInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddWordGameBffAuthentication(builder.Configuration);
builder.Services.AddWordGameBffRateLimiting(builder.Configuration);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
});

var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();
var isDevelopment = builder.Environment.IsDevelopment();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();

        if (isDevelopment)
        {
            policy.SetIsOriginAllowed(origin =>
                corsSettings.AllowedOrigins.Contains(origin, StringComparer.Ordinal)
                || DevLanOriginPolicy.IsAllowed(origin));
        }
        else if (corsSettings.AllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsSettings.AllowedOrigins);
        }
    });
});

builder.Services.AddSignalR();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var realtimeOptions = builder.Configuration.GetSection(RealtimeOptions.SectionName).Get<RealtimeOptions>() ?? new RealtimeOptions();
var usesPostgresBackplane = string.Equals(realtimeOptions.BackplaneType, "PostgreSQL", StringComparison.OrdinalIgnoreCase)
    && !string.IsNullOrWhiteSpace(realtimeOptions.Backplane.ConnectionString);
var usesPostgresStores = StoreConnectionResolver.UsePostgreSqlStores(builder.Configuration);

var healthChecks = builder.Services.AddHealthChecks();
if (usesPostgresBackplane || usesPostgresStores)
{
    var postgresConnectionString = StoreConnectionResolver.Resolve(builder.Configuration);
    if (!string.IsNullOrWhiteSpace(postgresConnectionString))
    {
        healthChecks.AddNpgSql(postgresConnectionString, name: "postgres", tags: ["ready"]);
    }
}

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseCors();
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = async (context, _) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { status = "healthy" });
    },
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var status = report.Status == HealthStatus.Healthy ? "healthy" : "unhealthy";
        context.Response.StatusCode = report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { status });
    },
}).AllowAnonymous();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .AllowAnonymous();

app.MapAuthEndpoints();
app.MapGameEndpoints();
app.MapSecretWordEndpoints();

app.MapHub<GameHub>(GameHub.Path)
    .RequireRateLimiting(RateLimitingExtensions.HubIpPolicy);

app.Run();

public partial class Program;
