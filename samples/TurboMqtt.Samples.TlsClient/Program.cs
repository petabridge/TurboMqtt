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
var system = ActorSystem.Create("TurboMqttTlsClient");

// Create the MQTT client factory
var clientFactory = new MqttClientFactory(system);

// Configure connection options
var connectOptions = new MqttClientConnectOptions("tls-client", MqttProtocolVersion.V3_1_1)
{
    UserName = "admin",
    Password = "public"
};

// Configure TLS TCP connection options
// Note: Change "localhost" and 8883 to your broker hostname and TLS port
var tcpOptions = new MqttClientTcpOptions("localhost", 8883);

// Configure TLS options
// This example shows three common scenarios:

// 1. Use system CA validation (default, recommended for production)
var tlsOptions = new MqttClientTlsOptions();

// 2. For self-signed certificates in development only:
// var tlsOptions = new MqttClientTlsOptions
// {
//     ServerCertificateValidationCallback = (sender, cert, chain, errors) =>
//     {
//         // WARNING: This bypasses certificate validation. Use only in development!
//         // Always validate certificates in production.
//         Console.WriteLine("Accepting self-signed certificate (development only)");
//         return true;
//     }
// };

// 3. For mutual TLS (client certificate authentication):
// var clientCert = new System.Security.Cryptography.X509Certificates.X509Certificate2("client-cert.pfx", "password");
// var tlsOptions = new MqttClientTlsOptions
// {
//     ClientCertificates = new System.Security.Cryptography.X509Certificates.X509CertificateCollection { clientCert }
// };

try
{
    // Create and connect the client with TLS
    await using var client = await clientFactory.CreateTlsTcpClient(connectOptions, tcpOptions, tlsOptions);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var connectResult = await client.ConnectAsync(cts.Token);

    if (!connectResult.IsSuccess)
    {
        Console.WriteLine($"Failed to connect: {connectResult.Reason}");
        return;
    }

    Console.WriteLine("Connected to MQTT broker over TLS!");

    // Subscribe to a topic
    const string topic = "sensors/temperature";
    var subscribeResult = await client.SubscribeAsync(topic, QualityOfService.AtLeastOnce, cts.Token);

    if (!subscribeResult.IsSuccess)
    {
        Console.WriteLine($"Failed to subscribe: {subscribeResult.Reason}");
        return;
    }

    Console.WriteLine($"Subscribed to {topic}");

    // Publish a message
    var message = new MqttMessage(topic, "23.5"u8.ToArray())
    {
        QoS = QualityOfService.AtLeastOnce,
        Retain = false
    };

    var publishResult = await client.PublishAsync(message, cts.Token);

    if (!publishResult.IsSuccess)
    {
        Console.WriteLine($"Failed to publish: {publishResult.Reason}");
        return;
    }

    Console.WriteLine("Published temperature reading!");

    // Wait to receive the message
    using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    if (await client.ReceivedMessages.WaitToReadAsync(receiveTimeout.Token))
    {
        if (client.ReceivedMessages.TryRead(out var receivedMessage))
        {
            Console.WriteLine($"Received message on {receivedMessage.Topic}");
            var temperature = System.Text.Encoding.UTF8.GetString(receivedMessage.Payload.Span);
            Console.WriteLine($"Temperature: {temperature}°C");
        }
    }

    // Disconnect
    await client.DisconnectAsync(cts.Token);
    Console.WriteLine("Disconnected from broker");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
    }
}
finally
{
    // Clean up
    await system.Terminate();
}
