using System.Net;

namespace WordGameBff.Api.Cors;

/// <summary>
/// Permits browser origins from loopback or RFC1918 private networks over HTTP during local development.
/// </summary>
public static class DevLanOriginPolicy
{
    public static bool IsAllowed(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsLoopbackOrPrivateHost(uri.Host);
    }

    private static bool IsLoopbackOrPrivateHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (bytes[0] == 10)
        {
            return true;
        }

        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return true;
        }

        return bytes[0] == 192 && bytes[1] == 168;
    }
}
