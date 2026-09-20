using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DesktopPet.Services.Ai;

/// <summary>
/// Google Gemini (free tier) via the generativelanguage REST API. Never throws to the caller:
/// problems come back as an <see cref="AiResult"/> carrying both a friendly in-character line
/// and a specific <see cref="AiFailure"/> so Settings can tell a bad key from an empty wallet.
/// </summary>
public sealed class GeminiProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public string Name => $"Gemini ({_model})";
    public bool SupportsNativeTools => true;

    public GeminiProvider(HttpClient http, string apiKey, string model)
    {
        _http   = http;
        _apiKey = apiKey;
        _model  = string.IsNullOrWhiteSpace(model) ? "gemini-2.5-flash-lite" : model;
    }

    public Task<AiResult> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> turns,
                                        IReadOnlyList<AiToolSpec>? tools, CancellationToken ct)
        => Run(systemPrompt, turns, tools, null, ct);

    public Task<AiResult> StreamAsync(string systemPrompt, IReadOnlyList<ChatTurn> turns,
                                      IReadOnlyList<AiToolSpec>? tools, Action<string> onDelta,
                                      CancellationToken ct)
        => Run(systemPrompt, turns, tools, onDelta, ct);

    private object BuildBody(string systemPrompt, IReadOnlyList<ChatTurn> turns,
                             IReadOnlyList<AiToolSpec>? tools)
    {
        var contents = new List<object>(turns.Count);
        foreach (var t in turns)
            contents.Add(new { role = t.Role, parts = new[] { new { text = t.Text } } });

        var generationConfig = new
        {
            temperature = 0.9,
            maxOutputTokens = 400,
            // Gemini 2.5 models bill their internal reasoning against maxOutputTokens. With
            // thinking left on, a short budget can be consumed entirely by thought tokens and
            // the call returns MAX_TOKENS with no text at all — the cat would just say
            // "*mrrp*" forever. The cat does not need to reason; turn it off so any 2.5
            // model works. Ignored by models that do not support thinking.
            thinkingConfig = new { thinkingBudget = 0 }
        };

        if (tools == null || tools.Count == 0)
            return new { system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                         contents, generationConfig };

        // Native function calling. Every PixelPaws tool is argument-free, so each declaration is
        // just a name and a description; an empty OBJECT schema says "takes nothing".
        var declarations = new List<object>(tools.Count);
        foreach (var t in tools)
            declarations.Add(new
            {
                name = t.Name,
                description = t.Description,
                parameters = new { type = "OBJECT", properties = new Dictionary<string, object>() }
            });

        return new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents,
            generationConfig,
            tools = new[] { new { function_declarations = declarations } }
        };
    }

    private async Task<AiResult> Run(string systemPrompt, IReadOnlyList<ChatTurn> turns,
                                     IReadOnlyList<AiToolSpec>? tools,
                                     Action<string>? onDelta, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return new AiResult("Add a free Gemini key in Settings and I'll chat! [neutral]", AiFailure.MissingKey);

        bool stream = onDelta != null;
        string method = stream ? "streamGenerateContent?alt=sse" : "generateContent";
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:{method}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(BuildBody(systemPrompt, turns, tools)),
                                            Encoding.UTF8, "application/json")
            };
            // The key goes in a header, not the `?key=` query parameter Google's samples use:
            // query strings are the part of a request that ends up in proxy and gateway logs.
            req.Headers.Add("x-goog-api-key", _apiKey);

            using var resp = await _http.SendAsync(
                req, stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct);

            if (!resp.IsSuccessStatusCode)
            {
                string body = await SafeReadAsync(resp, ct);
                return Classify(resp.StatusCode, body);
            }

            return stream
                ? await ReadStreamAsync(resp, onDelta!, ct)
                : ReadWhole(await resp.Content.ReadAsStringAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new AiResult("*yawn* never mind [neutral]", AiFailure.None);
        }
        catch (TaskCanceledException)
        {
            // HttpClient surfaces its own timeout as a cancellation with no token set.
            return new AiResult("*mew?* (that took too long) [neutral]", AiFailure.Network, "timed out");
        }
        catch (HttpRequestException ex)
        {
            return new AiResult("*mew?* (I couldn't reach the cloud) [neutral]", AiFailure.Network, ex.Message);
        }
        catch (Exception ex)
        {
            return new AiResult("*mew?* (something went wrong) [neutral]", AiFailure.Unknown, ex.Message);
        }
    }

    /// <summary>Turn an HTTP failure into advice the user can act on.</summary>
    private static AiResult Classify(HttpStatusCode code, string body)
    {
        string? detail = ExtractApiMessage(body);

        return code switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new AiResult("*mew?* (my key isn't working) [grumpy]", AiFailure.BadKey, detail),

            // Gemini returns 400 INVALID_ARGUMENT for a malformed key as well as for bad requests.
            HttpStatusCode.BadRequest when detail != null &&
                (detail.Contains("API key", StringComparison.OrdinalIgnoreCase) ||
                 detail.Contains("API_KEY", StringComparison.Ordinal)) =>
                new AiResult("*mew?* (my key isn't working) [grumpy]", AiFailure.BadKey, detail),

            HttpStatusCode.TooManyRequests =>
                new AiResult("*yawn* I'm all chatted out for now [sleepy]", AiFailure.QuotaExceeded, detail),

            HttpStatusCode.NotFound =>
                new AiResult("*mrrp?* (I don't know that model) [curious]", AiFailure.UnknownModel, detail),

            _ when (int)code >= 500 =>
                new AiResult("*mew?* (the cloud is having a moment) [neutral]", AiFailure.Unknown, detail),

            _ => new AiResult("*mew?* (the cloud didn't answer) [neutral]", AiFailure.Unknown, detail ?? code.ToString()),
        };
    }

    /// <summary>Pull `error.message` out of an API error body, if it looks like one.</summary>
    private static string? ExtractApiMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
                return msg.GetString();
        }
        catch { /* not JSON — fall through */ }
        return body.Length > 200 ? body[..200] : body;
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return ""; }
    }

    private static AiResult ReadWhole(string json)
    {
        var sb = new StringBuilder();
        string? toolCall;
        try
        {
            using var doc = JsonDocument.Parse(json);
            toolCall = AppendText(doc.RootElement, sb);
        }
        catch (JsonException ex)
        {
            return new AiResult("*mrrp* [neutral]", AiFailure.Unknown, ex.Message);
        }

        string text = sb.ToString().Trim();
        if (toolCall != null) return new AiResult(text) { ToolCall = toolCall };

        return text.Length > 0
            ? new AiResult(text)
            : new AiResult("*mrrp* [neutral]", AiFailure.EmptyReply);
    }

    /// <summary>
    /// Read a server-sent-event stream, handing each chunk to <paramref name="onDelta"/> as it
    /// lands. Gemini sends one `data: {json}` line per chunk with the usual candidates shape.
    /// </summary>
    private static async Task<AiResult> ReadStreamAsync(HttpResponseMessage resp, Action<string> onDelta,
                                                        CancellationToken ct)
    {
        var all = new StringBuilder();
        string? toolCall = null;
        try
        {
            using var body   = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(body, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                string payload = line[5..].Trim();
                if (payload.Length == 0 || payload == "[DONE]") continue;

                var chunk = new StringBuilder();
                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    toolCall ??= AppendText(doc.RootElement, chunk);
                }
                catch (JsonException) { continue; }   // partial/keepalive frame

                if (chunk.Length == 0) continue;
                all.Append(chunk);
                // A tool round-trip is about to replace this text, so don't type it out.
                if (toolCall == null) onDelta(chunk.ToString());
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A stream that dies halfway still has usable text; keep what we got.
            if (all.Length == 0)
                return new AiResult("*mew?* (I lost my train of thought) [neutral]", AiFailure.Network, ex.Message);
        }

        string text = all.ToString().Trim();
        if (toolCall != null) return new AiResult(text) { ToolCall = toolCall };

        return text.Length > 0
            ? new AiResult(text)
            : new AiResult("*mrrp* [neutral]", AiFailure.EmptyReply);
    }

    /// <summary>
    /// Append every candidate part's text from one response object, and return the name of a
    /// functionCall part if the model asked for one.
    /// </summary>
    private static string? AppendText(JsonElement root, StringBuilder sb)
    {
        if (!root.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0) return null;
        if (!cands[0].TryGetProperty("content", out var content)) return null;
        if (!content.TryGetProperty("parts", out var parts)) return null;

        string? toolCall = null;
        foreach (var p in parts.EnumerateArray())
        {
            if (p.TryGetProperty("text", out var txt))
                sb.Append(txt.GetString());

            if (p.TryGetProperty("functionCall", out var fc) &&
                fc.TryGetProperty("name", out var fnName))
                toolCall ??= fnName.GetString();
        }
        return toolCall;
    }
}
