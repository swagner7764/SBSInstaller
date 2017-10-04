using SBSInstaller.Utils;

namespace SBSInstaller.Tasks
{
    public abstract class InstallBaseTask
    {
        public virtual void Execute(InstallInfo installInfo)
        {
            FileUtils.Install(installInfo);
        }
    }
}