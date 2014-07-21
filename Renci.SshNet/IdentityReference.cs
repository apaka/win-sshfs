namespace Renci.SshNet
{
    public class IdentityReference
    {
        public string Type { get; private set; }
        public byte[] Blob { get; private set; }
        public string Comment { get; private set; }

        public IdentityReference(string type,byte[] blob,string comment )
        {
           this.Type = type;
           this.Blob = blob;
           this.Comment = comment;
        }

    }
}