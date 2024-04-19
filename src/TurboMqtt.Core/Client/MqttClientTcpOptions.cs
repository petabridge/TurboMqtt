// -----------------------------------------------------------------------
// <copyright file="MqttClientTcpOptions.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Sockets;

namespace TurboMqtt.Core.Client;

/// <summary>
/// Used to configure the TCP connection for the MQTT client.
/// </summary>
public sealed record MqttClientTcpOptions
{
    public MqttClientTcpOptions(string host, int port)
    {
        Host = host;
        Port = port;
    }

    /// <summary>
    /// Would love to just do IPV6, but that still meets resistance everywhere
    /// </summary>
    public AddressFamily AddressFamily { get; set; } = AddressFamily.Unspecified;
    
    /// <summary>
    /// Frames are limited to this size in bytes. A frame can contain multiple packets.
    /// </summary>
    public int MaxFrameSize { get; set; } = 128 * 1024; // 128kb
    
    public string Host { get; }
    
    public int Port { get; }
    
    /// <summary>
    /// How long should we wait before attempting to reconnect the client?
    /// </summary>
    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(5);
    
    /// <summary>
    /// Maximum number of times we should attempt to reconnect the client before giving up.
    ///
    /// Resets back to 0 after a successful connection.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 10;
}