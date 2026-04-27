using MediaBrowser.Common.Configuration;

namespace TestProject
{
    internal class FakeApplicationPaths : IApplicationPaths
    {
        public string ProgramDataPath => throw new NotImplementedException();

        public string WebPath => throw new NotImplementedException();

        public string ProgramSystemPath => throw new NotImplementedException();

        public string DataPath => throw new NotImplementedException();

        public string ImageCachePath => throw new NotImplementedException();

        public string PluginsPath => throw new NotImplementedException();

        public string PluginConfigurationsPath => throw new NotImplementedException();

        public string LogDirectoryPath => throw new NotImplementedException();

        public string ConfigurationDirectoryPath => throw new NotImplementedException();

        public string SystemConfigurationFilePath => throw new NotImplementedException();

        public string CachePath => Path.GetTempPath();

        public string TempDirectory => throw new NotImplementedException();

        public string VirtualDataPath => throw new NotImplementedException();

        public string TrickplayPath => throw new NotImplementedException();

        public string BackupPath => throw new NotImplementedException();

        public void CreateAndCheckMarker(string path, string markerName, bool recursive = false)
        {
            throw new NotImplementedException();
        }

        public void MakeSanityCheckOrThrow()
        {
            throw new NotImplementedException();
        }
    }
}
