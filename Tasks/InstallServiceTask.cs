using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using log4net;
using SBSInstaller.Config;
using SBSInstaller.Utils;
using TimeoutException = System.ServiceProcess.TimeoutException;

namespace SBSInstaller.Tasks
{
    public class InstallServiceTask
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof (InstallServiceTask));
        private ServiceControllerStatus _originalServiceStatus;

        public void Execute(InstallInfo installInfo)
        {
            var sc = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == installInfo.ServiceName);
            using (sc)
            {
                StopService(installInfo, sc);
            }

            DeleteService(installInfo, sc);
            FileUtils.Install(installInfo);
            StartService(installInfo);
        }

        protected void StopService(InstallInfo installInfo, ServiceController sc)
        {
            if (sc == null) return;

            _originalServiceStatus = sc.Status;
            if (_originalServiceStatus == ServiceControllerStatus.Running)
            {
                Log.Info(string.Format("Stopping service: {0}.", installInfo.ServiceName));
                try
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }
                catch (TimeoutException e)
                {
                    Log.Error(string.Format("Service: {0} did not stop in expected time, installation aborted.",
                        installInfo.AssemblyName), e);
                    throw;
                }
                catch (Exception e)
                {
                    Log.Error(string.Format("Service: {0} could not be stopped, examine details, cause: {1}.",
                        installInfo.AssemblyName, e.Message), e);
                    throw;
                }
            }
        }

        protected void DeleteService(InstallInfo installInfo, ServiceController sc)
        {
            if (sc == null) return;

            var winDir = Environment.GetEnvironmentVariable("windir");
            var scPath = string.Format("{0}\\system32\\sc.exe", winDir);
            try
            {
                FileUtils.RunProcess(new FileInfo(scPath), "delete " + installInfo.ServiceName);
            }
            catch (Exception e)
            {
                Log.Error(string.Format("Service: {0} could not be removed, examine details, cause: {1}.",
                    installInfo.AssemblyName, e.Message), e);
                throw;
            }
        }

        protected void StartService(InstallInfo installInfo)
        {
            var winDir = Environment.GetEnvironmentVariable("windir");
            var config = (InstallDefaultsSection) ConfigurationManager.GetSection("installDefaults");
            var dotNetLocation = new DirectoryInfo(string.Format(config["DotNet.Dir"], winDir));

            var serviceInstall = new FileInfo(string.Format("{0}\\InstallUtil.exe", dotNetLocation));
            var serviceExePath = string.Format("{0}\\{1}.exe", installInfo.SymbolicLink.FullName,
                installInfo.AssemblyName);
            FileUtils.RunProcess(serviceInstall, "/LogToConsole=true " + serviceExePath);
            if (_originalServiceStatus != ServiceControllerStatus.Stopped)
            {
                var sc = new ServiceController(installInfo.ServiceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    try
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    }
                    catch (TimeoutException e)
                    {
                        Log.Error(string.Format("Service: {0} did not start in expected time, installation aborted.",
                            installInfo.AssemblyName), e);
                        throw;
                    }
                    catch (Exception e)
                    {
                        Log.Error(
                            string.Format(
                                "Service: {0} could not be installed or started, examine details, cause: {1}.",
                                installInfo.AssemblyName, e.Message), e);
                        throw;
                    }
                }
            }
        }
    }
}