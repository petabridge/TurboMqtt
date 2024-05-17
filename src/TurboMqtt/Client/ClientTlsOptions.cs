// -----------------------------------------------------------------------
// <copyright file="ICertificateProvider.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Security;

namespace TurboMqtt.Client;

/// <summary>
/// Used to provide the necessary certificates and keys for establishing a secure connection with the MQTT broker.
/// </summary>
public sealed record ClientTlsOptions
{
    public static readonly ClientTlsOptions Default = new();
    
    public bool UseTls { get; init; } = false;
    
    public SslClientAuthenticationOptions? SslOptions { get; init; }
}