using System.Collections.Generic;

namespace Renci.SshNet
{
    public interface IAgentProtocol
    {
        IEnumerable<IdentityReference> GetIdentities();

        byte[] SignData(IdentityReference identity, byte[] data);
    }
}