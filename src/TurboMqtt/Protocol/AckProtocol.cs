// -----------------------------------------------------------------------
// <copyright file="AckProtocol.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;

namespace TurboMqtt.Protocol;

/// <summary>
/// Ack protocol for client-side messages that require an acknowledgment from the broker.
/// </summary>
public interface IAckResponse
{
    bool IsSuccess { get; }
    string? Reason { get; }
}

/// <summary>
/// Responses to the various client-side messages that require an acknowledgment from the broker.
/// </summary>
public static class AckProtocol
{
    public sealed class SubscribeSuccess : IAckResponse
    {
        public SubscribeSuccess(SubAckPacket subAck)
        {
            SubAck = subAck;
        }

        public SubAckPacket SubAck { get; }

        public bool IsSuccess => true;
        public string? Reason => SubAck.ReasonString;
    }

    public sealed class SubscribeFailure : IAckResponse
    {
        public SubscribeFailure(string reason)
        {
            Reason = reason;
        }

        public SubscribeFailure(SubAckPacket subAck)
        {
            SubAck = subAck;
            Reason = SubAck.ReasonString ?? SubAck.ReasonCodes[0].ToString();
        }

        public SubAckPacket? SubAck { get; }

        public bool IsSuccess => false;
        public string Reason { get; }
    }

    public sealed class ConnectSuccess : IAckResponse
    {
        public ConnectSuccess(ConnAckPacket connAck)
        {
            ConnAck = connAck;
            Reason = ConnAck.ReasonString ?? ConnAck.ReasonCode.ToString();
        }

        public ConnectSuccess(string reasonString)
        {
            Reason = reasonString;
        }

        public ConnAckPacket? ConnAck { get; }
        public bool IsSuccess => true;
        public string Reason { get; }
    }

    public sealed class ConnectFailure : IAckResponse
    {
        public ConnectFailure(ConnAckPacket connAck)
        {
            ConnAck = connAck;
            Reason = ConnAck.ReasonString ?? ConnAck.ReasonCode.ToString();
        }

        public ConnectFailure(string reason)
        {
            Reason = reason;
        }

        public ConnAckPacket? ConnAck { get; }

        public bool IsSuccess => false;
        public string Reason { get; }
    }

    public sealed class DisconnectSuccess : IAckResponse
    {
        public static readonly DisconnectSuccess Instance = new DisconnectSuccess();

        private DisconnectSuccess()
        {
        }

        public bool IsSuccess => true;
        public string? Reason => null;
    }

    public sealed class UnsubscribeSuccess : IAckResponse
    {
        public UnsubscribeSuccess(UnsubAckPacket unsubAck)
        {
            UnsubAck = unsubAck;
        }

        public UnsubAckPacket UnsubAck { get; }
        public bool IsSuccess => true;
        public string? Reason => UnsubAck.ReasonString;
    }

    public sealed class UnsubscribeFailure : IAckResponse
    {
        public UnsubscribeFailure(string reason)
        {
            Reason = reason;
        }

        public UnsubscribeFailure(UnsubAckPacket unsubAck)
        {
            UnsubAck = unsubAck;
            Reason = UnsubAck.ReasonString;
        }

        public UnsubAckPacket? UnsubAck { get; }

        public bool IsSuccess => false;
        public string Reason { get; }
    }
}