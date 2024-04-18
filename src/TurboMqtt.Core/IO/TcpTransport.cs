// -----------------------------------------------------------------------
// <copyright file="TcpTransport.cs" company="Petabridge, LLC">
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
/// TCP implementation of <see cref="IMqttTransport"/>.
/// </summary>
internal sealed class TcpTransport : IMqttTransport
{
    internal TcpTransport(ILoggingAdapter log, TcpTransportActor.ConnectionState state, IActorRef connectionActor)
    {
        Log = log;
        State = state;
        _connectionActor = connectionActor;
        Reader = state.Reader;
        Writer = state.Writer;
        MaxFrameSize = state.MaxFrameSize;
    }

    public ILoggingAdapter Log { get; }
    public ConnectionStatus Status => State.Status;

    private TcpTransportActor.ConnectionState State { get; }
    private readonly IActorRef _connectionActor;

    public Task<ConnectionTerminatedReason> WaitForTermination()
    {
        return State.WhenTerminated;
    }

    public Task CloseAsync(CancellationToken ct = default)
    {
        return _connectionActor.Ask<TcpTransportActor.CloseResult>(new TcpTransportActor.DoClose(ct), ct);
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        return _connectionActor.Ask<TcpTransportActor.ConnectResult>(new TcpTransportActor.DoConnect(ct), ct);
    }

    public int MaxFrameSize { get; }
    public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> Writer { get; }
    public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> Reader { get; }
}

/// <summary>
/// Actor responsible for managing the TCP transport layer for MQTT.
/// </summary>
internal sealed class TcpTransportActor : UntypedActor
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

    public sealed record CloseResult(ConnectionStatus Status, string ReasonMessage);
    
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

    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _writesToTransport =
        Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();

    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _readsFromTransport =
        Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();

    private readonly TaskCompletionSource<ConnectionTerminatedReason> _whenTerminated = new();
    private readonly ILoggingAdapter _log = Context.GetLogger();

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

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case CreateTcpTransport when State.Status == ConnectionStatus.NotStarted:
            {
                // first things first - attempt to create the socket
                _tcpClient = new TcpClient(TcpOptions.AddressFamily)
                {
                    ReceiveBufferSize = (int)TcpOptions.BufferSize,
                    SendBufferSize = (int)TcpOptions.BufferSize,
                    NoDelay = true,
                    LingerState = new LingerOption(true, 2) // give us a little time to flush the socket
                };

                // return the transport to the client
                var tcpTransport = new TcpTransport(_log, State, Self);
                Sender.Tell(tcpTransport);
                _log.Debug("Created new TCP transport for client connecting to [{0}]", TcpOptions.RemoteEndpoint);
                Become(TransportCreated);
                break;
            }
            case CreateTcpTransport when State.Status != ConnectionStatus.NotStarted:
            {
                _log.Warning("Attempted to create a TCP transport when one already exists.");
                break;
            }
        }
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
                        await DoConnectAsync(resolved, dns.Port, Sender, ct);
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
                    DoConnectAsync(new[] { ip.Address }, ip.Port, Sender, connect.Cancel);
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
                BecomeRunning();
                break;
            }
            case ConnectResult { Status: ConnectionStatus.Failed }:
            {
                // TODO: automatically retry the connection
                _log.Error("Failed to connect to [{0}]: {1}", TcpOptions.RemoteEndpoint, message);
                State.Status = ConnectionStatus.Failed;
                break;
            }
        }
    }
    
    private void BecomeRunning()
    {
        Become(Running);
        
        // need to begin reading from the socket and writing to it
        
        
    }
    
    private async Task DoReadFromSocketAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var stream = _tcpClient!.GetStream();
            
            // read from the socket
            var bytesRead = await stream.ReadAsync(_readBuffer, ct);
            if (bytesRead == 0)
            {
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
        
    }
}