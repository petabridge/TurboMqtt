// -----------------------------------------------------------------------
// <copyright file="Mqtt5AuthStateMachine.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;

namespace TurboMqtt.Client;

/// <summary>
/// State machine for MQTT 5.0 Enhanced Authentication flow (OASIS §4.12).
/// </summary>
/// <remarks>
/// Manages authentication state transitions:
/// <c>AwaitingConnAck → InChallenge → Authenticated</c>, or → <c>Failed</c> on error.
/// This class is not thread-safe; use only from within a single Akka actor.
/// </remarks>
internal sealed class Mqtt5AuthStateMachine
{
    /// <summary>
    /// Represents the current state of the MQTT 5.0 enhanced authentication flow.
    /// </summary>
    public enum AuthState
    {
        /// <summary>Initial state after sending CONNECT. Waiting for CONNACK or AUTH challenge.</summary>
        AwaitingConnAck,
        /// <summary>The broker has sent an AUTH challenge; we are processing it.</summary>
        InChallenge,
        /// <summary>Authentication completed successfully (CONNACK received with success code).</summary>
        Authenticated,
        /// <summary>Authentication failed (challenge error or CONNACK with failure code).</summary>
        Failed
    }

    private readonly IMqtt5AuthHandler _handler;

    public Mqtt5AuthStateMachine(IMqtt5AuthHandler handler)
    {
        _handler = handler;
        State = AuthState.AwaitingConnAck;
    }

    /// <summary>
    /// Gets the current authentication state.
    /// </summary>
    public AuthState State { get; private set; }

    /// <summary>
    /// Gets the authentication method name from the handler.
    /// </summary>
    public string AuthenticationMethod => _handler.AuthenticationMethod;

    /// <summary>
    /// Gets the initial authentication data to embed in the CONNECT packet.
    /// </summary>
    public ReadOnlyMemory<byte> GetInitialAuthData() => _handler.GetInitialAuthData();

    /// <summary>
    /// Handles an AUTH challenge packet from the broker (Reason Code 0x18).
    /// Transitions state to <see cref="AuthState.InChallenge"/> and returns the AUTH response to send.
    /// </summary>
    /// <param name="incoming">The AUTH packet received from the broker.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The AUTH response packet to send back to the broker.</returns>
    /// <exception cref="InvalidOperationException">Thrown if called in an invalid state.</exception>
    public async ValueTask<AuthPacket> HandleChallengeAsync(AuthPacket incoming, CancellationToken ct)
    {
        if (State == AuthState.Authenticated)
            throw new InvalidOperationException("Authentication is already complete.");
        if (State == AuthState.Failed)
            throw new InvalidOperationException("Authentication has already failed.");

        State = AuthState.InChallenge;
        var responseData = await _handler.HandleChallengeAsync(incoming.AuthenticationData, ct);

        return new AuthPacket(_handler.AuthenticationMethod, AuthReasonCode.ContinueAuthentication)
        {
            AuthenticationData = responseData
        };
    }

    /// <summary>
    /// Transitions to <see cref="AuthState.Authenticated"/> when CONNACK with success arrives.
    /// </summary>
    public void Complete() => State = AuthState.Authenticated;

    /// <summary>
    /// Transitions to <see cref="AuthState.Failed"/> on error.
    /// </summary>
    public void Fail() => State = AuthState.Failed;
}
