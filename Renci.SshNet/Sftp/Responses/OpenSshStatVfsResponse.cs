namespace Renci.SshNet.Sftp.Responses
{
    internal class OpenSshStatVfsResponse : SftpExtendedReplyResponse
    {
      

        public OpenSshFilesytemInformation FilesytemInformation { get; private set; }

        protected override void LoadData()
        {
         base.LoadData();
         ulong f_bsize = this.ReadUInt64();
         ulong f_frsize= this.ReadUInt64();
         ulong f_blocks= this.ReadUInt64();
         ulong f_bfree= this.ReadUInt64();
         ulong f_bavail= this.ReadUInt64();
         ulong f_files= this.ReadUInt64();
         ulong f_ffree= this.ReadUInt64();
         ulong f_favail= this.ReadUInt64();
         ulong f_sid= this.ReadUInt64();
         ulong f_flag= this.ReadUInt64();
         ulong f_namemax= this.ReadUInt64();
            this.FilesytemInformation=new OpenSshFilesytemInformation(f_bsize,f_frsize,f_blocks,f_bfree,f_bavail,f_files,f_ffree,f_favail,f_sid,f_flag,f_namemax);
        }
    }
}
