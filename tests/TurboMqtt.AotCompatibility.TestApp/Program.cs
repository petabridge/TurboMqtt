using Akka.Actor;
using TurboMqtt.Core;
using TurboMqtt.Core.Client;

var actorSystem = ActorSystem.Create("AotTestSystem");
var clientFactory = new MqttClientFactory(actorSystem);

var inMemoryClient =
    await clientFactory.CreateInMemoryClient(new MqttClientConnectOptions("test-client",
        TurboMqtt.Core.Protocol.MqttProtocolVersion.V3_1_1));
        
var connectResult = await inMemoryClient.ConnectAsync(CancellationToken.None);

if (!connectResult.IsSuccess)
{
    Console.WriteLine("Failed to connect to broker: " + connectResult.Reason);
    return;
}

Console.WriteLine("Connected to broker.");

// go with QoS 2 because it exercises the most stuff
var subscribeResult = await inMemoryClient.SubscribeAsync("test-topic", QualityOfService.ExactlyOnce, CancellationToken.None);

if (!subscribeResult.IsSuccess)
{
    Console.WriteLine("Failed to subscribe to topic: " + subscribeResult.Reason);
    return;
}

Console.WriteLine("Subscribed to topic.");

const int MsgCount = 10;

// publish 10 messages with QoS 2
for (var i = 0; i < MsgCount; i++)
{
    var publishResult = await inMemoryClient.PublishAsync(new MqttMessage("test-topic", "test-payload")
    {
        QoS = QualityOfService.ExactlyOnce
    }, CancellationToken.None);

    if (!publishResult.IsSuccess)
    {
        Console.WriteLine("Failed to publish message: " + publishResult.Reason);
        return;
    }

    Console.WriteLine("Published message.");
}

Console.WriteLine("Published all messages.");

// receive all of the messages we just published
using var quickCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var receivedMessages = inMemoryClient.ReceivedMessages;
var recvMsgCount = 0;
while(recvMsgCount < MsgCount && await receivedMessages.WaitToReadAsync(quickCts.Token))
{
    while(receivedMessages.TryRead(out var msg))
    {
        Console.WriteLine("Received message: " + msg.Payload);
        recvMsgCount++;
    }
}

Console.WriteLine("Received all messages.");
await inMemoryClient.DisconnectAsync(CancellationToken.None);