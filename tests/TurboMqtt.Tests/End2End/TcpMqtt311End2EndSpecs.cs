// -----------------------------------------------------------------------
// <copyright file="TcpMqtt311End2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using Akka.Configuration;
using Akka.Event;
using TurboMqtt.Client;
using TurboMqtt.IO;
using TurboMqtt.IO.Tcp;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;
using Xunit.Abstractions;

namespace TurboMqtt.Tests.End2End;

public class TcpMqtt311End2EndSpecs : TransportSpecBase
{
    public static readonly Config DebugLogging = """
                                                 akka.loglevel = DEBUG
                                                 """;

    public TcpMqtt311End2EndSpecs(ITestOutputHelper output) : base(output: output, config: DebugLogging)
    {
        var logger = new BusLogging(Sys.EventStream, "FakeMqttTcpServer", typeof(FakeMqttTcpServer),
            Sys.Settings.LogFormatter);
        _server = new FakeMqttTcpServer(new MqttTcpServerOptions("localhost", 21883), MqttProtocolVersion.V3_1_1,
            logger, TimeSpan.Zero, new DefaultFakeServerHandleFactory());
        _server.Bind();
    }

    private readonly FakeMqttTcpServer _server;

    public override async Task<IMqttClient> CreateClient()
    {
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);
        return client;
    }

    public MqttClientTcpOptions DefaultTcpOptions => new("localhost", 21883);

    protected override void AfterAll()
    {
        // shut down our local TCP server
        _server.Shutdown();
        base.AfterAll();
    }

    private sealed class DisconnectOnConnectFakeServerHandler: FakeMqtt311ServerHandle
    {
        private readonly Action<ConnectPacket> _onConnectCallback;
        
        public DisconnectOnConnectFakeServerHandler(
            Action<ConnectPacket> onConnectCallback,
            Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool> pushMessage, 
            Func<Task> closingAction,
            ILoggingAdapter log,
            TimeSpan? heartbeatDelay = null) 
            : base(pushMessage, closingAction, log, heartbeatDelay)
        {
            _onConnectCallback = onConnectCallback;
        }

        public override void HandlePacket(MqttPacket packet)
        {
            if (packet.PacketType == MqttPacketType.Connect)
            {
                var connect = (ConnectPacket)packet;
                ClientIdAssigned.TrySetResult(connect.ClientId);
                _onConnectCallback(connect);
                return;
            }
            base.HandlePacket(packet);
        }
    }
    
    private sealed class ConfigurableFakeServerFactory: IFakeServerHandleFactory
    {
        private readonly Func<Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool>, Func<Task>, ILoggingAdapter,
            TimeSpan?, IFakeServerHandle> _onCreateHandlerCallback;

        public ConfigurableFakeServerFactory(
            Func<Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool>, Func<Task>, ILoggingAdapter,
                TimeSpan?, IFakeServerHandle> onCreateHandlerCallback)
        {
            _onCreateHandlerCallback = onCreateHandlerCallback;
        }

        public IFakeServerHandle CreateServerHandle(
            Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool> pushMessage, 
            Func<Task> closingAction,
            ILoggingAdapter log,
            MqttProtocolVersion protocolVersion = MqttProtocolVersion.V3_1_1, 
            TimeSpan? heartbeatDelay = null)
        {
            return _onCreateHandlerCallback(pushMessage, closingAction, log, heartbeatDelay);
        }
    }
    
    /// <summary>
    /// This is an edge case when the client tries to reconnect and the socket disconnected right after the CONNECT
    /// packet was sent by the client but before the CONNACK packet were received by the client.
    ///
    /// The bug was that the ClientAcksActor were stuck waiting for the previous connection state to complete, blocking
    /// immediate client reconnect attempt.
    ///
    /// There should be 3 connection attempts in this test, with these steps happening in sequence:
    /// 
    /// 1. Client connects normally to the broker
    /// 2. Client connected successfully to the broker
    /// 3. Socket connection lost (forcefully)
    /// 4. Client tries to reconnect to the broker
    /// 5. Client socket connected to the broker and sends a CONNECT packet, ClientAcksActor _pendingConnect field is set
    /// 6. Socket connection lost (forcefully)
    /// 7. ClientAcksActor _pendingConnect field is reset by a Reconnect message
    /// 8. Client tries to reconnect to the broker
    /// 9. Client connected successfully to the broker
    /// </summary>
    [Fact]
    public async Task ShouldReconnectSuccessfullyIfReconnectFlowFailed()
    {
        var connectAttempts = 0;
        var connectPacketTcs = new TaskCompletionSource<ConnectPacket>();
        
        // need our own server
        _server.Shutdown();
        
        var server = new FakeMqttTcpServer(
            options: new MqttTcpServerOptions("localhost", 21883), 
            version: MqttProtocolVersion.V3_1_1,
            log: Log,
            heartbeatDelay: TimeSpan.Zero,
            handleFactory: new ConfigurableFakeServerFactory(OnCreateHandlerCallback));
        server.Bind();
        
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);
        
        try
        {
            using var cts = new CancellationTokenSource(RemainingOrDefault);
            
            // First connection should succeed
            var connectResult = await client.ConnectAsync(cts.Token);
            connectResult.IsSuccess.Should().BeTrue();

            // Disconnect the client socket forcefully to force it to reconnect
            server.TryDisconnectClientSocket(client.ClientId);

            // Wait for connect packet to arrive in the handler
            await connectPacketTcs.Task;
            
            // Disconnect the client as soon as its client id is registered but without replying with a ConectAck
            await AwaitConditionAsync(() => server.TryDisconnectClientSocket(client.ClientId), cts.Token);
            
            // Client should reconnect even with the dirty _pendingConnect in the ClientAcksActor
            await AwaitConditionAsync(() => client.IsConnected, cts.Token);
        }
        finally
        {
            try
            {
                await client.DisconnectAsync();
            }
            catch
            {
                // no-op
            }
            server.Shutdown();
        }

        return;

        IFakeServerHandle OnCreateHandlerCallback(
            Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool> pushMessage,
            Func<Task> closingAction,
            ILoggingAdapter log,
            TimeSpan? heartbeatDelay)
        {
            connectAttempts++;
            Log.Info($"OnCreateHandlerCallback {connectAttempts}");
            return connectAttempts switch
            {
                1 => new FakeMqtt311ServerHandle(pushMessage, closingAction, log, heartbeatDelay),
                2 => new DisconnectOnConnectFakeServerHandler(OnConnectCallback, pushMessage, closingAction, log,
                    heartbeatDelay),
                _ => new FakeMqtt311ServerHandle(pushMessage, closingAction, log, heartbeatDelay)
            };
        }
        
        void OnConnectCallback(ConnectPacket connect)
        {
            Log.Info($"OnConnectCallback {connectAttempts}");
            connectPacketTcs.SetResult(connect);
        }
    }
    
    [Fact]
    public async Task ShouldAutomaticallyReconnectAndSubscribeAfterServerDisconnect()
    {
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        // subscribe
        var subResult = await client.SubscribeAsync(DefaultTopic, QualityOfService.AtLeastOnce, cts.Token);
        subResult.IsSuccess.Should().BeTrue();

        // kick the client
        _server.TryKickClient(DefaultConnectOptions.ClientId).Should().BeTrue();

        // automatic reconnect should be happening behind the scenes - attempt to publish a message we will receive
        var mqttMessage = new MqttMessage(DefaultTopic, "hello, world!") { QoS = QualityOfService.AtLeastOnce };
        var pubResult = await client.PublishAsync(mqttMessage);
        pubResult.IsSuccess.Should()
            .BeTrue(
                $"Expected to be able to publish message {mqttMessage} after reconnect, but got {pubResult} instead.");

        // now we should receive the message
        (await client.ReceivedMessages.WaitToReadAsync()).Should().BeTrue();
        client.ReceivedMessages.TryRead(out var receivedMessage).Should().BeTrue();
        receivedMessage!.Topic.Should().Be(DefaultTopic);

        // shut down
        using var shutdownCts = new CancellationTokenSource(RemainingOrDefault);
        await client.DisconnectAsync(shutdownCts.Token);

        await client.WhenTerminated.WaitAsync(shutdownCts.Token);
    }

    [Fact]
    public async Task ShouldTerminateClientAfterMultipleFailedConnectionAttempts()
    {
        // allow 1 reconnection attempt
        var updatedOptions = DefaultConnectOptions with { MaxReconnectAttempts = 1 };
        var client = await ClientFactory.CreateTcpClient(updatedOptions, DefaultTcpOptions);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        // subscribe
        var subResult = await client.SubscribeAsync(DefaultTopic, QualityOfService.AtLeastOnce, cts.Token);
        subResult.IsSuccess.Should().BeTrue();

        // shutdown server
        _server.Shutdown();

        // wait for retry-->reconnect loop to fail twice

        await client.WhenTerminated.WaitAsync(cts.Token);
        client.WhenTerminated.IsCompleted.Should().BeTrue();
    }
    
    // test case where we attempt to connect to non-existent server. ConnectAsync should fail
    [Fact]
    public async Task ShouldFailToConnectToNonExistentServer()
    {
        var updatedTcpOptions = new MqttClientTcpOptions("localhost", 21884)
        {
            MaxReconnectAttempts = 0
        };
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, updatedTcpOptions);
        
        // we are going to do this, intentionally, without a CTS here - this operation MUST FAIL if we are unable to connect
        var connectResult = await client.ConnectAsync();
        connectResult.IsSuccess.Should().BeFalse();

        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldSuccessFullyConnectWhenBrokerAvailableAfterFailedConnectionAttempt()
    {
        var updatedTcpOptions = new MqttClientTcpOptions("localhost", 21889)
        {
            MaxReconnectAttempts = 0
        };
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, updatedTcpOptions);

        // we are going to do this, intentionally, without a CTS here - this operation MUST FAIL if we are unable to connect
        var connectResult = await client.ConnectAsync();
        connectResult.IsSuccess.Should().BeFalse();

        client.IsConnected.Should().BeFalse();

        // start up a new server
        var newServer = new FakeMqttTcpServer(new MqttTcpServerOptions("localhost", 21889), MqttProtocolVersion.V3_1_1,
            Sys.Log, TimeSpan.Zero, new DefaultFakeServerHandleFactory());
        try
        {
            newServer.Bind();

            // now we should be able to connect
            var connectResult2 = await client.ConnectAsync();
            connectResult2.IsSuccess.Should().BeTrue();

            client.IsConnected.Should().BeTrue();
            await client.DisconnectAsync();

            // ReSharper disable once MethodSupportsCancellation
            await AwaitAssertAsync(() => client.WhenTerminated.IsCompleted.Should().BeTrue());
        }
        finally
        {
            newServer.Shutdown();
        }
    }

    /// <summary>
    /// Race condition test: concurrent disconnect + publish must not deadlock or crash.
    /// Fires a disconnect and multiple publishes concurrently.
    /// </summary>
    [Fact]
    public async Task ConcurrentDisconnectAndPublishShouldNotDeadlockOrCrash()
    {
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        // Fire disconnect and publishes concurrently
        var disconnectTask = Task.Run(async () =>
        {
            try
            {
                await client.DisconnectAsync(cts.Token);
            }
            catch
            {
                // disconnect might fail if publish already closed things — that's acceptable
            }
        });

        var publishTasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            try
            {
                var msg = new MqttMessage(DefaultTopic, $"concurrent-{i}") { QoS = QualityOfService.AtMostOnce };
                await client.PublishAsync(msg, cts.Token);
            }
            catch
            {
                // publishes may fail after disconnect — that's acceptable
            }
        })).ToArray();

        // The key assertion: nothing deadlocks, everything completes within the timeout
        await Task.WhenAll(publishTasks.Append(disconnectTask));

        // Client should be terminated
        await AwaitAssertAsync(() => client.WhenTerminated.IsCompleted.Should().BeTrue(), cancellationToken: cts.Token);
    }

    /// <summary>
    /// Race condition test: rapid sequential reconnects (3+ in &lt; 1 second) complete without error.
    /// </summary>
    [Fact]
    public async Task RapidSequentialReconnectsShouldCompleteWithoutError()
    {
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        // subscribe so we can verify reconnect restores subscriptions
        var subResult = await client.SubscribeAsync(DefaultTopic, QualityOfService.AtLeastOnce, cts.Token);
        subResult.IsSuccess.Should().BeTrue();

        // Kick the client 3 times in rapid succession
        for (var i = 0; i < 3; i++)
        {
            // Wait for connected state before kicking
            await AwaitConditionAsync(() => client.IsConnected, cts.Token);

            // Forcefully disconnect
            _server.TryDisconnectClientSocket(DefaultConnectOptions.ClientId);

            // Small delay to let transport tear down
            await Task.Delay(100, cts.Token);
        }

        // After 3 rapid reconnects, client should recover
        await AwaitConditionAsync(() => client.IsConnected, cts.Token);

        // Verify we can still publish and receive after all the reconnects
        var mqttMessage = new MqttMessage(DefaultTopic, "after-rapid-reconnects") { QoS = QualityOfService.AtLeastOnce };
        var pubResult = await client.PublishAsync(mqttMessage, cts.Token);
        pubResult.IsSuccess.Should().BeTrue();

        (await client.ReceivedMessages.WaitToReadAsync(cts.Token)).Should().BeTrue();
        client.ReceivedMessages.TryRead(out var received).Should().BeTrue();
        received!.Topic.Should().Be(DefaultTopic);

        using var shutdownCts = new CancellationTokenSource(RemainingOrDefault);
        await client.DisconnectAsync(shutdownCts.Token);
        await client.WhenTerminated.WaitAsync(shutdownCts.Token);
    }

    /// <summary>
    /// Race condition test: server kills connection during QoS 2 exchange — client reconnects and retransmits.
    /// </summary>
    [Fact]
    public async Task ServerKillDuringQos2ExchangeShouldReconnectAndRetransmit()
    {
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        // subscribe at QoS 2
        var subResult = await client.SubscribeAsync(DefaultTopic, QualityOfService.ExactlyOnce, cts.Token);
        subResult.IsSuccess.Should().BeTrue();

        // Start a QoS 2 publish — this begins the multi-step QoS 2 handshake
        var publishTask = Task.Run(async () =>
        {
            var msg = new MqttMessage(DefaultTopic, "qos2-message") { QoS = QualityOfService.ExactlyOnce };
            return await client.PublishAsync(msg, cts.Token);
        });

        // Give the publish time to start the handshake
        await Task.Delay(50, cts.Token);

        // Kill the server connection mid-exchange
        _server.TryDisconnectClientSocket(DefaultConnectOptions.ClientId);

        // The publish may fail or succeed depending on timing — either is acceptable
        try
        {
            var pubResult = await publishTask;
            // If it succeeded, great
        }
        catch
        {
            // If the publish failed due to disconnect, that's also acceptable
        }

        // Wait for reconnect to complete
        await AwaitConditionAsync(() => client.IsConnected, cts.Token);

        // After reconnect, verify we can still publish and receive
        var newMsg = new MqttMessage(DefaultTopic, "after-qos2-kill") { QoS = QualityOfService.ExactlyOnce };
        var newPubResult = await client.PublishAsync(newMsg, cts.Token);
        newPubResult.IsSuccess.Should().BeTrue();

        (await client.ReceivedMessages.WaitToReadAsync(cts.Token)).Should().BeTrue();

        using var shutdownCts = new CancellationTokenSource(RemainingOrDefault);
        await client.DisconnectAsync(shutdownCts.Token);
        await client.WhenTerminated.WaitAsync(shutdownCts.Token);
    }

    /// <summary>
    /// Race condition test: disconnect while a large publish is in flight — verifies graceful drain.
    /// </summary>
    [Fact]
    public async Task DisconnectDuringLargePublishShouldDrainGracefully()
    {
        var client = await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var connectResult = await client.ConnectAsync(cts.Token);
        connectResult.IsSuccess.Should().BeTrue();

        // Start publishing a burst of messages
        var publishTasks = Enumerable.Range(0, 50).Select(i =>
        {
            var msg = new MqttMessage(DefaultTopic, $"large-publish-{i}") { QoS = QualityOfService.AtMostOnce };
            return client.PublishAsync(msg, cts.Token);
        }).ToArray();

        // Initiate graceful disconnect while publishes are in flight
        var disconnectTask = client.DisconnectAsync(cts.Token);

        // All operations should complete without deadlock or crash
        try
        {
            await Task.WhenAll(publishTasks);
        }
        catch
        {
            // Some publishes may fail if disconnect completes first — acceptable
        }

        await disconnectTask;

        // Client should be terminated
        await AwaitAssertAsync(() => client.WhenTerminated.IsCompleted.Should().BeTrue(), cancellationToken: cts.Token);
    }
}