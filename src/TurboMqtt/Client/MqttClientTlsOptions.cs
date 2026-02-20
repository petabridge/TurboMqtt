// -----------------------------------------------------------------------
// <copyright file="MqttClientTlsOptions.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace TurboMqtt.Client;

/// <summary>
/// TLS/SSL options for securing MQTT client connections.
/// </summary>
public sealed record MqttClientTlsOptions
{
    /// <summary>
    /// Client certificates for mutual TLS authentication.
    /// </summary>
    public X509CertificateCollection? ClientCertificates { get; init; }

    /// <summary>
    /// Custom server certificate validation callback.
    /// When null, uses default system validation.
    /// </summary>
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; }

    /// <summary>
    /// The TLS/SSL protocols to use. Defaults to <see cref="SslProtocols.None"/> (system default, typically TLS 1.2+).
    /// </summary>
    public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;

    /// <summary>
    /// Target host name for TLS SNI (Server Name Indication).
    /// When null, defaults to <see cref="MqttClientTcpOptions.Host"/>.
    /// </summary>
    public string? TargetHost { get; init; }
}
