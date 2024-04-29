// -----------------------------------------------------------------------
// <copyright file="Mqtt311End2EndTcpBenchmarks.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using TurboMqtt.Client;
using TurboMqtt.IO;
using TurboMqtt.IO.Tcp;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Benchmarks.Mqtt311;

[SimpleJob(RunStrategy.Monitoring, launchCount: 10, warmupCount: 10)]
[Config(typeof(MonitoringConfig))]
public class Mqtt311EndToEndTcpBenchmarks
{
    [Params(QualityOfService.AtMostOnce, QualityOfService.AtLeastOnce, QualityOfService.ExactlyOnce)]
    public QualityOfService QoSLevel { get; set; }

    [Params(10, 1024, 2 * 1024)] public int PayloadSizeBytes { get; set; }

    [Params(MqttProtocolVersion.V3_1_1)] public MqttProtocolVersion ProtocolVersion { get; set; }

    public const int PacketCount = 1_000;

    private ActorSystem? _system;
    private FakeMqttTcpServer? _server;
    private IMqttClientFactory? _clientFactory;
    private IMqttClient? _subscribeClient;

    private MqttMessage? _testMessage;
    
    private MqttClientConnectOptions? _defaultConnectOptions;
    private MqttClientTcpOptions? _defaultTcpOptions;

    private const string Topic = "test";
    private const string Host = "localhost";
    private int Port;
    
    private ReadOnlyMemory<byte> CreateMsgPayload()
    {
        var payload = new byte[PayloadSizeBytes];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte) (i % 256);
        }

        return new ReadOnlyMemory<byte>(payload);
    }

    [GlobalSetup]
    public void StartFixture()
    {
        _system = ActorSystem.Create("Mqtt311EndToEndTcpBenchmarks", "akka.loglevel=ERROR");
        var loggingAdapter = new BusLogging(_system.EventStream, "FakeMqttTcpServer", typeof(FakeMqttTcpServer),
            _system.Settings.LogFormatter);
        
        // bind to a random port
        // set max frame size to 1mb
        
        _server = new FakeMqttTcpServer(new MqttTcpServerOptions(Host, 0){ MaxFrameSize = 1024*1024}, MqttProtocolVersion.V3_1_1, loggingAdapter,
            TimeSpan.Zero, new DefaultFakeServerHandleFactory());
        
        _clientFactory = new MqttClientFactory(_system);
        _testMessage = new MqttMessage(Topic, CreateMsgPayload())
        {
            PayloadFormatIndicator = PayloadFormatIndicator.Unspecified,
            ContentType = "application/binary",
            QoS = QoSLevel
        };
        
        _server.Bind();
        Port = _server.BoundPort;

        _defaultTcpOptions = new MqttClientTcpOptions(Host, Port) { MaxFrameSize = 256 * 1024 };
        _defaultConnectOptions = new MqttClientConnectOptions("test-subscriber", ProtocolVersion)
        {
            UserName = "testuser",
            Password = "testpassword",
            KeepAliveSeconds = 60,
            MaxReconnectAttempts = 10,
            PublishRetryInterval = TimeSpan.FromSeconds(5)
        };
    }
    
    [GlobalCleanup]
    public void StopFixture()
    {
        _server?.Shutdown();
        _system?.Dispose();
        _server = null;
        _system = null;
    }

    [IterationSetup]
    public void SetupPerIteration()
    {
        DoSetup().Wait();
        return;

        async Task DoSetup()
        {
            using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(5));
            _subscribeClient = await _clientFactory!.CreateTcpClient(_defaultConnectOptions!, _defaultTcpOptions!);
            var r = await _subscribeClient.ConnectAsync(cts.Token);
            if (!r.IsSuccess)
            {
                throw new Exception("Failed to connect to server.");
            }
            var subR = await _subscribeClient.SubscribeAsync(Topic, QoSLevel, cts.Token);
            if (!subR.IsSuccess)
            {
                throw new Exception("Failed to subscribe to topic.");
            }
        }
    }
    
    [IterationCleanup]
    public void CleanUpPerIteration()
    {
        DoCleanup().Wait();
        return;

        async Task DoCleanup()
        {
            using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(5));
            await _subscribeClient!.DisconnectAsync(cts.Token);
            await _subscribeClient!.WhenTerminated.WaitAsync(cts.Token);
            await _subscribeClient!.DisposeAsync();
        }
    }
    
    [Benchmark(OperationsPerInvoke = PacketCount)]
    public async Task<int> PublishAndReceiveMessages()
    {
        using var cts = new CancellationTokenSource(System.TimeSpan.FromMinutes(2));
        for (var i = 0; i < PacketCount; i++)
        {
            _ = _subscribeClient!.PublishAsync(_testMessage!, cts.Token);
        }

        var processedMessages = PacketCount;
        while(await _subscribeClient!.ReceivedMessages.WaitToReadAsync(cts.Token))
        {
            while(_subscribeClient.ReceivedMessages.TryRead(out _))
            {
                processedMessages--;
                if(processedMessages == 0)
                    return 0;
            }
        }
        
        return processedMessages;
    }
}