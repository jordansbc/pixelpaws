namespace DesktopPet.Services.Ai;

/// <summary>One turn of a conversation. <see cref="Role"/> is "user" or "model".</summary>
public readonly record struct ChatTurn(string Role, string Text);

/// <summary>
/// Why a call failed, so the UI can say something specific instead of a catch-all shrug. A user
/// who typed their key wrong and a user who ran out of free quota need very different advice.
/// </summary>
public enum AiFailure
{
    None,
    /// <summary>No key configured at all.</summary>
    MissingKey,
    /// <summary>The service rejected the key (401/403, or 400 on a malformed key).</summary>
    BadKey,
    /// <summary>Rate limited or out of free quota (429).</summary>
    QuotaExceeded,
    /// <summary>The model name was not recognised (404).</summary>
    UnknownModel,
    /// <summary>Could not reach the service at all.</summary>
    Network,
    /// <summary>The service answered, but with nothing usable.</summary>
    EmptyReply,
    /// <summary>Anything else, including 5xx.</summary>
    Unknown,
}

/// <summary>
/// The outcome of one model call. <see cref="Text"/> is the raw reply (emotion tag and any
/// tokens still embedded); on failure it carries a friendly in-character line so callers can
/// always show something, while <see cref="Failure"/> and <see cref="Detail"/> explain what
/// actually went wrong.
/// </summary>
public readonly record struct AiResult(string Text, AiFailure Failure = AiFailure.None, string? Detail = null)
{
    public bool Ok => Failure == AiFailure.None;

    /// <summary>Name of a tool the model asked to run, via native function calling. Null if none.</summary>
    public string? ToolCall { get; init; }

    /// <summary>A short, plain-language explanation suitable for a settings-screen diagnostic.</summary>
    public string Explain() => Failure switch
    {
        AiFailure.None           => "Working.",
        AiFailure.MissingKey     => "No API key set. Paste a free key in Settings.",
        AiFailure.BadKey         => "The service rejected that API key. Check it in Settings.",
        AiFailure.QuotaExceeded  => "Out of free quota for now — try again later.",
        AiFailure.UnknownModel   => $"The model name was not recognised{(Detail is null ? "" : $" ({Detail})")}.",
        AiFailure.Network        => "Couldn't reach the service. Check your connection.",
        AiFailure.EmptyReply     => "The service replied with nothing usable.",
        _                        => Detail ?? "Something went wrong.",
    };
}

/// <summary>
/// A tool the model may call. PixelPaws' tools take no arguments, which keeps the schema we
/// have to describe (and every provider has to agree on) down to a name and a sentence.
/// </summary>
public readonly record struct AiToolSpec(string Name, string Description);

/// <summary>
/// A pluggable LLM backend. Implementations are cheap to construct and stateless per call —
/// the conversation history is passed in each time.
/// </summary>
public interface IAiProvider
{
    /// <summary>A short name for this backend, for diagnostics.</summary>
    string Name { get; }

    /// <summary>True if this backend asks for tools natively rather than via text tokens.</summary>
    bool SupportsNativeTools { get; }

    /// <summary>Send the system prompt plus the conversation and return the model's reply.</summary>
    Task<AiResult> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> turns,
                                 IReadOnlyList<AiToolSpec>? tools, CancellationToken ct);

    /// <summary>
    /// Same as <see cref="CompleteAsync"/>, but invokes <paramref name="onDelta"/> with each
    /// chunk of text as it arrives so the caller can type the reply out. The returned result
    /// still carries the complete text. Implementations that cannot stream should just call
    /// <see cref="CompleteAsync"/> and emit the whole thing once.
    /// </summary>
    Task<AiResult> StreamAsync(string systemPrompt, IReadOnlyList<ChatTurn> turns,
                               IReadOnlyList<AiToolSpec>? tools, Action<string> onDelta,
                               CancellationToken ct);
}
