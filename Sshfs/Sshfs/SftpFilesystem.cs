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
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using FileAccess = DokanNet.FileAccess;

namespace Sshfs
{
    internal sealed class SftpFilesystem : BaseClient, IDokanOperations
    {
        
        #region Constants

        // ReSharper disable InconsistentNaming
        //  private static readonly string[] _filter = {
        //       "desktop.ini", "Desktop.ini", "autorun.inf",
        //    "AutoRun.inf", //"Thumbs.db",
        // };

        // private static readonly Regex _dfregex = new Regex(@"^[a-z0-9/]+\s+(?<blocks>[0-9]+)K\s+(?<used>[0-9]+)K"
        // , RegexOptions.Compiled);

        // ReSharper restore InconsistentNaming 

        #endregion

        #region Fields

        private readonly MemoryCache _cache = MemoryCache.Default;

        private SftpSession _sftpSession;
        private readonly TimeSpan _operationTimeout = TimeSpan.FromSeconds(30);//new TimeSpan(0, 0, 0, 0, -1);
        private string _rootpath;

        private readonly bool _useOfflineAttribute;
        private readonly bool _debugMode;


        private int _userId;
        private HashSet<int> _userGroups;

        private readonly int _attributeCacheTimeout;
        private readonly int _directoryCacheTimeout;

        private bool _supportsPosixRename;
        private bool _supportsStatVfs;

        private readonly string _volumeLabel;

        #endregion

        #region Constructors

        public SftpFilesystem(ConnectionInfo connectionInfo, string rootpath, string label = null,
                              bool useOfflineAttribute = false,
                              bool debugMode = false, int attributeCacheTimeout = 5, int directoryCacheTimeout = 60)
            : base(connectionInfo)
        {
            _rootpath = rootpath;
            _directoryCacheTimeout = directoryCacheTimeout;
            _attributeCacheTimeout = attributeCacheTimeout;
            _useOfflineAttribute = useOfflineAttribute;
            _debugMode = debugMode;
            _volumeLabel = label ?? String.Format("{0} on '{1}'", ConnectionInfo.Username, ConnectionInfo.Host);
        }

        #endregion

        #region Method overrides

        protected override void OnConnected()
        {
            base.OnConnected();

            _sftpSession = new SftpSession(Session, _operationTimeout);


            _sftpSession.Connect();


            _userId = GetUserId();
            if (_userId != -1)
                _userGroups = new HashSet<int>(GetUserGroupsIds());


            if (String.IsNullOrWhiteSpace(_rootpath))
            {
                _rootpath = _sftpSession.RequestRealPath(".").First().Key;
            }

            _supportsPosixRename =
                _sftpSession.Extentions.Contains(new KeyValuePair<string, string>("posix-rename@openssh.com", "1"));
            _supportsStatVfs =
                _sftpSession.Extentions.Contains(new KeyValuePair<string, string>("statvfs@openssh.com", "2"));
            // KeepAliveInterval=TimeSpan.FromSeconds(5);

           //  Session.Disconnected+= (sender, args) => Debugger.Break();
        }


        protected override void Dispose(bool disposing)
        {
            if (_sftpSession != null)
            {
                _sftpSession.Dispose();
                _sftpSession = null;
            }
            base.Dispose(disposing);
        }

        #endregion

        #region  Methods

        private string GetUnixPath(string path)
        {
            // return String.Concat(_rootpath, path.Replace('\\', '/'));
            return String.Format("{0}{1}", _rootpath, path.Replace('\\', '/'));
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

        private IEnumerable<int> GetUserGroupsIds()
        {
            using (var cmd = new SshCommand(Session, "id -G ", Encoding.ASCII))
            {
                cmd.Execute();
                return cmd.Result.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries).Select(Int32.Parse);
            }
        }

        private int GetUserId()
        {
            using (var cmd = new SshCommand(Session, "id -u ", Encoding.ASCII))
                // Thease commands seems to be POSIX so the only problem would be Windows enviroment
            {
                cmd.Execute();
                return cmd.ExitStatus == 0 ? Int32.Parse(cmd.Result) : -1;
            }
        }

