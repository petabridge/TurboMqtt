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

    public override void Flush()
    {
        // channels don't really have a concept of Flush
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotImplementedException();
    }

    public override void SetLength(long value)
    {
        throw new NotImplementedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
    {
        return _output.WriteAsync((new UnsharedMemoryOwner<byte>(buffer), buffer.Length), cancellationToken);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
}