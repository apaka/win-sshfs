using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Renci.SshNet.Sftp.Responses;

namespace Renci.SshNet.Sftp.Requests
{
    internal class OpenSshPosixRenameRequest : SftpRequest
    {
        public override SftpMessageTypes SftpMessageType
        {
            get { return SftpMessageTypes.Extended; }
        }

        public string OldPath { get; private set; }

        public string NewPath { get; private set; }

        public OpenSshPosixRenameRequest(uint requestId, string oldPath, string newPath, Action<SftpStatusResponse> statusAction)
            : base(requestId, statusAction)
        {
            this.OldPath = oldPath;
            this.NewPath = newPath;
        }

        protected override void SaveData()
        {
            base.SaveData();
            this.Write("posix-rename@openssh.com");
            this.Write(this.OldPath);
            this.Write(this.NewPath);
        }
    }
}
