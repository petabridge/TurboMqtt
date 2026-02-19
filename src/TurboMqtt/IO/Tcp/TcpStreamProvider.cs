// -----------------------------------------------------------------------
// <copyright file="TcpStreamProvider.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Sockets;
using TurboMqtt.Client;

namespace TurboMqtt.IO.Tcp;

/// <summary>
/// Creates a plain TCP connection and returns a <see cref="NetworkStream"/>.
/// </summary>
internal sealed class TcpStreamProvider : IStreamProvider
{
    private readonly AddressFamily _addressFamily;
    private readonly int _maxFrameSize;
    private Socket? _socket;

    public TcpStreamProvider(AddressFamily addressFamily, int maxFrameSize)
    {
        _addressFamily = addressFamily;
        _maxFrameSize = maxFrameSize;
    }

    public TcpStreamProvider(MqttClientTcpOptions tcpOptions)
        : this(tcpOptions.AddressFamily, tcpOptions.MaxFrameSize)
    {
    }

    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        _socket = CreateSocket();

        var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        if (addresses.Length == 0)
            throw new ArgumentException($"Could not resolve any IP addresses for host '{host}'.", nameof(host));

        await _socket.ConnectAsync(addresses, port, ct).ConfigureAwait(false);

        return new NetworkStream(_socket, ownsSocket: false);
    }

    public void Close()
    {
        if (_socket is null)
            return;

        try
        {
            _socket.Close();
            _socket.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }
        finally
        {
            _socket = null;
        }
    }

    private Socket CreateSocket()
    {
        var bufferSize = TcpTransportActor.ScaleBufferSize(_maxFrameSize);

        if (_addressFamily == AddressFamily.Unspecified)
            return new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
                LingerState = new LingerOption(true, 2),
                ReceiveBufferSize = bufferSize,
                SendBufferSize = bufferSize
            };

        return new Socket(_addressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
            LingerState = new LingerOption(true, 2),
            ReceiveBufferSize = bufferSize,
            SendBufferSize = bufferSize
        };
    }
}
