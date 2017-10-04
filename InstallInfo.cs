using System.Configuration;
using System.Diagnostics;
using System.IO;
using log4net;
using SBSInstaller.Config;
using SBSInstaller.Tasks;

namespace SBSInstaller
{
    public class InstallInfo
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(InstallInfo));

        private DirectoryInfo _deployLocation;
        private DirectoryInfo _symbolicLink;
        private string _version;
        private string _websiteName;
        public InstallType Type { get; set; }
        public string AssemblyName { get; set; }
        public string ServiceName { get; set; }
        public string AppPoolName { get; set; }
        public DirectoryInfo AssemblyLocation { get; set; }

        public string Version
        {
            get
            {
                if (_version != null)
                    return _version;

                if (AssemblyLocation == null || AssemblyName == null)
                    return null;

                var path = string.Format("{0}\\{1}.{2}", AssemblyLocation.FullName, AssemblyName, "exe");
                if (Type == InstallType.WebService || Type == InstallType.Library)
                    path = string.Format("{0}\\bin\\{1}.{2}", AssemblyLocation.FullName, AssemblyName, "dll");

                Log.Info(path);

                var versionInfo = FileVersionInfo.GetVersionInfo(path);
                _version = versionInfo.ProductVersion;

                Log.Info(_version);
                return _version;
            }
        }

        public DirectoryInfo DeployLocation
        {
            get
            {
                if (_deployLocation != null)
                    return _deployLocation;

                if (AssemblyName == null || Version == null)
                    return null;

                var config = (InstallDefaultsSection) ConfigurationManager.GetSection("installDefaults");
                var destRootDir = config["App.Dir"];
                _deployLocation =
                    new DirectoryInfo(string.Format("{0}\\_{1}\\{1}-{2}", destRootDir, AssemblyName, Version));
                return _deployLocation;
            }
        }

        public DirectoryInfo SymbolicLink
        {
            get
            {
                if (_symbolicLink != null)
                    return _symbolicLink;

                if (AssemblyName == null || Version == null)
                    return null;

                var config = (InstallDefaultsSection) ConfigurationManager.GetSection("installDefaults");
                var destRootDir = config["App.Dir"];

                _symbolicLink = new DirectoryInfo(string.Format("{0}\\{1}", destRootDir, AssemblyName));
                return _symbolicLink;
            }
        }

        public string WebsiteName
        {
            get { return _websiteName ?? "Default Web Site"; }
            set { _websiteName = value; }
        }
    }

    public enum InstallType
    {
        Service,
        WebService,
        Application,
        ScheduledTask,
        Library,
        SelfHostedService
    }
}