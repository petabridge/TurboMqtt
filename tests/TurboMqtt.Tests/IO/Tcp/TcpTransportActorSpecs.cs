// -----------------------------------------------------------------------
// <copyright file="TcpTransportActorSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using TurboMqtt.Client;
using TurboMqtt.IO;
using TurboMqtt.IO.Tcp;
using Xunit.Abstractions;

namespace TurboMqtt.Tests.IO.Tcp;

/// <summary>
/// A controllable <see cref="IStreamProvider"/> for testing the <see cref="TcpTransportActor"/> FSM.
/// </summary>
internal sealed class FakeStreamProvider : IStreamProvider
{
    private readonly TaskCompletionSource<Stream> _connectTcs = new();
    private readonly FakeStream _stream;
    private bool _closed;

    public FakeStreamProvider(FakeStream? stream = null)
    {
        _stream = stream ?? new FakeStream();
    }

    public FakeStream Stream => _stream;

    /// <summary>
    /// Complete the pending ConnectAsync call successfully.
    /// </summary>
    public void CompleteConnect() => _connectTcs.TrySetResult(_stream);

    /// <summary>
    /// Fail the pending ConnectAsync call.
    /// </summary>
    public void FailConnect(Exception ex) => _connectTcs.TrySetException(ex);

    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        await using var reg = ct.Register(() => _connectTcs.TrySetCanceled(ct));
        return await _connectTcs.Task.ConfigureAwait(false);
    }

    public void Close()
    {
        _closed = true;
        _stream.Shutdown();
    }

    public bool IsClosed => _closed;
}

/// <summary>
/// A controllable <see cref="Stream"/> that blocks reads until data is written or the stream is shut down.
/// </summary>
internal sealed class FakeStream : Stream
{
    private readonly SemaphoreSlim _dataAvailable = new(0);
    private readonly object _lock = new();
    private readonly Queue<byte[]> _readQueue = new();
    private bool _shutdown;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <summary>
    /// Enqueue data that will be returned by the next ReadAsync call.
    /// </summary>
    public void EnqueueReadData(byte[] data)
    {
        lock (_lock)
        {
            _readQueue.Enqueue(data);
        }
        _dataAvailable.Release();
    }

    /// <summary>
    /// Signal the stream to stop — ReadAsync will return 0 (graceful close).
    /// </summary>
    public void Shutdown()
    {
        _shutdown = true;
        _dataAvailable.Release();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _dataAvailable.WaitAsync(cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            if (_readQueue.TryDequeue(out var data))
            {
                var toCopy = Math.Min(data.Length, buffer.Length);
                data.AsMemory(0, toCopy).CopyTo(buffer);
                return toCopy;
            }
        }

        // No data and shutdown — return 0 (graceful close)
        if (_shutdown)
            return 0;

        return 0;
    }

    /// <summary>
    /// Track how many bytes have been written.
    /// </summary>
    public long TotalBytesWritten;

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Add(ref TotalBytesWritten, buffer.Length);
        return ValueTask.CompletedTask;
    }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Tests for the <see cref="TcpTransportActor"/> FSM — verifies all state transitions,
/// connect timeout, abort path, and graceful drain behavior.
/// </summary>
public class TcpTransportActorSpecs : TestKit
{
    private static readonly MqttClientTcpOptions DefaultTcpOptions = new("localhost", 1883)
    {
        ConnectTimeout = TimeSpan.FromSeconds(5)
    };

    public TcpTransportActorSpecs(ITestOutputHelper output) : base(output: output)
    {
    }

    private (IActorRef actor, FakeStreamProvider provider) CreateActor(MqttClientTcpOptions? options = null)
    {
        var provider = new FakeStreamProvider();
        var opts = options ?? DefaultTcpOptions;
        var actor = Sys.ActorOf(Props.Create(() => new TcpTransportActor(opts, provider)));
        return (actor, provider);
    }

    #region FSM State Transition Tests

    [Fact]
    public async Task Should_transition_NotStarted_to_Created_on_CreateTcpTransport()
    {
        var (actor, _) = CreateActor();

        var transport = await actor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance, TimeSpan.FromSeconds(3));

