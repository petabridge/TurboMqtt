// -----------------------------------------------------------------------
// <copyright file="FakeMqttTlsTcpServer.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Akka.Event;
using TurboMqtt.Protocol;

namespace TurboMqtt.IO.Tcp;

/// <summary>
/// A fake TLS TCP server for use in benchmarks and tests.
/// Generates a self-signed certificate at startup and wraps
/// each accepted connection in an <see cref="SslStream"/>.
/// </summary>
internal sealed class FakeMqttTlsTcpServer
{
    private static readonly X509Certificate2 ServerCertificate = CreateSelfSignedCertificate();

    private readonly MqttProtocolVersion _version;
    private readonly MqttTcpServerOptions _options;
    private readonly CancellationTokenSource _shutdownTcs = new();
    private readonly ILoggingAdapter _log;
    private readonly TimeSpan _heartbeatDelay;
    private readonly IFakeServerHandleFactory _handleFactory;
    private Socket? _bindSocket;

    public int BoundPort { get; private set; }

    public FakeMqttTlsTcpServer(MqttTcpServerOptions options, MqttProtocolVersion version, ILoggingAdapter log,
        TimeSpan heartbeatDelay, IFakeServerHandleFactory handleFactory)
    {
        _options = options;
        _version = version;
        _log = log;
        _heartbeatDelay = heartbeatDelay;
        _handleFactory = handleFactory;
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("cn=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(san.Build());

        var tempCert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        // Export to PFX and reload using the non-deprecated API so the private key remains
        // accessible after the RSA key object is disposed, and works on all platforms.
        var pfxBytes = tempCert.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfxBytes, ReadOnlySpan<char>.Empty);
    }

    public void Bind()
    {
        if (_bindSocket != null)
            throw new InvalidOperationException("Cannot bind the same server twice.");

        _bindSocket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            ReceiveBufferSize = TcpTransportActor.ScaleBufferSize(_options.MaxFrameSize),
            SendBufferSize = TcpTransportActor.ScaleBufferSize(_options.MaxFrameSize),
            DualMode = true,
            NoDelay = true,
            LingerState = new LingerOption(false, 0)
        };

        var hostAddress = Dns.GetHostAddresses(_options.Host).First();
        _bindSocket.Bind(new IPEndPoint(hostAddress, _options.Port));
        _bindSocket.Listen(100);

        BoundPort = _bindSocket.LocalEndPoint is IPEndPoint ipEndPoint ? ipEndPoint.Port : 0;

        _ = BeginAcceptAsync();
    }

    public void Shutdown()
    {
        _log.Info("Shutting down TLS fake server.");
        try
        {
            _shutdownTcs.Cancel();
            _bindSocket?.Close();
        }
        catch (Exception)
        {
            // idempotent
        }
    }

    private async Task BeginAcceptAsync()
    {
        while (!_shutdownTcs.IsCancellationRequested)
        {
            try
            {
                var socket = await _bindSocket!.AcceptAsync();
                _ = ProcessClientAsync(socket);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception) when (_shutdownTcs.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private static async Task ReadFromPipeAsync(PipeReader reader, IFakeServerHandle handle, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;

                if (!buffer.IsEmpty)
                {
                    var newMemory = new Memory<byte>(new byte[buffer.Length]);
                    buffer.CopyTo(newMemory.Span);
                    handle.HandleBytes(newMemory);
                }

                if (result.IsCompleted || result.IsCanceled)
                    return;

                if (!buffer.IsEmpty && !ct.IsCancellationRequested)
                {
                    try
                    {
                        reader.AdvanceTo(buffer.End);
                    }
                    catch (Exception ex)
                    {
                        handle.Log.Debug(ex, "Error advancing TLS pipe reader.");
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ProcessClientAsync(Socket socket)
    {
        using (socket)
        {
            SslStream? sslStream = null;
            try
            {
                var networkStream = new NetworkStream(socket, ownsSocket: false);
                sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
                await sslStream.AuthenticateAsServerAsync(
                    ServerCertificate,
                    clientCertificateRequired: false,
                    checkCertificateRevocation: false);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "TLS handshake failed for incoming connection.");
                sslStream?.Dispose();
                return;
            }

            using (sslStream)
            {
                var closed = false;
                var pipe = new Pipe(new PipeOptions(
                    pauseWriterThreshold: TcpTransportActor.ScaleBufferSize(_options.MaxFrameSize),
                    resumeWriterThreshold: TcpTransportActor.ScaleBufferSize(_options.MaxFrameSize) / 2,
                    useSynchronizationContext: false));
                var clientShutdownCts = new CancellationTokenSource();
                var linkedCts =
                    CancellationTokenSource.CreateLinkedTokenSource(clientShutdownCts.Token, _shutdownTcs.Token);

                var handle =
                    _handleFactory.CreateServerHandle(PushMessage, ClosingAction, _log, _version, _heartbeatDelay);

                _ = ReadFromPipeAsync(pipe.Reader, handle, linkedCts.Token);

                while (!linkedCts.IsCancellationRequested)
                {
                    if (closed) break;
                    try
                    {
                        var memory = pipe.Writer.GetMemory(_options.MaxFrameSize / 4);
                        var bytesRead = await sslStream.ReadAsync(memory, linkedCts.Token);
                        if (bytesRead == 0)
                        {
                            _log.Info("TLS client disconnected.");
                            break;
                        }

                        pipe.Writer.Advance(bytesRead);

                        var flushResult = await pipe.Writer.FlushAsync(linkedCts.Token);
                        if (flushResult.IsCompleted)
                        {
                            _log.Info("Done reading from TLS client.");
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error processing TLS client message.");
                        break;
                    }
                }

                if (!closed)
                    handle.DisconnectFromServer();

                await handle.WhenTerminated;
                await pipe.Writer.CompleteAsync();
                await pipe.Reader.CompleteAsync();

                return;

                bool PushMessage((IMemoryOwner<byte> buffer, int estimatedSize) msg)
                {
                    try
                    {
                        if (socket.Connected && !linkedCts.IsCancellationRequested)
                        {
                            // SslStream.Write is synchronous and sends all bytes atomically.
                            sslStream.Write(msg.buffer.Memory.Span[..msg.estimatedSize]);
                            return true;
                        }

                        return false;
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error writing to TLS client.");
                        return false;
                    }
                    finally
                    {
                        msg.buffer.Dispose();
                    }
                }

                async Task ClosingAction()
                {
                    closed = true;
                    await clientShutdownCts.CancelAsync();
                    try
                    {
                        sslStream.Close();
                    }
                    catch (Exception)
                    {
                        // ignore errors during close
                    }
                }
            }
        }
    }
}
