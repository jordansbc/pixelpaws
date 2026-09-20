using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopPet.Engine;

/// <summary>
/// Loads a sprite sheet described by a <see cref="PetManifest"/>, slices it into per-frame
/// images, and advances the current animation over time.
/// </summary>
public sealed class SpriteAnimator
{
    private readonly PetManifest _manifest;
    private readonly Dictionary<int, CroppedBitmap> _frames = new();

    private AnimationDef _current;
    private string _currentName = "";
    private int _frameIndex;
    private double _accumulator;

    public PetManifest Manifest => _manifest;

    /// <summary>True once a non-looping animation has shown its final frame.</summary>
    public bool Finished { get; private set; }

    /// <summary>Load from raw sprite-sheet bytes, so the sheet can come from disk or from an
    /// embedded resource without this class caring which.</summary>
    public SpriteAnimator(PetManifest manifest, byte[] sheetBytes)
    {
        _manifest = manifest;

        var sheet = new BitmapImage();
        using (var ms = new MemoryStream(sheetBytes))
        {
            sheet.BeginInit();
            sheet.CacheOption  = BitmapCacheOption.OnLoad;   // decode now; the stream closes below
            sheet.StreamSource = ms;
            sheet.EndInit();
        }
        sheet.Freeze();

        foreach (var anim in manifest.Animations.Values)
            foreach (var idx in anim.Frames)
                if (!_frames.ContainsKey(idx))
                    _frames[idx] = SliceFrame(sheet, idx);

        // Default to idle (or the first defined animation).
        _current = manifest.Animations.TryGetValue("idle", out var idle)
            ? idle
            : manifest.Animations.Values.First();
        _currentName = manifest.Animations.ContainsKey("idle") ? "idle" : manifest.Animations.Keys.First();
    }

    private CroppedBitmap SliceFrame(BitmapSource sheet, int index)
    {
        int col = index % _manifest.Columns;
        int row = index / _manifest.Columns;
        var rect = new System.Windows.Int32Rect(
            col * _manifest.CellWidth,
            row * _manifest.CellHeight,
            _manifest.CellWidth,
            _manifest.CellHeight);
        var cropped = new CroppedBitmap(sheet, rect);
        cropped.Freeze();
        return cropped;
    }

    /// <summary>
    /// Switch to a named animation. Re-playing the animation already running is a no-op for
    /// looping clips, but restarts a non-looping one: those hold their last frame and leave
    /// <see cref="Finished"/> latched true, so silently ignoring the call would make the
    /// caller's next Finished-gated state end on the frame it began.
    /// </summary>
    public void Play(string name)
    {
        if (name == _currentName && _current.Loop) return;
        if (!_manifest.Animations.TryGetValue(name, out var def))
            return; // unknown animation: keep current

        _current = def;
        _currentName = name;
        _frameIndex = 0;
        _accumulator = 0;
        Finished = false;
    }

    /// <summary>Advance by <paramref name="dt"/> seconds and return the current frame image.</summary>
    public ImageSource Tick(double dt)
    {
        if (_current.Frames.Length == 0)
            return _frames.Values.FirstOrDefault()!;

        double frameDuration = _current.Fps > 0 ? 1.0 / _current.Fps : double.MaxValue;
        _accumulator += dt;
        while (_accumulator >= frameDuration && _current.Frames.Length > 1)
        {
            _accumulator -= frameDuration;
            if (_frameIndex < _current.Frames.Length - 1)
            {
                _frameIndex++;
            }
            else if (_current.Loop)
            {
                _frameIndex = 0;
            }
            else
            {
                Finished = true;
                break;
            }
        }

        int gridIndex = _current.Frames[Math.Clamp(_frameIndex, 0, _current.Frames.Length - 1)];
        return _frames.TryGetValue(gridIndex, out var img) ? img : _frames.Values.First();
    }
}
