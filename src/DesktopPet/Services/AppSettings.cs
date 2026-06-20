using System.Text.Json.Serialization;

namespace DesktopPet.Services;

public sealed class AppSettings
{
    [JsonPropertyName("speed")] public double Speed { get; set; } = 1.0;
    [JsonPropertyName("enableWindowWalking")] public bool EnableWindowWalking { get; set; } = true;
    [JsonPropertyName("enableCursorChase")] public bool EnableCursorChase { get; set; } = true;
    [JsonPropertyName("enableSleep")] public bool EnableSleep { get; set; } = true;
    [JsonPropertyName("activePet")] public string ActivePet { get; set; } = "cat";
    [JsonPropertyName("autostart")] public bool Autostart { get; set; } = false;

    /// <summary>0 = off. Valid values: 15, 30, 45, 60 minutes.</summary>
    [JsonPropertyName("stretchIntervalMinutes")] public int StretchIntervalMinutes { get; set; } = 30;

    /// <summary>Cat unrolls toilet paper when you scroll the mouse wheel.</summary>
    [JsonPropertyName("enableScrollPlay")] public bool EnableScrollPlay { get; set; } = true;

    /// <summary>Overall size multiplier for the cat (1.0 = default). Range ~0.6–1.8.</summary>
    [JsonPropertyName("sizeScale")] public double SizeScale { get; set; } = 1.0;

    /// <summary>Lifelike moods: time-of-day rhythm plus energy/hunger drive behaviour.</summary>
    [JsonPropertyName("enableMoods")] public bool EnableMoods { get; set; } = true;

    /// <summary>React to the computer: naps when you're away, livelier under load,
    /// calmer in focus apps, rests more on low battery.</summary>
    [JsonPropertyName("enableSystemReactions")] public bool EnableSystemReactions { get; set; } = true;

    /// <summary>Check GitHub for a newer build on startup and offer a one-click update.</summary>
    [JsonPropertyName("enableAutoUpdate")] public bool EnableAutoUpdate { get; set; } = true;

    // ── AI companion (all default OFF — nothing runs and no network call is made unless enabled) ──

    /// <summary>Master switch for the AI companion. When false, zero AI code runs.</summary>
    [JsonPropertyName("enableAiCompanion")] public bool EnableAiCompanion { get; set; } = false;

    /// <summary>LLM provider id. Currently "gemini" (Google Gemini free tier).</summary>
    [JsonPropertyName("aiProvider")] public string AiProvider { get; set; } = "gemini";

    /// <summary>Provider API key. Stored only in %AppData%\PixelPaws\settings.json — NEVER committed.</summary>
    [JsonPropertyName("aiApiKey")] public string AiApiKey { get; set; } = "";

    /// <summary>Model id. gemini-2.5-flash-lite is free, fast, and good for short replies.</summary>
    [JsonPropertyName("aiModel")] public string AiModel { get; set; } = "gemini-2.5-flash-lite";

    /// <summary>Persona/system prompt describing the cat's character.</summary>
    [JsonPropertyName("aiPersona")] public string AiPersona { get; set; } =
        "You are Pixel, a small, affectionate pixel-art desktop cat who lives on the user's screen. " +
        "You are playful, warm, and a little cheeky. Reply in 1-2 short sentences.";

    /// <summary>Let the cat call cute tools (time, weather, system stats).</summary>
    [JsonPropertyName("aiEnableTools")] public bool AiEnableTools { get; set; } = true;

    /// <summary>Let the cat occasionally say something on its own (mood/time/app aware). AI must be on.</summary>
    [JsonPropertyName("aiProactiveChatter")] public bool AiProactiveChatter { get; set; } = true;

    /// <summary>Open the chat box with a global keyboard shortcut.</summary>
    [JsonPropertyName("aiHotkeyEnabled")] public bool AiHotkeyEnabled { get; set; } = true;

    /// <summary>Hotkey modifiers as WPF ModifierKeys flags (Alt=1, Control=2, Shift=4, Windows=8). Default Ctrl+Alt.</summary>
    [JsonPropertyName("aiHotkeyModifiers")] public int AiHotkeyModifiers { get; set; } = 2 | 1;

    /// <summary>Hotkey key as a WPF Key enum value. Default Key.C (46).</summary>
    [JsonPropertyName("aiHotkeyKey")] public int AiHotkeyKey { get; set; } = 46;

    /// <summary>Let the cat remember a few facts and your recent chats between sessions.</summary>
    [JsonPropertyName("aiEnableMemory")] public bool AiEnableMemory { get; set; } = true;
}
