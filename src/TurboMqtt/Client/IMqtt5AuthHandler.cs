// -----------------------------------------------------------------------
// <copyright file="IMqtt5AuthHandler.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Client;

/// <summary>
/// User-provided handler for MQTT 5.0 Enhanced Authentication (OASIS §3.15, §4.12).
/// </summary>
/// <remarks>
/// Implement this interface to participate in MQTT 5.0 challenge-response authentication.
/// Set it on <see cref="MqttClientConnectOptions.AuthHandler"/> before connecting.
/// The client calls <see cref="GetInitialAuthData"/> once per connection attempt, then
/// calls <see cref="HandleChallengeAsync"/> for each AUTH challenge received from the broker.
/// </remarks>
public interface IMqtt5AuthHandler
{
    /// <summary>
    /// The authentication method name (e.g., "SCRAM-SHA-256") sent in the CONNECT packet.
    /// Must match what the broker expects.
    /// </summary>
    string AuthenticationMethod { get; }

    /// <summary>
    /// Returns the initial authentication data sent with the CONNECT packet.
    /// Called once per connection attempt.
    /// </summary>
    ReadOnlyMemory<byte> GetInitialAuthData();

    /// <summary>
    /// Called when the broker sends an AUTH challenge (Reason Code 0x18 = Continue Authentication).
    /// Return the response data to send back to the broker, or throw to abort authentication.
    /// </summary>
    /// <param name="challengeData">The challenge data sent by the broker.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response data to send back in an AUTH packet.</returns>
    ValueTask<ReadOnlyMemory<byte>> HandleChallengeAsync(ReadOnlyMemory<byte> challengeData, CancellationToken ct);
}
