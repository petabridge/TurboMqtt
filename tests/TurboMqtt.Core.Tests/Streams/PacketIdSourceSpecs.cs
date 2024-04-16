// -----------------------------------------------------------------------
// <copyright file="PacketIdSourceSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit.Xunit2;
using TurboMqtt.Core.Streams;

namespace TurboMqtt.Core.Tests.Streams;

public class PacketIdSourceSpecs : TestKit
{
    [Fact]
    public async Task PacketIdSource_should_generate_unique_packet_ids()
    {
        var source = new PacketIdSource();
        var probe = this.CreateManualSubscriberProbe<ushort>();

        Source.FromGraph(source).RunWith(Sink.FromSubscriber(probe), Sys);

        var sub = await probe.ExpectSubscriptionAsync();
        sub.Request(3);

        await probe.ExpectNextAsync(1);
        await probe.ExpectNextAsync(2);
        await probe.ExpectNextAsync(3);
    }
    
    [Fact]
    public async Task PacketIdSource_should_wrap_around_when_max_value_reached()
    {
        var source = new PacketIdSource(ushort.MaxValue-1);
        var probe = this.CreateManualSubscriberProbe<ushort>();

        Source.FromGraph(source).RunWith(Sink.FromSubscriber(probe), Sys);

        var sub = await probe.ExpectSubscriptionAsync();
        sub.Request(3);

        await probe.ExpectNextAsync(ushort.MaxValue);
        // we should always skip zero, since it's an illegal value
        await probe.ExpectNextAsync(1); 
        await probe.ExpectNextAsync(2);
    }
}