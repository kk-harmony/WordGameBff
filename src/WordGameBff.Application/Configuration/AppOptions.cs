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
    /// <summary>~16 bits ≈ 65k hashes; targets ~1–3s browser solves for ~15-player parties.</summary>
    public int DifficultyBits { get; set; } = 16;
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
    public string BackplaneType { get; set; } = "Redis";
    public RealtimeBackplaneOptions Backplane { get; set; } = new();
    public int MaxConnectionsPerUser { get; set; } = 3;

    /// <summary>Caps the upstream membership check on hub connect so a cold game API cannot stall the handshake.</summary>
    public int HubJoinUpstreamTimeoutSeconds { get; set; } = 3;
}

public sealed class GameSnapshotOptions
{
    public const string SectionName = "GameSnapshot";

    /// <summary>When false, events stay lightweight and clients refetch via REST.</summary>
    public bool PushEnabled { get; set; } = true;

    /// <summary>Absolute TTL for the in-memory raw-game cache.</summary>
    public int CacheTtlSeconds { get; set; } = 120;

    /// <summary>Drop snapshotJson from backplane wire payload when it exceeds this many UTF-8 bytes.</summary>
    public int MaxPayloadBytes { get; set; } = 6000;
}

public sealed class RealtimeBackplaneOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ChannelName { get; set; } = "wordgamebff_backplane";
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

    /// <summary>~15 shared-NAT joins (challenge+verify) plus retry headroom; PoW still bounds scrapers.</summary>
    public int AuthIpPermitLimit { get; set; } = 120;
    public int AuthIpWindowMinutes { get; set; } = 1;
    /// <summary>Shared-IP tables: polls + mutations for ~15 concurrent players.</summary>
    public int ApiIpPermitLimit { get; set; } = 300;
    public int ApiIpWindowMinutes { get; set; } = 1;
    /// <summary>Per authenticated session (<c>sub</c>); requires auth before the rate limiter.</summary>
    public int ApiSessionPermitLimit { get; set; } = 180;
    public int ApiSessionWindowMinutes { get; set; } = 1;
    public int HubIpPermitLimit { get; set; } = 180;
    public int HubIpWindowMinutes { get; set; } = 1;
}
