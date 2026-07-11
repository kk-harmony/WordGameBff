using System.Text.Json;
using WordGameBff.Application.Realtime;

namespace WordGameBff.Application.Games;

public sealed class UpstreamErrorNormalizer : IUpstreamErrorNormalizer
{
    public string NormalizeErrorBody(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return """{"error":"HTTP_ERROR","message":"Request failed."}""";
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return responseBody;
            }

            if (TryReadClientError(root, out var clientError))
            {
                return JsonSerializer.Serialize(clientError, RealtimeJson.Options);
            }

            if (TryReadUpstreamError(root, out clientError))
            {
                return JsonSerializer.Serialize(clientError, RealtimeJson.Options);
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return responseBody;
    }

    private static bool TryReadClientError(JsonElement root, out ClientErrorPayload payload)
    {
        payload = default!;
        if (!root.TryGetProperty("error", out var errorElement)
            || !root.TryGetProperty("message", out var messageElement))
        {
            return false;
        }

        var error = errorElement.GetString();
        var message = messageElement.GetString();
        if (string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        payload = new ClientErrorPayload(error, message);
        return true;
    }

    private static bool TryReadUpstreamError(JsonElement root, out ClientErrorPayload payload)
    {
        payload = default!;
        if (!root.TryGetProperty("type", out var typeElement)
            || !root.TryGetProperty("message", out var messageElement))
        {
            return false;
        }

        var type = typeElement.GetString();
        var message = messageElement.GetString();
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        payload = new ClientErrorPayload(type, message);
        return true;
    }

    private sealed record ClientErrorPayload(string Error, string Message);
}
