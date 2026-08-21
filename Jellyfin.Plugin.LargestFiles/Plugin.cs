using System;
using System.Collections.Generic;
using Jellyfin.Plugin.LargestFiles.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LargestFiles;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "Largest Files";

    public override Guid Id => Guid.Parse("d8b2e9d4-5b1a-4c7e-9c2f-2a6f0e6a2b11");

    public static Plugin? Instance { get; private set; }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "largestfiles",
            EmbeddedResourcePath = string.Format("{0}.Web.largestFiles.html", GetType().Namespace)
        };
    }
}
