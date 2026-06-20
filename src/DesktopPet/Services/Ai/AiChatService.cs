using System.Text;
using System.Text.RegularExpressions;
using DesktopPet.Engine;

namespace DesktopPet.Services.Ai;

/// <summary>
/// Orchestrates one chat turn: wrap the persona (plus anything the cat remembers), run the model,
/// handle up to two cute-tool round-trips, capture any new memories the cat chose to keep, then
/// split the reply into visible text + a <see cref="PetState"/> emotion. A short rolling history
/// and a few remembered facts persist across sessions via <see cref="AiMemory"/>.
/// </summary>
public sealed class AiChatService
{
    private readonly AppSettings _settings;
    private readonly IAiProvider _provider;
    private readonly CuteTools _tools;
    private readonly AiMemory _memory;
    private readonly List<ChatTurn> _history = new();

    private static readonly Regex EmotionTag    = new(@"\[(?<tag>[a-zA-Z]+)\]\s*$", RegexOptions.Compiled);
    private static readonly Regex ToolToken     = new(@"<<tool:(?<name>[a-zA-Z_]+)>>", RegexOptions.Compiled);
    private static readonly Regex RememberToken = new(@"<<remember:(?<note>[^>]{1,160})>>", RegexOptions.Compiled);

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

    public readonly record struct Reply(string Text, PetState Emotion);

    public async Task<Reply> SendAsync(string userText, CancellationToken ct)
    {
        // Work on a copy so failed/tool turns never pollute the persisted history.
        var working = new List<ChatTurn>(_history) { new("user", userText) };
        string system = BuildSystemPrompt();

        string raw = await _provider.CompleteAsync(system, working, ct);

        for (int i = 0; i < 2 && _settings.AiEnableTools; i++)
        {
            var m = ToolToken.Match(raw);
            if (!m.Success) break;
            string tool   = m.Groups["name"].Value;
            string result = await _tools.RunAsync(tool, ct);
            working.Add(new ChatTurn("model", raw));
            working.Add(new ChatTurn("user", $"(tool {tool} says: {result}) Now answer me in character."));
            raw = await _provider.CompleteAsync(system, working, ct);
        }

        CaptureMemories(raw);
        var (text, tag) = ParseReply(raw);

        _history.Add(new ChatTurn("user", userText));
        _history.Add(new ChatTurn("model", text));
        TrimHistory();
        PersistMemory();

        return new Reply(text, EmotionMap.Resolve(tag));
    }

    /// <summary>An unprompted, in-character one-liner based on the current context. Not persisted to history.</summary>
    public async Task<Reply> ChatterAsync(string context, CancellationToken ct)
    {
        var working = new List<ChatTurn>(_history)
        {
            new("user", $"(Background note — do not mention that this is automated: {context}) " +
                        "Say one short, spontaneous, in-character line to me right now.")
        };
        string raw = await _provider.CompleteAsync(BuildSystemPrompt(), working, ct);
        var (text, tag) = ParseReply(raw);
        return new Reply(text, EmotionMap.Resolve(tag));
    }

    /// <summary>Wipe everything the cat remembers (notes + chat history), now and on disk.</summary>
    public void ForgetMemory()
    {
        _history.Clear();
        _memory.Clear();
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

        if (_settings.AiEnableTools)
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
    private static (string text, string tag) ParseReply(string raw)
    {
        string tag     = "neutral";
        string trimmed = RememberToken.Replace(raw, "").TrimEnd();
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
