using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DesktopPet.Services.Ai;

/// <summary>
/// Google Gemini (free tier) via the generativelanguage REST API. The first — and only —
/// HttpClient usage in PixelPaws, so it carries its own timeout and never throws to the caller:
/// network problems come back as a friendly in-character line.
/// </summary>
public sealed class GeminiProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiProvider(HttpClient http, string apiKey, string model)
    {
        _http   = http;
        _apiKey = apiKey;
        _model  = string.IsNullOrWhiteSpace(model) ? "gemini-2.0-flash" : model;
    }

    public async Task<string> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> turns, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return "Add a free Gemini key in Settings and I'll chat! [neutral]";

        var contents = new List<object>(turns.Count);
        foreach (var t in turns)
            contents.Add(new { role = t.Role, parts = new[] { new { text = t.Text } } });

        var body = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents,
            generationConfig = new { temperature = 0.9, maxOutputTokens = 200 }
        };

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            using var resp = await _http.SendAsync(req, ct);
            string json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return "*mew?* (the cloud didn't answer) [neutral]";

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0
                && cands[0].TryGetProperty("content", out var content)
                && content.TryGetProperty("parts", out var parts))
            {
                var sb = new StringBuilder();
                foreach (var p in parts.EnumerateArray())
                    if (p.TryGetProperty("text", out var txt)) sb.Append(txt.GetString());
                string text = sb.ToString().Trim();
                if (text.Length > 0) return text;
            }
            return "*mrrp* [neutral]";
        }
        catch (OperationCanceledException)
        {
            return "*yawn* never mind [neutral]";
        }
        catch
        {
            return "*mew?* (I couldn't reach the cloud) [neutral]";
        }
    }
}
