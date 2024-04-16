// -----------------------------------------------------------------------
// <copyright file="MqttTransport.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Threading.Channels;

namespace TurboMqtt.Core.IO;

/// <summary>
/// Represents the underlying transport mechanism used to send and receive MQTT messages.
///
/// Once the transport is established, it will be used to send and receive messages between parties.
///
/// If the transport is closed, it will be impossible to send or receive messages. You will need to establish a new transport.
/// </summary>
/// <remarks>
/// Usually a socket connection of some type.
/// </remarks>
internal interface IMqttTransport
{
    /// <summary>
    /// Reflects the current status of the connection.
    /// </summary>
    public ConnectionStatus Status { get; }

    /// <summary>
    /// A task we can use to wait for the connection to terminate.
    /// </summary>
    /// <remarks>
    /// Does not cause the connection to terminate - just waits for it to finish.
    /// </remarks>
    public Task<ConnectionTerminatedReason> WaitForTermination();
    
    /// <summary>
    /// Closes the transport connection.
    /// </summary>
    /// <remarks>
    /// We assume that all of the MQTT Disconnect / DisconnectAck messages have been sent and received before this method is called,
    /// although it's not this method's responsibility to enforce that.
    ///
    /// Also, this method is idempotent - it can be called multiple times without any side effects after the first call.
    /// </remarks>
    public Task CloseAsync();

    /// <summary>
    /// If this is a client, this method will be used to establish a connection to the server.
    ///
    /// If this is a server, this method will do nothing as we've already accepted a connection.
    /// </summary>
    /// <remarks>
    /// The connection information is passed into the implementation's constructor, so no need to specify here.
    /// </remarks>
    public Task ConnectAsync();
    
    /// <summary>
    /// Maximum packet size that can be sent or received over the wire.
    /// </summary>
    /// <remarks>
    /// It's not really a "packet packet" - just the maximum size of the payload that can be sent or received.
    ///
    /// We need to be able to fit up to 2x this size in the transport's buffer at any given time.
    /// </remarks>
    public int MaxFrameSize { get; }
    
    /// <summary>
    /// Used to write data to the underlying transport.
    /// </summary>
    public ChannelWriter<IMemoryOwner<byte>> Writer { get; }
    
    /// <summary>
    /// Used to read data from the underlying transport.
    /// </summary>
    public ChannelReader<IMemoryOwner<byte>> Reader { get; }
}

public enum ConnectionStatus
{
    Connecting,
    Connected,
    Disconnected,
    Aborted
}

public enum ConnectionTerminatedReason
{
    Normal,
    CouldNotConnect,
    Error,
    Timeout
}