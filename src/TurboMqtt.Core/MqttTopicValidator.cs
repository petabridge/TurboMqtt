// -----------------------------------------------------------------------
// <copyright file="MqttTopicValidator.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core;

public static class MqttTopicValidator
{
    /// <summary>
    /// Validates an MQTT topic ID for publishing.
    /// </summary>
    /// <param name="topic">The topic to validate.</param>
    /// <returns>A tuple indicating whether the topic is valid and an error message if it is not.</returns>
    public static (bool IsValid, string ErrorMessage) ValidatePublishTopic(string topic)
    {
        if (string.IsNullOrEmpty(topic))
        {
            return (false, "Topic must not be empty.");
        }

        if (topic.Contains('\0'))
        {
            return (false, "Topic must not contain null characters.");
        }

        if (topic.Contains('+') || topic.Contains('#'))
        {
            return (false, "Wildcards ('+' and '#') are not allowed in topics for publishing.");
        }

        if (topic.StartsWith('$'))
        {
            return (false, "Topics starting with '$' are reserved and should not be used by clients for publishing.");
        }

        if (topic.Length > 65535) // Example maximum length
        {
            return (false, "Topic exceeds the maximum length allowed.");
        }

        return (true, "Topic is valid.");
    }

    /// <summary>
    /// Validates an MQTT topic ID for subscribing.
    /// </summary>
    /// <param name="topic">The topic to validate.</param>
    /// <returns>A tuple indicating whether the topic is valid and an error message if it is not.</returns>
    public static (bool IsValid, string ErrorMessage) ValidateSubscribeTopic(string topic)
    {
        if (string.IsNullOrEmpty(topic))
        {
            return (false, "Topic must not be empty.");
        }

        if (topic.Contains('\0'))
        {
            return (false, "Topic must not contain null characters.");
        }

        int indexOfPlus = topic.IndexOf('+');
        while (indexOfPlus != -1)
        {
            if ((indexOfPlus > 0 && topic[indexOfPlus - 1] != '/') ||
                (indexOfPlus < topic.Length - 1 && topic[indexOfPlus + 1] != '/'))
            {
                return (false, "Single-level wildcard '+' must be located between slashes or at the beginning/end of the topic.");
            }
            indexOfPlus = topic.IndexOf('+', indexOfPlus + 1);
        }

        if (topic.Contains('#') && !topic.EndsWith("/#") && !topic.Equals("#"))
        {
            return (false, "Multi-level wildcard '#' must be at the end of the topic or after a '/'.");
        }

        if (topic.StartsWith('$'))
        {
            return (false, "Topics starting with '$' are reserved and should not be used by clients for publishing.");
        }

        if (topic.Length > 65535) // Example maximum length
        {
            return (false, "Topic exceeds the maximum length allowed.");
        }

        return (true, "Topic is valid.");
    }
}