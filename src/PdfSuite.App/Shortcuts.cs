using Avalonia.Input;

namespace PdfSuite.App;

/// <summary>
/// The platform's command key: Cmd on macOS, Ctrl elsewhere, so shortcuts follow each OS's
/// conventions (PRD §6, "platform-appropriate keyboard conventions").
/// </summary>
internal static class Shortcuts
{
    public static KeyModifiers Command { get; } = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>How a shortcut is written in tooltips: "Ctrl+W", or "⌘W" on macOS.</summary>
    public static string Label(string key) => OperatingSystem.IsMacOS() ? "⌘" + key : "Ctrl+" + key;

    public static KeyGesture Gesture(Key key) => new(key, Command);
}
