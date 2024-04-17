// -----------------------------------------------------------------------
// <copyright file="ClientAcksActorSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Akka.TestKit.Xunit2;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;
using Xunit.Abstractions;
using static TurboMqtt.Core.Protocol.AckProtocol;

namespace TurboMqtt.Core.Tests.Protocol;

public class ClientAcksActorSpecs : TestKit
{
    public ClientAcksActorSpecs(ITestOutputHelper output) : base(output: output)
    {
    }

    [Fact]
    public async Task ClientAcksActor_should_handle_pending_subscribes()
    {
        // create a new ClientAcksActor
        var clientAcksActor = Sys.ActorOf(Props.Create(() => new ClientAcksActor(TimeSpan.FromMinutes(1))));

        // create a new SubscribePacket
        var subscribePacket = new SubscribePacket
        {
            PacketId = 2, Topics = new []
            {
                new TopicSubscription("test/topic")
                {
                    Options = new SubscriptionOptions
                    {
                        QoS = QualityOfService.AtLeastOnce
                    }
                }
            }
        };
        
        // create sub ack packet
        var subAckPacket = new SubAckPacket
        {
            PacketId = 2,
            ReasonCodes = new [] {MqttSubscribeReasonCode.GrantedQoS0}
        };

        // send the SubscribePacket to the ClientAcksActor
        clientAcksActor.Tell(subscribePacket);
        clientAcksActor.Tell(subAckPacket);

        // expect the ClientAcksActor to have a pending subscribe
        var subSuccess = await ExpectMsgAsync<SubscribeSuccess>();
        subSuccess.IsSuccess.Should().BeTrue();
        subSuccess.SubAck.PacketId.Should().Be(subAckPacket.PacketId);
    }
}