        private bool UserCanRead(SftpFileAttributes attributes)
        {
            return _userId == -1 || (attributes.OwnerCanRead && attributes.UserId == _userId ||
                                     (attributes.GroupCanRead && _userGroups.Contains(attributes.GroupId) ||
                                      attributes.OthersCanRead));
        }

        private bool UserCanWrite(SftpFileAttributes attributes)
        {
            return _userId == -1 || (attributes.OwnerCanWrite && attributes.UserId == _userId ||
                                     (attributes.GroupCanWrite && _userGroups.Contains(attributes.GroupId) ||
                                      attributes.OthersCanWrite));
        }

        private bool UserCanExecute(SftpFileAttributes attributes)
        {
            return _userId == -1 || (attributes.OwnerCanExecute && attributes.UserId == _userId ||
                                     (attributes.GroupCanExecute && _userGroups.Contains(attributes.GroupId) ||
                                      attributes.OthersCanExecute));
        }

        private SftpFileAttributes GetAttributes(string path)
        {
            var sftpLStatAttributes = _sftpSession.RequestLStat(path, true);
            if (sftpLStatAttributes == null || !sftpLStatAttributes.IsSymbolicLink)
            {
                return sftpLStatAttributes;
            }
            var sftpStatAttributes = _sftpSession.RequestStat(path, true);
            return sftpStatAttributes ?? sftpLStatAttributes;
        }

