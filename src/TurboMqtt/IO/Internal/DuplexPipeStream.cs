// -----------------------------------------------------------------------
// <copyright file="DuplexChannelStream.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace TurboMqtt.IO.Internal;

/// <summary>
/// A helper for wrapping a Stream decorator from an <see cref="IDuplexPipe"/>.
/// </summary>
/// <typeparam name="TStream"></typeparam>
internal class DuplexPipeStreamAdapter<TStream> : DuplexPipeStream, IDuplexPipe where TStream : Stream
{
    private bool _disposed;
    private readonly object _disposeLock = new object();

    public DuplexPipeStreamAdapter(IDuplexPipe duplexPipe, Func<Stream, TStream> createStream) :
        this(duplexPipe, new StreamPipeReaderOptions(leaveOpen: true), new StreamPipeWriterOptions(leaveOpen: true), createStream)
    {
    }

    public DuplexPipeStreamAdapter(IDuplexPipe duplexPipe, StreamPipeReaderOptions readerOptions, StreamPipeWriterOptions writerOptions, Func<Stream, TStream> createStream) :
        base(duplexPipe.Input, duplexPipe.Output)
    {
        var stream = createStream(this);
        Stream = stream;
        Input = PipeReader.Create(stream, readerOptions);
        Output = PipeWriter.Create(stream, writerOptions);
    }

    public TStream Stream { get; }

    public PipeReader Input { get; }

    public PipeWriter Output { get; }

    public override async ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }

        await Input.CompleteAsync();
        await Output.CompleteAsync();
    }

    protected override void Dispose(bool disposing)
    {
        throw new NotSupportedException();
    }
}

internal class DuplexPipeStream : Stream
{
    private readonly PipeReader _input;
    private readonly PipeWriter _output;
    private readonly bool _throwOnCancelled;
    private volatile bool _cancelCalled;

    public DuplexPipeStream(PipeReader input, PipeWriter output, bool throwOnCancelled = false)
    {
        _input = input;
        _output = output;
        _throwOnCancelled = throwOnCancelled;
    }

    public void CancelPendingRead()
    {
        _cancelCalled = true;
        _input.CancelPendingRead();
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length
    {
        get
        {
            throw new NotSupportedException();
        }
    }

    public override long Position
    {
        get
        {
            throw new NotSupportedException();
        }
        set
        {
            throw new NotSupportedException();
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValueTask<int> vt = ReadAsyncInternal(new Memory<byte>(buffer, offset, count), default);
        return vt.IsCompleted ?
            vt.Result :
            vt.AsTask().GetAwaiter().GetResult();
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        return ReadAsyncInternal(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        return ReadAsyncInternal(destination, cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteAsync(buffer, offset, count).GetAwaiter().GetResult();
    }

    public override Task WriteAsync(byte[]? buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _output.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).GetAsTask();
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
    {
        return _output.WriteAsync(source, cancellationToken).GetAsValueTask();
    }

    public override void Flush()
    {
        FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _output.FlushAsync(cancellationToken).GetAsTask();
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<int> ReadAsyncInternal(Memory<byte> destination, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await _input.ReadAsync(cancellationToken);
            var readableBuffer = result.Buffer;
            try
            {
                if (_throwOnCancelled && result.IsCanceled && _cancelCalled)
                {
                    // Reset the bool
                    _cancelCalled = false;
                    throw new OperationCanceledException();
                }

                if (!readableBuffer.IsEmpty)
                {
                    // buffer.Count is int
                    var count = (int)Math.Min(readableBuffer.Length, destination.Length);
                    readableBuffer = readableBuffer.Slice(0, count);
                    readableBuffer.CopyTo(destination.Span);
                    return count;
                }

                if (result.IsCompleted)
                {
                    return 0;
                }
            }
            finally
            {
                _input.AdvanceTo(readableBuffer.End, readableBuffer.End);
            }
        }
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        return TaskToApm.Begin(ReadAsync(buffer, offset, count), callback, state);
    }

    public override int EndRead(IAsyncResult asyncResult)
    {
        return TaskToApm.End<int>(asyncResult);
    }

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        return TaskToApm.Begin(WriteAsync(buffer, offset, count), callback, state);
    }

    public override void EndWrite(IAsyncResult asyncResult)
    {
        TaskToApm.End(asyncResult);
    }
}