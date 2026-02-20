// -----------------------------------------------------------------------
// <copyright file="TlsStreamProvider.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Security;
using TurboMqtt.Client;

namespace TurboMqtt.IO.Tcp;

/// <summary>
/// Creates a TLS-secured TCP connection by wrapping a <see cref="TcpStreamProvider"/>
/// with an <see cref="SslStream"/> and completing TLS handshake.
/// </summary>
internal sealed class TlsStreamProvider : IStreamProvider
{
    private readonly TcpStreamProvider _tcpProvider;
    private readonly MqttClientTlsOptions _tlsOptions;
    private readonly string _defaultHost;
    private SslStream? _sslStream;

    public TlsStreamProvider(MqttClientTcpOptions tcpOptions, MqttClientTlsOptions tlsOptions)
    {
        _tcpProvider = new TcpStreamProvider(tcpOptions);
        _tlsOptions = tlsOptions;
        _defaultHost = tcpOptions.Host;
    }

    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        var networkStream = await _tcpProvider.ConnectAsync(host, port, ct).ConfigureAwait(false);

        var sslStream = new SslStream(
            networkStream,
            leaveInnerStreamOpen: false,
            _tlsOptions.ServerCertificateValidationCallback);

        var targetHost = _tlsOptions.TargetHost ?? host;

        var authOptions = new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = _tlsOptions.EnabledSslProtocols,
            ClientCertificates = _tlsOptions.ClientCertificates
        };

        await sslStream.AuthenticateAsClientAsync(authOptions, ct).ConfigureAwait(false);

        _sslStream = sslStream;
        return sslStream;
    }

    public void Close()
    {
        if (_sslStream is not null)
        {
            try
            {
                _sslStream.Close();
                _sslStream.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // already disposed
            }
            finally
            {
                _sslStream = null;
            }
        }

        _tcpProvider.Close();
    }
}
