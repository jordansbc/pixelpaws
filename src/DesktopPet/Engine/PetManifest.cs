using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopPet.Engine;

/// <summary>
/// Describes how a sprite sheet is sliced into frames and which frames make up each animation.
/// Frames are addressed by index into a row-major grid of <see cref="Columns"/> columns.
/// </summary>
public sealed class PetManifest
{
    [JsonPropertyName("name")] public string Name { get; set; } = "cat";
    [JsonPropertyName("sheet")] public string Sheet { get; set; } = "spritesheet.png";
    [JsonPropertyName("cellWidth")] public int CellWidth { get; set; } = 64;
    [JsonPropertyName("cellHeight")] public int CellHeight { get; set; } = 64;
    [JsonPropertyName("columns")] public int Columns { get; set; } = 4;

    /// <summary>Display scale applied to the sprite when rendered (1 = native cell size).</summary>
    [JsonPropertyName("scale")] public double Scale { get; set; } = 1.0;

    /// <summary>Animation name (lower-case) -> definition. Expected keys: idle, walk, sleep, fall, drag, eat.</summary>
    [JsonPropertyName("animations")] public Dictionary<string, AnimationDef> Animations { get; set; } = new();

    public static PetManifest Load(string path)
    {
        var json = File.ReadAllText(path);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<PetManifest>(json, opts)
               ?? throw new InvalidDataException($"Could not parse manifest at {path}");
    }
}

public sealed class AnimationDef
{
    /// <summary>Grid indices of the frames, played in order.</summary>
    [JsonPropertyName("frames")] public int[] Frames { get; set; } = Array.Empty<int>();

    /// <summary>Frames per second for this animation.</summary>
    [JsonPropertyName("fps")] public double Fps { get; set; } = 4;

    /// <summary>Whether the animation loops (true) or holds the last frame (false).</summary>
    [JsonPropertyName("loop")] public bool Loop { get; set; } = true;
}
