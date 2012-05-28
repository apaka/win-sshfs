using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Renci.SshNet.Sftp.Responses;

namespace Renci.SshNet.Sftp.Requests
{
    internal class OpenSshStatVfsRequest : SftpRequest
    {
        public override SftpMessageTypes SftpMessageType
        {
            get { return SftpMessageTypes.Extended; }
        }

        public string Path { get; private set; }

        public OpenSshStatVfsRequest(uint requestId, string path, Action<SftpExtendedReplyResponse> extendedAction, Action<SftpStatusResponse> statusAction)
            : base(requestId, statusAction)
        {
            this.Path = path;
            this.SetAction(extendedAction);
        }

 

        protected override void SaveData()
        {
            base.SaveData();
            this.Write("statvfs@openssh.com");
            this.Write(this.Path);
        }
    }
}
