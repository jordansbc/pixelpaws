using System.Text;
using System.Text.RegularExpressions;
using DesktopPet.Engine;

namespace DesktopPet.Services.Ai;

/// <summary>
/// Orchestrates one chat turn: wrap the persona (plus anything the cat remembers), run the model,
/// handle up to two tool round-trips, capture any new memories the cat chose to keep, then
/// split the reply into visible text + a <see cref="PetState"/> emotion. A short rolling history
/// and a few remembered facts persist across sessions via <see cref="AiMemory"/>.
///
/// Tools are dispatched two ways. Providers that support native function calling (Gemini) get
/// real declarations and answer with a structured call. Everything else (local Ollama models,
/// whose tool support varies wildly) falls back to a text token the model is asked to emit.
/// </summary>
public sealed class AiChatService
{
    private readonly AppSettings _settings;
    private readonly IAiProvider _provider;
    private readonly CuteTools _tools;
    private readonly AiMemory _memory;
    private readonly List<ChatTurn> _history = new();

    private static readonly Regex EmotionTag = new(@"\[(?<tag>[a-zA-Z]+)\]\s*\.?\s*$", RegexOptions.Compiled);

    /// <summary>
    /// The text-token tool convention, parsed leniently. Models like to dress tokens up — wrapping
    /// them in backticks, bolding them, adding spaces around the colon — and a strict pattern
    /// silently degrades into the cat repeating the token at the user.
    /// </summary>
    private static readonly Regex ToolToken =
        new(@"[`*_]*<<\s*tool\s*:\s*(?<name>[a-zA-Z_]+)\s*>>[`*_]*", RegexOptions.Compiled);

    private static readonly Regex RememberToken =
        new(@"[`*_]*<<\s*remember\s*:\s*(?<note>[^>]{1,160})>>[`*_]*", RegexOptions.Compiled);

    /// <summary>What the cat can look up. Descriptions double as the native function schema.</summary>
    private static readonly AiToolSpec[] ToolSpecs =
    {
        new("get_time",     "Get the current local date and time."),
        new("system_stats", "Get the computer's current CPU load, battery level, and what kind of app is in front."),
        new("weather",      "Get the current weather and temperature where the user is."),
    };

    public AiChatService(AppSettings settings, IAiProvider provider, CuteTools tools, AiMemory memory)
    {
        _settings = settings;
        _provider = provider;
        _tools    = tools;
        _memory   = memory;

        // Resume the recent conversation across sessions.
        if (_settings.AiEnableMemory)
            foreach (var t in _memory.History)
                _history.Add(new ChatTurn(t.Role, t.Text));
    }

    public readonly record struct Reply(string Text, PetState Emotion, AiFailure Failure = AiFailure.None,
                                        string? Detail = null)
    {
        public bool Ok => Failure == AiFailure.None;
    }

    /// <summary>Name of the active backend, for the Settings diagnostic line.</summary>
    public string ProviderName => _provider.Name;

    /// <summary>
    /// Send one message. <paramref name="onDelta"/>, when given and streaming is enabled, is
    /// called with each chunk of the final reply as it arrives so the bubble can type it out.
    /// </summary>
    public async Task<Reply> SendAsync(string userText, CancellationToken ct, Action<string>? onDelta = null)
    {
        // Work on a copy so failed/tool turns never pollute the persisted history.
        var working = new List<ChatTurn>(_history) { new("user", userText) };
        string system = BuildSystemPrompt();

        bool useTools     = _settings.AiEnableTools;
        bool nativeTools  = useTools && _provider.SupportsNativeTools;
        var  toolSpecs    = nativeTools ? ToolSpecs : null;
        bool stream       = _settings.AiStreaming && onDelta != null;

        AiResult result = stream
            ? await _provider.StreamAsync(system, working, toolSpecs, onDelta!, ct)
            : await _provider.CompleteAsync(system, working, toolSpecs, ct);

        // Tool round-trips. At most two, so a confused model can't loop forever.
        for (int i = 0; i < 2 && useTools; i++)
        {
            string? tool = result.ToolCall ?? MatchTokenTool(result.Text);
            if (tool == null) break;

            string toolResult = await _tools.RunAsync(tool, ct);
            working.Add(new ChatTurn("model", string.IsNullOrWhiteSpace(result.Text)
                                                  ? $"<<tool:{tool}>>" : result.Text));
            working.Add(new ChatTurn("user", $"(tool {tool} says: {toolResult}) Now answer me in character."));

            result = stream
                ? await _provider.StreamAsync(system, working, toolSpecs, onDelta!, ct)
                : await _provider.CompleteAsync(system, working, toolSpecs, ct);
        }

        CaptureMemories(result.Text);
        var (text, tag) = ParseReply(result.Text);

        // Only remember exchanges that actually worked — persisting an error line would teach
        // the cat to keep saying it.
        if (result.Ok)
        {
            _history.Add(new ChatTurn("user", userText));
            _history.Add(new ChatTurn("model", text));
            TrimHistory();
            PersistMemory();
        }

        return new Reply(text, EmotionMap.Resolve(tag), result.Failure, result.Detail);
    }

