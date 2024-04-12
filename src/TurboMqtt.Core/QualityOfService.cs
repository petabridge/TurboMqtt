namespace TurboMqtt.Core;

/// <summary>
/// QoS value - corresponds to the MQTT specification.
/// </summary>
public enum QualityOfService
{
    AtMostOnce = 0,
    AtLeastOnce = 1,
    ExactlyOnce = 2
}