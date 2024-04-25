// -----------------------------------------------------------------------
// <copyright file="DecodingBenchmarks.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Benchmarks.Mqtt311;

[Config(typeof(MicroBenchmarkConfig))]
public class Mqtt311ConnectCodecBenchmarks
{
    private readonly Mqtt311Decoder _decoder = new();

    private readonly ConnectPacket _connectPacket = new ConnectPacket(MqttProtocolVersion.V3_1_1)
    {
        ClientId = "benchmark-client",
        Username = "benchmark-user",
        Password = "benchmark-password",
        ProtocolName = "MQTT",
        KeepAliveSeconds = 2,
        ConnectFlags = new ConnectFlags
        {
            CleanSession = true,
            WillFlag = false,
            WillQoS = QualityOfService.AtMostOnce,
            WillRetain = false
        },
        Will = new MqttLastWill("benchmark-topic", new ReadOnlyMemory<byte>([0x1, 0x2, 0x3, 0x4]))
        {
            ResponseTopic = null,
            WillCorrelationData = null,
            ContentType = null,
            PayloadFormatIndicator = PayloadFormatIndicator.Unspecified,
            DelayInterval = default,
            MessageExpiryInterval = 0,
            WillProperties = null
        }
    };

    private byte[] _writeableBytes = Array.Empty<byte>();
    private ReadOnlyMemory<byte> _encodedConnectPacket;
    private int _estimatedConnectPacketSize;
    private int _estimatedHeaderSize;

    [GlobalSetup]
    public void Setup()
    {
        var estimate = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(_connectPacket);
        var headerSize =
            MqttPacketSizeEstimator.GetPacketLengthHeaderSize(estimate) + 1; // add 1 for the lead byte
        _writeableBytes = new byte[estimate + headerSize];
        var memory = new Memory<byte>(new byte[estimate + headerSize]);
        _encodedConnectPacket = memory;
        Mqtt311Encoder.EncodePacket(_connectPacket, ref memory, estimate);
        _estimatedConnectPacketSize = estimate;
        _estimatedHeaderSize = headerSize;
    }

    private Memory<byte> _writeableBuffer;

    [IterationSetup]
    public void IterationSetup()
    {
        _writeableBuffer = new Memory<byte>(_writeableBytes);
    }

    [Benchmark]
    public int EstimatedConnectPacketSize() => MqttPacketSizeEstimator.EstimateMqtt3PacketSize(_connectPacket);

    [Benchmark]
    public ImmutableList<MqttPacket> DecodeConnectPacket()
    {
        _decoder.TryDecode(_encodedConnectPacket, out var packets);
        return packets;
    }

    [Benchmark]
    public int EncodeConnectPacket()
    {
        return Mqtt311Encoder.EncodePacket(_connectPacket, ref _writeableBuffer, _estimatedConnectPacketSize);
    }
}