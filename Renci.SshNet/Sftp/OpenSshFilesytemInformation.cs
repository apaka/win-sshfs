namespace Renci.SshNet.Sftp
{
    public class OpenSshFilesytemInformation
    {
        private ulong f_bsize;
        private ulong f_frsize;
        private ulong f_blocks;
        private ulong f_bfree;
        private ulong f_bavail;
        private ulong f_files;
        private ulong f_ffree;
        private ulong f_favail;
        private ulong f_sid;
        private ulong f_flag;
        private ulong f_namemax;

        private const ulong SSH_FXE_STATVFS_ST_RDONLY = 0x1;
        private const ulong SSH_FXE_STATVFS_ST_NOSUID = 0x2;
        public OpenSshFilesytemInformation(ulong bsize, ulong frsize, ulong blocks, ulong bfree, ulong bavail, ulong files, ulong ffree, ulong favail, ulong sid, ulong flag, ulong namemax)
        {
            f_bsize = bsize;
            f_namemax = namemax;
            f_flag = flag;
            f_sid = sid;
            f_favail = favail;
            f_ffree = ffree;
            f_files = files;
            f_bavail = bavail;
            f_bfree = bfree;
            f_blocks = blocks;
            f_frsize = frsize;
        }

        public ulong BlockSize
        {
            get { return f_frsize; }
        }

        public ulong TotalBlocks
        {
            get { return f_blocks; }
        }

        public ulong FreeBlocks
        {
            get { return f_bfree; }
        }

        public ulong AvailableBlocks
        {
            get { return f_bavail; }
        }

        public ulong TotalNodes
        {
            get { return f_files; }
        }

        public ulong FreeNodes
        {
            get { return f_ffree; }
        }

        public ulong AvailableNodes
        {
            get { return f_favail; }
        }

        public ulong ID
        {
            get { return f_sid; }
        }

        public bool IsReadOnly
        {
            get
            {
                return (f_flag & SSH_FXE_STATVFS_ST_RDONLY) == SSH_FXE_STATVFS_ST_RDONLY;
            }
        }
        public bool SuportsSUID
        {
            get
            {
                return (f_flag & SSH_FXE_STATVFS_ST_NOSUID) == 0;
            }
        }

        public ulong MaxNameLenght
        {
            get
            {
                return f_namemax;
            }
        }
    }
}
