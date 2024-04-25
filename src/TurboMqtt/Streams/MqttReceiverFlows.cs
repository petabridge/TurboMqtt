// -----------------------------------------------------------------------
// <copyright file="MqttReceiverFlows.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using Akka;
using Akka.Streams;
using TurboMqtt.PacketTypes;

namespace TurboMqtt.Streams;

/// <summary>
/// Used to power the business logic for receiving MQTT packets from the broker.
/// </summary>
internal static class MqttReceiverFlows
{
    public static IGraph<FlowShape<MqttPacket, MqttPacket>, NotUsed> ClientAckingFlow(int bufferSize, TimeSpan bufferExpiry, ChannelWriter<MqttPacket> outboundPackets, MqttRequiredActors actors)
    {
        var g = new ClientAckingFlow(bufferSize, bufferExpiry, outboundPackets, actors);
        return g;
    }
}