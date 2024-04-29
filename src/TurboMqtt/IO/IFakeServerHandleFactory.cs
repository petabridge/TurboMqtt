// -----------------------------------------------------------------------
// <copyright file="IFakeServerHandleFactory.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using Akka.Event;
using TurboMqtt.Protocol;

namespace TurboMqtt.IO;

/// <summary>
/// Simple factory for creating <see cref="IFakeServerHandle"/> instances.
/// </summary>
internal interface IFakeServerHandleFactory
{
    IFakeServerHandle CreateServerHandle(Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool> pushMessage,
        Func<Task> closingAction, ILoggingAdapter log, MqttProtocolVersion protocolVersion = MqttProtocolVersion.V3_1_1, TimeSpan? heartbeatDelay = null);
}

/// <summary>
/// INTERNAL API
/// </summary>
internal sealed class DefaultFakeServerHandleFactory : IFakeServerHandleFactory
{
    public IFakeServerHandle CreateServerHandle(Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool> pushMessage, Func<Task> closingAction, ILoggingAdapter log,
        MqttProtocolVersion protocolVersion = MqttProtocolVersion.V3_1_1, TimeSpan? heartbeatDelay = null)
    {
        return protocolVersion switch
        {
            MqttProtocolVersion.V3_1_1 => new FakeMqtt311ServerHandle(pushMessage, closingAction, log, heartbeatDelay),
            _ => throw new NotSupportedException($"Protocol version {protocolVersion} not supported.")
        };
    }
}