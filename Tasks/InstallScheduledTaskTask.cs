using System;
using System.IO;
using System.Linq;
using System.Xml;
using log4net;
using Microsoft.Win32.TaskScheduler;

namespace SBSInstaller.Tasks
{
    public class InstallScheduledTaskTask : InstallBaseTask
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof (InstallScheduledTaskTask));

        public override void Execute(InstallInfo installInfo)
        {
            var taskDef = string.Format("{0}\\~TaskDefs.xml", installInfo.AssemblyLocation.FullName);
            if (!File.Exists(taskDef))
            {
                Log.Warn(taskDef + " not found, scheduled task will not be configured.");
                base.Execute(installInfo);
                return;
            }

            var doc = new XmlDocument();
            doc.Load(taskDef);

            DeleteTask(doc);
            base.Execute(installInfo);
            InstallTask(doc, installInfo);

            File.Delete(string.Format("{0}\\~TaskDefs.xml", installInfo.DeployLocation.FullName));
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
                                        x => x is ExecAction && ((ExecAction) x).Path == "${AssemblyName}");
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
    }

    internal class Credentials
    {
        internal string UserName { get; set; }
        internal string Password { get; set; }
    }
}