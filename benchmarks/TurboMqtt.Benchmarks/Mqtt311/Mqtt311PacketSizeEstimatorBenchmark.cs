// -----------------------------------------------------------------------
// <copyright file="Mqtt311PacketSizeEstimatorBenchmark.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Benchmarks.Mqtt311;

public class Mqtt311PacketSizeEstimatorBenchmark
{
    public static IEnumerable<object> Packets() // for multiple arguments it's an IEnumerable of array of objects (object[])
    {
        yield return PingReqPacket.Instance;
        yield return PingRespPacket.Instance;
        yield return DisconnectPacket.Instance;
        yield return new ConnectPacket(MqttProtocolVersion.V3_1_1)
        {
            ClientId = "client1",
            UserName = "user1",
            Password = "password1",
            KeepAliveSeconds = 5,
            ConnectFlags = new ConnectFlags()
            {
                CleanSession = true,
                PasswordFlag = true,
                UsernameFlag = true
            }
        };
        yield return new ConnAckPacket() { ReasonCode = ConnAckReasonCode.Success };
        yield return new SubscribePacket()
        {
            PacketId = 1,
            Topics = new[]
            {
                new TopicSubscription("test1")
                    { Options = new SubscriptionOptions() { QoS = QualityOfService.AtLeastOnce } }
            }
        };
        yield return new SubAckPacket()
        {
            PacketId = 1,
            ReasonCodes = new[] { MqttSubscribeReasonCode.GrantedQoS0 }
        };
        yield return new UnsubscribePacket()
        {
            PacketId = 1,
            Topics = new[] { "test1" }
        };
        yield return new UnsubAckPacket() { PacketId = 1, Duplicate = false };
        yield return new PublishPacket(QualityOfService.AtLeastOnce, false, false, "topic1")
        {
            PacketId = 1,
            Payload = new ReadOnlyMemory<byte>(new byte[1024])
        };
        yield return new PubAckPacket() { PacketId = 1 };
        yield return new PubRecPacket() { PacketId = 1 };
        yield return new PubRelPacket() { PacketId = 1 };
        yield return new PubCompPacket() { PacketId = 1 };
    }


    [Benchmark]
    [ArgumentsSource(nameof(Packets))]
    public int EstimateMqttPacketSize(MqttPacket packet)
    {
        return MqttPacketSizeEstimator.EstimateMqtt3PacketSize(packet);
    }
}