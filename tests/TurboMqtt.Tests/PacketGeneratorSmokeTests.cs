// -----------------------------------------------------------------------
// <copyright file="PacketGeneratorSmokeTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using FsCheck;
using TurboMqtt.PacketTypes;

namespace TurboMqtt.Tests;

/// <summary>
/// Smoke tests that verify every MQTT 3.1.1 packet generator compiles and
/// produces non-null packets when sampled. Satisfies Task 2.1 Done-when criterion:
/// "All generators compile and produce non-null packets when sampled".
/// </summary>
public class PacketGeneratorSmokeTests
{
    [Fact]
    public void AllIndividualGeneratorsShouldProduceNonNullPackets()
    {
        var arbitraries = new (string name, FsCheck.Arbitrary<MqttPacket> arb)[]
        {
            ("Connect",     PacketGenerators.ConnectPacketArb()),
            ("ConnAck",     PacketGenerators.ConnAckPacketArb()),
            ("Publish",     PacketGenerators.PublishPacketArb()),
            ("PubAck",      PacketGenerators.PubAckPacketArb()),
            ("PubRec",      PacketGenerators.PubRecPacketArb()),
            ("PubRel",      PacketGenerators.PubRelPacketArb()),
            ("PubComp",     PacketGenerators.PubCompPacketArb()),
            ("Subscribe",   PacketGenerators.SubscribePacketArb()),
            ("SubAck",      PacketGenerators.SubAckPacketArb()),
            ("Unsubscribe", PacketGenerators.UnsubscribePacketArb()),
            ("UnsubAck",    PacketGenerators.UnsubAckPacketArb()),
            ("PingReq",     PacketGenerators.PingReqPacketArb()),
            ("PingResp",    PacketGenerators.PingRespPacketArb()),
            ("Disconnect",  PacketGenerators.DisconnectPacketArb()),
        };

        foreach (var (name, arb) in arbitraries)
        {
            var sample = arb.Generator.Sample(10, 1).ToList();
            var packet = Assert.Single(sample);
            Assert.NotNull(packet);
        }
    }

    [Fact]
    public void PacketArbShouldCoverAllFourteenPacketTypes()
    {
        var packetArb = PacketGenerators.PacketArb();

        // Sample enough packets to cover all 14 types with high probability.
        var samples = packetArb.Generator.Sample(10, 200).ToList();
        var types = samples.Select(p => p.PacketType).Distinct().ToHashSet();

        types.Should().Contain(MqttPacketType.Connect);
        types.Should().Contain(MqttPacketType.ConnAck);
        types.Should().Contain(MqttPacketType.Publish);
        types.Should().Contain(MqttPacketType.PubAck);
        types.Should().Contain(MqttPacketType.PubRec);
        types.Should().Contain(MqttPacketType.PubRel);
        types.Should().Contain(MqttPacketType.PubComp);
        types.Should().Contain(MqttPacketType.Subscribe);
        types.Should().Contain(MqttPacketType.SubAck);
        types.Should().Contain(MqttPacketType.Unsubscribe);
        types.Should().Contain(MqttPacketType.UnsubAck);
        types.Should().Contain(MqttPacketType.PingReq);
        types.Should().Contain(MqttPacketType.PingResp);
        types.Should().Contain(MqttPacketType.Disconnect);
    }
}
