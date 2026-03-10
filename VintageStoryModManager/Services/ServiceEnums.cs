namespace VintageStoryModManager.Services;

/// <summary>
///     Defines the available color themes for the application UI.
/// </summary>
public enum ColorTheme
{
    /// <summary>
    ///     The default Vintage Story game theme with warm, earthy tones.
    /// </summary>
    VintageStory,

    /// <summary>
    ///     A modern dark theme with high contrast.
    /// </summary>
    Dark,

    /// <summary>
    ///     A modern light theme with softer colors.
    /// </summary>
    Light,

    /// <summary>
    ///     Randomly selects a theme on startup.
    /// </summary>
    SurpriseMe,

    /// <summary>
    ///     A user-defined custom theme.
    /// </summary>
    Custom
}
