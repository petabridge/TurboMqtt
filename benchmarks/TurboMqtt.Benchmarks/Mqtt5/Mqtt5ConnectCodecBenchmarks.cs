// -----------------------------------------------------------------------
// <copyright file="Mqtt5ConnectCodecBenchmarks.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Benchmarks.Mqtt5;

[Config(typeof(MicroBenchmarkConfig))]
public class Mqtt5ConnectCodecBenchmarks
{
    private readonly Mqtt5Decoder _decoder = new();

    private readonly ConnectPacket _connectPacket = new ConnectPacket(MqttProtocolVersion.V5_0)
    {
        ClientId = "benchmark-client",
        UserName = "benchmark-user",
        Password = "benchmark-password",
        ProtocolName = "MQTT",
        KeepAliveSeconds = 2,
        Flags = new ConnectFlags
        {
            CleanSession = true,
            WillFlag = false,
            WillQoS = QualityOfService.AtMostOnce,
            WillRetain = false,
            UsernameFlag = true,
            PasswordFlag = true
        }
    };

    private byte[] _writeableBytes = Array.Empty<byte>();
    private ReadOnlyMemory<byte> _encodedConnectPacket;
    private PacketSize _estimatedConnectPacketSize;

    [GlobalSetup]
    public void Setup()
    {
        var estimate = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(_connectPacket);
        _writeableBytes = new byte[estimate.TotalSize];
        var memory = new Memory<byte>(new byte[estimate.TotalSize]);
        _encodedConnectPacket = memory;
        Mqtt5Encoder.EncodePacket(_connectPacket, ref memory, estimate);
        _estimatedConnectPacketSize = estimate;
    }

    private Memory<byte> _writeableBuffer;

    [IterationSetup]
    public void IterationSetup()
    {
        _writeableBuffer = new Memory<byte>(_writeableBytes);
    }

    [Benchmark]
    public ImmutableList<MqttPacket> DecodeConnectPacket()
    {
        _decoder.TryDecode(_encodedConnectPacket, out var packets);
        return packets;
    }

    [Benchmark]
    public int EncodeConnectPacket()
    {
        return Mqtt5Encoder.EncodePacket(_connectPacket, ref _writeableBuffer, _estimatedConnectPacketSize);
    }
}
