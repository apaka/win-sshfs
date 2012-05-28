#region

using System;
using System.Diagnostics;
using System.IO;

#endregion

namespace Sshfs
{
    internal static class Program
    {
        /// <summary>
        ///   The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main(params string[] args )
        {

#if DEBUG
            Debug.AutoFlush = true;
            Debug.Listeners.Add(new DelimitedListTraceListener(String.Format("{0}\\log{1:yyyy-MM-dd-HH-mm-ss}.txt",Environment.CurrentDirectory,DateTime.Now), "debug"));
#endif
            new SftpManagerApplication().Run(args);
        }
    }
}