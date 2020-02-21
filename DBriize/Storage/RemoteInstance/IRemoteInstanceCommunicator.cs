using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;

namespace DBriize.Storage.RemoteInstance
{
    public interface IRemoteInstanceCommunicator
    {
        byte[] Send(byte[] data);
    }
}