    /// <summary>An unprompted, in-character one-liner based on the current context. Not persisted.</summary>
    public async Task<Reply> ChatterAsync(string context, CancellationToken ct)
    {
        var working = new List<ChatTurn>(_history)
        {
            new("user", $"(Background note — do not mention that this is automated: {context}) " +
                        "Say one short, spontaneous, in-character line to me right now.")
        };

        // No tools for unprompted chatter: it should be instant and free.
        var result = await _provider.CompleteAsync(BuildSystemPrompt(), working, null, ct);
        var (text, tag) = ParseReply(result.Text);
        return new Reply(text, EmotionMap.Resolve(tag), result.Failure, result.Detail);
    }

    /// <summary>Round-trip the provider with a trivial prompt, to check a key or a local server.</summary>
    public async Task<Reply> TestAsync(CancellationToken ct)
    {
        var probe = new List<ChatTurn> { new("user", "Say hi in three words.") };
        var result = await _provider.CompleteAsync(BuildSystemPrompt(), probe, null, ct);
        var (text, tag) = ParseReply(result.Text);
        return new Reply(text, EmotionMap.Resolve(tag), result.Failure, result.Detail);
    }

    /// <summary>Wipe everything the cat remembers (notes + chat history), now and on disk.</summary>
    public void ForgetMemory()
    {
        _history.Clear();
        _memory.Clear();
    }

    private static string? MatchTokenTool(string raw)
    {
        var m = ToolToken.Match(raw);
        return m.Success ? m.Groups["name"].Value : null;
    }

    private string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.Append(_settings.AiPersona.Trim());

        if (_settings.AiEnableMemory && _memory.Notes.Count > 0)
        {
            sb.Append("\n\nThings you remember about the user:");
            foreach (var n in _memory.Notes) sb.Append("\n- ").Append(n);
        }

        sb.Append("\n\nAlways end your reply with exactly one emotion tag in square brackets, chosen from: ");
        sb.Append(string.Join(", ", EmotionMap.AllowedTags.Select(t => "[" + t + "]")));
        sb.Append(". Keep replies to 1-2 short sentences and stay in character as the cat.");

        if (_settings.AiEnableMemory)
            sb.Append("\n\nIf the user shares a lasting fact worth remembering (their name, a preference), " +
                      "include <<remember: the fact>> somewhere in your reply — it is hidden from the user.");

        // Only describe the text-token convention to providers that need it. Telling a model
        // about both native functions and a token convention invites it to use both at once.
        if (_settings.AiEnableTools && !_provider.SupportsNativeTools)
        {
            sb.Append("\n\nIf you need live information, reply with ONLY a tool token and nothing else: ");
            sb.Append("<<tool:get_time>> for the current date/time, <<tool:system_stats>> for CPU/battery/app, ");
            sb.Append("or <<tool:weather>> for the weather. You'll be told the result, then reply in character.");
        }
        return sb.ToString();
    }

    /// <summary>Pull any &lt;&lt;remember: …&gt;&gt; notes out of a raw reply into long-term memory.</summary>
    private void CaptureMemories(string raw)
    {
        if (!_settings.AiEnableMemory) return;
        foreach (Match m in RememberToken.Matches(raw))
        {
            string note = m.Groups["note"].Value.Trim();
            if (note.Length == 0 || _memory.Notes.Contains(note)) continue;
            _memory.Notes.Add(note);
            if (_memory.Notes.Count > 12) _memory.Notes.RemoveAt(0);   // keep it small
        }
    }

    private void PersistMemory()
    {
        if (!_settings.AiEnableMemory) return;
        _memory.History = _history
            .Select(t => new AiMemory.StoredTurn { Role = t.Role, Text = t.Text })
            .ToList();
        _memory.Save();
    }

    /// <summary>Split a raw model reply into visible text and the trailing [emotion] tag.</summary>
    public static (string text, string tag) ParseReply(string raw)
    {
        string tag     = "neutral";
        string trimmed = RememberToken.Replace(raw ?? "", "").TrimEnd();
        var em = EmotionTag.Match(trimmed);
        if (em.Success) { tag = em.Groups["tag"].Value; trimmed = trimmed[..em.Index]; }
        string text = ToolToken.Replace(trimmed, "").Trim();
        if (text.Length == 0) text = "*mrrp*";
        return (text, tag);
    }

    private void TrimHistory()
    {
        const int max = 12;   // ~6 exchanges; keeps tokens small and cheap
        if (_history.Count > max)
            _history.RemoveRange(0, _history.Count - max);
    }
}
