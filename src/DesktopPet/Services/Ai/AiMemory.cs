using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopPet.Services.Ai;

/// <summary>
/// Persisted long-term memory for the AI cat: a handful of remembered facts about the user plus
/// the most recent chat turns, stored in %AppData%\PixelPaws\ai_memory.json. Kept tiny and cheap.
/// </summary>
public sealed class AiMemory
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelPaws");
    private static readonly string FilePath = Path.Combine(Dir, "ai_memory.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    [JsonPropertyName("notes")]   public List<string> Notes { get; set; } = new();
    [JsonPropertyName("history")] public List<StoredTurn> History { get; set; } = new();

    public sealed class StoredTurn
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "user";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
    }

    public static AiMemory Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AiMemory>(File.ReadAllText(FilePath)) ?? new AiMemory();
        }
        catch { /* corrupt/unreadable — start fresh */ }
        return new AiMemory();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch { /* non-fatal: memory just won't persist this time */ }
    }

    /// <summary>Wipe everything the cat remembers, on disk and in memory.</summary>
    public void Clear()
    {
        Notes.Clear();
        History.Clear();
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
    }
}
