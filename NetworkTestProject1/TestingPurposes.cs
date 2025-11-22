// Provides attributes and classes for unit testing, like [TestClass] and [TestMethod].
namespace NetworkTestProject1
{
    // Provides fundamental classes and base types, like Exception and Func.
    using System;

    // Provides LINQ (Language Integrated Query) capabilities, used here for sequence comparison.
    using System.Linq;

    // Provides classes for network protocols, like IPAddress and IPEndPoint.
    using System.Net;

    // Provides classes for implementing network sockets, like Socket.
    using System.Net.Sockets;

    // A fast binary serialization library used to convert objects to byte arrays.
    using MessagePack;

    // Provides classes and attributes for unit testing in Visual Studio.
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    // A popular mocking library for creating fake objects for testing purposes.
    using Moq;

    // Contains data structures for network packets, like MousePacket.
    using OmniMouse.Core.Packets;

    // Contains the network-related classes from the main project, like UdpMouseTransmitter.
    using OmniMouse.Network;

    // The [TestClass] attribute tells the test runner that this class contains unit tests.
    [TestClass]
    public sealed class TestingPurposes
    {

    }
}
