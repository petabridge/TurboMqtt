// -----------------------------------------------------------------------
// <copyright file="FakeMqttTcpServer.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Akka.Event;
using Akka.Streams.Implementation.Fusing;
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
    private readonly ILoggingAdapter _log;
    private Socket? bindSocket;

    public FakeMqttTcpServer(MqttTcpServerOptions options, MqttProtocolVersion version, ILoggingAdapter log)
    {
        _options = options;
        _version = version;
        _log = log;

        if (_version == MqttProtocolVersion.V5_0)
            throw new NotSupportedException("V5.0 not supported.");
    }

    public void Bind()
    {
        if (bindSocket != null)
            throw new InvalidOperationException("Cannot bind the same server twice.");

        bindSocket = new Socket(_options.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            ReceiveBufferSize = _options.MaxFrameSize * 2,
            SendBufferSize = _options.MaxFrameSize * 2
        };
        
        var hostAddress = Dns.GetHostAddresses(_options.Host).First();
        
        bindSocket.Bind(new IPEndPoint(hostAddress, _options.Port));
        bindSocket.Listen(10);

        // begin the accept loop
        var _ = BeginAcceptAsync();
    }

    public void Shutdown()
    {
        try
        {
            _shutdownTcs.Cancel();
            bindSocket?.Close();
        }
        catch (Exception)
        {
            // do nothing - this method is idempotent
        }
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
        using (socket)
        {
            Memory<byte> buffer = new byte[_options.MaxFrameSize];

            var handle = new FakeMqtt311ServerHandle(PushMessage, ClosingAction, _log);

            while (!_shutdownTcs.IsCancellationRequested)
            {
                try
                {
                    var bytesRead = await socket.ReceiveAsync(buffer, SocketFlags.None, _shutdownTcs.Token);
                    if (bytesRead == 0)
                    {
                        socket.Close();
                        return;
                    }

                    // process the incoming message, send any necessary replies back
                    handle.HandleBytes(buffer.Slice(0, bytesRead));
                }
                catch (OperationCanceledException)
                {
                    _log.Warning("Server shutting down...");
                }
            }
            
            // send a disconnect message
            handle.DisconnectFromServer();

            return;

            bool PushMessage((IMemoryOwner<byte> buffer, int estimatedSize) msg)
            {
                try
                {
                    if (socket.Connected)
                    {
                        var sent = socket.Send(msg.buffer.Memory.Span.Slice(0, msg.estimatedSize));
                        while (sent < msg.estimatedSize)
                        {
                            if (sent == 0) return false; // we are shutting down

                            var remaining = msg.buffer.Memory.Slice(sent);
                            var sent2 = socket.Send(remaining.Span);
                            if (sent2 == remaining.Length)
                                sent += sent2;
                            else
                                return false;
                        }

                        return true;
                    }

                    return false;
                }
                finally
                {
                    msg.buffer.Dispose(); // release any shared memory
                }
            }

            Task ClosingAction()
            {
                if (socket.Connected) socket.Close();
                return Task.CompletedTask;
            }
        }
    }
}