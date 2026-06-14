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
}
