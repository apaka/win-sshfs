#region

using Microsoft.VisualBasic.ApplicationServices;

#endregion

namespace Sshfs
{
    internal class SftpManagerApplication : WindowsFormsApplicationBase
    {
        public SftpManagerApplication()
        {
            IsSingleInstance = true;
            EnableVisualStyles = true;
        }

        protected override void OnCreateMainForm()
        {
            MainForm = new MainForm();
        }
     
        protected override void OnStartupNextInstance(StartupNextInstanceEventArgs eventArgs)
        {
            (MainForm as MainForm).ReShow();
        }
    }
}