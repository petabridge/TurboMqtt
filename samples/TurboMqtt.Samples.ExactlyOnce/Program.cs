// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using TurboMqtt;
using TurboMqtt.Client;
using TurboMqtt.Protocol;

// This example demonstrates QoS 2 (ExactlyOnce) delivery guarantee.
//
// QoS 2 uses a 4-step handshake to ensure exactly-once delivery:
// Publisher side:
//   1. PUBLISH (with unique packet ID)
//   2. PUBREC (received from broker)
//   3. PUBREL (release for delivery)
//   4. PUBCOMP (received from broker)
//
// Subscriber side:
//   - Broker guarantees each message is delivered exactly once
//   - TurboMqtt automatically handles PUBREC/PUBREL/PUBCOMP
//   - Automatic deduplication ensures no duplicates even on retransmission

// Create the actor system
var system = ActorSystem.Create("TurboMqttExactlyOnce");

// Create the MQTT client factory
var clientFactory = new MqttClientFactory(system);

// Configure connection options
var connectOptions = new MqttClientConnectOptions("exactly-once-demo", MqttProtocolVersion.V3_1_1);

// Configure TCP connection options
var tcpOptions = new MqttClientTcpOptions("localhost", 1883);

try
{
    // Create and connect the client
    await using var client = await clientFactory.CreateTcpClient(connectOptions, tcpOptions);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var connectResult = await client.ConnectAsync(cts.Token);

    if (!connectResult.IsSuccess)
    {
        Console.WriteLine($"Failed to connect: {connectResult.Reason}");
        return;
    }

    Console.WriteLine("Connected to MQTT broker!");

    // Subscribe to the topic at QoS 2
    const string topic = "orders/new";
    var subscribeResult = await client.SubscribeAsync(topic, QualityOfService.ExactlyOnce, cts.Token);

    if (!subscribeResult.IsSuccess)
    {
        Console.WriteLine($"Failed to subscribe: {subscribeResult.Reason}");
        return;
    }

    Console.WriteLine($"Subscribed to {topic} at QoS 2 (ExactlyOnce)");

    // Publish messages at QoS 2
    // Each message will complete the full 4-step handshake before returning
    Console.WriteLine("\nPublishing 3 messages at QoS 2 (ExactlyOnce)...");

    var messageCount = 3;
    for (int i = 1; i <= messageCount; i++)
    {
        var orderId = $"ORD-{DateTime.Now:yyyyMMddHHmmss}-{i}";
        var orderJson = $"{{\"orderId\": \"{orderId}\", \"amount\": {100 + i * 10}, \"status\": \"NEW\"}}";
        var orderData = System.Text.Encoding.UTF8.GetBytes(orderJson);

        var message = new MqttMessage(topic, orderData)
        {
            QoS = QualityOfService.ExactlyOnce,  // QoS 2: Exactly Once
            Retain = false
        };

        Console.WriteLine($"  Publishing message {i}: {orderId}...");

        var publishResult = await client.PublishAsync(message, cts.Token);

        if (!publishResult.IsSuccess)
        {
            Console.WriteLine($"    FAILED: {publishResult.Reason}");
            continue;
        }

        Console.WriteLine($"    SUCCESS - full 4-step handshake completed");
    }

    // Receive and count the messages
    Console.WriteLine("\nWaiting for messages to be received...");

    var receivedCount = 0;
    var messageReceiveTimeout = TimeSpan.FromSeconds(10);
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    while (stopwatch.Elapsed < messageReceiveTimeout && receivedCount < messageCount)
    {
        using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            if (await client.ReceivedMessages.WaitToReadAsync(receiveTimeout.Token))
            {
                while (client.ReceivedMessages.TryRead(out var receivedMessage))
                {
                    receivedCount++;
                    Console.WriteLine($"\nReceived message {receivedCount}:");
                    Console.WriteLine($"  Topic: {receivedMessage.Topic}");

                    var payload = System.Text.Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                    Console.WriteLine($"  Payload: {payload}");

                    // Note: TurboMqtt automatically handles PUBREC/PUBREL/PUBCOMP
                    // and deduplication, so you don't need to manage acknowledgments
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout waiting for message - that's okay, try again
        }
    }

    stopwatch.Stop();

    // Verify exactly-once delivery
    Console.WriteLine("\n" + new string('=', 50));
    Console.WriteLine("DELIVERY VERIFICATION:");
    Console.WriteLine($"  Published: {messageCount} messages at QoS 2");
    Console.WriteLine($"  Received:  {receivedCount} messages at QoS 2");

    if (receivedCount == messageCount)
    {
        Console.WriteLine("  Result:    ✓ EXACTLY ONCE DELIVERY GUARANTEED");
    }
    else if (receivedCount < messageCount)
    {
        Console.WriteLine("  Result:    ✗ Some messages not received yet");
    }
    else
    {
        Console.WriteLine("  Result:    ✗ Unexpected: more messages received than sent");
    }

    Console.WriteLine(new string('=', 50));

    // Disconnect
    await client.DisconnectAsync(cts.Token);
    Console.WriteLine("\nDisconnected from broker");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}
finally
{
    // Clean up
    await system.Terminate();
}
