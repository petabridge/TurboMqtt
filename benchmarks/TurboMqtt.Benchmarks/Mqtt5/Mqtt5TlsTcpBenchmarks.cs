// -----------------------------------------------------------------------
// <copyright file="Mqtt5TlsTcpBenchmarks.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Event;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using TurboMqtt.Client;
using TurboMqtt.IO;
using TurboMqtt.IO.Tcp;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Benchmarks.Mqtt5;

/// <summary>
/// End-to-end throughput benchmarks for MQTT 5.0 over TLS (TCP+SSL).
/// Uses an in-process <see cref="FakeMqttTlsTcpServer"/> with a self-signed certificate
/// to measure TLS overhead relative to plain TCP (<see cref="Mqtt5EndToEndTcpBenchmarks"/>).
/// </summary>
[SimpleJob(RunStrategy.Monitoring, launchCount: 10, warmupCount: 10)]
[Config(typeof(MonitoringConfig))]
public class Mqtt5TlsEndToEndTcpBenchmarks
{
    [Params(QualityOfService.AtMostOnce, QualityOfService.AtLeastOnce)]
    public QualityOfService QoSLevel { get; set; }

    [Params(10, 1024)] public int PayloadSizeBytes { get; set; }

    public const int PacketCount = 1_000;

    private ActorSystem? _system;
    private IMqttClientFactory? _clientFactory;
    private IMqttClient? _subscribeClient;

    private MqttMessage? _testMessage;

    private MqttClientConnectOptions? _defaultConnectOptions;
    private MqttClientTcpOptions? _defaultTcpOptions;

    // Accept the fake server's self-signed certificate without validation.
    private static readonly MqttClientTlsOptions DefaultTlsOptions = new()
    {
        ServerCertificateValidationCallback = AcceptAllCertificates
    };

    private static bool AcceptAllCertificates(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors) => true;

    private const string TopicConst = "test";
    private string Topic = TopicConst;
    private const string Host = "localhost";
    private const int Port = 19915;
    private FakeMqttTlsTcpServer? _server;

    private ReadOnlyMemory<byte> CreateMsgPayload()
    {
        var payload = new byte[PayloadSizeBytes];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i % 256);
        return new ReadOnlyMemory<byte>(payload);
    }

    [GlobalSetup]
    public void StartFixture()
    {
        _system = ActorSystem.Create("Mqtt5TlsEndToEndTcpBenchmarks", "akka.loglevel=ERROR");
        var logger = new BusLogging(_system.EventStream, "FakeMqttTlsTcpServer", typeof(FakeMqttTlsTcpServer),
            _system.Settings.LogFormatter);
        _server = new FakeMqttTlsTcpServer(new MqttTcpServerOptions(Host, Port), MqttProtocolVersion.V5_0,
            logger, TimeSpan.Zero, new DefaultFakeServerHandleFactory());
        _server.Bind();
        _clientFactory = new MqttClientFactory(_system);
        _defaultTcpOptions = new MqttClientTcpOptions(Host, Port) { MaxFrameSize = 256 * 1024 };
    }

    [GlobalCleanup]
    public void StopFixture()
    {
        _server?.Shutdown();
        _system?.Dispose();
        _system = null;
    }

    [IterationSetup]
    public void SetupPerIteration()
    {
        Topic = TopicConst + Guid.NewGuid();
        _defaultConnectOptions =
            new MqttClientConnectOptions("test-tls-subscriber-" + Guid.NewGuid(), MqttProtocolVersion.V5_0)
            {
                KeepAliveSeconds = 5,
                MaxReconnectAttempts = 3,
                PublishRetryInterval = TimeSpan.FromSeconds(5)
            };

        _testMessage = new MqttMessage(Topic, CreateMsgPayload())
        {
            PayloadFormatIndicator = PayloadFormatIndicator.Unspecified,
            QoS = QoSLevel
        };

        DoSetup().Wait();
        return;

        async Task DoSetup()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _subscribeClient = await _clientFactory!.CreateTlsTcpClient(
                _defaultConnectOptions!, _defaultTcpOptions!, DefaultTlsOptions);
            var r = await _subscribeClient.ConnectAsync(cts.Token);
            if (!r.IsSuccess)
                throw new Exception("Failed to connect to TLS server.");
            var subR = await _subscribeClient.SubscribeAsync(Topic, QoSLevel, cts.Token);
            if (!subR.IsSuccess)
                throw new Exception("Failed to subscribe to topic.");
        }
    }

    [IterationCleanup]
    public void CleanUpPerIteration()
    {
        DoCleanup().Wait();
        return;

        async Task DoCleanup()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _subscribeClient!.DisconnectAsync(cts.Token);
            await _subscribeClient!.WhenTerminated.WaitAsync(cts.Token);
            await _subscribeClient!.DisposeAsync();
        }
    }

    [Benchmark(OperationsPerInvoke = PacketCount * 2)]
    public async Task<int> PublishAndReceiveMessages()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var writes = WriteMessages(cts.Token);

        var processedMessages = PacketCount;
        while (await _subscribeClient!.ReceivedMessages.WaitToReadAsync(cts.Token))
        {
            while (_subscribeClient.ReceivedMessages.TryRead(out _))
            {
                processedMessages--;
                if (processedMessages == 0)
                {
                    await writes;
                    return processedMessages;
                }
            }
        }

        if (processedMessages > 0)
            throw new Exception("Failed to process all messages.");

        return processedMessages;

        async Task<int> WriteMessages(CancellationToken ct)
        {
            var tasks = new List<Task>(PacketCount);
            for (var i = 0; i < PacketCount; i++)
                tasks.Add(_subscribeClient!.PublishAsync(_testMessage!, ct));

            await Task.WhenAll(tasks);
            return 0;
        }
    }
}
