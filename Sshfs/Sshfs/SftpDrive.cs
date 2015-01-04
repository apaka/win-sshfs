// Copyright (c) 2012 Dragan Mladjenovic
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.


#region

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DokanNet;
using Renci.SshNet;
using Sshfs.Properties;
#endregion

namespace Sshfs
{
    [Serializable]
    public class SftpDrive : IDisposable, ISerializable
    {
        
        private readonly CancellationTokenSource _mountCancel = new CancellationTokenSource();
        private readonly AutoResetEvent _pauseEvent = new AutoResetEvent(false);
        private readonly CancellationTokenSource _threadCancel = new CancellationTokenSource();
        private bool _exeptionThrown;
        private SftpFilesystem _filesystem;
      
        private Exception _lastExeption;
        private Thread _mountThread;

        private string _connection;

        public string Name { get; set; }

        public char Letter { get; set; }

        public ConnectionType ConnectionType { get; set; }

        public bool StorePassword { get; set; }

        public string PrivateKey { get; set; }

        public string Password { get; set; }

        public string Username { get; set; }

        public string Host { get; set; }


        public int Port { get; set; }


        public bool Automount { get; set; }


        public string Root { get; set; }

        public object Tag { get; set; }

        public DriveStatus Status { get; private set; }




        public SftpDrive(){}
       
        private void OnStatusChanged(EventArgs args)
        {
            if (StatusChanged != null)
            {
                StatusChanged(this, args);
            }
        }

        public event EventHandler<EventArgs> StatusChanged;

        

        private void SetupFilesystem()
        {
            Debug.WriteLine("SetupFilesystem");

            var info = ConnectionType == ConnectionType.Password
                                      ? (ConnectionInfo) new PasswordConnectionInfo(Host, Port, Username, Password)
                                      : (ConnectionInfo) new PrivateKeyConnectionInfo(Host, Port, Username, new PrivateKeyFile(PrivateKey, Password));

            _connection = Settings.Default.UseNetworkDrive ? String.Format("\\\\{0}\\{1}\\{2}", info.Host, Root, info.Username) : Name;

            _filesystem = new SftpFilesystem(info, Root, _connection, Settings.Default.UseOfflineAttribute, false,
                                             (int) Settings.Default.AttributeCacheTimeout, (int) Settings.Default.DirContentCacheTimeout);
            Debug.WriteLine("Connecting...");
            _filesystem.Connect();
        }

        private void SetupMountThread()
        {
            if (_mountThread == null)
            {
                Debug.WriteLine("Thread:Created");
                _mountThread = new Thread(MountLoop) {IsBackground = true};
                _mountThread.Start();
            }
        }

