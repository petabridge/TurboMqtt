// -----------------------------------------------------------------------
// <copyright file="DuplexChannel.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Threading.Channels;

namespace TurboMqtt.IO;

internal interface IDuplexChannel
{
    public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> Writer { get; }
    public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> Reader { get; }
}

internal sealed class DuplexChannel : IDuplexChannel
{
    public DuplexChannel(ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> writer, ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> reader)
    {
        Writer = writer;
        Reader = reader;
    }

    public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> Writer { get; }
    public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> Reader { get; }
}