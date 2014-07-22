using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Caching;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using DokanNet;

using FileAccess = DokanNet.FileAccess;


namespace Sshfs
{
    internal sealed class VirtualFilesystem : IDokanOperations
    {

        #region Fields

        private readonly string _volumeLabel;
        private bool _debugMode = false;

        private readonly List<SftpDrive> _subsytems = new List<SftpDrive>();

        #endregion

        #region Constructors

        public VirtualFilesystem(string label = null)
        {
            _volumeLabel = label;
        }

        #endregion

        #region  Methods

        internal void AddSubFS(SftpDrive sftpDrive)
        {
            _subsytems.Add(sftpDrive);
        }

        internal void RemoveSubFS(SftpDrive sftpDrive)
        {
            _subsytems.Remove(sftpDrive);
        }


        [Conditional("DEBUG")]
        private void Log(string format, params object[] arg)
        {
            if (_debugMode)
            {
                Console.WriteLine(format, arg);
            }
            Debug.Write(DateTime.Now.ToLongTimeString() + " ");
            Debug.WriteLine(format, arg);
        }

        #endregion

        #region DokanOperations

        DokanError IDokanOperations.CreateFile(string fileName, FileAccess access, FileShare share,
                                               FileMode mode, FileOptions options,
                                               FileAttributes attributes, DokanFileInfo info)
        {
            if (fileName.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("autorun.inf", StringComparison.OrdinalIgnoreCase)) //....
            {
                return DokanError.ErrorFileNotFound;
            }
            return DokanError.ErrorAccessDenied;
        }

        private SftpDrive GetDriveByMountPoint(string fileName, out string subfspath)
        {
            if (fileName.Length>1)
            {
                string path = fileName.Substring(1);
                foreach (SftpDrive drive in this._subsytems)
                {
                    if (drive.MountPoint.Length > 0)
                    {
                        if (path == drive.MountPoint) // path contains leading \
                        {
                            subfspath = path.Substring(drive.MountPoint.Length + 1);
                            return drive;
                        }
                    }
                }
            }
            subfspath = fileName;
            return null;
        }

        private IDokanOperations GetSubSystemOperations(SftpDrive drive)
        {
            if (drive == null)
                return null;

            if (drive.Status != DriveStatus.Mounted)
            {
                drive.Mount();
            }
            if (drive == null)
                return null;

            return ((IDokanOperations)drive._filesystem);
        }

        DokanError IDokanOperations.OpenDirectory(string fileName, DokanFileInfo info)
        {
            Log("VFS OpenDir:{0}", fileName);

            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).OpenDirectory(fileName, info);

            info.IsDirectory = true;

            if (fileName.Length == 1) //root dir
                return DokanError.ErrorSuccess;

            string path = fileName.Substring(1);//cut leading \

            foreach (SftpDrive subdrive in _subsytems)
            {
                string mp = subdrive.MountPoint; //  mp1 || mp1\mp2 ...
                if (path == mp)
                    return DokanError.ErrorSuccess;

                if (mp.IndexOf(path + '\\') == 0) //path is part of mount point
                    return DokanError.ErrorSuccess;
            }

            return DokanError.ErrorPathNotFound;
        }

