// -----------------------------------------------------------------------
// <copyright file="MqttClientIdValidator.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace TurboMqtt;

internal static class MqttClientIdValidator
{
    /// <summary>
    /// Validates an MQTT client ID according to MQTT 3.1.1 and 5.0 specifications.
    /// </summary>
    /// <param name="clientId">The client ID to validate.</param>
    /// <returns>A tuple indicating whether the client ID is valid and an error message if it is not.</returns>
    public static (bool IsValid, string ErrorMessage) ValidateClientId(string clientId)
    {
        // Check if the client ID is empty - this is allowed under MQTT 3.1.1 and 5.0 as the server assigns a ClientID.
        if (string.IsNullOrEmpty(clientId))
        {
            return (true, "Client ID is valid or will be assigned by the server.");
        }

        // Check for maximum length of 65535 characters
        if (clientId.Length > 65535)
        {
            return (false, "Client ID exceeds the maximum length allowed (65535 characters).");
        }

        // Check if the client ID contains only valid UTF-8 characters (printable ASCII and some extended sets)
        // This regex checks for printable ASCII characters and disallows control characters
        if (!Regex.IsMatch(clientId, @"^[ -~]+$")) // This regex covers the range of printable ASCII characters from space (32) to tilde (126)
        {
            return (false, "Client ID contains invalid characters.");
        }

        // If all checks pass
        return (true, "Client ID is valid.");
    }
}