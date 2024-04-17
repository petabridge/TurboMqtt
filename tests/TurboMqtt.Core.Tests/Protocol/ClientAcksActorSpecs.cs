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
using TurboMqtt.Core.Protocol.Pub;
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
    
    // handle a timeout scenario for Subscribes
    [Fact]
    public async Task ClientAcksActor_should_handle_pending_subscribes_with_timeout()
    {
        // create a new ClientAcksActor
        var clientAcksActor = Sys.ActorOf(Props.Create(() => new ClientAcksActor(TimeSpan.Zero)));

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

        // send the SubscribePacket to the ClientAcksActor
        clientAcksActor.Tell(subscribePacket);
        clientAcksActor.Tell(PublishProtocolDefaults.CheckTimeout.Instance);

        // expect the ClientAcksActor to have a pending subscribe
        var subFailure = await ExpectMsgAsync<SubscribeFailure>();
        subFailure.IsSuccess.Should().BeFalse();
        subFailure.Reason.Should().Be("Timeout");
    }
    
    //test a SubAck scenario where we get an unsuccessful SubAck packet back (i.e. bad return code for all topics)
    [Fact]
    public async Task ClientAcksActor_should_handle_pending_subscribes_with_failure()
    {
        // create a new ClientAcksActor
        var clientAcksActor = Sys.ActorOf(Props.Create(() => new ClientAcksActor(TimeSpan.Zero)));

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
            ReasonCodes = new [] {MqttSubscribeReasonCode.NotAuthorized}
        };

        // send the SubscribePacket to the ClientAcksActor
        clientAcksActor.Tell(subscribePacket);
        clientAcksActor.Tell(subAckPacket);

        // expect the ClientAcksActor to have a pending subscribe
        var subFailure = await ExpectMsgAsync<SubscribeFailure>();
        subFailure.IsSuccess.Should().BeFalse();
        subFailure.Reason.Should().Be(subAckPacket.ReasonCodes[0].ToString());
    }
    
    [Fact]
    public async Task ClientAcksActor_should_handle_pending_unsubscribes()
    {
        // create a new ClientAcksActor
        var clientAcksActor = Sys.ActorOf(Props.Create(() => new ClientAcksActor(TimeSpan.FromMinutes(1))));

        // create a new UnsubscribePacket
        var unsubscribePacket = new UnsubscribePacket
        {
            PacketId = 2, Topics = new [] {"test/topic"}
        };
        
        // create unsub ack packet
        var unsubAckPacket = new UnsubAckPacket
        {
            PacketId = 2
        };

        // send the UnsubscribePacket to the ClientAcksActor
        clientAcksActor.Tell(unsubscribePacket);
        clientAcksActor.Tell(unsubAckPacket);

        // expect the ClientAcksActor to have a pending unsubscribe
        var unsubSuccess = await ExpectMsgAsync<UnsubscribeSuccess>();
        unsubSuccess.IsSuccess.Should().BeTrue();
        unsubSuccess.UnsubAck.PacketId.Should().Be(unsubAckPacket.PacketId);
    }
    
    // test an UnsubAck scenario where we get an unsuccessful UnsubAck packet back (i.e. bad return code)
    [Fact]
    public async Task ClientAcksActor_should_handle_pending_unsubscribes_with_failure()
    {
        // create a new ClientAcksActor
        var clientAcksActor = Sys.ActorOf(Props.Create(() => new ClientAcksActor(TimeSpan.FromMinutes(1))));

        // create a new UnsubscribePacket
        var unsubscribePacket = new UnsubscribePacket
        {
            PacketId = 2, Topics = new [] {"test/topic"}
        };
        
        // create unsub ack packet
        var unsubAckPacket = new UnsubAckPacket
        {
            PacketId = 2,
            ReasonCodes = new [] {MqttUnsubscribeReasonCode.NotAuthorized}
        };

        // send the UnsubscribePacket to the ClientAcksActor
        clientAcksActor.Tell(unsubscribePacket);
        clientAcksActor.Tell(unsubAckPacket);

        // expect the ClientAcksActor to have a pending unsubscribe
        var unsubFailure = await ExpectMsgAsync<UnsubscribeFailure>();
        unsubFailure.IsSuccess.Should().BeFalse();
    }
    
    [Fact]
    public async Task ClientAcksActor_should_handle_pending_connects()
    {
        // create a new ClientAcksActor
        var clientAcksActor = Sys.ActorOf(Props.Create(() => new ClientAcksActor(TimeSpan.FromMinutes(1))));

        // create a new ConnectPacket
        var connectPacket = new ConnectPacket(MqttProtocolVersion.V3_1_1)
        {
            ClientId = "test-client"
        };
        
        // create conn ack packet
        var connAckPacket = new ConnAckPacket
        {
            ReasonCode = ConnAckReasonCode.Success 
        };

        // send the ConnectPacket to the ClientAcksActor
        clientAcksActor.Tell(connectPacket);
        clientAcksActor.Tell(connAckPacket);

        // expect the ClientAcksActor to have a pending connect
        var connectSuccess = await ExpectMsgAsync<ConnectSuccess>();
        connectSuccess.IsSuccess.Should().BeTrue();
        connectSuccess.ConnAck.ReasonCode.Should().Be(connAckPacket.ReasonCode);
    }
    
    // test a ConnAck scenario where we get an unsuccessful ConnAck packet back (i.e. bad return code)
    [Fact]
    public async Task ClientAcksActor_should_handle_pending_connects_with_failure()
    {
        // create a new ClientAcksActor
        var clientAcksActor = Sys.ActorOf(Props.Create(() => new ClientAcksActor(TimeSpan.FromMinutes(1))));

        // create a new ConnectPacket
        var connectPacket = new ConnectPacket(MqttProtocolVersion.V3_1_1)
        {
            ClientId = "test-client"
        };
        
        // create conn ack packet
        var connAckPacket = new ConnAckPacket
        {
            ReasonCode = ConnAckReasonCode.BadUsernameOrPassword
        };

        // send the ConnectPacket to the ClientAcksActor
        clientAcksActor.Tell(connectPacket);
        clientAcksActor.Tell(connAckPacket);

        // expect the ClientAcksActor to have a pending connect
        var connectFailure = await ExpectMsgAsync<ConnectFailure>();
        connectFailure.IsSuccess.Should().BeFalse();
        connectFailure.Reason.Should().Be(connAckPacket.ReasonCode.ToString());
    }
    
    // add a timeout scenario for Connects
    [Fact]
    public async Task ClientAcksActor_should_handle_pending_connects_with_timeout()
    {
        // create a new ClientAcksActor
        var clientAcksActor = Sys.ActorOf(Props.Create(() => new ClientAcksActor(TimeSpan.Zero)));

        // create a new ConnectPacket
        var connectPacket = new ConnectPacket(MqttProtocolVersion.V3_1_1)
        {
            ClientId = "test-client"
        };

        // send the ConnectPacket to the ClientAcksActor
        clientAcksActor.Tell(connectPacket);
        clientAcksActor.Tell(PublishProtocolDefaults.CheckTimeout.Instance);

        // expect the ClientAcksActor to have a pending connect
        var connectFailure = await ExpectMsgAsync<ConnectFailure>();
        connectFailure.IsSuccess.Should().BeFalse();
        connectFailure.Reason.Should().Be("Timeout");
    }
}