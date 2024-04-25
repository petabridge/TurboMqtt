// -----------------------------------------------------------------------
// <copyright file="TcpConnectionManager.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using TurboMqtt.Client;
using TurboMqtt.Protocol;

namespace TurboMqtt.IO.Tcp;

/// <summary>
/// Actor responsible for managing all TCP connections for the MQTT client.
/// </summary>
internal sealed class TcpConnectionManager : UntypedActor
{
    public sealed record CreateTcpTransport(
        MqttClientTcpOptions Options,
        MqttProtocolVersion ProtocolVersion);
    
    private readonly ILoggingAdapter _log = Context.GetLogger();

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case CreateTcpTransport create:
            {
                _log.Debug("Creating new TCP transport for [{0}]", create);
                var tcpTransport = Context.ActorOf(Props.Create(() => new TcpTransportActor(create.Options)));
                Sender.Tell(tcpTransport);
                break;
            }
        }
    }
}