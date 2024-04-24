// -----------------------------------------------------------------------
// <copyright file="ClientManagerActor.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using TurboMqtt.Core.IO;
using TurboMqtt.Core.IO.Tcp;
using TurboMqtt.Core.Streams;

namespace TurboMqtt.Core.Client;

/// <summary>
/// Aggregate root actor for managing all TurboMqtt clients.
/// </summary>
internal sealed class ClientManagerActor : UntypedActor
{
    public sealed class StartClientActor(string clientId)
    {
        public string ClientId { get; } = clientId;
    }

    public sealed class ClientDied(string clientId)
    {
        public string ClientId { get; } = clientId;
    }

    /// <summary>
    /// Used to help generate unique actor names for each client.
    /// </summary>
    private int _clientCounter = 0;
    private readonly HashSet<string> _activeClientIds = new();
    private IActorRef _tcpConnectionManager = ActorRefs.Nobody;

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case StartClientActor start:
            {
                if (!_activeClientIds.Add(start.ClientId))
                {
                    Sender.Tell(new Status.Failure(new InvalidOperationException($"Client with ID {start.ClientId} already exists.")));
                }
                else
                {
                    var actorName = Uri.EscapeDataString($"mqttclient-{start.ClientId}-{_clientCounter++}");
                    var client = Context.ActorOf(Props.Create(() => new ClientStreamOwner()), actorName);
                    Context.WatchWith(client, new ClientDied(start.ClientId));
                    Sender.Tell(client);
                }
                break;
            }
            case ClientDied died:
            {
                _activeClientIds.Remove(died.ClientId);
                break;
            }
            case TcpConnectionManager.CreateTcpTransport start:
            {
                // forward this message to our TcpConnectionManager
                _tcpConnectionManager.Forward(start);
                break;
            }
        }
    }

    protected override void PreStart()
    {
        _tcpConnectionManager = Context.ActorOf(Props.Create<TcpConnectionManager>(), "tcp");
    }
}