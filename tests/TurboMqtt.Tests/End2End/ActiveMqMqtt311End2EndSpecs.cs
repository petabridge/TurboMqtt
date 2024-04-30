// -----------------------------------------------------------------------
// <copyright file="ActiveMqMqtt311End2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Runtime.InteropServices;
using System.Text;
using Akka.Configuration;
using Akka.TestKit.Xunit2;
using Testcontainers.ActiveMq;
using TurboMqtt.Client;
using TurboMqtt.Protocol;
using Xunit.Abstractions;

namespace TurboMqtt.Tests.End2End;

// public class ActiveMqMqtt311End2EndSpecs: TestKit, IAsyncLifetime
// {
//     public static readonly Config DebugLogging = 
//         """
//         akka.loglevel = DEBUG
//         """;
//     
//     private readonly ArtemisContainer? _container;
//     
//     public ActiveMqMqtt311End2EndSpecs(ITestOutputHelper output, Config? config = null) : base(output:output, config:config)
//     {
//         ClientFactory = new MqttClientFactory(Sys);
//         
//         if(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
//             _container = new ArtemisBuilder()
//                 .WithImage("apache/activemq-artemis:2.31.2")
//                 .WithUsername("test")
//                 .WithPassword("test")
//                 .WithPortBinding(21883, 1883)
//                 .Build();
//     }
//
//     public async Task<IMqttClient> CreateClient()
//         => await ClientFactory.CreateTcpClient(DefaultConnectOptions, DefaultTcpOptions);
//
//     private static MqttClientTcpOptions DefaultTcpOptions => new("localhost", 21883);
//
//     public async Task InitializeAsync()
//     {
//         if(_container is not null)
//             await _container.StartAsync();
//     }
//
//     public async Task DisposeAsync()
//     {
//         if(_container is not null)
//             await _container.StopAsync();
//     }
//     
//     public virtual MqttClientConnectOptions DefaultConnectOptions =>
//         new MqttClientConnectOptions("test-client", MqttProtocolVersion.V3_1_1)
//         {
//             UserName = "test",
//             Password = "test",
//             //PublishRetryInterval = TimeSpan.FromSeconds(1),
//             KeepAliveSeconds = 60 // so it's not a relevant factor during testing
//         };
//
//     public MqttClientFactory ClientFactory { get; }
//     
//     public const string DefaultTopic = "topic";
//
//     [NonWindowsFact]
//     public async Task ShouldConnectAndDisconnect()
//     {
//         var client = await CreateClient();
//
//         using var cts = new CancellationTokenSource(RemainingOrDefault);
//         var connectResult = await client.ConnectAsync(cts.Token);
//         connectResult.IsSuccess.Should().BeTrue();
//         await client.DisconnectAsync(cts.Token);
//         client.IsConnected.Should().BeFalse();
//     }
//
//     public static readonly TheoryData<MqttMessage[]> PublishMessages = new()
//     {
//         new MqttMessage[]
//         {
//             new MqttMessage(DefaultTopic, "hello world")
//             {
//                 QoS = QualityOfService.AtMostOnce,
//                 Retain = false,
//             }
//         },
//         new MqttMessage[]
//         {
//             new MqttMessage(DefaultTopic, "hello world 1")
//             {
//                 QoS = QualityOfService.AtMostOnce,
//                 Retain = false,
//             },
//             new MqttMessage(DefaultTopic, "hello world 2")
//             {
//                 QoS = QualityOfService.AtMostOnce,
//                 Retain = false,
//             },
//             new MqttMessage(DefaultTopic, "hello world 3")
//             {
//                 QoS = QualityOfService.AtMostOnce,
//                 Retain = false,
//             }
//         },
//         new MqttMessage[]
//         {
//             new MqttMessage(DefaultTopic, "hello world 1")
//             {
//                 QoS = QualityOfService.AtLeastOnce,
//                 Retain = false,
//             },
//         },
//         new MqttMessage[]
//         {
//             new MqttMessage(DefaultTopic, "hello world 1")
//             {
//                 QoS = QualityOfService.ExactlyOnce,
//                 Retain = false,
//             },
//         }
//     };
//
//     [NonWindowsTheory]
//     [MemberData(nameof(PublishMessages))]
//     public async Task ShouldPublishMessages(MqttMessage[] messages)
//     {
//         var client = await CreateClient();
//
//         using var cts = new CancellationTokenSource(RemainingOrDefault);
//         var connectResult = await client.ConnectAsync(cts.Token);
//         connectResult.IsSuccess.Should().BeTrue();
//
//         foreach (var message in messages)
//         {
//             var publishResult = await client.PublishAsync(message, cts.Token);
//             publishResult.IsSuccess.Should().BeTrue();
//         }
//
//         await client.DisconnectAsync(cts.Token);
//         await AwaitAssertAsync(() => client.IsConnected.Should().BeFalse(), cancellationToken: cts.Token);
//     }
//
//     [NonWindowsTheory]
//     [MemberData(nameof(PublishMessages))]
//     public async Task ShouldSubscribeAndReceiveMessages(MqttMessage[] messages)
//     {
//         var client = await CreateClient();
//
//         using var cts = new CancellationTokenSource(RemainingOrDefault);
//         var connectResult = await client.ConnectAsync(cts.Token);
//         connectResult.IsSuccess.Should().BeTrue();
//
//         var subscribeResult = await client.SubscribeAsync(DefaultTopic, QualityOfService.ExactlyOnce, cts.Token);
//         subscribeResult.IsSuccess.Should().BeTrue();
//
//         var receivedMessages = new List<MqttMessage>();
//
//         // technically, we can receive our own messages that we publish - so let's do that
//         foreach (var message in messages)
//         {
//             var publishResult = await client.PublishAsync(message, cts.Token);
//             publishResult.IsSuccess.Should().BeTrue();
//         }
//
//         await foreach (var message in client.ReceivedMessages.ReadAllAsync(cts.Token))
//         {
//             receivedMessages.Add(message);
//             if (receivedMessages.Count == messages.Length)
//             {
//                 break;
//             }
//         }
//
//         receivedMessages.Should().BeEquivalentTo(messages, options => options.Excluding(c => c.Payload));
//         
//         // check that all the payloads are the same
//         for (var i = 0; i < messages.Length; i++)
//         {
//             // UTF8 decode both payloads and compare
//             var expectedPayload = Encoding.UTF8.GetString(messages[i].Payload.Span);
//             var actualPayload = Encoding.UTF8.GetString(receivedMessages[i].Payload.Span);
//             actualPayload.Should().BeEquivalentTo(expectedPayload);
//         }
//
//         await client.DisconnectAsync(cts.Token);
//         await AwaitAssertAsync(() => client.IsConnected.Should().BeFalse(), cancellationToken: cts.Token);
//     }
//     
//     [NonWindowsFact]
//     public async Task ShouldSubscribeAndReceiveMessagesWithMultipleSubscriptions()
//     {
//         var client = await CreateClient();
//
//         using var cts = new CancellationTokenSource(RemainingOrDefault);
//         var connectResult = await client.ConnectAsync(cts.Token);
//         connectResult.IsSuccess.Should().BeTrue();
//
//         var subscribeResult1 = await client.SubscribeAsync("topic1", QualityOfService.AtLeastOnce, cts.Token);
//         subscribeResult1.IsSuccess.Should().BeTrue();
//         
//         var subscribeResult2 = await client.SubscribeAsync("topic2", QualityOfService.AtLeastOnce, cts.Token);
//         subscribeResult2.IsSuccess.Should().BeTrue();
//
//         var receivedMessages = new List<MqttMessage>();
//
//         // technically, we can receive our own messages that we publish - so let's do that
//         var messages = new[]
//         {
//             new MqttMessage("topic1", "hello world 1")
//             {
//                 QoS = QualityOfService.AtLeastOnce,
//                 Retain = false,
//             },
//             new MqttMessage("topic2", "hello world 2")
//             {
//                 QoS = QualityOfService.AtLeastOnce,
//                 Retain = false,
//             }
//         };
//         
//         foreach (var message in messages)
//         {
//             var publishResult = await client.PublishAsync(message, cts.Token);
//             publishResult.IsSuccess.Should().BeTrue();
//         }
//
//         await foreach (var message in client.ReceivedMessages.ReadAllAsync(cts.Token))
//         {
//             receivedMessages.Add(message);
//             if (receivedMessages.Count == messages.Length)
//             {
//                 break;
//             }
//         }
//
//         receivedMessages.Should().BeEquivalentTo(messages, options => options.Excluding(c => c.Payload));
//         
//         // check that all the payloads are the same
//         for (var i = 0; i < messages.Length; i++)
//         {
//             // UTF8 decode both payloads and compare
//             var expectedPayload = Encoding.UTF8.GetString(messages[i].Payload.Span);
//             var actualPayload = Encoding.UTF8.GetString(receivedMessages[i].Payload.Span);
//             actualPayload.Should().BeEquivalentTo(expectedPayload);
//         }
//
//         await client.DisconnectAsync(cts.Token);
//         await AwaitAssertAsync(() => client.IsConnected.Should().BeFalse(), cancellationToken: cts.Token);
//     }
//     
//     [NonWindowsFact]
//     public async Task ShouldSubscribeAndReceiveMessagesWithMultipleSubscriptionsAndUnsubscribe()
//     {
//         var client = await CreateClient();
//
//         using var cts = new CancellationTokenSource(RemainingOrDefault);
//         var connectResult = await client.ConnectAsync(cts.Token);
//         connectResult.IsSuccess.Should().BeTrue();
//
//         var subscribeResult1 = await client.SubscribeAsync("topic1", QualityOfService.AtLeastOnce, cts.Token);
//         subscribeResult1.IsSuccess.Should().BeTrue();
//         
//         var subscribeResult2 = await client.SubscribeAsync("topic2", QualityOfService.AtLeastOnce, cts.Token);
//         subscribeResult2.IsSuccess.Should().BeTrue();
//
//         var receivedMessages = new List<MqttMessage>();
//
//         // technically, we can receive our own messages that we publish - so let's do that
//         var messages = new[]
//         {
//             new MqttMessage("topic1", "hello world 1")
//             {
//                 QoS = QualityOfService.AtLeastOnce,
//                 Retain = false,
//             },
//             new MqttMessage("topic2", "hello world 2")
//             {
//                 QoS = QualityOfService.AtLeastOnce,
//                 Retain = false,
//             }
//         };
//         
//         foreach (var message in messages)
//         {
//             var publishResult = await client.PublishAsync(message, cts.Token);
//             publishResult.IsSuccess.Should().BeTrue();
//         }
//
//         await foreach (var message in client.ReceivedMessages.ReadAllAsync(cts.Token))
//         {
//             receivedMessages.Add(message);
//             if (receivedMessages.Count == messages.Length)
//             {
//                 break;
//             }
//         }
//
//         receivedMessages.Should().BeEquivalentTo(messages, options => options.Excluding(c => c.Payload));
//         
//         // check that all the payloads are the same
//         for (var i = 0; i < messages.Length; i++)
//         {
//             // UTF8 decode both payloads and compare
//             var expectedPayload = Encoding.UTF8.GetString(messages[i].Payload.Span);
//             var actualPayload = Encoding.UTF8.GetString(receivedMessages[i].Payload.Span);
//             actualPayload.Should().BeEquivalentTo(expectedPayload);
//         }
//
//         var unsubscribeResult1 = await client.UnsubscribeAsync("topic1", cts.Token);
//         unsubscribeResult1.IsSuccess.Should().BeTrue();
//         
//         var unsubscribeResult2 = await client.UnsubscribeAsync("topic2", cts.Token);
//         unsubscribeResult2.IsSuccess.Should().BeTrue();
//         
//         // publish some additional messages
//         var additionalMessages = new[]
//         {
//             new MqttMessage("topic1", "hello world 3")
//             {
//                 QoS = QualityOfService.AtLeastOnce,
//                 Retain = false,
//             },
//             new MqttMessage("topic2", "hello world 4")
//             {
//                 QoS = QualityOfService.AtLeastOnce,
//                 Retain = false,
//             }
//         };
//         
//         foreach (var message in additionalMessages)
//         {
//             var publishResult = await client.PublishAsync(message, cts.Token);
//             publishResult.IsSuccess.Should().BeTrue();
//         }
//         
//         // we shouldn't receive any more messages
//         // try reading 1 message from the IAsyncEnumerable - or give up after 1 second
//         
//         var a = async () =>
//         {
//             using var shortCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
//             var receivedMessage = await client.ReceivedMessages.ReadAllAsync(shortCts.Token).FirstOrDefaultAsync(shortCts.Token);
//         };
//         await a.Should().ThrowAsync<OperationCanceledException>();
//     }
// }