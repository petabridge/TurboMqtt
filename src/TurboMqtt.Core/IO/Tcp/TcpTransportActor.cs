// -----------------------------------------------------------------------
// <copyright file="TcpTransportActor.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboMqtt.Core.Client;
using TurboMqtt.Core.Protocol;
using Debug = System.Diagnostics.Debug;

namespace TurboMqtt.Core.IO;

/// <summary>
/// Actor responsible for managing the TCP transport layer for MQTT.
/// </summary>
internal sealed class TcpTransportActor : UntypedActor, IWithStash
{
    #region Internal Types

    /// <summary>
    /// Mutable state shared between the TcpTransport and the TcpTransportActor.
    /// </summary>
    /// <remarks>
    /// Can values can only be set by the TcpTransportActor itself.
    /// </remarks>
    public sealed class ConnectionState
    {
        public ConnectionState(ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> writer,
            ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> reader,
            Task<ConnectionTerminatedReason> whenTerminated, MqttProtocolVersion protocolVersion, int maxFrameSize)
        {
            Writer = writer;
            Reader = reader;
            WhenTerminated = whenTerminated;
            ProtocolVersion = protocolVersion;
            MaxFrameSize = maxFrameSize;
        }

        public MqttProtocolVersion ProtocolVersion { get; }

        public ConnectionStatus Status { get; set; } = ConnectionStatus.NotStarted;

        public CancellationTokenSource ShutDownCts { get; set; } = new();

        public int MaxFrameSize { get; }

        public Task<ConnectionTerminatedReason> WhenTerminated { get; }

        public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> Writer { get; }

        /// <summary>
        /// Used to read data from the underlying transport.
        /// </summary>
        public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> Reader { get; }
    }

    /// <summary>
    /// Has us create the client and share state, but the socket is not open yet.
    /// </summary>
    public sealed class CreateTcpTransport
    {
        private CreateTcpTransport()
        {
        }

        public static CreateTcpTransport Instance { get; } = new();
    }

    public sealed record DoConnect(CancellationToken Cancel);

    public sealed record ConnectResult(ConnectionStatus Status, string ReasonMessage);

    public sealed record DoClose(CancellationToken Cancel);

    /// <summary>
    /// We are done reading from the socket.
    /// </summary>
    public sealed class ReadFinished
    {
        private ReadFinished()
        {
        }

        public static ReadFinished Instance { get; } = new();
    }

    /// <summary>
    /// In the event that our connection gets aborted by the broker, we will send ourselves this message.
    /// </summary>
    /// <param name="Reason"></param>
    /// <param name="ReasonMessage"></param>
    public sealed record ConnectionUnexpectedlyClosed(ConnectionTerminatedReason Reason, string ReasonMessage);

    #endregion

    public MqttClientTcpOptions TcpOptions { get; }
    public int MaxFrameSize { get; }
    public bool AutomaticRestarts { get; }

    public ConnectionState State { get; private set; }

    private TcpClient? _tcpClient;
    private Stream? _tcpStream;

    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _writesToTransport =
        Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();

    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _readsFromTransport =
        Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();

    private readonly TaskCompletionSource<ConnectionTerminatedReason> _whenTerminated = new();
    private readonly ILoggingAdapter _log = Context.GetLogger();

    private int _reconnectAttempts = 0;

    /// <summary>
    /// We always use this for the initial socket read
    /// </summary>
    private readonly Memory<byte> _readBuffer;

    public TcpTransportActor(MqttClientTcpOptions tcpOptions, int maxFrameSize, bool automaticRestarts,
        MqttProtocolVersion protocolVersion)
    {
        TcpOptions = tcpOptions;
        MaxFrameSize = maxFrameSize;
        AutomaticRestarts = automaticRestarts;

        State = new ConnectionState(_writesToTransport.Writer, _readsFromTransport.Reader, _whenTerminated.Task,
            protocolVersion, maxFrameSize);
        _readBuffer = new byte[maxFrameSize];
    }

