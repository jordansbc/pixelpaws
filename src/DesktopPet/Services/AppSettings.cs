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

    /// <summary>Settle down while you present, screen-share, or play a full-screen game.</summary>
    [JsonPropertyName("enableQuietMode")] public bool EnableQuietMode { get; set; } = true;

    /// <summary>Hide the cat entirely during those moments instead of just calming it down.</summary>
    [JsonPropertyName("hideWhenPresenting")] public bool HideWhenPresenting { get; set; } = false;

    /// <summary>
    /// Simulation ticks per second; 0 follows the compositor. Not exposed in Settings, because
    /// measurement showed it saves nothing: the CPU cost of the pet is dominated by being
    /// subscribed to WPF's render loop at all (~2.9% of a core with an empty handler), not by
    /// the work done per tick. It is kept as a cap for very high refresh-rate displays, where
    /// the simulation would otherwise run several times more often than anything can show.
    /// </summary>
    [JsonPropertyName("targetFps")] public int TargetFps { get; set; } = 60;

    // ── Continuity: remember where the cat was and how it felt ──────────────────

    /// <summary>Pick up where the cat left off (position and mood) instead of resetting.</summary>
    [JsonPropertyName("rememberPetState")] public bool RememberPetState { get; set; } = true;

    /// <summary>Last known position in DIPs. NaN = never saved.</summary>
    [JsonPropertyName("lastPetX")] public double LastPetX { get; set; } = double.NaN;
    [JsonPropertyName("lastPetY")] public double LastPetY { get; set; } = double.NaN;

    /// <summary>Last known drives, 0..1. Negative = never saved.</summary>
    [JsonPropertyName("lastEnergy")] public double LastEnergy { get; set; } = -1;
    [JsonPropertyName("lastHunger")] public double LastHunger { get; set; } = -1;

    // ── AI companion (all default OFF — nothing runs and no network call is made unless enabled) ──

    /// <summary>Master switch for the AI companion. When false, zero AI code runs.</summary>
    [JsonPropertyName("enableAiCompanion")] public bool EnableAiCompanion { get; set; } = false;

    /// <summary>LLM provider id: "gemini" (Google's free tier) or "ollama" (local, private, free).</summary>
    [JsonPropertyName("aiProvider")] public string AiProvider { get; set; } = "gemini";

    /// <summary>Base URL of a local Ollama server, used when <see cref="AiProvider"/> is "ollama".</summary>
    [JsonPropertyName("ollamaUrl")] public string OllamaUrl { get; set; } = "http://localhost:11434";

    /// <summary>Model name to ask Ollama for, e.g. "llama3.2" or "qwen2.5:3b".</summary>
    [JsonPropertyName("ollamaModel")] public string OllamaModel { get; set; } = "llama3.2";

    /// <summary>Type the cat's reply out a piece at a time instead of popping it in whole.</summary>
    [JsonPropertyName("aiStreaming")] public bool AiStreaming { get; set; } = true;

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
