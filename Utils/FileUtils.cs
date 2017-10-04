using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using log4net;

namespace SBSInstaller.Utils
{
    public static class FileUtils
    {
        public enum SymbolicLink
        {
            File = 0,
            Directory = 1
        }

        private static readonly ILog Log = LogManager.GetLogger(typeof (FileUtils));

        [DllImport("kernel32.dll")]
        public static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName,
            SymbolicLink dwFlags);

        public static void Install(InstallInfo installInfo)
        {
            if (installInfo.DeployLocation.Exists)
                installInfo.DeployLocation.Delete(true);

            Log.Info(string.Format("Installing {0} to directory: {1}.", installInfo.AssemblyName,
                installInfo.DeployLocation.FullName));
            DirectoryCopy(installInfo.AssemblyLocation, installInfo.DeployLocation, true, false);

            var symbolicLink = installInfo.SymbolicLink.FullName;
            if (Directory.Exists(symbolicLink))
                Directory.Delete(symbolicLink);

            Log.Info(string.Format("Creating symbolic link: {0} to install directory: {1}.", symbolicLink,
                installInfo.DeployLocation.FullName));
            var success = CreateSymbolicLink(symbolicLink, installInfo.DeployLocation.FullName, SymbolicLink.Directory);
            if (!success)
            {
                var msg =
                    string.Format(
                        "Unable to create symbolic link: {0} to install directory: {1}.  Verfiy this application has correct permissions to this directory.",
                        symbolicLink, installInfo.DeployLocation.FullName);
                Log.Error(msg);
                throw new Exception(msg);
            }
        }

        public static void DirectoryCopy(DirectoryInfo sourceDirectory, DirectoryInfo destDirectory, bool copySubDirs,
            bool overwrite)
        {
            if (!sourceDirectory.Exists)
            {
                throw new DirectoryNotFoundException(
                    string.Format("Source directory: {0} does not exist or could not be found.",
                        sourceDirectory.FullName));
            }

            destDirectory.Refresh();
            // If the destination directory doesn't exist, create it. 
            if (!destDirectory.Exists)
            {
                destDirectory.Create();
            }

            // Get the files in the directory and copy them to the new location.
            var files = sourceDirectory.GetFiles();
            foreach (var file in files)
            {
                var tempPath = Path.Combine(destDirectory.FullName, file.Name);
                Log.Debug(string.Format("Copying file: {0}.", tempPath));
                file.CopyTo(tempPath, overwrite);
            }

            // If copying subdirectories, copy them and their contents to new location. 
            if (copySubDirs)
            {
                var subDirs = sourceDirectory.GetDirectories();
                foreach (var subDir in subDirs)
                {
                    var tempPath = Path.Combine(destDirectory.FullName, subDir.Name);
                    DirectoryCopy(subDir, new DirectoryInfo(tempPath), true, overwrite);
                }
            }
        }

        public static void RunProcess(FileInfo exe, string args)
        {
            var p = new Process();
            // Redirect the output stream of the child process.
            p.StartInfo.CreateNoWindow = false;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = exe.FullName;
            p.StartInfo.Arguments = args;
            p.Start();

            var reader = p.StandardOutput;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                Log.Debug(line);
            }
            p.WaitForExit();
        }
    }
}