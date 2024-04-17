// -----------------------------------------------------------------------
// <copyright file="MqttRequiredActors.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using TurboMqtt.Core.Protocol;
using TurboMqtt.Core.Protocol.Pub;

namespace TurboMqtt.Core.Streams;

/// <summary>
/// All of the actors needed to power the MQTT client.
/// </summary>
/// <param name="Qos2Actor">The <see cref="ExactlyOncePublishRetryActor"/></param>
/// <param name="Qos1Actor">The <see cref="AtLeastOncePublishRetryActor"/></param>
/// <param name="ClientAck">The <see cref="ClientAcksActor"/></param>
/// <param name="HeartBeatActor">The <see cref="HeartBeatActor"/></param>
internal sealed record MqttRequiredActors(IActorRef Qos2Actor, IActorRef Qos1Actor, IActorRef ClientAck, IActorRef HeartBeatActor);