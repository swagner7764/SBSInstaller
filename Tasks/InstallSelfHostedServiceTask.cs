using System;
using System.Configuration;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.ServiceProcess;
using System.Net;
using System.Net.NetworkInformation;
using log4net;
using SBSInstaller.Config;
using SBSInstaller.Utils;
using System.Xml;
using Microsoft.Win32.TaskScheduler;
using TimeoutException = System.ServiceProcess.TimeoutException;
using System.Management;

namespace SBSInstaller.Tasks
{
    class InstallSelfHostedServiceTask
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(InstallSelfHostedServiceTask));
        private ServiceControllerStatus _originalServiceStatus;

        public void Execute(InstallInfo installInfo)
        {
            var sc = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == installInfo.ServiceName);
            if (sc == null)
            {
                Log.Warn("service controller could not find service, skipping service removal tasks");
            }
            else
            {
                Log.Info("found service - " + sc.ServiceName);
                using (sc)
                {
                    StopService(installInfo, sc);
                    Log.Info("Starting Service Delete");
                    DeleteService(installInfo, sc);
                    Log.Info("Service Delete Complete");
                }
            }

            FileUtils.Install(installInfo);

            //add in support for scheduled tasks
            var taskDef = string.Format("{0}\\~TaskDefs.xml", installInfo.AssemblyLocation.FullName);
            if (!File.Exists(taskDef))
            {
                Log.Warn(taskDef + " not found, scheduled task will not be configured. continuing with install of service");
            }
            else
            {
                var doc = new XmlDocument();
                doc.Load(taskDef);
                DeleteTask(doc);
                InstallTask(doc, installInfo);
                File.Delete(string.Format("{0}\\~TaskDefs.xml", installInfo.DeployLocation.FullName));
            }

            StartService(installInfo);
        }

        private void DeleteTask(XmlDocument doc)
        {
            var xmlNodeList = doc.SelectNodes("/ScheduledTasks/ScheduledTask");
            if (xmlNodeList == null)
                return;

            using (var ts = new TaskService())
            {
                foreach (XmlNode node in xmlNodeList)
                {
                    if (node.Attributes == null)
                        continue;
                    var attNode = node.Attributes["name"];
                    var taskName = attNode == null ? null : attNode.Value;

                    var td = ts.AllTasks.FirstOrDefault(x => x.Path == "\\" + taskName);
                    if (td == null)
                    {
                        Log.Debug("Task: " + taskName + " not configured.");
                        continue;
                    }
                    try
                    {
                        td.Stop();
                        td.Dispose();
                        ts.RootFolder.DeleteTask(taskName);
                    }
                    catch (Exception e)
                    {
                        Log.Error(
                            string.Format(
                                "Scheduled task: {0} could not be stopped or removed, examine details, cause: {1}.",
                                taskName, e.Message), e);
                        throw;
                    }
                }
            }
        }

        private void InstallTask(XmlDocument doc, InstallInfo installInfo)
        {
            var xmlNodeList = doc.SelectNodes("/ScheduledTasks/ScheduledTask");
            if (xmlNodeList == null)
            {
                Log.Warn("No scheduled tasks found in file. No tasks will be installed.");
                return;
            }

            using (var ts = new TaskService())
            {
                foreach (XmlNode node in xmlNodeList)
                {
                    if (node.Attributes == null)
                        continue;
                    var attNode = node.Attributes["name"];
                    var taskName = attNode == null ? null : attNode.Value;
                    var taskNode = node.FirstChild;
                    try
                    {
                        using (var td = ts.NewTask())
                        {
                            var credentials = GetCredentialsForTask(taskNode);
                            td.XmlText = taskNode.OuterXml;
                            var execAction =
                                (ExecAction)
                                    td.Actions.FirstOrDefault(
                                        x => x is ExecAction && ((ExecAction)x).Path == "${AssemblyName}");
                            if (execAction == null)
                                continue;

                            execAction.Path = installInfo.AssemblyName + ".exe";
                            execAction.WorkingDirectory = installInfo.SymbolicLink.FullName;
                            if (credentials == null)
                                ts.RootFolder.RegisterTaskDefinition(taskName, td);
                            else
                                ts.RootFolder.RegisterTaskDefinition(taskName, td, TaskCreation.Create,
                                    credentials.UserName, credentials.Password, TaskLogonType.Password);
                        }
                        Log.Info(string.Format("Scheduled task: {0} created", taskName));
                    }
                    catch (Exception e)
                    {
                        Log.Error(
                            string.Format(
                                "Scheduled task: {0} could not be could not be created, examine details, cause: {1}.",
                                taskName, e.Message), e);
                        throw;
                    }
                }
            }
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
            var config = (InstallDefaultsSection)ConfigurationManager.GetSection("installDefaults");

            //uninstalling service
            Log.Info("service uninstalling");
            try
            {
                var dotNetLocation = new DirectoryInfo(string.Format(config["DotNet.Dir"], winDir));
                var serviceInstall = new FileInfo(string.Format("{0}\\InstallUtil.exe", dotNetLocation));
                var serviceExePath = GetServicePath(sc).Replace("\" \"WEB\"", "\"");
                Log.Info("Uninstalling - " + serviceExePath);
                FileUtils.RunProcess(serviceInstall, "/u /LogToConsole=true " + serviceExePath);
            }
            catch (Exception e)
            {
                Log.Error(string.Format("Service: {0} could not be unisntalled, examine details, cause: {1}.",
                    installInfo.AssemblyName, e.Message), e);
                throw;
            }
            Log.Info("service uninstalled");

            Log.Info("deleting service");
            //deleting service
            var scPath = string.Format("{0}\\system32\\sc.exe", winDir);
            try
            {
                FileUtils.RunProcess(new FileInfo(scPath), "delete " + sc.ServiceName);
            }
            catch (Exception e)
            {
                Log.Error(string.Format("Service: {0} could not be removed, examine details, cause: {1}.",
                    installInfo.AssemblyName, e.Message), e);
            }
            Log.Info("service delete");
        }

        protected String GetServicePath(ServiceController sc)
        {
            WqlObjectQuery wqlObjectQuery = new WqlObjectQuery(string.Format("SELECT * FROM Win32_Service WHERE Name = '{0}'", sc.ServiceName));
            ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher(wqlObjectQuery);
            ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
            Log.Info(managementObjectCollection.Count);

            foreach (ManagementObject managementObject in managementObjectCollection)
            {
                Log.Info(managementObject.GetPropertyValue("PathName").ToString());
                return managementObject.GetPropertyValue("PathName").ToString();
            }

            return null;
        }

        protected void StartService(InstallInfo installInfo)
        {
            var winDir = Environment.GetEnvironmentVariable("windir");
            var config = (InstallDefaultsSection)ConfigurationManager.GetSection("installDefaults");
            var dotNetLocation = new DirectoryInfo(string.Format(config["DotNet.Dir"], winDir));

            var serviceInstall = new FileInfo(string.Format("{0}\\InstallUtil.exe", dotNetLocation));
            var serviceExePath = string.Format("{0}\\{1}.exe", installInfo.SymbolicLink.FullName, installInfo.AssemblyName);
            var serviceURLauth = new FileInfo(string.Format("{0}\\System32\\netsh.exe", winDir));
            var userauthpath = new FileInfo(string.Format("{0}\\System32\\icacls.exe", winDir));

            //get service user and password
            string configpath = string.Format("{0}\\{1}.exe.config", installInfo.SymbolicLink.FullName, installInfo.AssemblyName);
            Log.Info(configpath);
            XmlDocument appcfgdoc = null;
            XmlNodeList usernamenodes = null;
            XmlNodeList userpassnodes = null;
            XmlNodeList serviceurls = null;
            try
            {
                XmlReaderSettings xmlreadsetting = new XmlReaderSettings();
                xmlreadsetting.IgnoreComments = true;
                XmlReader xmrd = XmlReader.Create(configpath, xmlreadsetting);
                appcfgdoc = new XmlDocument();
                appcfgdoc.Load(xmrd);
                xmrd.Close();
                usernamenodes = appcfgdoc.SelectNodes("configuration/appSettings/add[@key='ServiceUser']");
                userpassnodes = appcfgdoc.SelectNodes("configuration/appSettings/add[@key='ServiceUserPassword']");
                serviceurls = appcfgdoc.DocumentElement.GetElementsByTagName("baseAddresses").Item(0).ChildNodes;
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
                Log.Error(e.StackTrace);
            }

            if (usernamenodes.Count < 1 || userpassnodes.Count < 1 || serviceurls.Count < 1)
            {
                //manual credential setup
                FileUtils.RunProcess(serviceInstall, "/LogToConsole=true " + serviceExePath);
            }
            else
            {
                string username = usernamenodes.Item(0).Attributes.Item(1).InnerText;
                string userpass = userpassnodes.Item(0).Attributes.Item(1).InnerText;
                string domainname = username.Split('\\')[0];
                string shortusername = username.Split('\\')[1];

                //apply user urls before installing service
                foreach (XmlNode urlnode in serviceurls)
                {
                    string myfqdn = GetFQDN();
                    string serviceurlacl = urlnode.Attributes.Item(0).InnerText.Replace("localhost", "+");
                    urlnode.Attributes.Item(0).InnerText = serviceurlacl.Replace("+", myfqdn);
                    string urlargs = string.Format("http add urlacl url={0} user={1}\\{2}", serviceurlacl, domainname, shortusername);
                    Log.Info(urlargs);
                    try
                    {
                        FileUtils.RunProcess(serviceURLauth, urlargs);
                    }
                    catch (Exception e)
                    {
                        Log.Error("user http urlacl failed, will continue on");
                        Log.Error(e.Message);
                    }
                }

                //save updated xml config doc
                appcfgdoc.Save(configpath);

                //edit user for privlages on dist folder
                string userauthargs = string.Format("D:\\dist /grant {0}\\{1}:(OI)(CI)m",domainname,shortusername);
                Log.Info(userauthargs);
                try
                {
                    FileUtils.RunProcess(userauthpath, userauthargs);
                }
                catch (Exception e)
                {
                    Log.Error("user auth error");
                    Log.Error(e.Message);
                }

                string args = string.Format("/username={0}\\{1} /password={2} /LogToConsole=true {3}", domainname, shortusername, userpass, serviceExePath);
                Log.Info(args);
                FileUtils.RunProcess(serviceInstall, args);
            }

            if (_originalServiceStatus != ServiceControllerStatus.Stopped)
            {
                var sc = new ServiceController(installInfo.ServiceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    try
                    {
                        //sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMinutes(1));
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

        private static Credentials GetCredentialsForTask(XmlNode taskNode)
        {
            // ReSharper disable once PossibleNullReferenceException
            var ns = new XmlNamespaceManager(taskNode.OwnerDocument.NameTable);
            ns.AddNamespace("x", taskNode.NamespaceURI);
            var principalNode = taskNode.SelectSingleNode("x:Principals/x:Principal", ns);
            if (principalNode == null)
                return null;

            var node = principalNode.SelectSingleNode("x:UserId", ns);
            var user = node == null ? null : node.InnerText;
            node = principalNode.SelectSingleNode("x:Password", ns);
            if (node != null)
                principalNode.RemoveChild(node);
            var pass = node == null ? null : node.InnerText;

            if (user != null && pass != null)
                return new Credentials { UserName = user, Password = pass };

            return null;
        }
        private static String GetFQDN()
        {
            string domainName = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            string hostName = Dns.GetHostName();

            domainName = "." + domainName;
            if (!hostName.EndsWith(domainName))  // if hostname does not already include domain name
            {
                hostName += domainName;   // add the domain name part
            }

            return hostName;
        }
        internal class Credentials
        {
            internal string UserName { get; set; }
            internal string Password { get; set; }
        }
    }
}
