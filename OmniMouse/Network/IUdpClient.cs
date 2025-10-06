using System;// used for IDisposable
//using System.Diagnostics.Contracts;
using System.Net; // used for IpEndPoint and IPAddress 
using System.Net.Sockets;// Socket

namespace OmniMouse.Network
{
    public interface IUdpClient : IDisposable
    {//The IUdpClient interface adds the contract of IDisposable to its own.
       // IUdpClient has an implicit contract that it inherits from IDisposable. this is, in addition to the methods and properties we explicitly wrote.

       Socket Client { get; }// allows users of the contract to accesss the socket directly if needed.
        void Close();//calls the underlying Socket's Close() method.
        int Send(byte[] dgram, int bytes, IPEndPoint? endPoint);// first 2 params are the data (datagram) to be sent and its length. The third param is the destination IP endpoint.
        byte[] Receive(ref IPEndPoint remoteEP); //return value is the array containing the data (datagram) to be sent.
    }//ref IPEndPoint	An object that, upon return, will contain the IP address and port of the sender.
}