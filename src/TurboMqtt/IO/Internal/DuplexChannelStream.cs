// -----------------------------------------------------------------------
// <copyright file="DuplexChannelStream.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Threading.Channels;

namespace TurboMqtt.IO.Internal;

public class DuplexChannelStream : Stream
{
    private readonly ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> _output;
    private readonly ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> _input;
    private readonly bool _throwOnCancelled;
    private volatile bool _cancelCalled;

    public DuplexChannelStream(ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> output, 
        ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> input, bool throwOnCancelled)
    {
        _output = output;
        _input = input;
        _throwOnCancelled = throwOnCancelled;
    }
    
    public void CancelPendingRead()
    {
        _cancelCalled = true;
    }
    
}