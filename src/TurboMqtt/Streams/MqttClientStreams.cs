// -----------------------------------------------------------------------
// <copyright file="MqttClientStreams.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Threading.Channels;
using Akka;
using Akka.Streams.Dsl;
using TurboMqtt.IO;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;
using TurboMqtt.Telemetry;

namespace TurboMqtt.Streams;

/// <summary>
/// INTERNAL API
/// </summary>
internal static class MqttClientStreams
{
    public static Sink<MqttPacket, NotUsed> Mqtt311OutboundPacketSink(string clientId, IMqttTransport transport,
        MemoryPool<byte> memoryPool, int maxFrameSize, int maxPacketSize, bool withTelemetry = true)
    {
        var finalSink = Sink.FromWriter(transport.Writer, true);
        if (withTelemetry)
            return Flow.Create<MqttPacket>()
                .Via(OpenTelemetryFlows.MqttPacketRateTelemetryFlow(MqttProtocolVersion.V3_1_1, clientId,
                    OpenTelemetrySupport.Direction.Outbound))
                .Via(MqttEncodingFlows.Mqtt311Encoding(memoryPool, maxFrameSize, maxPacketSize))
                .Via(OpenTelemetryFlows.MqttBitRateTelemetryFlow(MqttProtocolVersion.V3_1_1, clientId,
                    OpenTelemetrySupport.Direction.Outbound))
                .To(finalSink);

        return Flow.Create<MqttPacket>()
            .Via(MqttEncodingFlows.Mqtt311Encoding(memoryPool, maxFrameSize, maxPacketSize))
            .To(finalSink);
    }

    public static Source<MqttMessage, NotUsed> Mqtt311InboundMessageSource(string clientId, IMqttTransport transport,
        ChannelWriter<MqttPacket> outboundPackets,
        MqttRequiredActors actors, int maxRememberedPacketIds, TimeSpan packetIdExpiry, bool withTelemetry = true)

    {
        if (withTelemetry)
            return (ChannelSource.FromReader(transport.Reader)
                .Via(OpenTelemetryFlows.MqttBitRateTelemetryFlow(MqttProtocolVersion.V3_1_1, clientId,
                    OpenTelemetrySupport.Direction.Inbound))
                .Via(MqttDecodingFlows.Mqtt311Decoding())
                .Via(OpenTelemetryFlows.MqttPacketRateTelemetryFlow(MqttProtocolVersion.V3_1_1, clientId,
                    OpenTelemetrySupport.Direction.Inbound))
                .Via(MqttReceiverFlows.ClientAckingFlow(maxRememberedPacketIds, packetIdExpiry, outboundPackets,
                    actors))
                .Where(c => c.PacketType == MqttPacketType.Publish)
                .Select(c => ((PublishPacket)c).FromPacket()));

        return (ChannelSource.FromReader(transport.Reader)
            .Via(MqttDecodingFlows.Mqtt311Decoding())
            .Via(MqttReceiverFlows.ClientAckingFlow(maxRememberedPacketIds, packetIdExpiry, outboundPackets, actors))
            .Where(c => c.PacketType == MqttPacketType.Publish)
            .Select(c => ((PublishPacket)c).FromPacket()));
    }
}