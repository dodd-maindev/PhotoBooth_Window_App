namespace Photobooth.Desktop.Models;

public sealed class FilterOption
{
    public FilterOption(string key, string displayName, string description)
    {
        Key = key;
        DisplayName = displayName;
        Description = description;
    }

    public string Key { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public override string ToString() => DisplayName;
}