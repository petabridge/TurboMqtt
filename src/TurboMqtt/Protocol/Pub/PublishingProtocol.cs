// -----------------------------------------------------------------------
// <copyright file="PublishingProtocol.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;

namespace TurboMqtt.Protocol.Pub;

/// <summary>
/// Sent by <see cref="TurboMqtt.Client.ClientStreamOwner"/> to cross-register the sibling
/// publish actor so that buffered messages can be promoted across QoS levels when a shared
/// <see cref="SharedReceiveMaximumQuota"/> slot is freed.
/// </summary>
internal sealed class SetSiblingPublisher
{
    public SetSiblingPublisher(IActorRef sibling) => Sibling = sibling;
    public IActorRef Sibling { get; }
}

/// <summary>
/// Sent from one publish retry actor to its sibling to trigger a buffer-drain attempt when
/// a <see cref="SharedReceiveMaximumQuota"/> slot has been freed but the sender's own
/// buffer is empty.
/// </summary>
internal sealed class TryDequeueBuffered
{
    public static readonly TryDequeueBuffered Instance = new();
    private TryDequeueBuffered() { }
}

public interface IPublishResult : INoSerializationVerificationNeeded
{
    public PublishingStatus Status { get; }
    
    public bool IsSuccess => Status == PublishingStatus.Completed;
    
    public string Reason { get; }
}

public enum PublishingStatus
{
    /// <summary>
    /// Pub is being sent to the broker.
    /// </summary>
    Publishing,
    
    /// <summary>
    /// Only used in QoS 2.0
    /// </summary>
    PubRecReceived,
    
    /// <summary>
    /// The message was successfully published.
    /// </summary>
    /// <remarks>
    /// PubAck for QoS 1.0, PubComp for QoS 2.0
    /// </remarks>
    Completed,
    
    /// <summary>
    /// Message failed to be fully published for some reason
    /// </summary>
    Failed

}

/// <summary>
/// INTERNAL API - messaging protocol used to communicate with outbound reliable delivery actors.
/// </summary>
public static class PublishingProtocol{
    /// <summary>
    /// Configures the receive maximum for the retry actor after receiving a CONNACK with ReceiveMaximum set.
    /// When set, the actor buffers QoS 1/2 publishes beyond this limit and releases them as ACKs arrive.
    /// </summary>
    public sealed class SetReceiveMaximum
    {
        public SetReceiveMaximum(ushort value) { Value = value; }
        public ushort Value { get; }
    }

    /// <summary>
    /// Message was successfully published and fully received.
    /// </summary>
    public sealed class PublishSuccess : IPublishResult
    {
        public static readonly PublishSuccess Instance = new();
        private PublishSuccess(){}
        public PublishingStatus Status => PublishingStatus.Completed;
        public string Reason => string.Empty;
    }
    
    public sealed class PublishFailure(string reason) : IPublishResult
    {
        public string Reason { get; } = reason;
        public PublishingStatus Status => PublishingStatus.Failed;

        public override string ToString()
        {
            return $"PublishFailure({Reason})";
        }
    }
    
    /// <summary>
    /// Sent implicitly by the end-user when a <see cref="CancellationToken"/> expires
    /// on a publish operation.
    /// </summary>
    public sealed class PublishCancelled : IPublishResult
    {
        public PublishCancelled(NonZeroUInt16 packetId)
        {
            PacketId = packetId;
        }

        public NonZeroUInt16 PacketId { get; }
        public PublishingStatus Status => PublishingStatus.Failed;
        public string Reason => "Publish operation was cancelled.";
    }
}