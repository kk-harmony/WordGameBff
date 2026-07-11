using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WordGameBff.Application.Configuration;

namespace WordGameBff.Api.Configuration;

public sealed class SessionOptionsValidator : IValidateOptions<WordGameBff.Application.Configuration.SessionOptions>
{
    private readonly IHostEnvironment _environment;

    public SessionOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, WordGameBff.Application.Configuration.SessionOptions options)
    {
        if (_environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.SigningKey) || options.SigningKey.Length < 32)
        {
            return ValidateOptionsResult.Fail("Session:SigningKey must be at least 32 characters in production.");
        }

        if (IsPlaceholderKey(options.SigningKey))
        {
            return ValidateOptionsResult.Fail("Session:SigningKey must not use a placeholder value in production.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsPlaceholderKey(string key)
    {
        ReadOnlySpan<string> placeholders =
        [
            "change-me",
            "dev-only-change-me-to-a-long-random-string",
            "change-me-to-a-long-random-string-at-least-32-chars",
        ];

        foreach (var placeholder in placeholders)
        {
            if (key.Equals(placeholder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class CustomAuthOptionsValidator : IValidateOptions<CustomAuthOptions>
{
    private readonly IHostEnvironment _environment;

    public CustomAuthOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, CustomAuthOptions options)
    {
        if (_environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            return ValidateOptionsResult.Fail("CustomAuth:ClientId and CustomAuth:ClientSecret are required in production.");
        }

        return ValidateOptionsResult.Success;
    }
}

public sealed class CorsSettingsValidator : IValidateOptions<CorsSettings>
{
    private readonly IHostEnvironment _environment;

    public CorsSettingsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, CorsSettings options)
    {
        if (_environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        if (options.AllowedOrigins.Length == 0)
        {
            return ValidateOptionsResult.Fail("Cors:AllowedOrigins must contain at least one origin in production.");
        }

        return ValidateOptionsResult.Success;
    }
}

public sealed class RealtimeOptionsValidator : IValidateOptions<RealtimeOptions>
{
    private readonly IHostEnvironment _environment;

    public RealtimeOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, RealtimeOptions options)
    {
        if (_environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        if (!string.Equals(options.BackplaneType, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail("Realtime:BackplaneType must be PostgreSQL in production.");
        }

        if (string.IsNullOrWhiteSpace(options.Backplane.ConnectionString))
        {
            return ValidateOptionsResult.Fail("Realtime:Backplane:ConnectionString is required in production.");
        }

        return ValidateOptionsResult.Success;
    }
}

public sealed class StoreOptionsValidator : IValidateOptions<StoreOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public StoreOptionsValidator(IHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
    }

    public ValidateOptionsResult Validate(string? name, StoreOptions options)
    {
        if (_environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        if (!string.Equals(options.Type, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail("Stores:Type must be PostgreSQL in production.");
        }

        var connectionString = string.IsNullOrWhiteSpace(options.ConnectionString)
            ? _configuration.GetSection(RealtimeOptions.SectionName).Get<RealtimeOptions>()?.Backplane.ConnectionString
            : options.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return ValidateOptionsResult.Fail(
                "Stores:ConnectionString or Realtime:Backplane:ConnectionString is required when Stores:Type is PostgreSQL.");
        }

        return ValidateOptionsResult.Success;
    }
}

public static class ConfigurationValidationExtensions
{
    public static IServiceCollection AddWordGameBffConfigurationValidation(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<WordGameBff.Application.Configuration.SessionOptions>, SessionOptionsValidator>();
        services.AddSingleton<IValidateOptions<CustomAuthOptions>, CustomAuthOptionsValidator>();
        services.AddSingleton<IValidateOptions<CorsSettings>, CorsSettingsValidator>();
        services.AddSingleton<IValidateOptions<RealtimeOptions>, RealtimeOptionsValidator>();
        services.AddSingleton<IValidateOptions<StoreOptions>, StoreOptionsValidator>();

        services.AddOptions<WordGameBff.Application.Configuration.SessionOptions>()
            .BindConfiguration(WordGameBff.Application.Configuration.SessionOptions.SectionName)
            .ValidateOnStart();
        services.AddOptions<CustomAuthOptions>()
            .BindConfiguration(CustomAuthOptions.SectionName)
            .ValidateOnStart();
        services.AddOptions<CorsSettings>()
            .BindConfiguration(CorsSettings.SectionName)
            .ValidateOnStart();
        services.AddOptions<RealtimeOptions>()
            .BindConfiguration(RealtimeOptions.SectionName)
            .ValidateOnStart();
        services.AddOptions<StoreOptions>()
            .BindConfiguration(StoreOptions.SectionName)
            .ValidateOnStart();

        return services;
    }
}