        private void InvalidateParentCache(string fileName)
        {
            int index = fileName.LastIndexOf('\\');
            _cache.Remove(index != 0 ? fileName.Substring(0, index) : "\\");
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

            string path = GetUnixPath(fileName);
            //  var  sftpFileAttributes = GetAttributes(path);
            var sftpFileAttributes = _cache.Get(path) as SftpFileAttributes;

            if (sftpFileAttributes == null)
            {
                Log("cache miss");
                
                sftpFileAttributes = GetAttributes(path);
                if (sftpFileAttributes != null)
                    _cache.Add(path, sftpFileAttributes, DateTimeOffset.UtcNow.AddSeconds(_attributeCacheTimeout));
            }
           
         


            Log("Open| Name:{0},\n Mode:{1},\n Share{2},\n Disp:{3},\n Flags{4},\n Attr:{5}\n", fileName, access,
                share, mode, options, attributes);

            switch (mode)
            {
                case FileMode.Open:
                    if (sftpFileAttributes != null)
                    {
                        if (((uint) access & 0xe0000027) == 0 || sftpFileAttributes.IsDirectory)
                            //check if only wants to read attributes,security info or open directory
                        {
                            Log("JustInfo:{0},{1}", fileName, sftpFileAttributes.IsDirectory);
                            info.IsDirectory = sftpFileAttributes.IsDirectory;
                            info.Context = new SftpContext(sftpFileAttributes);
                            return DokanError.ErrorSuccess;
                        }
                    }
                    else return DokanError.ErrorFileNotFound;
                    break;
                case FileMode.CreateNew:
                    if (sftpFileAttributes != null)
                        return DokanError.ErrorAlreadyExists;

                    InvalidateParentCache(fileName); // cache invalidate
                    break;
                case FileMode.Truncate:
                    if (sftpFileAttributes == null)
                        return DokanError.ErrorFileNotFound;
                    InvalidateParentCache(fileName);
                    _cache.Remove(path);
                    break;
                default:

                    InvalidateParentCache(fileName);
                    break;
            }
            Log("NotJustInfo:{0}-{1}", info.Context, mode);
            try
            {
                info.Context = new SftpContext(_sftpSession, path, mode,
                                               ((ulong) access & 0x40010006) == 0
                                                   ? System.IO.FileAccess.Read
                                                   : System.IO.FileAccess.ReadWrite, sftpFileAttributes);
            }
            catch (SshException) // Don't have access rights or try to read broken symlink
            {
              
                return DokanError.ErrorAccessDenied;
            }


            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.OpenDirectory(string fileName, DokanFileInfo info)
        {
            Log("OpenDir:{0}", fileName);




            string path = GetUnixPath(fileName);
            // var sftpFileAttributes = GetAttributes(GetUnixPath(fileName));
            var sftpFileAttributes = _cache.Get(path) as SftpFileAttributes;

            if (sftpFileAttributes == null)
            {
                Log("cache miss");
               
                sftpFileAttributes = GetAttributes(path);
                if (sftpFileAttributes != null)
                    _cache.Add(path, sftpFileAttributes, DateTimeOffset.UtcNow.AddSeconds(_attributeCacheTimeout));
            }
            
         


            if (sftpFileAttributes != null && sftpFileAttributes.IsDirectory)
            {
                if (!UserCanExecute(sftpFileAttributes) || !UserCanRead(sftpFileAttributes))
                {
                    return DokanError.ErrorAccessDenied;
                }


                info.IsDirectory = true;
                info.Context = new SftpContext(sftpFileAttributes);

                var dircahe = _cache.Get(fileName) as Tuple<DateTime, IList<FileInformation>>;
                if (dircahe != null && dircahe.Item1 != sftpFileAttributes.LastWriteTime)
                {
                    _cache.Remove(fileName);
                }
                return DokanError.ErrorSuccess;
            }
            return DokanError.ErrorPathNotFound;
        }

        DokanError IDokanOperations.CreateDirectory(string fileName, DokanFileInfo info)
        {
            Log("CreateDir:{0}", fileName);


            try
            {
                _sftpSession.RequestMkDir(GetUnixPath(fileName));
                InvalidateParentCache(fileName); //invalidate dircahe of the parent
            }
            catch (SftpPermissionDeniedException)
            {
                return DokanError.ErrorAccessDenied;
            }
            catch (SshException) // operation should fail with generic error if file already exists
            {
                return DokanError.ErrorAlreadyExists;
            }

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.Cleanup(string fileName, DokanFileInfo info)
        {
            Log("Cleanup:{0},Delete:{1}", info.Context,info.DeleteOnClose);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (info.Context != null)
            {
                (info.Context as SftpContext).Release();

                info.Context = null;
            }

            if (info.DeleteOnClose)
            {
                string path = GetUnixPath(fileName);
                if (info.IsDirectory)
                {
                    try
                    {
                        _sftpSession.RequestRmDir(path);
                    }
                    catch (SftpPathNotFoundException) //in case we are dealing with simbolic link
                    {
                        _sftpSession.RequestRemove(path);
                    }
                }
                else
                {
                    _sftpSession.RequestRemove(path);
                }
                InvalidateParentCache(fileName);
                _cache.Remove(path);
            }

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
            Log("ReadFile:{0}:{1}|lenght:[{2}]|offset:[{3}]", fileName,
                info.Context , buffer.Length, offset);

            if (info.Context == null)
            {
                //called when file is read as memory memory mapeded file usualy notepad and stuff
                var handle = _sftpSession.RequestOpen(GetUnixPath(fileName), Flags.Read);
                var data = _sftpSession.RequestRead(handle, (ulong) offset, (uint) buffer.Length);
                _sftpSession.RequestClose(handle);
                Buffer.BlockCopy(data, 0, buffer, 0, data.Length);
                bytesRead = data.Length;
            }
            else
            {
                // var watch = Stopwatch.StartNew();
                var stream = (info.Context as SftpContext).Stream;
                lock (stream)
                {
                    stream.Position = offset;
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
                //  watch.Stop();
                // Log("{0}",watch.ElapsedMilliseconds);
            }
            Log("END READ:{0},{1}",offset,info.Context);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset,
                                              DokanFileInfo info)
        {
           

                Log("WriteFile:{0}:{1}|lenght:[{2}]|offset:[{3}]", fileName,
                    info.Context, buffer.Length, offset);
               
               
                if (info.Context == null) // who would guess
                {
                    var handle = _sftpSession.RequestOpen(GetUnixPath(fileName), Flags.Write);
                 //   using (var wait = new AutoResetEvent(false))
                    {
                        _sftpSession.RequestWrite(handle, (ulong) offset, buffer/*, wait*/);
                    }
                    _sftpSession.RequestClose(handle);
                    bytesWritten = buffer.Length;
                }
                else
                {
                    var stream = (info.Context as SftpContext).Stream;
                    lock (stream)
                    {
                        stream.Position = offset;
                        stream.Write(buffer, 0, buffer.Length);
                    }
                    //    stream.Flush();
                    bytesWritten = buffer.Length;
                    // TODO there are still some apps that don't check disk free space before write
                }
              
               // Log("END WRITE:{0},{1},{2}", offset,info.Context,watch.ElapsedMilliseconds);
                return DokanError.ErrorSuccess;
            }
        

        DokanError IDokanOperations.FlushFileBuffers(string fileName, DokanFileInfo info)
        {
            Log("FLUSH:{0}", fileName);

            (info.Context as SftpContext).Stream.Flush(); //I newer saw it get called ,but ..

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetFileInformation(string fileName, out FileInformation fileInfo,
                                                       DokanFileInfo info)
        {
            Log("GetInfo:{0}:{1}", fileName,info.Context);

            var context = info.Context as SftpContext;

            SftpFileAttributes sftpFileAttributes;
            if (context != null)
            {
                sftpFileAttributes = context.Attributes;
            }
            else
            {
                string path = GetUnixPath(fileName);
                sftpFileAttributes = _cache.Get(path) as SftpFileAttributes;

                if (sftpFileAttributes == null)
                {
                    sftpFileAttributes = GetAttributes(path);
                    if (sftpFileAttributes != null)
                        _cache.Add(path, sftpFileAttributes, DateTimeOffset.UtcNow.AddSeconds(_attributeCacheTimeout));
                }
            }


            fileInfo = new FileInformation
                           {
                               Attributes =
                                   FileAttributes.NotContentIndexed,
                               FileName = String.Empty,
                               // GetInfo info doesn't use it maybe for sorting .
                               CreationTime = sftpFileAttributes.LastWriteTime,
                               LastAccessTime = sftpFileAttributes.LastAccessTime,
                               LastWriteTime = sftpFileAttributes.LastWriteTime,
                               Length = sftpFileAttributes.Size
                           };
            if (sftpFileAttributes.IsDirectory)
            {
                fileInfo.Attributes |= FileAttributes.Directory;
                fileInfo.Length = 0; // Windows directories use length of 0 
            }
            else
            {
                fileInfo.Attributes |= FileAttributes.Normal;
            }
            if (fileName.Length != 1 && fileName[fileName.LastIndexOf('\\') + 1] == '.')
                //aditional check if filename isn't \\
            {
                fileInfo.Attributes |= FileAttributes.Hidden;
            }

            if (!UserCanWrite(sftpFileAttributes))
            {
                fileInfo.Attributes |= FileAttributes.ReadOnly;
            }
            if (_useOfflineAttribute)
            {
                fileInfo.Attributes |= FileAttributes.Offline;
            }
            //  Console.WriteLine(sftpattributes.UserId + "|" + sftpattributes.GroupId + "L" +
            //  sftpattributes.OthersCanExecute + "K" + sftpattributes.OwnerCanExecute);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.FindFiles(string fileName, out IList<FileInformation> files, DokanFileInfo info)
        {
            Log("FindFiles:{0}", fileName);

            var dircache = _cache.Get(fileName) as Tuple<DateTime, IList<FileInformation>>;
            if (dircache != null)
            {
                files = (dircache).Item2;
                Log("CacheHit:{0}", fileName);
                return DokanError.ErrorSuccess;
            }


            byte[] handle;
            try
            {
                handle = _sftpSession.RequestOpenDir(GetUnixPath(fileName));
            }
            catch (SftpPermissionDeniedException)
            {
                files = null;
                return DokanError.ErrorAccessDenied;
            }


            files = new List<FileInformation>();
            for (var sftpFiles = _sftpSession.RequestReadDir(handle);
                 sftpFiles != null;
                 sftpFiles = _sftpSession.RequestReadDir(handle))
            {

              


                (files as List<FileInformation>).AddRange(sftpFiles.Select(
                    file =>
                        {
                            var sftpFileAttributes = file.Value;
                            if (sftpFileAttributes.IsSymbolicLink)
                            {
                                sftpFileAttributes = _sftpSession.RequestStat(
                                    GetUnixPath(String.Format("{0}{1}", fileName, file.Key)), true) ??
                                                     file.Value;
                            }


                            var fileInformation = new FileInformation
                                                      {
                                                          Attributes =
                                                              FileAttributes.NotContentIndexed,
                                                          CreationTime
                                                              =
                                                              sftpFileAttributes
                                                              .
                                                              LastWriteTime,
                                                          FileName
                                                              =
                                                              file.Key
                                                          ,
                                                          LastAccessTime
                                                              =
                                                              sftpFileAttributes
                                                              .
                                                              LastAccessTime,
                                                          LastWriteTime
                                                              =
                                                              sftpFileAttributes
                                                              .
                                                              LastWriteTime,
                                                          Length
                                                              =
                                                              sftpFileAttributes
                                                              .
                                                              Size
                                                      };
                            if (sftpFileAttributes.IsDirectory)
                            {
                                fileInformation.Attributes
                                    |=
                                    FileAttributes.
                                        Directory;
                                fileInformation.Length = 0;
                            }
                            else
                            {
                                fileInformation.Attributes |= FileAttributes.Normal;
                            }
                            if (file.Key[0] == '.')
                            {
                                fileInformation.Attributes
                                    |=
                                    FileAttributes.
                                        Hidden;
                            }

                            if (
                                !UserCanWrite(
                                    sftpFileAttributes))
                            {
                                fileInformation.Attributes
                                    |=
                                    FileAttributes.
                                        ReadOnly;
                            }
                            if (_useOfflineAttribute)
                            {
                                fileInformation.Attributes
                                    |=
                                    FileAttributes.
                                        Offline;
                            }
                            return fileInformation;
                        }));



               int timeout = Math.Max(_attributeCacheTimeout + 2, _attributeCacheTimeout +  sftpFiles.Length / 10);

               foreach (
                    var file in
                        sftpFiles.Where(
                            pair => !pair.Value.IsSymbolicLink))
                {
                    _cache.Set(GetUnixPath(String.Format("{0}{1}", fileName, file.Key)), file.Value,
                               DateTimeOffset.UtcNow.AddSeconds(timeout));
                }
            }


            _sftpSession.RequestClose(handle);


            _cache.Add(fileName, new Tuple<DateTime, IList<FileInformation>>(
                                     (info.Context as SftpContext).Attributes.LastWriteTime,
                                     files),
                       DateTimeOffset.UtcNow.AddSeconds(Math.Max(_attributeCacheTimeout,
                                                                 Math.Min(files.Count, _directoryCacheTimeout))));

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
            Log("TrySetFileTime:{0}\n|c:{1}\n|a:{2}\n|w:{3}", filename, creationTime, lastAccessTime,
                lastWriteTime);
            var sftpattributes = (info.Context as SftpContext).Attributes;

            var mtime = lastWriteTime ?? (creationTime ?? sftpattributes.LastWriteTime);

            var atime = lastAccessTime ?? sftpattributes.LastAccessTime;

            _sftpSession.RequestSetStat(GetUnixPath(filename), new SftpFileAttributes(atime, mtime, -1, -1, -1, 0, null));
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.DeleteFile(string fileName, DokanFileInfo info)
        {
            Log("DeleteFile:{0}", fileName);

            string parentPath = GetUnixPath(fileName.Substring(0, fileName.LastIndexOf('\\')));

            var sftpFileAttributes = _cache.Get(parentPath) as SftpFileAttributes;

            if (sftpFileAttributes == null)
            {
                sftpFileAttributes = GetAttributes(parentPath);
                if (sftpFileAttributes != null)
                    _cache.Add(parentPath, sftpFileAttributes, DateTimeOffset.UtcNow.AddSeconds(_attributeCacheTimeout));
            }


            return
                UserCanWrite(
                    sftpFileAttributes)
                    ? DokanError.ErrorSuccess
                    : DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.DeleteDirectory(string fileName, DokanFileInfo info)
        {
            Log("DeleteDirectory:{0}", fileName);


            string parentPath = GetUnixPath(fileName.Substring(0, fileName.LastIndexOf('\\')));

            var sftpFileAttributes = _cache.Get(parentPath) as SftpFileAttributes;

            if (sftpFileAttributes == null)
            {
                sftpFileAttributes = GetAttributes(parentPath);
                if (sftpFileAttributes != null)
                    _cache.Add(parentPath, sftpFileAttributes, DateTimeOffset.UtcNow.AddSeconds(_attributeCacheTimeout));
            }


            if (
                !UserCanWrite(
                    sftpFileAttributes))
            {
                return DokanError.ErrorAccessDenied;
            }
            var dircache = _cache.Get(fileName) as Tuple<DateTime, IList<FileInformation>>;
            if (dircache != null)
            {
                Log("DelateCacheHit:{0}", fileName);


                return dircache.Item2.Count == 0 || dircache.Item2.All(i => i.FileName == "." || i.FileName == "..")
                           ? DokanError.ErrorSuccess
                           : DokanError.ErrorDirNotEmpty;
            }

            var handle = _sftpSession.RequestOpenDir(GetUnixPath(fileName), true);

            if (handle == null)
                return DokanError.ErrorAccessDenied;

            var dir = _sftpSession.RequestReadDir(handle);
            _sftpSession.RequestClose(handle);
            // usualy there are two entries . and ..

            return dir.Length == 0 || dir.All(i => i.Key == "." || i.Key == "..")
                       ? DokanError.ErrorSuccess
                       : DokanError.ErrorDirNotEmpty;
        }

        DokanError IDokanOperations.MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
        {
            Log("MoveFile |Name:{0} ,NewName:{3},Reaplace{4},IsDirectory:{1} ,Context:{2}",
                oldName, info.IsDirectory,
                info.Context, newName, replace);
            string oldpath = GetUnixPath(oldName);
            /*  if (_generalSftpSession.RequestLStat(oldpath, true) == null)
                return DokanError.ErrorPathNotFound;
            if (oldName.Equals(newName))
                return DokanError.ErrorSuccess;*/
            string newpath = GetUnixPath(newName);

            if (_sftpSession.RequestLStat(newpath, true) == null)
            {
                (info.Context as SftpContext).Release();

                info.Context = null;
                try
                {
                    _sftpSession.RequestRename(oldpath, newpath);
                    InvalidateParentCache(oldName);
                    InvalidateParentCache(newName);
                    _cache.Remove(oldpath);
                }
                catch (SftpPermissionDeniedException)
                {
                    return DokanError.ErrorAccessDenied;
                }

                return DokanError.ErrorSuccess;
            }
            else if (replace)
            {
                (info.Context as SftpContext).Release();

                info.Context = null;


                try
                {
                    if (_supportsPosixRename)
                    {
                        _sftpSession.RequestPosixRename(oldpath, newpath);
                    }
                    else
                    {
                        if (!info.IsDirectory)
                            _sftpSession.RequestRemove(newpath);
                        _sftpSession.RequestRename(oldpath, newpath);
                    }


                    InvalidateParentCache(oldName);
                    InvalidateParentCache(newName);
                    _cache.Remove(oldpath);
                }
                catch (SftpPermissionDeniedException)
                {
                    return DokanError.ErrorAccessDenied;
                } // not tested on sftp3
                return DokanError.ErrorSuccess;
            }
            return DokanError.ErrorAlreadyExists;
        }

        DokanError IDokanOperations.SetEndOfFile(string fileName, long length, DokanFileInfo info)
        {
            Log("SetEnd");
            (info.Context as SftpContext).Stream.SetLength(length);
            InvalidateParentCache(fileName);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.SetAllocationSize(string fileName, long length, DokanFileInfo info)
        {
            Log("SetSize");
            (info.Context as SftpContext).Stream.SetLength(length);
            InvalidateParentCache(fileName);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.LockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.UnlockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetDiskFreeSpace(out long free, out long total,
                                                     out long used, DokanFileInfo info)
        {

            
            Log("GetDiskFreeSpace");

            var diskSpaceInfo = _cache.Get(_volumeLabel) as Tuple<long, long, long>;

            if (diskSpaceInfo != null)
            {
                free = diskSpaceInfo.Item1;
                total = diskSpaceInfo.Item2;
                used = diskSpaceInfo.Item3;
            }
            else
            {
                if (_supportsStatVfs)
                {
                    var information = _sftpSession.RequestStatVfs(_rootpath, true);
                    total = (long) (information.TotalBlocks*information.BlockSize);
                    free = (long) (information.FreeBlocks*information.BlockSize);
                    used = (long) (information.AvailableBlocks*information.BlockSize);
                }
                else
                    using (var cmd = new SshCommand(Session, String.Format(" df -Pk  {0}", _rootpath), Encoding.ASCII))
                        // POSIX standard df
                    {
                        cmd.Execute();
                        if (cmd.ExitStatus == 0)
                        {
                            var values = cmd.Result.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);

                            total = Int64.Parse(values[values.Length - 5]) << 10;
                            used = Int64.Parse(values[values.Length - 4]) << 10;
                            free = Int64.Parse(values[values.Length - 3]) << 10; //<======maybe to cache all this
                        }
                        else
                        {
                            total = 0x1900000000; //100 GiB
                            used = 0xc80000000; // 50 Gib
                            free = 0xc80000000;
                        }
                    }

                _cache.Add(_volumeLabel, new Tuple<long, long, long>(free, total, used),
                           DateTimeOffset.UtcNow.AddMinutes(3));
            }
            
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features,
                                                         out string filesystemName, DokanFileInfo info)
        {
            Log("GetVolumeInformation");

            volumeLabel = _volumeLabel;

            filesystemName = "SSHFS";

            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch |
                       FileSystemFeatures.SupportsRemoteStorage | FileSystemFeatures.UnicodeOnDisk;
            //FileSystemFeatures.PersistentAcls


            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetFileSecurity(string filename, out FileSystemSecurity security,
                                                    AccessControlSections sections, DokanFileInfo info)
        {
            Log("GetSecurrityInfo:{0}:{1}", filename, sections);


            var sftpattributes = (info.Context as SftpContext).Attributes;
            var rights = FileSystemRights.ReadPermissions | FileSystemRights.ReadExtendedAttributes |
                         FileSystemRights.ReadAttributes | FileSystemRights.Synchronize;


            if (UserCanRead(sftpattributes))
            {
                rights |= FileSystemRights.ReadData;
            }
            if (UserCanWrite(sftpattributes))
            {
                rights |= FileSystemRights.Write;
            }
            if (UserCanExecute(sftpattributes) && info.IsDirectory)
            {
                rights |= FileSystemRights.Traverse;
            }
            security = info.IsDirectory ? new DirectorySecurity() as FileSystemSecurity : new FileSecurity();
            // if(sections.HasFlag(AccessControlSections.Access))
            security.AddAccessRule(new FileSystemAccessRule("Everyone", rights, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule("Everyone", FileSystemRights.FullControl ^ rights,
                                                            AccessControlType.Deny));
            //not sure this works at all, needs testing
            // if (sections.HasFlag(AccessControlSections.Owner))
            security.SetOwner(new NTAccount("None"));
            // if (sections.HasFlag(AccessControlSections.Group))
            security.SetGroup(new NTAccount("None"));

            return DokanError.ErrorSuccess;
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

        #region Events

        public event EventHandler<EventArgs> Disconnected
        {
            add { Session.Disconnected += value; }
            remove { Session.Disconnected -= value; }
        }

        #endregion
    }
}