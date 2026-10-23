namespace MikeShift.Core.Actions;

/// <summary>
/// Which wheel page an action lives on. (Named <c>WheelPage</c>, not <c>Page</c>, so that App code that also imports
/// <c>Microsoft.UI.Xaml.Controls</c> never hits an ambiguous <c>Page</c>.)
/// </summary>
public enum WheelPage
{
    /// <summary>Output formats ("Convert to PNG"); tiles show the extension. Shown first, toggled with Shift.</summary>
    Format,
    /// <summary>Tools (Compress, Resize, Crop, Trim, Split, Merge, Zip...); tiles show a glyph. Toggled with Ctrl.</summary>
    Tool,
}
