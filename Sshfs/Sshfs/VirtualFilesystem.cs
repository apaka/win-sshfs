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

            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).CreateFile(fileName, access, share, mode, options, attributes, info);

            //Todo: check against mountpoints
            info.IsDirectory = true;
            info.Context = this;

            return DokanError.ErrorSuccess;
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
                        if (path.IndexOf(drive.MountPoint)==0)
                        {
                            subfspath = path.Substring(drive.MountPoint.Length);
                            if (subfspath == "") 
                                subfspath = "\\";
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

            if ((drive.Status != DriveStatus.Mounted)&&(drive.Status != DriveStatus.Mounting))
            {
                try
                {
                    drive.Mount();
                }
                catch (Exception e)
                {
                    Log("VFS: Mount error: {0}", e.Message);

                    //maybe failed because of letter blocked:
                    char l = drive.Letter;
                    drive.Letter = ' ';
                    try
                    {
                        drive.Mount();
                        drive.Letter = l;
                    }
                    catch
                    {
                        //connection error
                        drive.Letter = l;
                        Log("VFS: Mount error: {0}", e.Message);
                        return null;
                    }
                }
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
            {
                IDokanOperations ops = GetSubSystemOperations(drive);
                if (ops == null)
                {
                    return DokanError.ErrorError;
                }
                return ops.OpenDirectory(fileName, info);
            }

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
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).CreateDirectory(fileName, info);

            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.Cleanup(string fileName, DokanFileInfo info)
        {
            Log("VFS Cleanup:{0},Delete:{1}", info.Context, info.DeleteOnClose);

            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).Cleanup(fileName, info);

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.CloseFile(string fileName, DokanFileInfo info)
        {
            Log("VFS Close:{0}", info.Context);

            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).CloseFile(fileName, info);

            return DokanError.ErrorSuccess;
        }


        DokanError IDokanOperations.ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset,
                                             DokanFileInfo info)
        {
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).ReadFile(fileName, buffer, out bytesRead, offset, info);

            bytesRead = 0;
            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset,
                                              DokanFileInfo info)
        {
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).WriteFile(fileName, buffer, out bytesWritten, offset, info);

            bytesWritten = 0;
            return DokanError.ErrorAccessDenied;
        }


        DokanError IDokanOperations.FlushFileBuffers(string fileName, DokanFileInfo info)
        {
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).FlushFileBuffers(fileName, info);

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetFileInformation(string fileName, out FileInformation fileInfo,
                                                       DokanFileInfo info)
        {
            Log("VFS GetInfo:{0}:{1}", fileName, info.Context);
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).GetFileInformation(fileName, out fileInfo, info);


            fileInfo = new FileInformation();
            fileInfo.FileName = fileName;
            fileInfo.CreationTime = fileInfo.LastAccessTime = fileInfo.LastWriteTime = DateTime.Now;
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

        DokanError IDokanOperations.FindFilesWithPattern(string fileName,string pattern, out IList<FileInformation> files, DokanFileInfo info)
        {
            Log("VFS FindFiles:{0}", fileName);

            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).FindFilesWithPattern(fileName, pattern, out files, info);


            files = new List<FileInformation>();

            string path = fileName.Substring(1);//cut leading \
            foreach (SftpDrive subdrive in _subsytems)
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
                if (cuttmp > 0) // have submountpoint like  mp1\mp2 
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
            Log("VFS TrySetAttributes:{0}\n{1};", fileName, attributes);
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).SetFileAttributes(fileName, attributes, info);

            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime,
                                                DateTime? lastWriteTime, DokanFileInfo info)
        {
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).SetFileTime(fileName, creationTime, lastAccessTime, lastWriteTime, info);

            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.DeleteFile(string fileName, DokanFileInfo info)
        {
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).DeleteFile(fileName, info);

            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.DeleteDirectory(string fileName, DokanFileInfo info)
        {
            Log("VFS DeleteDirectory:{0}", fileName);
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).DeleteDirectory(fileName, info);

            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
        {
            Log("VFS MoveFile |Name:{0} ,NewName:{3},Reaplace{4},IsDirectory:{1} ,Context:{2}",
                oldName, info.IsDirectory,
                info.Context, newName, replace);
            //todo: check newname?
            SftpDrive drive = this.GetDriveByMountPoint(oldName, out oldName);
            if (drive != null)
            {
                SftpDrive drive2 = this.GetDriveByMountPoint(newName, out newName);
                if (drive2 != drive)
                {
                    return DokanError.ErrorNotImplemented;
                }

                return GetSubSystemOperations(drive).MoveFile(oldName, newName, replace, info);
            }

            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.SetEndOfFile(string fileName, long length, DokanFileInfo info)
        {
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).SetEndOfFile(fileName,length, info);
            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.SetAllocationSize(string fileName, long length, DokanFileInfo info)
        {
            Log("VFS SetSize");
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).SetAllocationSize(fileName, length, info);

            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.LockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).LockFile(fileName, offset, length, info);

            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.UnlockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).UnlockFile(fileName, offset, length, info);
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

        DokanError IDokanOperations.GetFileSecurity(string fileName, out FileSystemSecurity security,
                                                    AccessControlSections sections, DokanFileInfo info)
        {
            Log("VFS GetSecurrityInfo:{0}:{1}", fileName, sections);

            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).GetFileSecurity(fileName, out security, sections, info);

            security = null;
            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.SetFileSecurity(string fileName, FileSystemSecurity security,
                                                    AccessControlSections sections, DokanFileInfo info)
        {
            Log("VFS TrySetSecurity:{0}", fileName);
            SftpDrive drive = this.GetDriveByMountPoint(fileName, out fileName);
            if (drive != null)
                return GetSubSystemOperations(drive).SetFileSecurity(fileName, security, sections, info);

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