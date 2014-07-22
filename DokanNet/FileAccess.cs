using System;

namespace DokanNet
{
    [Flags]
    public enum FileAccess : uint
    {
        GenericRead = 2147483648,       //0x80000000
        GenericWrite = 1073741824,      //0x40000000
        GenericExecute = 536870912,     //0x20000000
        ReadData = 1,                   //0x00000001
        WriteData = 2,                  //0x00000002
        AppendData = 4,                 //0x00000004
        ReadExtendedAttributes = 8,     //0x00000008
        WriteExtendedAttributes = 16,   //0x00000010
        Execute = 32,                   //0x00000020
        ReadAttributes = 128,           //0x00000080
        WriteAttributes = 256,          //0x00000100
        Delete = 65536,                 //0x00010000
        ReadPermissions = 131072,       //0x00020000
        ChangePermissions = 262144,     //0x00040000
        SetOwnership = 524288,          //0x00080000
        Synchronize = 1048576,          //0x00100000
    }
}