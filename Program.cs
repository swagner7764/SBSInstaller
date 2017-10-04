using System;
using System.IO;
using System.Reflection;
using CommandLine;
using CommandLine.Text;
using log4net;
using log4net.Config;
using SBSInstaller.Tasks;

namespace SBSInstaller
{
    internal class Program
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof (Program));

        private static void Main(string[] args)
        {
            XmlConfigurator.Configure();

            Log.Info("Begin installation.");
            Log.Info(string.Format("Args: {0}", string.Join(",", args)));

            var options = new Options();
            if (!Parser.Default.ParseArguments(args, options))
            {
                Log.Error(options.GetUsage());
                Environment.Exit(-1);
            }

            var installInfo = new InstallInfo();
            try
            {
                ProcessInstall(options, installInfo);
            }
            finally
            {
                var myDir = Path.GetDirectoryName(
                    new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath);
                myDir = myDir ?? "";

                if (!installInfo.DeployLocation.Exists)
                    installInfo.DeployLocation.Create();

                var src = Path.Combine(myDir, "Install.log");
                var dest = Path.Combine(installInfo.DeployLocation.FullName, "Install.log");

                Log.Debug(string.Format("Copying {0} to {1}", src, dest));
                LogManager.Shutdown();

                File.Copy(src, dest, true);
                File.Delete(src);
            }
        }

        private static void ProcessInstall(Options options, InstallInfo installInfo)
        {
            if (options.Type == null)
                throw new Exception(
                    string.Format("Invalid installation type: {0}, type must be {1}, {2}, {3} or {4}",
                        options.TypeString, InstallType.Application, InstallType.Service, InstallType.WebService,
                        InstallType.Library));

            installInfo.Type = (InstallType) options.Type;
            installInfo.AssemblyName = options.AssemblyName;
            installInfo.ServiceName = options.ServiceName;
            if ((options.Type == InstallType.Service || options.Type == InstallType.WebService) &&
                string.IsNullOrEmpty(installInfo.ServiceName))
                throw new Exception(
                    "Service name must be specified for Service, Scheduled Task and WebService installations.");
            installInfo.AssemblyLocation = new DirectoryInfo(options.AssemblyDirectory);
            if (!installInfo.AssemblyLocation.Exists)
                throw new Exception(string.Format(
                    "Assembly directory locations: {0} does not exist or is invalid.", installInfo.AssemblyLocation));
            installInfo.WebsiteName = options.WebsiteName;
            installInfo.AppPoolName = options.AppPoolName;

            switch (installInfo.Type)
            {
                case InstallType.Service:
                    new InstallServiceTask().Execute(installInfo);
                    break;
                case InstallType.WebService:
                    new InstallWebServiceTask().Execute(installInfo);
                    break;
                case InstallType.Application:
                    new InstallApplicationTask().Execute(installInfo);
                    break;
                case InstallType.ScheduledTask:
                    new InstallScheduledTaskTask().Execute(installInfo);
                    break;
                case InstallType.SelfHostedService:
                    new InstallSelfHostedServiceTask().Execute(installInfo);
                    break;
            }

            Log.Debug(string.Format("Installation of {0} complete.", installInfo.AssemblyName));
        }
    }


    // Define a class to receive parsed values
    internal class Options
    {
        [Option('t', "type", Required = true,
            HelpText = "The type of application we are installing [WebService, Service, Application, ScheduledTask].")]
        public string TypeString { get; set; }

        [Option('a', "assembly_name", Required = true,
            HelpText = "The assembly name of this application, not necessarily the project name.")]
        public string AssemblyName { get; set; }

        [Option('s', "service_name", Required = false,
            HelpText = "The service name. Only applies to WebService and Service installations.")]
        public string ServiceName { get; set; }

        [Option('w', "website_name", Required = false,
            HelpText = "The IIS website name. Only applies to WebService installations.")]
        public string WebsiteName { get; set; }

        [Option('p', "apppool_name", Required = false,
            HelpText = "The IIS app pool name. Only applies to WebService installations.")]
        public string AppPoolName { get; set; }

        [Option('d', "assembly_directory", Required = true,
            HelpText = "The directory where the assembly binaries exist on disk.")]
        public string AssemblyDirectory { get; set; }

        [ParserState]
        public IParserState LastParserState { get; set; }

        public InstallType? Type
        {
            get
            {
                switch (TypeString.ToUpper())
                {
                    case "APP":
                    case "APPLICATION":
                        return InstallType.Application;
                    case "SCHED":
                    case "SCHEDULEDTASK":
                        return InstallType.ScheduledTask;
                    case "WEB":
                    case "WEBSERVICE":
                    case "WEB_SERVICE":
                        return InstallType.WebService;
                    case "SVC":
                    case "SERVICE":
                        return InstallType.Service;
                    case "SELFHOSTEDSERVICE":
                        return InstallType.SelfHostedService;
                    default:
                        return null;
                }
            }
        }

        [HelpOption]
        public string GetUsage()
        {
            return HelpText.AutoBuild(this,
                (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
        }
    }
}