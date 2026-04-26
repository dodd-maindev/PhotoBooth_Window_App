namespace Photobooth.Desktop.Models;

/// <summary>Represents a selectable image-processing filter with display metadata and icon geometry.</summary>
public sealed class FilterOption
{
    /// <summary>
    /// Initialises a new <see cref="FilterOption"/>.
    /// </summary>
    /// <param name="key">API key used when calling the Python service.</param>
    /// <param name="displayName">Human-readable label shown in the UI.</param>
    /// <param name="description">Short Vietnamese description shown beneath the label.</param>
    /// <param name="iconData">WPF geometry mini-language path string for the sidebar icon.</param>
    public FilterOption(string key, string displayName, string description, string iconData = "")
    {
        Key = key;
        DisplayName = displayName;
        Description = description;
        IconData = iconData;
    }

    /// <summary>Gets the API identifier for this filter.</summary>
    public string Key { get; }

    /// <summary>Gets the UI display name.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the short Vietnamese description.</summary>
    public string Description { get; }

    /// <summary>Gets the WPF geometry path data for the sidebar icon.</summary>
    public string IconData { get; }

    /// <inheritdoc/>
    public override string ToString() => DisplayName;
}