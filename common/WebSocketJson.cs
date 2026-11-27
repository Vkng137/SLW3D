using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace SLW3D;

/// <summary>Reads a complete text WebSocket JSON object across one or more frames, with a size cap.</summary>
public static class WebSocketJson
{
    public const string TokenHeader = "X-SLW3D-Token";

    /// <summary>Returns null when the peer closed or exceeded <paramref name="maxBytes"/>;
    /// returns an empty JsonObject when a text message was not valid JSON.</summary>
    public static async Task<JsonObject?> ReceiveAsync(WebSocket ws, CancellationToken ct, int maxBytes = 65536)
    {
        var buf = new byte[8192];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await ws.ReceiveAsync(buf, ct);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            if (r.MessageType != WebSocketMessageType.Text)
            {
                if (r.EndOfMessage) return new JsonObject();
                continue;
            }
            ms.Write(buf, 0, r.Count);
            if (ms.Length > maxBytes) return null;
            if (r.EndOfMessage) break;
        }
        try { return JsonNode.Parse(Encoding.UTF8.GetString(ms.ToArray())) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }
}
