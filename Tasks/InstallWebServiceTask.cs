using System;
using System.Linq;
using log4net;
using Microsoft.Web.Administration;
using SBSInstaller.Utils;

namespace SBSInstaller.Tasks
{
    public class InstallWebServiceTask
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof (InstallWebServiceTask));

        public void Execute(InstallInfo installInfo)
        {
            StopService(installInfo);
            FileUtils.Install(installInfo);
            StartService(installInfo);
        }

        protected void StopService(InstallInfo installInfo)
        {
            var serverManager = new ServerManager();
            using (serverManager)
            {
                var serviceNames = installInfo.ServiceName.Split(',').Select(sValue => sValue.Trim()).ToArray();
                foreach (var serviceName in serviceNames)
                {
                    var application =
                        serverManager.Sites.Select(site => site.Applications["/" + serviceName])
                            .FirstOrDefault(a => a != null);
                    if (application != null)
                        application.Delete();
                }
                serverManager.CommitChanges();
            }
        }

        protected void StartService(InstallInfo installInfo)
        {
            var serverManager = new ServerManager();
            using (serverManager)
            {
                var site = serverManager.Sites.FirstOrDefault(s => s.Name == installInfo.WebsiteName);
                if (site == null)
                {
                    throw new Exception(
                        string.Format("The web site name '{0}' does not exist in the IIS server.",
                            installInfo.WebsiteName));
                }

                var serviceNames = installInfo.ServiceName.Split(',').Select(sValue => sValue.Trim()).ToArray();
                try
                {
                    foreach (var serviceName in serviceNames)
                    {
                        Log.Info(string.Format("Adding web service: {0}.", serviceName));
                        site.Applications.Add("/" + serviceName, installInfo.SymbolicLink.FullName);
                        Log.Info(string.Format("Web service: {0} added successfully.", serviceName));

                        if (!string.IsNullOrEmpty(installInfo.AppPoolName))
                        {
                            Log.Info(string.Format("Configuring app pool for web service: {0}", serviceName));
                            var application = site.Applications["/" + serviceName];
                            if (application == null)
                            {
                                throw new Exception(
                                    string.Format("The just added web service {0} was not found.", serviceName));
                            }
                            application.ApplicationPoolName = installInfo.AppPoolName;
                            Log.Info(string.Format("App pool for web service {0} set to {1}",
                                serviceName, installInfo.AppPoolName));
                        }
                    }
                    serverManager.CommitChanges();
                }
                catch (Exception e)
                {
                    Log.Error(string.Format("Web service: {0} could not be started, examine details, cause: {1}.",
                        installInfo.AssemblyName, e.Message), e);
                    throw;
                }
            }
        }
    }
}