        transport.Should().NotBeNull();
        transport.Status.Should().Be(ConnectionStatus.NotStarted); // status stays NotStarted until DoConnect
    }

    [Fact]
    public async Task Should_transition_Created_to_Connecting_to_Connected()
    {
        var (actor, provider) = CreateActor();

        // NotStarted → Created
        var transport = await actor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance, TimeSpan.FromSeconds(3));

        // Created → Connecting → Connected
        // Complete the connection asynchronously
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            provider.CompleteConnect();
        });

        var result = await actor.Ask<TcpTransportActor.ConnectResult>(
            new TcpTransportActor.DoConnect(CancellationToken.None), TimeSpan.FromSeconds(5));

        result.Status.Should().Be(ConnectionStatus.Connected);

        // The actor processes the self-tell asynchronously — wait for state to settle
        await AwaitConditionAsync(() => Task.FromResult(transport.Status == ConnectionStatus.Connected), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Should_transition_full_happy_path_NotStarted_to_Stopped()
    {
        var (actor, provider) = CreateActor();

        // Watch for termination
        Watch(actor);

        // NotStarted → Created
        var transport = await actor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance, TimeSpan.FromSeconds(3));

        // Created → Connecting → Connected
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            provider.CompleteConnect();
        });

        var result = await actor.Ask<TcpTransportActor.ConnectResult>(
            new TcpTransportActor.DoConnect(CancellationToken.None), TimeSpan.FromSeconds(5));
        result.Status.Should().Be(ConnectionStatus.Connected);

        // Connected → Draining → Closing → Stopped
        actor.Tell(new TcpTransportActor.DoClose(CancellationToken.None));

        // Actor should terminate
        await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(10));

        // Final status should be Disconnected (set in PostStop/FullShutdown)
        transport.Status.Should().Be(ConnectionStatus.Disconnected);
    }

    [Fact]
    public async Task Should_transition_Connecting_to_Failed_on_connection_error()
    {
        var (actor, provider) = CreateActor();

        Watch(actor);

        // NotStarted → Created
        await actor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance, TimeSpan.FromSeconds(3));

        // Created → Connecting → Failed
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            provider.FailConnect(new InvalidOperationException("Connection refused"));
        });

        var result = await actor.Ask<TcpTransportActor.ConnectResult>(
            new TcpTransportActor.DoConnect(CancellationToken.None), TimeSpan.FromSeconds(5));

        result.Status.Should().Be(ConnectionStatus.Failed);
        result.ReasonMessage.Should().Contain("Connection refused");

        // Actor should stop
        await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Aborted Short-Circuit Tests

    [Fact]
    public async Task Should_transition_Connected_to_Aborted_on_unexpected_close()
    {
        var (actor, provider) = CreateActor();

        Watch(actor);

        // NotStarted → Created → Connected
        var transport = await actor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance, TimeSpan.FromSeconds(3));

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            provider.CompleteConnect();
        });

        var result = await actor.Ask<TcpTransportActor.ConnectResult>(
            new TcpTransportActor.DoConnect(CancellationToken.None), TimeSpan.FromSeconds(5));
        result.Status.Should().Be(ConnectionStatus.Connected);

        // Simulate unexpected close — shut down the stream so ReadAsync returns 0
        provider.Stream.Shutdown();

        // Actor should terminate via ReadFinished → Closing → Stopped
        await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Should_transition_Connected_to_Aborted_on_ConnectionUnexpectedlyClosed()
    {
        var (actor, provider) = CreateActor();

        Watch(actor);

        // Get to Connected state
        await actor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance, TimeSpan.FromSeconds(3));

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            provider.CompleteConnect();
        });

        var connectResult = await actor.Ask<TcpTransportActor.ConnectResult>(
            new TcpTransportActor.DoConnect(CancellationToken.None), TimeSpan.FromSeconds(5));
        connectResult.Status.Should().Be(ConnectionStatus.Connected);

        // Send ConnectionUnexpectedlyClosed directly
        actor.Tell(new TcpTransportActor.ConnectionUnexpectedlyClosed(
            TurboMqtt.PacketTypes.DisconnectReasonCode.UnspecifiedError, "Broker killed connection"));

        // Actor should terminate
        await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(10));
    }

    #endregion

    #region Drain During Publish Tests

    [Fact]
    public async Task Should_inject_exactly_one_DISCONNECT_on_graceful_drain_path()
    {
        var (actor, provider) = CreateActor();

        Watch(actor);

        var transport = await actor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance, TimeSpan.FromSeconds(3));

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            provider.CompleteConnect();
        });

        var result = await actor.Ask<TcpTransportActor.ConnectResult>(
            new TcpTransportActor.DoConnect(CancellationToken.None), TimeSpan.FromSeconds(5));
        result.Status.Should().Be(ConnectionStatus.Connected);

        // Trigger graceful drain → Draining → Closing path
        actor.Tell(new TcpTransportActor.DoClose(CancellationToken.None));

        // Wait for actor to fully terminate
        await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(10));

        // Drain the reads channel and count items.
        // The FakeStream produces no inbound data, so the only items are injected DISCONNECT packets.
        var itemCount = 0;
        while (transport.Reader.TryRead(out var item))
        {
            itemCount++;
            item.buffer.Dispose();
        }

        // Exactly one DISCONNECT packet must be injected — not two.
        itemCount.Should().Be(1, "the Draining→Closing path must inject exactly one DISCONNECT packet");
    }

    [Fact]
    public async Task Should_flush_outbound_before_close_during_drain()
    {
        var (actor, provider) = CreateActor();

        Watch(actor);

        // Get to Connected state
        var transport = await actor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance, TimeSpan.FromSeconds(3));

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            provider.CompleteConnect();
        });

        var result = await actor.Ask<TcpTransportActor.ConnectResult>(
            new TcpTransportActor.DoConnect(CancellationToken.None), TimeSpan.FromSeconds(5));
        result.Status.Should().Be(ConnectionStatus.Connected);

        // Write some data to the outbound channel — simulating a large publish in flight
        var data = new byte[4096];
        Random.Shared.NextBytes(data);
        var pooled = MemoryPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(pooled.Memory);
        transport.Writer.TryWrite((pooled, data.Length)).Should().BeTrue();

        // Give the write task a moment to pick up the data
        await Task.Delay(100);

        // Request graceful close — should enter Draining, wait for flush, then Closing
        actor.Tell(new TcpTransportActor.DoClose(CancellationToken.None));

        // Actor should eventually terminate
        await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(10));

        // Verify data was written to the stream before shutdown
        provider.Stream.TotalBytesWritten.Should().BeGreaterThanOrEqualTo(data.Length);
    }

    #endregion

    #region Connect Timeout Tests

    [Fact]
    public async Task Should_fail_connect_when_timeout_expires()
    {
        // Use a very short timeout
        var options = new MqttClientTcpOptions("unreachable.host", 1883)
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(200)
        };

        var provider = new FakeStreamProvider();
        var actor = Sys.ActorOf(Props.Create(() => new TcpTransportActor(options, provider)));

        Watch(actor);

        // NotStarted → Created
        await actor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance, TimeSpan.FromSeconds(3));

        // Don't complete the connection — let it time out
        var result = await actor.Ask<TcpTransportActor.ConnectResult>(
            new TcpTransportActor.DoConnect(CancellationToken.None), TimeSpan.FromSeconds(5));

        result.Status.Should().Be(ConnectionStatus.Failed);
        result.ReasonMessage.Should().Contain("timed out");

        // Actor should stop after failed connect
        await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Should_respect_caller_cancellation_over_timeout()
    {
        var options = new MqttClientTcpOptions("localhost", 1883)
        {
            ConnectTimeout = TimeSpan.FromSeconds(30) // long timeout
        };

        var provider = new FakeStreamProvider();
        var actor = Sys.ActorOf(Props.Create(() => new TcpTransportActor(options, provider)));

        Watch(actor);

        await actor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance, TimeSpan.FromSeconds(3));

        // Cancel quickly via caller token
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await actor.Ask<TcpTransportActor.ConnectResult>(
            new TcpTransportActor.DoConnect(cts.Token), TimeSpan.FromSeconds(5));

        result.Status.Should().Be(ConnectionStatus.Failed);

        await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task Should_ignore_duplicate_DoClose_in_Draining_state()
    {
        var (actor, provider) = CreateActor();

        Watch(actor);

        // Get to Connected state
        await actor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance, TimeSpan.FromSeconds(3));

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            provider.CompleteConnect();
        });

        var result = await actor.Ask<TcpTransportActor.ConnectResult>(
            new TcpTransportActor.DoConnect(CancellationToken.None), TimeSpan.FromSeconds(5));
        result.Status.Should().Be(ConnectionStatus.Connected);

        // Send multiple DoClose messages — should not crash
        actor.Tell(new TcpTransportActor.DoClose(CancellationToken.None));
        actor.Tell(new TcpTransportActor.DoClose(CancellationToken.None));
        actor.Tell(new TcpTransportActor.DoClose(CancellationToken.None));

        // Actor should still terminate cleanly
        await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Should_not_crash_when_DoConnect_arrives_in_Connected_state()
    {
        var (actor, provider) = CreateActor();

        Watch(actor);

        var transport = await actor.Ask<IMqttTransport>(TcpTransportActor.CreateTcpTransport.Instance, TimeSpan.FromSeconds(3));

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            provider.CompleteConnect();
        });

        var result = await actor.Ask<TcpTransportActor.ConnectResult>(
            new TcpTransportActor.DoConnect(CancellationToken.None), TimeSpan.FromSeconds(5));
        result.Status.Should().Be(ConnectionStatus.Connected);

        // Wait for actor to fully transition
        await AwaitConditionAsync(() => Task.FromResult(transport.Status == ConnectionStatus.Connected), TimeSpan.FromSeconds(3));

        // Send a spurious DoConnect via Tell — should be silently ignored (Connected swallows unknown messages)
        actor.Tell(new TcpTransportActor.DoConnect(CancellationToken.None));

        // Actor should still be alive and healthy
        await Task.Delay(200);
        transport.Status.Should().Be(ConnectionStatus.Connected);

        // Clean shutdown
        actor.Tell(new TcpTransportActor.DoClose(CancellationToken.None));
        await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(10));
    }

    #endregion
}
