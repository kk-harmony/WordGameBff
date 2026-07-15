namespace WordGameBff.Application.Configuration;

public sealed class GameApiOptions
{
    public const string SectionName = "GameApi";
    public string BaseUrl { get; set; } = "http://wordgames:8081";
    public string WarmupPath { get; set; } = "/q/health/live";
}

public sealed class CustomAuthOptions
{
    public const string SectionName = "CustomAuth";
    public string Authority { get; set; } = "https://customauth.fly.dev/";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Audience { get; set; } = "wordgame";
}

public sealed class SessionOptions
{
    public const string SectionName = "Session";
    public string SigningKey { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60;
    public string Issuer { get; set; } = "wordgamebff";
}

public sealed class PowOptions
{
    public const string SectionName = "Pow";
    public int DifficultyBits { get; set; } = 20;
    public int ChallengeExpirySeconds { get; set; } = 300;
}

public sealed class CorsSettings
{
    public const string SectionName = "Cors";
    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";
    public string Transport { get; set; } = "SignalR";
    public string BackplaneType { get; set; } = "PostgreSQL";
    public RealtimeBackplaneOptions Backplane { get; set; } = new();
    public int MaxConnectionsPerUser { get; set; } = 3;
}

public sealed class RealtimeBackplaneOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class StoreOptions
{
    public const string SectionName = "Stores";
    public string Type { get; set; } = "InMemory";
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int AuthIpPermitLimit { get; set; } = 10;
    public int AuthIpWindowMinutes { get; set; } = 1;
    public int ApiIpPermitLimit { get; set; } = 60;
    public int ApiIpWindowMinutes { get; set; } = 1;
    public int ApiSessionPermitLimit { get; set; } = 120;
    public int ApiSessionWindowMinutes { get; set; } = 1;
    public int HubIpPermitLimit { get; set; } = 120;
    public int HubIpWindowMinutes { get; set; } = 1;
}
