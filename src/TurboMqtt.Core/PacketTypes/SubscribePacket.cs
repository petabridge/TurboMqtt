namespace TurboMqtt.Core.PacketTypes;

public sealed class SubscribePacket : MqttPacketWithId
{
    public override MqttPacketType PacketType => MqttPacketType.Subscribe;
    
    /// <summary>
    /// The unique identity of this subscription for this client.
    /// </summary>
    /// <remarks>
    /// Must be a value greater than 0.
    /// </remarks>
    public NonZeroUInt32 SubscriptionIdentifier { get; set; }
    
    /// <summary>
    /// The set of topics we're subscribing to.
    /// </summary>
    public IReadOnlyList<TopicSubscription> Topics { get; set; } = Array.Empty<TopicSubscription>();
    
    /// <summary>
    /// User Property, available in MQTT 5.0.
    /// This is a key-value pair that can be sent multiple times to convey additional information that is not covered by other means.
    /// </summary>
    public IReadOnlyDictionary<string, string>? UserProperties { get; set; } // MQTT 5.0 only
    
    public override string ToString()
    {
        return $"Subscribe: [PacketIdentifier={PacketId}] [SubscriptionIdentifier={SubscriptionIdentifier}] [Topics={string.Join(", ", Topics.Select(c => c))}]";
    }
}

public sealed class TopicSubscription(string topic)
{
    /// <summary>
    /// The topic to subscribe to.
    /// </summary>
    public string Topic { get; } = topic;

    /// <summary>
    /// The subscription options - QoS, No Local, Retain As Published, Retain Handling.
    /// </summary>
    /// <remarks>
    /// Some of these are MQTT 5.0 features and will not be used in MQTT 3.1.1 or 3.0.
    /// </remarks>
    public SubscriptionOptions Options { get; set; }
    
    public override string ToString()
    {
        return $"Topic: {Topic}, Options: {Options}";
    }
}

public enum RetainHandlingOption
{
    SendAtSubscribe = 0, // 00 binary
    SendAtSubscribeIfNew = 1, // 01 binary
    DoNotSendAtSubscribe = 2 // 10 binary
}

public struct SubscriptionOptions
{
    /// <summary>
    /// Gets or sets the Quality of Service level to use when sending messages to the client.
    /// </summary>
    public QualityOfService QoS { get; set; }
    
    /// <summary>
    /// MQTT 5.0 Feature: indicates whether or not the sender can receive its own messages.
    /// </summary>
    public bool NoLocal { get; set; }
    
    /// <summary>
    /// MQTT 5.0 Feature: indicates whether or not the message should be retained by the broker.
    /// </summary>
    public bool RetainAsPublished { get; set; }
    
    /// <summary>
    /// MQTT 5.0 Feature: indicates how the broker should handle retained messages.
    /// </summary>
    public RetainHandlingOption RetainHandling { get; set; }
    
    public override string ToString()
    {
        return $"QoS: {QoS}, No Local: {NoLocal}, Retain As Published: {RetainAsPublished}, Retain Handling: {RetainHandling}";
    }
}

internal static class SubscriptionOptionsHelpers
{
    public static byte ToByte(this SubscriptionOptions subscriptionOptions)
    {
        byte result = 0;

        // Set the QoS bits (bit 0 and 1)
        result |= (byte)subscriptionOptions.QoS;

        // Set the No Local bit (bit 2)
        if (subscriptionOptions.NoLocal)
        {
            result |= 1 << 2;
        }

        // Set the Retain As Published bit (bit 3)
        if (subscriptionOptions.RetainAsPublished)
        {
            result |= 1 << 3;
        }

        // Set the Retain Handling bits (bit 4 and 5)
        result |= (byte)((int)subscriptionOptions.RetainHandling << 4);

        return result;
    }
    
    public static SubscriptionOptions ToSubscriptionOptions(this byte subscriptionOptions)
    {
        var result = new SubscriptionOptions
        {
            QoS = (QualityOfService)(subscriptionOptions & 0b11),
            NoLocal = (subscriptionOptions & (1 << 2)) != 0,
            RetainAsPublished = (subscriptionOptions & (1 << 3)) != 0,
            RetainHandling = (RetainHandlingOption)((subscriptionOptions & 0b11000) >> 3)
        };

        return result;
    }
}