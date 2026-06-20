using System.Text;
using System.Text.RegularExpressions;
using DesktopPet.Engine;

namespace DesktopPet.Services.Ai;

/// <summary>
/// Orchestrates one chat turn: wrap the persona, run the model, handle up to two cute-tool
/// round-trips, then split the reply into visible text + a <see cref="PetState"/> emotion.
/// Holds a short rolling history so the cat remembers the last few exchanges.
/// </summary>
public sealed class AiChatService
{
    private readonly AppSettings _settings;
    private readonly IAiProvider _provider;
    private readonly CuteTools _tools;
    private readonly List<ChatTurn> _history = new();

    private static readonly Regex EmotionTag = new(@"\[(?<tag>[a-zA-Z]+)\]\s*$", RegexOptions.Compiled);
    private static readonly Regex ToolToken  = new(@"<<tool:(?<name>[a-zA-Z_]+)>>", RegexOptions.Compiled);

    public AiChatService(AppSettings settings, IAiProvider provider, CuteTools tools)
    {
        _settings = settings;
        _provider = provider;
        _tools    = tools;
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

        // Split out the trailing [emotion] tag and strip any leftover tool tokens.
        string tag     = "neutral";
        string trimmed = raw.TrimEnd();
        var em = EmotionTag.Match(trimmed);
        if (em.Success) { tag = em.Groups["tag"].Value; trimmed = trimmed[..em.Index]; }
        string text = ToolToken.Replace(trimmed, "").Trim();
        if (text.Length == 0) text = "*mrrp*";

        _history.Add(new ChatTurn("user", userText));
        _history.Add(new ChatTurn("model", text));
        TrimHistory();

        return new Reply(text, EmotionMap.Resolve(tag));
    }

    private string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.Append(_settings.AiPersona.Trim());
        sb.Append("\n\nAlways end your reply with exactly one emotion tag in square brackets, chosen from: ");
        sb.Append(string.Join(", ", EmotionMap.AllowedTags.Select(t => "[" + t + "]")));
        sb.Append(". Keep replies to 1-2 short sentences and stay in character as the cat.");
        if (_settings.AiEnableTools)
        {
            sb.Append("\n\nIf you need live information, reply with ONLY a tool token and nothing else: ");
            sb.Append("<<tool:get_time>> for the current date/time, <<tool:system_stats>> for CPU/battery/app, ");
            sb.Append("or <<tool:weather>> for the weather. You'll be told the result, then reply in character.");
        }
        return sb.ToString();
    }

    private void TrimHistory()
    {
        const int max = 12;   // ~6 exchanges; keeps tokens small and cheap
        if (_history.Count > max)
            _history.RemoveRange(0, _history.Count - max);
    }
}
