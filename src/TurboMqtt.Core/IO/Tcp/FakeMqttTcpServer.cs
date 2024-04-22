// -----------------------------------------------------------------------
// <copyright file="FakeMqttTcpServer.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Sockets;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.IO;

internal sealed class MqttTcpServerOptions
{
    public MqttTcpServerOptions(string host, int port)
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
}

/// <summary>
/// A fake TCP server that can be used to simulate the behavior of a real MQTT broker.
/// </summary>
internal sealed class FakeMqttTcpServer
{
    private readonly Mqtt311Decoder _decoder = new();
    private readonly MqttProtocolVersion _version;
    private readonly MqttTcpServerOptions _options;
    private readonly CancellationTokenSource _shutdownTcs = new();
    private Socket? bindSocket;

    public FakeMqttTcpServer(MqttTcpServerOptions options, MqttProtocolVersion version)
    {
        _options = options;
        _version = version;
        
        if(_version == MqttProtocolVersion.V5_0)
            throw new NotSupportedException("V5.0 not supported.");
    }
    
    public void Bind()
    {
        if(bindSocket != null)
            throw new InvalidOperationException("Cannot bind the same server twice.");
        
        bindSocket = new Socket(_options.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            ReceiveBufferSize = _options.MaxFrameSize * 2,
            SendBufferSize = _options.MaxFrameSize * 2
        };
        bindSocket.Bind(new IPEndPoint(IPAddress.Parse(_options.Host), _options.Port));
        bindSocket.Listen(10);
        
        // begin the accept loop
        var _ =BeginAcceptAsync();
    }

    private async Task BeginAcceptAsync()
    {
        while (!_shutdownTcs.IsCancellationRequested)
        {
            var socket = await bindSocket!.AcceptAsync();
            var _ = ProcessClientAsync(socket);
        }
    }

    private async Task ProcessClientAsync(Socket socket)
    {
        Memory<byte> buffer = new byte[_options.MaxFrameSize];
        while (!_shutdownTcs.IsCancellationRequested)
        {
            var bytesRead = await socket.ReceiveAsync(buffer, SocketFlags.None);
            if (bytesRead == 0)
            {
                socket.Close();
                return;
            }

            // process the incoming message
            if (_decoder.TryDecode(buffer.Slice(0, bytesRead), out var remaining))
            {
                
            }
        
            // echo the message back to the client
            await socket.SendAsync(buffer, SocketFlags.None);
        }
    }
}