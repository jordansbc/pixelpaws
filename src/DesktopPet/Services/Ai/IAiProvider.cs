namespace DesktopPet.Services.Ai;

/// <summary>One turn of a conversation. <see cref="Role"/> is "user" or "model".</summary>
public readonly record struct ChatTurn(string Role, string Text);

/// <summary>
/// A pluggable LLM backend. Implementations are cheap to construct and stateless per call —
/// the conversation history is passed in each time. Swap in Groq/Ollama later without touching callers.
/// </summary>
public interface IAiProvider
{
    /// <summary>Send the system prompt plus the conversation and return the model's raw reply text.</summary>
    Task<string> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> turns, CancellationToken ct);
}
