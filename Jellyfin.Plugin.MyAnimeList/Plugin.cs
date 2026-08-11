using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;

namespace Jellyfin.Plugin.MyAnimeList
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            JikanAPI.Initialize(applicationPaths);
        }

        /// <inheritdoc />
        public override string Name => Constants.PluginName;

        public new string DataFolderPath
        {
            get
            {
                var path = Path.Combine(
                    ApplicationPaths.PluginsPath,
                    $"myanimelist_{Version.ToString(3)}"
                );

                Directory.CreateDirectory(path);
                return path;
            }
        }

        /// <inheritdoc />
        public override Guid Id => Guid.Parse(Constants.PluginGuid);

        public static Plugin Instance { get; private set; }

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return
            [
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = string.Format("{0}.Configuration.configPage.html", GetType().Namespace)
                }
            ];
        }
    }
}
