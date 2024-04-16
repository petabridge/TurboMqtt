// -----------------------------------------------------------------------
// <copyright file="PublishProtocolDefaults.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.Protocol;

/// <summary>
/// INTERNAL API
/// </summary>
internal static class PublishProtocolDefaults
{
    public sealed class CheckPublishTimeout
    {
        public static CheckPublishTimeout Instance { get; } = new();
        private CheckPublishTimeout() { }
    }
    
    /// <summary>
    /// if we don't receive an acknowledgement from the server within this time frame, we'll retry the publish
    /// </summary>
    public static readonly TimeSpan DefaultPublishTimeout = TimeSpan.FromSeconds(5);
    public const int DefaultMaxRetries = 3;
}