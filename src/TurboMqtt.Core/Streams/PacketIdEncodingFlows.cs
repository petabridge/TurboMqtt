// -----------------------------------------------------------------------
// <copyright file="PacketIdEncodingFlows.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka;
using Akka.Streams;
using TurboMqtt.Core.PacketTypes;

namespace TurboMqtt.Core.Streams;

/// <summary>
/// Used to encode packet IDs into the <see cref="MqttPacketWithId"/> instances
/// </summary>
public static class PacketIdEncodingFlows
{
    /// <summary>
    /// Creates a flow that will assign packet ids to <see cref="MqttPacketWithId"/> instances
    /// in accordance with the MQTT 3.1.1 and MQTT 5.0 specifications.
    /// </summary>
    /// <returns>An Akka.Streams graph - still needs to be connected to a source and a sink in order to run.</returns>
    public static IGraph<FlowShape<MqttPacket, MqttPacket>, NotUsed> PacketIdEncoding()
    {
        var g = new PacketIdFlow();
        return g;
    }
}