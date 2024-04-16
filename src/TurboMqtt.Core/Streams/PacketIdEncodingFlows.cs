// -----------------------------------------------------------------------
// <copyright file="PacketIdEncodingFlows.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboMqtt.Core.PacketTypes;

namespace TurboMqtt.Core.Streams;

/// <summary>
/// Used to encode packet IDs into the <see cref="MqttPacketWithId"/> instances
/// </summary>
public static class PacketIdEncodingFlows
{
    /// <summary>
    /// Determines if the packet ID is required for the given <see cref="MqttPacket"/>
    /// </summary>
    /// <param name="packet">The type of packet</param>
    public static bool IsPacketIdRequired(MqttPacket packet)
    {
        if(packet is PublishPacket publishPacket && publishPacket.QualityOfService != QualityOfService.AtMostOnce)
            return true;
        return packet is MqttPacketWithId;
    }
    
    /// <summary>
    /// Creates a flow that will assign packet ids to <see cref="MqttPacketWithId"/> instances
    /// in accordance with the MQTT 3.1.1 and MQTT 5.0 specifications.
    /// </summary>
    /// <returns>An Akka.Streams graph - still needs to be connected to a source and a sink in order to run.</returns>
    public static IGraph<FlowShape<MqttPacketWithId, MqttPacket>, NotUsed> PacketIdEncoding()
    {
        var c = Flow.Create<MqttPacketWithId>()
            .Zip(new PacketIdSource())
            .Select(c =>
            {
                c.Item1.PacketId = c.Item2;
                return (MqttPacket)c.Item1;
            });

        return c;
    }
}