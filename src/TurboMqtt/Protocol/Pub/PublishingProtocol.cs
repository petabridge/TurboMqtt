// -----------------------------------------------------------------------
// <copyright file="PublishingProtocol.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;

namespace TurboMqtt.Protocol.Pub;

public interface IPublishControlMessage : INoSerializationVerificationNeeded
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
    /// Message was successfully published and fully received.
    /// </summary>
    public sealed class PublishSuccess : IPublishControlMessage
    {
        public static readonly PublishSuccess Instance = new();
        private PublishSuccess(){}
        public PublishingStatus Status => PublishingStatus.Completed;
        public string Reason => string.Empty;
    }
    
    public sealed class PublishFailure(string reason) : IPublishControlMessage
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
    public sealed class PublishCancelled : IPublishControlMessage
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