namespace Sshfs
{
    public enum ConnectionType:byte 
    {
        Password = 0x0,
        PrivateKey = 0x1,
        Pageant = 0x2,
    }
}