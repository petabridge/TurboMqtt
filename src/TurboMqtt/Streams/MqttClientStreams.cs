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
       int maxFrameSize, int maxPacketSize, bool withTelemetry = true)
    {
        var finalSink = MqttEncodingFlows.Mqtt311EncodingSink(transport.Channel.Output, maxFrameSize, maxPacketSize);
        if (withTelemetry)
            return Flow.Create<MqttPacket>()
                .Via(OpenTelemetryFlows.MqttPacketRateTelemetryFlow(MqttProtocolVersion.V3_1_1, clientId,
                    OpenTelemetrySupport.Direction.Outbound))
                // .Via(OpenTelemetryFlows.MqttBitRateTelemetryFlow(MqttProtocolVersion.V3_1_1, clientId,
                //     OpenTelemetrySupport.Direction.Outbound))
                .To(finalSink);

        return Flow.Create<MqttPacket>()
            .To(finalSink);
    }

    public static Source<MqttMessage, NotUsed> Mqtt311InboundMessageSource(string clientId, IMqttTransport transport,
        ChannelWriter<MqttPacket> outboundPackets,
        MqttRequiredActors actors, int maxRememberedPacketIds, TimeSpan packetIdExpiry, TaskCompletionSource<DisconnectPacket> disconnectPromise, bool withTelemetry = true)

    {
        if (withTelemetry)
            return MqttDecodingFlows.Mqtt311DecoderSource(transport.Channel.Input)
                .Async()
                .Via(OpenTelemetryFlows.MqttMultiPacketRateTelemetryFlow(MqttProtocolVersion.V3_1_1, clientId,
                    OpenTelemetrySupport.Direction.Inbound))
                .Via(MqttReceiverFlows.ClientAckingFlow(outboundPackets,
                    actors, disconnectPromise))
                .Async()
                .Where(c => c.PacketType == MqttPacketType.Publish)
                .Select(c => ((PublishPacket)c).FromPacket());

        return MqttDecodingFlows.Mqtt311DecoderSource(transport.Channel.Input)
            .Async()
            .Via(MqttReceiverFlows.ClientAckingFlow(outboundPackets, actors, disconnectPromise))
            .Async()
            .Where(c => c.PacketType == MqttPacketType.Publish)
            .Select(c => ((PublishPacket)c).FromPacket());
    }
}