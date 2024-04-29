// -----------------------------------------------------------------------
// <copyright file="FakeMqttTcpServer.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Akka.Event;
using TurboMqtt.Protocol;

namespace TurboMqtt.IO.Tcp;

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
    private readonly MqttProtocolVersion _version;
    private readonly MqttTcpServerOptions _options;
    private readonly CancellationTokenSource _shutdownTcs = new();
    private readonly ILoggingAdapter _log;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _clientCts = new();
    private readonly TimeSpan _heatBeatDelay;
    private readonly IFakeServerHandleFactory _handleFactory;
    private Socket? _bindSocket;
    public int BoundPort { get; private set; }

    public FakeMqttTcpServer(MqttTcpServerOptions options, MqttProtocolVersion version, ILoggingAdapter log,
        TimeSpan heartbeatDelay, IFakeServerHandleFactory handleFactory)
    {
        _options = options;
        _version = version;
        _log = log;
        _heatBeatDelay = heartbeatDelay;
        _handleFactory = handleFactory;

        if (_version == MqttProtocolVersion.V5_0)
            throw new NotSupportedException("V5.0 not supported.");
    }

    public void Bind()
    {
        if (_bindSocket != null)
            throw new InvalidOperationException("Cannot bind the same server twice.");

        if (_options.AddressFamily == AddressFamily.Unspecified) // allows use of dual mode IPv4 / IPv6
        {
            _bindSocket = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                ReceiveBufferSize = TcpTransportActor.ScaleBufferSize(_options.MaxFrameSize),
                SendBufferSize = TcpTransportActor.ScaleBufferSize(_options.MaxFrameSize),
                DualMode = true,
                NoDelay = true,
                LingerState = new LingerOption(false, 0)
            };
        }
        else
        {
            _bindSocket = new Socket(_options.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                ReceiveBufferSize = TcpTransportActor.ScaleBufferSize(_options.MaxFrameSize),
                SendBufferSize = TcpTransportActor.ScaleBufferSize(_options.MaxFrameSize),
                DualMode = true,
                NoDelay = true,
                LingerState = new LingerOption(false, 0)
            };
        }

        var hostAddress = Dns.GetHostAddresses(_options.Host).First();

        _bindSocket.Bind(new IPEndPoint(hostAddress, _options.Port));
        _bindSocket.Listen(100);

        BoundPort = _bindSocket!.LocalEndPoint is IPEndPoint ipEndPoint ? ipEndPoint.Port : 0;

        // begin the accept loop
        _ = BeginAcceptAsync();
    }

    public bool TryKickClient(string clientId)
    {
        if (_clientCts.TryRemove(clientId, out var cts))
        {
            cts.Cancel();
            return true;
        }

        return false;
    }

    public void Shutdown()
    {
        _log.Info("Shutting down server.");
        try
        {
            _shutdownTcs.Cancel();
            _bindSocket?.Close();
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
            var socket = await _bindSocket!.AcceptAsync();
            _ = ProcessClientAsync(socket);
        }
    }

    private static async Task ReadFromPipeAsync(PipeReader reader, IFakeServerHandle handle, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;

                // consume this entire sequence by copying it into a new buffer
                // have to copy because there's no guarantee we can safely release a shared buffer
                // once we hand the message over to the end-user.
                var newMemory = new Memory<byte>(new byte[buffer.Length]);
                buffer.CopyTo(newMemory.Span);

                // tell the pipe we're done with this data
                reader.AdvanceTo(buffer.End);

                handle.HandleBytes(newMemory);
                
                if(result.IsCompleted || result.IsCanceled)
                    break;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ProcessClientAsync(Socket socket)
    {
        using (socket)
        {
            var closed = false;
            var pipe = new Pipe(new PipeOptions(
                pauseWriterThreshold: TcpTransportActor.ScaleBufferSize(_options.MaxFrameSize),
                resumeWriterThreshold: TcpTransportActor.ScaleBufferSize(_options.MaxFrameSize) / 2,
                useSynchronizationContext: false));
            var clientShutdownCts = new CancellationTokenSource();
            var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(clientShutdownCts.Token, _shutdownTcs.Token);
            
            var handle = _handleFactory.CreateServerHandle(PushMessage, ClosingAction, _log, _version, _heatBeatDelay);
            
            _ = handle.WhenClientIdAssigned.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    _clientCts.TryAdd(t.Result, clientShutdownCts);
                }
            }, clientShutdownCts.Token);
           

            _ = ReadFromPipeAsync(pipe.Reader, handle, linkedCts.Token);

            while (!linkedCts.IsCancellationRequested)
            {
                if (closed)
                    break;
                try
                {
                    var memory = pipe.Writer.GetMemory(_options.MaxFrameSize / 4);
                    var bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None, linkedCts.Token);
                    if (bytesRead == 0)
                    {
                        _log.Info("Client {0} disconnected from server.",
                            handle.WhenClientIdAssigned.IsCompletedSuccessfully
                                ? handle.WhenClientIdAssigned.Result
                                : "unknown");
                        break;
                    }
                    pipe.Writer.Advance(bytesRead);

                    var flushResult = await pipe.Writer.FlushAsync(linkedCts.Token);
                    if (flushResult.IsCompleted)
                    {
                        _log.Info("Done reading from client {0}.",
                            handle.WhenClientIdAssigned.IsCompletedSuccessfully
                                ? handle.WhenClientIdAssigned.Result
                                : "unknown");
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    _log.Warning("Client {0} is being disconnected from server.",
                        handle.WhenClientIdAssigned.IsCompletedSuccessfully
                            ? handle.WhenClientIdAssigned.Result
                            : "unknown");
                    break;
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error processing message from client {0}.",
                        handle.WhenClientIdAssigned.IsCompletedSuccessfully
                            ? handle.WhenClientIdAssigned.Result
                            : "unknown");
                }
            }

            // send a disconnect message
            if (!closed)
                // send a disconnect message
                handle.DisconnectFromServer();

            return;

            bool PushMessage((IMemoryOwner<byte> buffer, int estimatedSize) msg)
            {
                try
                {
                    if (socket.Connected && linkedCts.Token is { CanBeCanceled: true, IsCancellationRequested: false })
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
                catch (Exception ex)
                {
                    _log.Error(ex, "Error writing to client.");
                    return false;
                }
                finally
                {
                    msg.buffer.Dispose(); // release any shared memory
                }
            }

            async Task ClosingAction()
            {
                closed = true;
                await pipe.Writer.CompleteAsync();
                await pipe.Reader.CompleteAsync();
                // ReSharper disable once AccessToModifiedClosure
                clientShutdownCts.Cancel(false);
                if (socket.Connected) socket.Close();
            }
        }
    }
}