namespace Sshfs
{
    public enum DriveStatus:short 
    {
        Unmounted=0x0,
        Mounting=0x1,
        Mounted=0x2,
        Unmounting=0x3,
        
    }
}