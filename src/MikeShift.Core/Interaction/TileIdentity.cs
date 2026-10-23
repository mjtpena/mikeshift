namespace MikeShift.Core.Interaction;

/// <summary>
/// UI Automation hands the wheel a tile <i>index</i>, possibly long after the provider was created; by then the wheel may have been hidden and
/// shown again, or switched page, and the same index means a different action. A call is acted on only if it still names the same wheel
/// (show generation) and the tile at that index still has the same title.
/// </summary>
public static class TileIdentity
{
    /// <summary>True if a call made against (<paramref name="generation"/>, <paramref name="index"/>, <paramref name="title"/>) still targets that tile now.</summary>
    public static bool StillCurrent(int generation, int currentGeneration, int index, string title, IReadOnlyList<string> currentTitles) =>
        generation == currentGeneration
        && index >= 0 && index < currentTitles.Count
        && string.Equals(currentTitles[index], title, StringComparison.Ordinal);
}