        DokanError IDokanOperations.CreateDirectory(string fileName, DokanFileInfo info)
        {
            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.Cleanup(string fileName, DokanFileInfo info)
        {
            Log("Cleanup:{0},Delete:{1}", info.Context, info.DeleteOnClose);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.CloseFile(string fileName, DokanFileInfo info)
        {
            Log("Close:{0}", info.Context);

            return DokanError.ErrorSuccess;
        }


        DokanError IDokanOperations.ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset,
                                             DokanFileInfo info)
        {
            bytesRead = 0;
            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset,
                                              DokanFileInfo info)
        {
            bytesWritten = 0;
            return DokanError.ErrorAccessDenied;
        }


        DokanError IDokanOperations.FlushFileBuffers(string fileName, DokanFileInfo info)
        {
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetFileInformation(string fileName, out FileInformation fileInfo,
                                                       DokanFileInfo info)
        {
            Log("GetInfo:{0}:{1}", fileName, info.Context);
            fileInfo = new FileInformation();
            fileInfo.FileName = fileName;
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.FindFiles(string fileName, out IList<FileInformation> files, DokanFileInfo info)
        {
            Log("VFS FindFiles:{0}", fileName);

            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).FindFiles(fileName, out files, info);
            

            files = new List<FileInformation>();

            string path = fileName.Substring(1);//cut leading \
            foreach(SftpDrive subdrive in _subsytems)
            {
                string mp = subdrive.MountPoint; //  mp1 || mp1\mp2 ...

                if (path.Length > 0) //not root dir
                {
                    if (path == mp) //this shoud not happend, because is managed by drive
                    {
                        Log("Error, mountpoint not in drives?");
                        break;
                    }

                    if (mp.IndexOf(path + '\\') == 0) //path is part of mount point =>implies=> length of path>mp
                    {
                        mp = mp.Substring(path.Length + 1); //cut the path
                    }
                    else
                    {
                        continue;
                    }
                }

                int cuttmp = mp.IndexOf('\\');
                if (cuttmp>0) // have submountpoint like  mp1\mp2 
                {
                    mp = mp.Substring(0, cuttmp);
                }

                if (!files.Select(file => file.FileName).Contains(mp))
                {
                    FileInformation fi = new FileInformation();
                    fi.FileName = mp;
                    fi.Attributes = FileAttributes.Directory | FileAttributes.Offline;
                    fi.CreationTime = DateTime.Now;
                    fi.LastWriteTime = DateTime.Now;
                    fi.LastAccessTime = DateTime.Now;
                    files.Add(fi);
                }
            }
           
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
        {
            Log("TrySetAttributes:{0}\n{1};", fileName, attributes);

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.SetFileTime(string filename, DateTime? creationTime, DateTime? lastAccessTime,
                                                DateTime? lastWriteTime, DokanFileInfo info)
        {

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.DeleteFile(string fileName, DokanFileInfo info)
        {
            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.DeleteDirectory(string fileName, DokanFileInfo info)
        {
            Log("DeleteDirectory:{0}", fileName);
            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
        {
            Log("MoveFile |Name:{0} ,NewName:{3},Reaplace{4},IsDirectory:{1} ,Context:{2}",
                oldName, info.IsDirectory,
                info.Context, newName, replace);
            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.SetEndOfFile(string fileName, long length, DokanFileInfo info)
        {
            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.SetAllocationSize(string fileName, long length, DokanFileInfo info)
        {
            Log("SetSize");
            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.LockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.UnlockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.GetDiskFreeSpace(out long free, out long total,
                                                     out long used, DokanFileInfo info)
        {
            Log("GetDiskFreeSpace");

            free = 0;
            total = 1024;
            used = 4;
            free = total - used;

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features,
                                                         out string filesystemName, DokanFileInfo info)
        {
            Log("GetVolumeInformation");

            volumeLabel = _volumeLabel;

            filesystemName = "SSHVFS";

            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch |
                       FileSystemFeatures.SupportsRemoteStorage | FileSystemFeatures.UnicodeOnDisk;
            //FileSystemFeatures.PersistentAcls


            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetFileSecurity(string filename, out FileSystemSecurity security,
                                                    AccessControlSections sections, DokanFileInfo info)
        {
            Log("GetSecurrityInfo:{0}:{1}", filename, sections);

            security = null;

            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.SetFileSecurity(string filename, FileSystemSecurity security,
                                                    AccessControlSections sections, DokanFileInfo info)
        {
            Log("TrySetSecurity:{0}", filename);

            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.Unmount(DokanFileInfo info)
        {
            Log("UNMOUNT");

            // Disconnect();
            return DokanError.ErrorSuccess;
        }

        #endregion

        /*
        #region Events

        public event EventHandler<EventArgs> Disconnected
        {
            add { Session.Disconnected += value; }
            remove { Session.Disconnected -= value; }
        }

        #endregion*/
    }
}