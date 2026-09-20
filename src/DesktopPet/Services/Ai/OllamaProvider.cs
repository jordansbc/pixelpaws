using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DesktopPet.Services.Ai;

/// <summary>
/// A local model served by Ollama (https://ollama.com). No API key, no quota, no network egress —
/// the cat keeps working on a plane and nothing you say to it leaves the machine.
///
/// Uses Ollama's /api/chat endpoint, which streams newline-delimited JSON objects rather than
/// server-sent events. Tool support across local models is inconsistent and many small models
/// will happily hallucinate a call, so this provider reports
/// <see cref="SupportsNativeTools"/> = false and lets <see cref="AiChatService"/> fall back to
/// the text-token convention, which works on any model that can follow an instruction.
/// </summary>
public sealed class OllamaProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;

    public string Name => $"Ollama ({_model})";
    public bool SupportsNativeTools => false;

    public OllamaProvider(HttpClient http, string baseUrl, string model)
    {
        _http    = http;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:11434" : baseUrl.TrimEnd('/');
        _model   = string.IsNullOrWhiteSpace(model) ? "llama3.2" : model;
    }

    public Task<AiResult> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> turns,
                                        IReadOnlyList<AiToolSpec>? tools, CancellationToken ct)
        => Run(systemPrompt, turns, null, ct);

    public Task<AiResult> StreamAsync(string systemPrompt, IReadOnlyList<ChatTurn> turns,
                                      IReadOnlyList<AiToolSpec>? tools, Action<string> onDelta,
                                      CancellationToken ct)
        => Run(systemPrompt, turns, onDelta, ct);

    private async Task<AiResult> Run(string systemPrompt, IReadOnlyList<ChatTurn> turns,
                                     Action<string>? onDelta, CancellationToken ct)
    {
        bool stream = onDelta != null;

        // Ollama speaks the OpenAI-ish role vocabulary: "assistant", not Gemini's "model".
        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        foreach (var t in turns)
            messages.Add(new { role = t.Role == "model" ? "assistant" : "user", content = t.Text });

        var body = new
        {
            model = _model,
            messages,
            stream,
            options = new { temperature = 0.9, num_predict = 400 }
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };

            using var resp = await _http.SendAsync(
                req, stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct);

            if (!resp.IsSuccessStatusCode)
            {
                string err = await SafeReadAsync(resp, ct);
                if (resp.StatusCode == HttpStatusCode.NotFound)
                    return new AiResult("*mrrp?* (that model isn't pulled yet) [curious]",
                                        AiFailure.UnknownModel, $"ollama pull {_model}");
                return new AiResult("*mew?* (my local brain said no) [neutral]", AiFailure.Unknown, Trim(err));
            }

            return stream
                ? await ReadStreamAsync(resp, onDelta!, ct)
                : ReadWhole(await resp.Content.ReadAsStringAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new AiResult("*yawn* never mind [neutral]");
        }
        catch (TaskCanceledException)
        {
            return new AiResult("*mew?* (my local brain is slow today) [neutral]", AiFailure.Network, "timed out");
        }
        catch (HttpRequestException ex)
        {
            // Overwhelmingly the common case: Ollama simply is not running.
            return new AiResult("*mew?* (I can't find my local brain) [neutral]", AiFailure.Network,
                                $"Could not reach Ollama at {_baseUrl}. Is it running? ({ex.Message})");
        }
        catch (Exception ex)
        {
            return new AiResult("*mew?* (something went wrong) [neutral]", AiFailure.Unknown, ex.Message);
        }
    }

    private static AiResult ReadWhole(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            string text = ReadContent(doc.RootElement).Trim();
            return text.Length > 0
                ? new AiResult(text)
                : new AiResult("*mrrp* [neutral]", AiFailure.EmptyReply);
        }
        catch (JsonException ex)
        {
            return new AiResult("*mrrp* [neutral]", AiFailure.Unknown, ex.Message);
        }
    }

    /// <summary>Ollama streams one complete JSON object per line, not SSE frames.</summary>
    private static async Task<AiResult> ReadStreamAsync(HttpResponseMessage resp, Action<string> onDelta,
                                                        CancellationToken ct)
    {
        var all = new StringBuilder();
        try
        {
            using var body   = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(body, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                string? line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;

                string piece;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    piece = ReadContent(doc.RootElement);
                }
                catch (JsonException) { continue; }

                if (piece.Length == 0) continue;
                all.Append(piece);
                onDelta(piece);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (all.Length == 0)
                return new AiResult("*mew?* (I lost my train of thought) [neutral]", AiFailure.Network, ex.Message);
        }

        string text = all.ToString().Trim();
        return text.Length > 0
            ? new AiResult(text)
            : new AiResult("*mrrp* [neutral]", AiFailure.EmptyReply);
    }

    private static string ReadContent(JsonElement root) =>
        root.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c)
            ? c.GetString() ?? ""
            : "";

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return ""; }
    }

    private static string Trim(string s) => s.Length > 200 ? s[..200] : s;
}
