using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LargestFiles.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the number of largest items to return by default.
    /// </summary>
    public int DefaultLimit { get; set; } = 100;
}
