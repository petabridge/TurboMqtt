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

    [Params(10, 1024, 8 * 1024)] public int PayloadSizeBytes { get; set; }

    [Params(MqttProtocolVersion.V3_1_1)] public MqttProtocolVersion ProtocolVersion { get; set; }

    public const int PacketCount = 100_000;

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
        _system = ActorSystem.Create("Mqtt311EndToEndTcpBenchmarks", "akka.loglevel=INFO");
        
        _clientFactory = new MqttClientFactory(_system);

        _defaultTcpOptions = new MqttClientTcpOptions(Host, Port) { MaxFrameSize = 256 * 1024 };
            PublishRetryInterval = TimeSpan.FromSeconds(5),
            CleanSession = true
    }
    
    [GlobalCleanup]
    public void StopFixture()
    {
        _system?.Dispose();
        _system = null;
    }

    private string _uniqueTopicId = Topic;

    [IterationSetup]
    public void SetupPerIteration()
    {
        _writeTasks.Clear();
        _uniqueTopicId = Topic + Guid.NewGuid();
        _testMessage = new MqttMessage(_uniqueTopicId, CreateMsgPayload())
        {
            PayloadFormatIndicator = PayloadFormatIndicator.Unspecified,
            ContentType = "application/binary",
            QoS = QoSLevel
        };
        
        DoSetup().Wait();
        return;

        async Task DoSetup()
        {
            _defaultConnectOptions = new MqttClientConnectOptions("test-subscriber" + Guid.NewGuid(), ProtocolVersion)
            {
                UserName = "testuser",
                Password = "testpassword",
                KeepAliveSeconds = 60,
                MaxReconnectAttempts = 10,
                PublishRetryInterval = TimeSpan.FromSeconds(5)
            };
            
            using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(5));
            _subscribeClient = await _clientFactory!.CreateTcpClient(_defaultConnectOptions!, _defaultTcpOptions!);
            var r = await _subscribeClient.ConnectAsync(cts.Token);
            if (!r.IsSuccess)
            {
                throw new Exception("Failed to connect to server.");
            }
            var subR = await _subscribeClient.SubscribeAsync(_uniqueTopicId, QoSLevel, cts.Token);
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
            _writeTasks.Add(_subscribeClient!.PublishAsync(_testMessage!, cts.Token));
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

        await Task.WhenAll(_writeTasks).WaitAsync(cts.Token);
        
        return processedMessages;
    }
}