        private void MountLoop()
        {
            while (true)
            {
                Debug.WriteLine("Thread:Pause");
               
                _pauseEvent.WaitOne(-1);
                if (_threadCancel.IsCancellationRequested)
                {
                    Debug.WriteLine("Thread:Cancel");
                    break;
                }

                Debug.WriteLine("Thread:Mount");


                try
                {
                    _filesystem.Mount(String.Format("{0}:\\", Letter),
                        Settings.Default.UseNetworkDrive?DokanOptions.NetworkDrive|DokanOptions.KeepAlive: DokanOptions.RemovableDrive|DokanOptions.KeepAlive);
                }
                catch (Exception e)
                {
                    
                    _lastExeption = e;
                    _exeptionThrown = true;
                    _mountCancel.Cancel();
                }
                Status = DriveStatus.Unmounted;
                if (!_exeptionThrown)
                {
                   
                    OnStatusChanged(EventArgs.Empty);
                }

            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Mount()
        {
            Debug.WriteLine("Mount");
           

            if (Directory.GetLogicalDrives().Any(drive=>drive[0]==Letter))
            {
                throw new Exception("Drive with the same letter exists");
            }


            Status = DriveStatus.Mounting;

            try
            {
                SetupFilesystem();
            }
            catch
            {

                Status = DriveStatus.Unmounted;
                throw;
            }
                
                SetupMountThread();

                
               
                var mountEvent = Task.Factory.StartNew(() =>
                {
                    while (!_mountCancel.IsCancellationRequested &&
                           Directory.GetLogicalDrives().All(
                               drive => drive[0] != Letter))
                    {
                        Thread.Sleep(1000);
                    }
                }, _mountCancel.Token);


                _pauseEvent.Set();

                mountEvent.Wait();

                if (_exeptionThrown)
                {
                   
                    _exeptionThrown = false;
    
                    throw _lastExeption;
                }
            if(Settings.Default.UseNetworkDrive)
            Utilities.SetNetworkDriveName(_connection, Name);
            Status= DriveStatus.Mounted;
            OnStatusChanged(EventArgs.Empty);



        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Unmount()
        {
            Debug.WriteLine("Unmount");

            Status = DriveStatus.Unmounting;
            try
            {
               // Dokan.Unmount(Letter);
                Dokan.RemoveMountPoint(String.Format("{0}:\\", Letter));
                if (_filesystem != null)
                {

                    _filesystem.Dispose();


                   
                }
            }
            catch
            {
                //Status = DriveStatus.Unmounted;
                //  OnStatusChanged(EventArgs.Empty);
            }
            finally
            {
                _filesystem = null;  
            }

        }

        public override string ToString()
        {
            return String.Format("{0}[{1}:]", Name, Letter);
        }
        #region Implementation of IDisposable

        public void Dispose()
        {
            Debug.WriteLine("Dispose");


            if (_threadCancel != null) _threadCancel.Cancel();
            if (_pauseEvent != null) _pauseEvent.Set();
            try
            {
                Dokan.RemoveMountPoint(String.Format("{0}:\\", Letter));
                if (_filesystem != null)
                {
                    _filesystem.Dispose();


                    _filesystem = null;
                }
            }
            catch
            {
                Status = DriveStatus.Unmounted;
            }
            finally
            {
                _filesystem = null;
            }


            if (_mountCancel != null) {_mountCancel.Dispose();}
            if (_threadCancel != null) {_threadCancel.Dispose();}
            if (_pauseEvent != null) {_pauseEvent.Dispose();}
        }

        #endregion

        #region Implementation of ISerializable

        public SftpDrive(SerializationInfo info, StreamingContext context)
        {
            bool hasStorepw = false;

            foreach(SerializationEntry entry in info) {
                switch(entry.Name) {
                    case "storepw":
                        hasStorepw = true;
                        break;
                }
            }

            Name = info.GetString("name");
            Host = info.GetString("host");
            Port = info.GetInt32("port");
            Letter = info.GetChar("drive");
            Root = info.GetString("path");
            Automount = info.GetBoolean("mount");
            Username = info.GetString("user");
            ConnectionType = (ConnectionType) info.GetByte("c");
            StorePassword = hasStorepw ? info.GetBoolean("storepw") : true;
            if (ConnectionType == ConnectionType.Password)
            {
                if (StorePassword)
                {
                    Password = Utilities.UnprotectString(info.GetString("p"));
                }
            }
            else
            {
                if (StorePassword)
                {
                    Password = Utilities.UnprotectString(info.GetString("p"));
                }
                PrivateKey = info.GetString("k");
            }
        }



        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("name", Name);
            info.AddValue("host", Host);
            info.AddValue("port", Port);
            info.AddValue("drive", Letter);
            info.AddValue("path",Root);
            info.AddValue("mount", Automount);
            info.AddValue("user", Username);
            info.AddValue("c", (byte)ConnectionType);
            info.AddValue("storepw", StorePassword);
            if (ConnectionType == ConnectionType.Password)
            {
                if (StorePassword)
                {
                    info.AddValue("p", Utilities.ProtectString(Password));
                }
            }
            else
            {
                if (StorePassword)
                {
                    info.AddValue("p", Utilities.ProtectString(Password));
                }
                info.AddValue("k", PrivateKey);
            }
        }

        #endregion

      
    }
}