namespace MikeShift.Core.Interaction;

/// <summary>Runtime ids for the wheel's UI Automation tiles.</summary>
public static class UiaRuntimeId
{
    /// <summary><c>UiaAppendRuntimeId</c> from UIAutomationCoreApi.h: the first element of a fragment's runtime id tells UI Automation to append the rest to the host window's id.</summary>
    public const int Append = 3;

    /// <summary>Tools-page tiles are numbered from here so they never collide with a format tile of the same index.</summary>
    public const int ToolPageOffset = 1000;

    /// <summary>The runtime id of tile <paramref name="index"/> on the format or tools page.</summary>
    public static int[] ForTile(bool toolsPage, int index) => [Append, (toolsPage ? ToolPageOffset : 0) + index + 1];
}
