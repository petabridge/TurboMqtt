// -----------------------------------------------------------------------
// <copyright file="AuthPacket.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.PacketTypes;

/// <summary>
/// Used for authentication exchange or error reporting concerning authentication.
/// </summary>
/// <remarks>
/// This packet is only applicable in MQTT 5.0 and is used both in the initial connection phase and for dynamic re-authentication.
/// </remarks>
public sealed class AuthPacket(string authenticationMethod, AuthReasonCode reasonCode) : MqttPacket
{
    // turn the reason code into a ReasonString

    public override MqttPacketType PacketType => MqttPacketType.Auth;

    /// <summary>
    /// The Reason Code for the AUTH packet, which indicates the status of the authentication or any authentication errors.
    /// </summary>
    public AuthReasonCode ReasonCode { get; } = reasonCode;

    // MQTT 5.0 - Optional Properties
    /// <summary>
    /// Authentication Method, used to specify the method of authentication.
    /// </summary>
    public string AuthenticationMethod { get; } = authenticationMethod; // Required if Auth Packet is used

    /// <summary>
    /// Authentication Data, typically containing credentials or challenge/response data, depending on the auth method.
    /// </summary>
    public ReadOnlyMemory<byte> AuthenticationData { get; set; }

    /// <summary>
    /// User Properties, available in MQTT 5.0.
    /// This is a key-value pair that can be sent multiple times to convey additional information that is not covered by other means.
    /// </summary>
    public IReadOnlyDictionary<string, string>? UserProperties { get; set; }

    /// <summary>
    /// Reason String providing additional information about the authentication status.
    /// </summary>
    public string? ReasonString { get; set; } = reasonCode.ToReasonString();

    public override string ToString()
    {
        return $"Auth: [ReasonCode={ReasonCode}]";
    }
}

/// <summary>
/// Enumerates the reason codes applicable to the AUTH packet in MQTT 5.0.
/// </summary>
public enum AuthReasonCode
{
    Success = 0x00,
    ContinueAuthentication = 0x18,
    ReAuthenticate = 0x19
}

internal static class AuthReasonCodeHelpers
{
    public static string ToReasonString(this AuthReasonCode reasonCode)
    {
        return reasonCode switch
        {
            AuthReasonCode.Success => "Success",
            AuthReasonCode.ContinueAuthentication => "Continue Authentication",
            AuthReasonCode.ReAuthenticate => "Re-Authenticate",
            _ => "Unknown Reason Code"
        };
    }
}