using MikeShift.Core.Hotkeys;
using MikeShift.Core.Interaction;

namespace MikeShift.Core.Feedback;

/// <summary>What the welcome window and Settings say about the activation key, the hotkey and optional codecs. One place, so both agree and the tests can read the words.</summary>
public static class OnboardingCopy
{
    /// <summary>The question of the first-run choice.</summary>
    public const string ArmKeyQuestion = "How do you want to open the wheel while dragging?";

    /// <summary>
    /// A neutral line under the two options, so the choice is informed rather than a default: Shift-drag is rare in daily use but means
    /// "move" in Explorer; Ctrl is Explorer's own "copy".
    /// </summary>
    public const string ArmKeyTradeoff =
        "Shift is rarely used while dragging, so it seldom gets in the way. Ctrl is File Explorer's \"copy\" key, so letting go early can never move a file.";

    /// <summary>"Press Ctrl or scroll the mouse wheel to switch to tools ... Press Shift to go back."</summary>
    public static string ToolsDescription(ArmKey arm, ToolsKey tools)
    {
        var back = ToolsKeys.Label(arm);
        var list = "tools like Compress, Resize, Crop, Trim and Zip";
        return ToolsKeys.ToolsModifier(arm, tools) == Modifiers.None
            ? $"While the wheel is open, scroll the mouse wheel to switch to {list}. Press {back} to go back."
            : $"While the wheel is open, press {ToolsKeys.Label(arm, tools)} or scroll the mouse wheel to switch to {list}. Press {back} to go back.";
    }

    /// <summary>The heading of the tools tip: "Press Ctrl for tools" or "Scroll for tools".</summary>
    public static (string Verb, string Key) ToolsHeading(ArmKey arm, ToolsKey tools) =>
        ToolsKeys.ToolsModifier(arm, tools) == Modifiers.None ? ("Use the", "Scroll wheel") : ("Press", ToolsKeys.Label(arm, tools));

    /// <summary>The line about the keyboard shortcut for the current Explorer selection, or null when the hotkey is off.</summary>
    public static string? HotkeyHint(HotkeySpec? hotkey) => hotkey is { } h
        ? $"Select files in File Explorer and press {h} to open the wheel from the keyboard."
        : null;

    /// <summary>The description of the hotkey card in Settings.</summary>
    public const string HotkeyDescription =
        "Press this shortcut while File Explorer or the desktop is in front to open the wheel for the selected files. It works with the keyboard alone.";

    /// <summary>The title and body of the optional first-run card for the missing extensions, or null when nothing is missing.</summary>
    public static (string Title, string Body)? CodecCard(IReadOnlyList<CodecPrompt> missing)
    {
        ArgumentNullException.ThrowIfNull(missing);
        if (missing.Count == 0) return null;
        var heic = missing.Any(m => m.Need is CodecNeed.Heif or CodecNeed.Hevc);
        if (heic)
        {
            var both = missing.Count(m => m.Need is CodecNeed.Heif or CodecNeed.Hevc) == 2;
            var body = both
                ? "Windows needs the free \"HEIF Image Extensions\" and Microsoft's \"HEVC Video Extensions\" to open HEIC photos. MikeShift works without them; HEIC photos just can't be converted yet."
                : missing.Any(m => m.Need == CodecNeed.Heif)
                    ? "Windows needs the free \"HEIF Image Extensions\" to open HEIC photos. MikeShift works without it; HEIC photos just can't be converted yet."
                    : "Windows also needs Microsoft's \"HEVC Video Extensions\" to open HEIC photos. MikeShift works without it; HEIC photos just can't be converted yet.";
            return ("Optional: add support for iPhone photos (HEIC)", body);
        }
        return missing[0].Need == CodecNeed.Av1
            ? ("Optional: add support for AVIF photos", "Windows needs the free \"AV1 Video Extension\" to open AVIF photos. MikeShift works without it.")
            : ("Optional: add support for WebP images", "Windows can add the free \"WebP Image Extensions\". MikeShift works without it.");
    }

    /// <summary>The label of a Store button of the codec card: "Get HEIF extension".</summary>
    public static string StoreButtonLabel(CodecPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return prompt.Need switch
        {
            CodecNeed.Heif => "Get HEIF extension",
            CodecNeed.Hevc => "Get HEVC extension",
            CodecNeed.Av1 => "Get AV1 extension",
            _ => "Get WebP extension",
        };
    }
}