    /*
     * FSM:
     * OnReceive (nothing has happened) --> CreateTcpTransport --> TransportCreated BECOME Connecting
     * Connecting --> DoConnect --> Connecting (already connecting) --> ConnectResult (Connected) BECOME Running
     * Running --> DoReadFromSocketAsync --> Running (read data from socket) --> DoWriteToSocketAsync --> Running (write data to socket)
     */

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case CreateTcpTransport when State.Status == ConnectionStatus.NotStarted:
            {
                // first things first - attempt to create the socket
                CreateTcpClient();

                // return the transport to the client
                var tcpTransport = new TcpTransport(_log, State, Self);
                Sender.Tell(tcpTransport);
                _log.Debug("Created new TCP transport for client connecting to [{0}]", TcpOptions.RemoteEndpoint);
                Stash.UnstashAll();
                Become(TransportCreated);
                break;
            }
            case CreateTcpTransport when State.Status != ConnectionStatus.NotStarted:
            {
                _log.Warning("Attempted to create a TCP transport when one already exists.");
                break;
            }
            default:
                Stash.Stash(); // stash all other messages
                break;
        }
    }

    private void CreateTcpClient()
    {
        _tcpClient = new TcpClient(TcpOptions.AddressFamily)
        {
            ReceiveBufferSize = (int)TcpOptions.BufferSize,
            SendBufferSize = (int)TcpOptions.BufferSize,
            NoDelay = true,
            LingerState = new LingerOption(true, 2) // give us a little time to flush the socket
        };
    }

    private async Task DoConnectAsync(IPAddress[] addresses, int port, IActorRef destination,
        CancellationToken ct = default)
    {
        ConnectResult connectResult;
        try
        {
            if (addresses.Length == 0)
                throw new ArgumentException("No IP addresses provided to connect to.", nameof(addresses));

            Debug.Assert(_tcpClient != null, nameof(_tcpClient) + " != null");
            await _tcpClient.ConnectAsync(addresses, port, ct);
            connectResult = new ConnectResult(ConnectionStatus.Connected, "Connected.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect to [{0}]", TcpOptions.RemoteEndpoint);
            connectResult = new ConnectResult(ConnectionStatus.Failed, ex.Message);
        }

        // let both parties know the result of the connection attempt
        destination.Tell(connectResult);
        _closureSelf.Tell(connectResult);
    }

    private readonly IActorRef _closureSelf = Context.Self;

    private void TransportCreated(object message)
    {
        switch (message)
        {
            case DoConnect connect when State.Status == ConnectionStatus.NotStarted:
            {
                if (State.Status == ConnectionStatus.Connected)
                {
                    _log.Warning(
                        "Attempted to connect to [{0}] when already connected - sending back positive ACK but you probably have bugs in your code",
                        TcpOptions.RemoteEndpoint);
                    Sender.Tell(new ConnectResult(ConnectionStatus.Connected, "Already connected."));
                    break;
                }

                var sender = Sender;


                // check to see what type of endpoint we're dealing with
                if (TcpOptions.RemoteEndpoint is DnsEndPoint dns)
                {
                    // need to resolve DNS to an IP address
                    async Task ResolveAndConnect(CancellationToken ct)
                    {
                        var resolved = await Dns.GetHostAddressesAsync(dns.Host, ct);
                        await DoConnectAsync(resolved, dns.Port, sender, ct);
                    }

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    ResolveAndConnect(connect.Cancel);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                }
                else
                {
                    // we have an IP address already
                    var ip = (IPEndPoint)TcpOptions.RemoteEndpoint;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    DoConnectAsync([ip.Address], ip.Port, sender, connect.Cancel);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                }

                // set status to connecting
                State.Status = ConnectionStatus.Connecting;
                break;
            }
            case DoConnect _ when State.Status == ConnectionStatus.Connecting:
            {
                _log.Warning("Already attempting to connect to [{0}]",
                    TcpOptions.RemoteEndpoint);
                Sender.Tell(new ConnectResult(ConnectionStatus.Connecting, "Already connecting."));
                break;
            }
            case ConnectResult { Status: ConnectionStatus.Connected }:
            {
                _log.Info("Successfully connected to [{0}]", TcpOptions.RemoteEndpoint);
                State.Status = ConnectionStatus.Connected;
                _reconnectAttempts = 0; // reset the reconnect attempts
                _tcpStream = _tcpClient!.GetStream();

                BecomeRunning();
                break;
            }
            case ConnectResult { Status: ConnectionStatus.Failed }:
            {
                _log.Error("Failed to connect to [{0}]: {1}", TcpOptions.RemoteEndpoint, message);
                State.Status = ConnectionStatus.Failed;
                DoReconnect();
                break;
            }
            default:
                Stash.Stash();
                break;
        }
    }

    private void BecomeRunning()
    {
        Become(Running);
        Stash.UnstashAll();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        DoReadFromSocketAsync(State.ShutDownCts.Token);
        DoWriteToSocketAsync(State.ShutDownCts.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    }

    private async Task DoWriteToSocketAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (buffer, readableBytes) = await _writesToTransport.Reader.ReadAsync(ct);
                try
                {
                    await _tcpStream!.WriteAsync(buffer.Memory.Slice(0, readableBytes), ct);
                }
                finally
                {
                    // free the pooled buffer
                    buffer.Dispose();
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to write to socket.");
                // we are done writing
                _closureSelf.Tell(new ConnectionUnexpectedlyClosed(ConnectionTerminatedReason.Error, ex.Message));
                // socket was closed
                // abort ongoing reads and writes, but don't shutdown the transport
                await State.ShutDownCts.CancelAsync();
                return;
            }
        }
    }

    private async Task DoReadFromSocketAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // read from the socket
            var bytesRead = await _tcpStream!.ReadAsync(_readBuffer, ct);
            if (bytesRead == 0)
            {
                // we are done reading
                _closureSelf.Tell(ReadFinished.Instance);
                // socket was closed
                // abort ongoing reads and writes, but don't shutdown the transport
                await State.ShutDownCts.CancelAsync();
                return;
            }

            // send the data to the reader

            // copy data into new appropriately sized buffer (we do not do pooling on reads)
            Memory<byte> realBuffer = new byte[bytesRead];
            _readBuffer.Span.Slice(0, bytesRead).CopyTo(realBuffer.Span);
            var owner = new UnsharedMemoryOwner<byte>(realBuffer);
            _readsFromTransport.Writer.TryWrite((owner, bytesRead));
        }
    }

    private void Running(object message)
    {
        switch (message)
        {
            case DoClose:
            {
                // we are done
                Context.Stop(Self); // will perform a full shutdown
                break;
            }

            case ConnectionUnexpectedlyClosed closed:
            {
                _log.Warning("Connection to [{0}] was unexpectedly closed: {1}", TcpOptions.RemoteEndpoint,
                    closed.ReasonMessage);
                DisposeSocket(ConnectionStatus.Aborted); // got aborted by broker
                DoReconnect();
                break;
            }
        }
    }

    private void DisposeSocket(ConnectionStatus newStatus)
    {
        if (_tcpStream is null && _tcpClient is null)
            return; // already disposed

        try
        {
            State.Status = newStatus;


            // stop reading from the socket
            State.ShutDownCts.Cancel();

            _tcpStream?.Dispose();
            _tcpClient?.Close();
            _tcpClient?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to cleanly dispose of TCP client and stream.");
        }
        finally
        {
            _tcpStream = null;
            _tcpClient = null;
        }
    }

    /// <summary>
    /// This is for when we run out of retries or we were explicitly told to close the connection.
    /// </summary>
    private void FullShutdown(ConnectionTerminatedReason reason = ConnectionTerminatedReason.Normal)
    {
        var newStatus = reason switch
        {
            ConnectionTerminatedReason.Error => ConnectionStatus.Failed,
            ConnectionTerminatedReason.Normal => ConnectionStatus.Disconnected,
            ConnectionTerminatedReason.CouldNotConnect => ConnectionStatus.Failed,
            _ => ConnectionStatus.Aborted
        };

        DisposeSocket(newStatus);

        // mark the channels as complete
        _writesToTransport.Writer.Complete();
        _readsFromTransport.Writer.Complete();

        _whenTerminated.TrySetResult(reason);
    }

    private void DoReconnect()
    {
        if (_reconnectAttempts >= TcpOptions.MaxReconnectAttempts)
        {
            _log.Warning("Failed to reconnect to [{0}] after {1} attempts.", TcpOptions.RemoteEndpoint,
                _reconnectAttempts);
            FullShutdown(ConnectionTerminatedReason.CouldNotConnect);
            Context.Stop(Self);
            return;
        }

        _reconnectAttempts++;
        _log.Info("Attempting to reconnect to [{0}] - attempt {1}", TcpOptions.RemoteEndpoint, _reconnectAttempts);
        CreateTcpClient();
        Become(TransportCreated);
        State.ShutDownCts = new CancellationTokenSource(); // need a fresh CTS
        CancellationTokenSource cts = new(TcpOptions.ReconnectInterval);
        Self.Tell(new DoConnect(cts.Token));
    }


    protected override void PostStop()
    {
        FullShutdown();
    }

    // These will both get assigned automatically by Akka.NET
    public IStash Stash { get; set; } = null!;
}