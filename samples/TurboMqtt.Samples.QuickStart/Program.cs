// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using TurboMqtt;
using TurboMqtt.Client;
using TurboMqtt.Protocol;

// Create the actor system
var system = ActorSystem.Create("TurboMqttQuickStart");

// Create the MQTT client factory
var clientFactory = new MqttClientFactory(system);

// Configure connection options
var connectOptions = new MqttClientConnectOptions("quickstart-client", MqttProtocolVersion.V3_1_1);

// Configure TCP connection options
// Note: Change "localhost" to your broker hostname and 1883 to your broker port
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

    // Subscribe to a topic
    const string topic = "test/topic";
    var subscribeResult = await client.SubscribeAsync(topic, QualityOfService.AtLeastOnce, cts.Token);

    if (!subscribeResult.IsSuccess)
    {
        Console.WriteLine($"Failed to subscribe: {subscribeResult.Reason}");
        return;
    }

    Console.WriteLine($"Subscribed to {topic}");

    // Publish a message
    var message = new MqttMessage(topic, "Hello from TurboMqtt!"u8.ToArray())
    {
        QoS = QualityOfService.AtLeastOnce
    };

    var publishResult = await client.PublishAsync(message, cts.Token);

    if (!publishResult.IsSuccess)
    {
        Console.WriteLine($"Failed to publish: {publishResult.Reason}");
        return;
    }

    Console.WriteLine("Published message!");

    // Wait for a message to be received (with timeout)
    using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    if (await client.ReceivedMessages.WaitToReadAsync(receiveTimeout.Token))
    {
        if (client.ReceivedMessages.TryRead(out var receivedMessage))
        {
            Console.WriteLine($"Received message on {receivedMessage.Topic}");
            Console.WriteLine($"Payload: {System.Text.Encoding.UTF8.GetString(receivedMessage.Payload.Span)}");
        }
    }

    // Disconnect
    await client.DisconnectAsync(cts.Token);
    Console.WriteLine("Disconnected from broker");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
finally
{
    // Clean up
    await system.Terminate();
}
