// -----------------------------------------------------------------------
// <copyright file="Mqtt311End2EndTcpBenchmarks.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using TurboMqtt.Client;
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

    public const int PacketCount = 100_00;

    private ActorSystem? _system;
    private IMqttClientFactory? _clientFactory;
    private IMqttClient? _subscribeClient;

    private MqttMessage? _testMessage;
    
    private MqttClientConnectOptions? _defaultConnectOptions;
    private MqttClientTcpOptions? _defaultTcpOptions;

    private const string Topic = "test";
    private const string Host = "localhost";
    private const int Port = 1883;
    
    private List<Task> _writeTasks = new();
    
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
        _writeTasks = new List<Task>(PacketCount);
        _system = ActorSystem.Create("Mqtt311EndToEndTcpBenchmarks", "akka.loglevel=ERROR");
        
        _clientFactory = new MqttClientFactory(_system);
        _testMessage = new MqttMessage(Topic, CreateMsgPayload())
        {
            PayloadFormatIndicator = PayloadFormatIndicator.Unspecified,
            ContentType = "application/binary",
            QoS = QoSLevel
        };

        _defaultTcpOptions = new MqttClientTcpOptions(Host, Port) { MaxFrameSize = 256 * 1024 };
    }
    
    [GlobalCleanup]
    public void StopFixture()
    {
        _system?.Dispose();
        _system = null;
    }

    [IterationSetup]
    public void SetupPerIteration()
    {
        _defaultConnectOptions = new MqttClientConnectOptions("test-subscriber" + Guid.NewGuid(), ProtocolVersion)
        {
            UserName = "admin",
            Password = "public",
            KeepAliveSeconds = 5,
            MaxReconnectAttempts = 3,
            PublishRetryInterval = TimeSpan.FromSeconds(5)
        };
        
        _writeTasks.Clear();
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

    public const int ChunkSize = PacketCount / 15;
    
    [Benchmark(OperationsPerInvoke = PacketCount * 2)]
    public async Task<int> PublishAndReceiveMessages()
    {
        using var cts = new CancellationTokenSource(System.TimeSpan.FromMinutes(2));

        var writes = WriteMessages(cts.Token);

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

        await writes;
        
        return processedMessages;

        async Task<int> WriteMessages(CancellationToken ct)
        {
            for (var i = 0; i < PacketCount; i += ChunkSize)
            {
                var tasks = Enumerable.Range(0, ChunkSize).Select(c => _subscribeClient!.PublishAsync(_testMessage!, cts.Token));
                await Task.WhenAll(tasks).WaitAsync(ct);
            }
            
            return 0;
        }
    }
}