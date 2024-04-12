namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Used by a client to indicate that it will disconnect cleanly, or by the server to notify of a disconnect.
/// </summary>
public sealed class DisconnectPacket : MqttPacket
{
    public override MqttPacketType PacketType => MqttPacketType.Disconnect;

    // MQTT 5.0 - Optional Reason Code and Properties
    public DisconnectReasonCode? ReasonCode { get; set; } // MQTT 5.0 only

    /// <summary>
    /// User Properties, available in MQTT 5.0.
    /// This is a key-value pair that can be sent multiple times to convey additional information that is not covered by other means.
    /// </summary>
    public IReadOnlyDictionary<string, string>? UserProperties { get; set; } // MQTT 5.0 only

    /// <summary>
    /// The Server Reference property, available in MQTT 5.0.
    /// This optional property can suggest another server for the client to use.
    /// </summary>
    public string? ServerReference { get; set; } // MQTT 5.0 only

    /// <summary>
    /// Session Expiry Interval, available in MQTT 5.0.
    /// This optional property can indicate the session expiry interval in seconds when the disconnect is initiated.
    /// </summary>
    public uint? SessionExpiryInterval { get; set; } // MQTT 5.0 only
    
    public override string ToString()
    {
        return $"Disconnect: [ReasonCode={ReasonCode}]";
    }
}

public enum DisconnectReasonCode
{
    NormalDisconnection = 0x00,
    DisconnectWithWillMessage = 0x04,
    UnspecifiedError = 0x80,
    MalformedPacket = 0x81,
    ProtocolError = 0x82,
    ImplementationSpecificError = 0x83,
    NotAuthorized = 0x87,
    ServerBusy = 0x89,
    ServerShuttingDown = 0x8B,
    KeepAliveTimeout = 0x8D,
    SessionTakenOver = 0x8E,
    TopicFilterInvalid = 0x8F,
    TopicNameInvalid = 0x90,
    ReceiveMaximumExceeded = 0x93,
    TopicAliasInvalid = 0x94,
    PacketTooLarge = 0x95,
    MessageRateTooHigh = 0x96,
    QuotaExceeded = 0x97,
    AdministrativeAction = 0x98,
    PayloadFormatInvalid = 0x99,
    RetainNotSupported = 0x9A,
    QoSNotSupported = 0x9B,
    UseAnotherServer = 0x9C,
    ServerMoved = 0x9D,
    SharedSubscriptionsNotSupported = 0x9E,
    ConnectionRateExceeded = 0x9F,
    MaximumConnectTime = 0xA0,
    SubscriptionIdentifiersNotSupported = 0xA1,
    WildcardSubscriptionsNotSupported = 0xA2
}
