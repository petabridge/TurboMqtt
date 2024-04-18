// -----------------------------------------------------------------------
// <copyright file="TcpTransport.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Threading.Channels;
using Akka.Event;

namespace TurboMqtt.Core.IO;

public sealed class TcpTransport : IMqttTransport
{
    public ILoggingAdapter Log { get; }
    public ConnectionStatus Status { get; }
    public Task<ConnectionTerminatedReason> WaitForTermination()
    {
        throw new NotImplementedException();
    }

    public Task CloseAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public int MaxFrameSize { get; }
    public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> Writer { get; }
    public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> Reader { get; }
}