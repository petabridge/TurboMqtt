// -----------------------------------------------------------------------
// <copyright file="TcpConnectionManager.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using TurboMqtt.Core.Client;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.IO;

/// <summary>
/// Actor responsible for managing all TCP connections for the MQTT client.
/// </summary>
internal sealed class TcpConnectionManager : UntypedActor
{
    public sealed record CreateTcpTransport(
        MqttClientTcpOptions Options,
        int MaxFrameSize,
        bool AutomaticRestarts,
        MqttProtocolVersion ProtocolVersion);
    
    private readonly ILoggingAdapter _log = Context.GetLogger();

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case CreateTcpTransport create:
            {
                _log.Debug("Creating new TCP transport for [{0}]", create);
                var tcpTransport = Context.ActorOf(Props.Create(() => new TcpTransportActor(create.Options,
                    create.MaxFrameSize, create.AutomaticRestarts, create.ProtocolVersion)));
                tcpTransport.Tell(TcpTransportActor.CreateTcpTransport.Instance);
                Sender.Tell(tcpTransport);
                break;
            }
        }
    }
}