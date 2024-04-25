// -----------------------------------------------------------------------
// <copyright file="PublishOnlyServerHandler.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using Akka.Event;
using TurboMqtt.IO;
using TurboMqtt.PacketTypes;

namespace TurboMqtt.Benchmarks.Utility;

/// <summary>
/// Used to simulate a server that only accepts <see cref="MqttPacketType.Publish"/> packets.
/// </summary>
/// <remarks>
/// In order to keep the benchmark clean, we don't want to have to deal with the overhead of trying to actually
/// publish messages from the client back to other clients. We just discard them.
/// </remarks>
internal sealed class PublishOnlyMqtt311ServerHandler : FakeMqtt311ServerHandle
{
    public PublishOnlyMqtt311ServerHandler(Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool> pushMessage,
        Func<Task> closingAction, ILoggingAdapter log, TimeSpan? heartbeatDelay = null) : base(pushMessage,
        closingAction, log, heartbeatDelay)
    {
    }

    public override void TryPush(MqttPacket packet)
    {
        if (packet.PacketType == MqttPacketType.Publish)
            return; // don't bother writing
        
        base.TryPush(packet);
    }
}

internal sealed class ReceiveOnlyMqtt311ServerHandler : FakeMqtt311ServerHandle
{
    public ReceiveOnlyMqtt311ServerHandler(Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool> pushMessage,
        Func<Task> closingAction, ILoggingAdapter log, TimeSpan? heartbeatDelay = null) : base(pushMessage,
        closingAction, log, heartbeatDelay)
    {
    }

    public override void TryPush(MqttPacket packet)
    {
        if (packet.PacketType == MqttPacketType.Publish)
            return; // don't bother writing
        
        base.TryPush(packet);
    }
}