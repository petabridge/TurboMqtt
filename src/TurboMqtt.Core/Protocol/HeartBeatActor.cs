// -----------------------------------------------------------------------
// <copyright file="HeartBeatActor.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboMqtt.Core.Client;
using TurboMqtt.Core.PacketTypes;

namespace TurboMqtt.Core.Protocol;

/// <summary>
/// Triggered when the server hasn't received a heartbeat from the client in a while.
///
/// Leverages <see cref="PingReqPacket"/> and <see cref="PingRespPacket"/> to do so.
/// </summary>
internal sealed class FailureDetector
{
    private readonly IActorRef _failedConnectionActor;

    public FailureDetector(TimeSpan heartbeatInterval, IActorRef failedConnectionActor)
    {
        HeartbeatInterval = heartbeatInterval;
        _failedConnectionActor = failedConnectionActor;
    }

    public TimeSpan HeartbeatInterval { get; }

    public void Trigger(Exception ex)
    {
        var code = ex switch
        {
            TimeoutException _ => DisconnectReasonCode.KeepAliveTimeout,
            _ => DisconnectReasonCode.UnspecifiedError
        };
            
        
        _failedConnectionActor.Tell(new ClientStreamOwner.ServerDisconnect(DisconnectReasonCode.ServerBusy));
    }
}

/// <summary>
/// Responsible for managing the keep-alive heartbeat between the client and the broker.
/// </summary>
internal sealed class HeartBeatActor : UntypedActor, IWithTimers
{
    /// <summary>
    /// Used to trigger a heartbeat failure
    /// </summary>
    public sealed class HeartbeatTimeout
    {
        public static readonly HeartbeatTimeout Instance = new();

        private HeartbeatTimeout()
        {
        }
    }

    /// <summary>
    /// Used to send a heartbeat to the broker.
    /// </summary>
    public sealed class CheckHeartbeat
    {
        public static readonly CheckHeartbeat Instance = new();

        private CheckHeartbeat()
        {
        }
    }

    private const string HeartbeatTimerKey = "heartbeat";
    private const string HeartbeatFailureTimerKey = "heartbeat-failure";

    /// <summary>
    /// Used to send packets back to the broker.
    /// </summary>
    private readonly ChannelWriter<MqttPacket> _outboundPackets;

    private DateTime _lastHeartbeat = DateTime.UtcNow;
    private readonly FailureDetector _failureDetector;
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public HeartBeatActor(ChannelWriter<MqttPacket> outboundPackets, FailureDetector failureDetector)
    {
        _outboundPackets = outboundPackets;
        _failureDetector = failureDetector;
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case CheckHeartbeat:
            {
                _log.Debug("Sending PINGREQ to broker.");
                _outboundPackets.TryWrite(PingReqPacket.Instance);
                return;
            }
            case PingRespPacket:
            {
                _log.Debug("Received PINGRESP from broker.");
                _lastHeartbeat = DateTime.UtcNow;
                RestartHeartbeatTimeout();
                return;
            }
            case HeartbeatTimeout:
            {
                var now = DateTime.UtcNow;
                var elapsed = now - _lastHeartbeat;
                if (elapsed > _failureDetector.HeartbeatInterval)
                {
                    var errorMsg = $"No heartbeat received from broker in {elapsed}";
                    var ex = new TimeoutException(errorMsg);
                    _log.Error(ex, errorMsg);
                    _failureDetector.Trigger(ex); // should result in the listener being notified
                }

                break;
            }
            default:
                Unhandled(message);
                break;
        }
    }

    protected override void PreStart()
    {
        // heartbeat interval will be 1/4th of the keep-alive interval
        var heartbeatInterval = TimeSpan.FromMilliseconds(_failureDetector.HeartbeatInterval.TotalMilliseconds / 4);
        Timers.StartPeriodicTimer(HeartbeatTimerKey, CheckHeartbeat.Instance, heartbeatInterval);
        RestartHeartbeatTimeout();
    }

    private void RestartHeartbeatTimeout()
    {
        if(Timers.IsTimerActive(HeartbeatFailureTimerKey))
            Timers.Cancel(HeartbeatFailureTimerKey);
        Timers.StartSingleTimer(HeartbeatFailureTimerKey, HeartbeatTimeout.Instance,
            _failureDetector.HeartbeatInterval);
    }

    public ITimerScheduler Timers { get; set; } = null!;
}