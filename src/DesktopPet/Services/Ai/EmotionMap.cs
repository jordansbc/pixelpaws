using DesktopPet.Engine;

namespace DesktopPet.Services.Ai;

/// <summary>
/// Maps an LLM emotion tag (e.g. "joy") to one of the cat's animation states — the core idea
/// borrowed from Open-LLM-VTuber. Only self-terminating states are used, so a chat reply can
/// never leave the cat stuck (e.g. <see cref="PetState.Pet"/>/<see cref="PetState.Hunt"/> need
/// external input to exit and are deliberately avoided here).
/// </summary>
public static class EmotionMap
{
    /// <summary>The emotion tags the model is allowed to emit, shown to it in the system prompt.</summary>
    public static readonly string[] AllowedTags =
    {
        "neutral", "joy", "love", "excited", "proud", "sleepy",
        "curious", "surprised", "content", "relaxed", "silly", "grumpy", "gift"
    };

    private static readonly Dictionary<string, PetState> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["neutral"]   = PetState.Talk,
        ["curious"]   = PetState.Talk,
        ["joy"]       = PetState.Proud,    // sparkle + hearts read as delight
        ["happy"]     = PetState.Proud,
        ["love"]      = PetState.Proud,    // EnterState(Proud) spawns hearts
        ["affection"] = PetState.Proud,
        ["excited"]   = PetState.Zoomies,
        ["playful"]   = PetState.Zoomies,
        ["proud"]     = PetState.Proud,
        ["smug"]      = PetState.Proud,
        ["sleepy"]    = PetState.Loaf,
        ["tired"]     = PetState.Loaf,
        ["sleep"]     = PetState.Loaf,
        ["surprised"] = PetState.Jump,
        ["alert"]     = PetState.Jump,
        ["startled"]  = PetState.Jump,
        ["content"]   = PetState.Groom,
        ["calm"]      = PetState.Groom,
        ["relaxed"]   = PetState.SideRest,
        ["sad"]       = PetState.SideRest,
        ["silly"]     = PetState.Spin,
        ["dizzy"]     = PetState.Spin,
        ["grumpy"]    = PetState.Spin,
        ["angry"]     = PetState.Spin,
        ["gift"]      = PetState.Gift,
        ["generous"]  = PetState.Gift,
    };

    /// <summary>Resolve an emotion tag to a state. Unknown/empty tags fall back to a gentle chat bob.</summary>
    public static PetState Resolve(string? tag)
    {
        if (!string.IsNullOrWhiteSpace(tag) && Map.TryGetValue(tag.Trim(), out var state))
            return state;
        return PetState.Talk;
    }
}
