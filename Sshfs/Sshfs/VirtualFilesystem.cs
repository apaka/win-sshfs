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

        private readonly Dictionary<string, SftpFilesystem> _subsytems = new Dictionary<string, SftpFilesystem>();

        #endregion

        #region Constructors

        public VirtualFilesystem(string label = null)
        {
            _volumeLabel = label;
        }

        #endregion

        #region  Methods

        internal void AddSubFS(string path, SftpFilesystem fileSystem)
        {
            _subsytems.Add(path, fileSystem);
        }

        internal void RemoveSubFS(string path, SftpFilesystem fileSystem)
        {
            _subsytems.Remove(path);
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

        private string GetSubSystemFileName(string fileName, out string subfs)
        {
            string[] parts = fileName.Split(new char[] { '\\'}, 3);
            if (parts.Count() > 1)
            {
                if (parts[0] != "")
                {
                    subfs = null;
                    return null;
                }
                subfs = parts[1];

                if (!this._subsytems.ContainsKey(subfs))
                {
                    subfs = null;
                    return "\\";
                }

                if (parts.Count()==3){
                    return "\\"+parts[2];
                }
                else
                {
                    return "\\";
                }
            }

            subfs = null;
            return fileName;
        }

        DokanError IDokanOperations.OpenDirectory(string fileName, DokanFileInfo info)
        {
            Log("VFS OpenDir:{0}", fileName);
            string subfs;
            string subfilename = GetSubSystemFileName(fileName, out subfs);
            if (subfs != null)
                return ((IDokanOperations)this._subsytems[subfs]).OpenDirectory(subfilename, info);

            if (fileName == "\\")
            {
                info.IsDirectory = true;
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
            Log("FindFiles:{0}", fileName);

            string subfs;
            string subfilename = GetSubSystemFileName(fileName, out subfs);
            if (subfs != null)
            {
                return ((IDokanOperations)this._subsytems[subfs]).FindFiles(subfilename, out files, info);
            }
            
            files = new List<FileInformation>();

            foreach (string dir in _subsytems.Keys)
            {
                //SftpFilesystem fs = _subsytems[path];
                FileInformation fi = new FileInformation();
                fi.FileName = dir;
                fi.Attributes = FileAttributes.Directory | FileAttributes.Offline;
                fi.CreationTime = DateTime.Now;
                fi.LastWriteTime = DateTime.Now;
                fi.LastAccessTime = DateTime.Now;
                files.Add(fi);
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