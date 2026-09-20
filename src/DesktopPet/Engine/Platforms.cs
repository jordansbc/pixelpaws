namespace DesktopPet.Engine;

/// <summary>
/// The standing-and-landing maths, kept free of engine state so it can be reasoned about (and
/// tested) on its own. All values are DIPs; "feet" is the pet's bottom edge.
/// </summary>
public static class Platforms
{
    /// <summary>How far from a surface the pet's feet may be and still count as standing on it.</summary>
    public const double DefaultTolerance = 3.0;

    // NOTE: these loop by index rather than with foreach. These run several times per frame over
    // a list that holds every visible window, and foreach over an IReadOnlyList<T> goes through
    // the boxed IEnumerator<T> — an allocation per call plus interface dispatch per element.
    // Indexing keeps them allocation-free.

    /// <summary>True if any surface is directly under the pet and level with its feet.</summary>
    public static bool IsSupported(IReadOnlyList<Surface> surfaces, double centerX, double feetY,
                                   double tolerance = DefaultTolerance)
    {
        for (int i = 0; i < surfaces.Count; i++)
        {
            var s = surfaces[i];
            if (s.ContainsX(centerX) && Math.Abs(s.Top - feetY) <= tolerance)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Find the surface a falling pet would land on this frame: the highest one under it whose
    /// top lies in the span the feet swept through. Returns false when it should keep falling.
    /// </summary>
    public static bool TryLand(IReadOnlyList<Surface> surfaces, double centerX,
                               double prevFeet, double nextFeet, out double landTop,
                               double tolerance = DefaultTolerance)
    {
        landTop = 0;
        bool found = false;

        for (int i = 0; i < surfaces.Count; i++)
        {
            var s = surfaces[i];
            if (!s.ContainsX(centerX)) continue;
            if (s.Top < prevFeet - tolerance) continue;   // already below us — can't land on it
            if (s.Top > nextFeet) continue;               // still beneath this frame's reach
            if (!found || s.Top < landTop) { landTop = s.Top; found = true; }
        }

        return found;
    }
}
