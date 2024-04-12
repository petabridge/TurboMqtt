namespace TurboMqtt.Core;

/// <summary>
/// All possible reason codes for the PubAck packet.
/// </summary>
public enum MqttPubAckReasonCode
{
    Success = 0x00,
    NoMatchingSubscribers = 0x10,
    UnspecifiedError = 0x80,
    ImplementationSpecificError = 0x83,
    NotAuthorized = 0x87,
    TopicNameInvalid = 0x90,
    PacketIdentifierInUse = 0x91,
    QuotaExceeded = 0x97,
    PayloadFormatInvalid = 0x99
}

// add a static helper method that can turn a MqttPubAckReason code into a hard-coded string representation
internal static class MqttPubAckHelpers
{
    public static string ReasonCodeToString(MqttPubAckReasonCode reasonCode)
    {
        return reasonCode switch
        {
            MqttPubAckReasonCode.Success => "Success",
            MqttPubAckReasonCode.NoMatchingSubscribers => "NoMatchingSubscribers",
            MqttPubAckReasonCode.UnspecifiedError => "UnspecifiedError",
            MqttPubAckReasonCode.ImplementationSpecificError => "ImplementationSpecificError",
            MqttPubAckReasonCode.NotAuthorized => "NotAuthorized",
            MqttPubAckReasonCode.TopicNameInvalid => "TopicNameInvalid",
            MqttPubAckReasonCode.PacketIdentifierInUse => "PacketIdentifierInUse",
            MqttPubAckReasonCode.QuotaExceeded => "QuotaExceeded",
            MqttPubAckReasonCode.PayloadFormatInvalid => "PayloadFormatInvalid",
            _ => throw new ArgumentOutOfRangeException(nameof(reasonCode), reasonCode, null)
        };
    }
}