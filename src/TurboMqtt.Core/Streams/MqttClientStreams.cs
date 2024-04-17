// -----------------------------------------------------------------------
// <copyright file="MqttClientStreams.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Threading.Channels;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboMqtt.Core.IO;
using TurboMqtt.Core.PacketTypes;

namespace TurboMqtt.Core.Streams;

internal static class MqttClientStreams
{
    public static IGraph<SinkShape<MqttPacket>> Mqtt311OutboundPacketSink(IMqttTransport transport,
        MemoryPool<byte> memoryPool, int maxFrameSize)
    {
        var finalSink = Sink.FromWriter(transport.Writer, true);
        var g = Flow.Create<MqttPacket>()
            .Via(PacketIdEncodingFlows.PacketIdEncoding())
            .Via(MqttEncodingFlows.Mqtt311Encoding(memoryPool, maxFrameSize))
            .To(finalSink);

        return g;
    }

    public static IGraph<SourceShape<MqttMessage>> Mqtt311InboundMessageSource(IMqttTransport transport, 
        ChannelWriter<MqttPacket> outboundPackets,
        MqttRequiredActors actors, int maxRememberedPacketIds, TimeSpan packetIdExpiry)
    {
        var finalSource = (ChannelSource.FromReader(transport.Reader)
            .Via(MqttDecodingFlows.Mqtt311Decoding())
            .Via(MqttReceiverFlows.ClientAckingFlow(maxRememberedPacketIds, packetIdExpiry, outboundPackets, actors))
            .Where(c => c.PacketType == MqttPacketType.Publish)
            .Select(c => ((PublishPacket)c).FromPacket()));

        return finalSource;
    }
}