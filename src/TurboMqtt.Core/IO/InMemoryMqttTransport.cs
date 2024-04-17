// -----------------------------------------------------------------------
// <copyright file="InMemoryMqttTransport.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Threading.Channels;
using Akka.Event;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.IO;

/// <summary>
/// Intended for use in testing scenarios where we want to simulate a network connection
/// </summary>
internal sealed class InMemoryMqttTransport : IMqttTransport
{
    private readonly TaskCompletionSource<ConnectionTerminatedReason> _terminationSource = new();

    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _writesToTransport =
        Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();

    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)> _readsFromTransport =
        Channel.CreateUnbounded<(IMemoryOwner<byte> buffer, int readableBytes)>();

    public InMemoryMqttTransport(int maxFrameSize, ILoggingAdapter log, MqttProtocolVersion protocolVersion)
    {
        MaxFrameSize = maxFrameSize;
        Log = log;
        ProtocolVersion = protocolVersion;
        Reader = _readsFromTransport;
        Writer = _writesToTransport;
    }

    public MqttProtocolVersion ProtocolVersion { get; }

    public ILoggingAdapter Log { get; }
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.NotStarted;

    public Task<ConnectionTerminatedReason> WaitForTermination()
    {
        return _terminationSource.Task;
    }

    public Task CloseAsync(CancellationToken ct = default)
    {
        Status = ConnectionStatus.Disconnected;
        _terminationSource.TrySetResult(ConnectionTerminatedReason.Normal);
        return Task.CompletedTask;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        if (Status != ConnectionStatus.NotStarted)
        {
            Status = ConnectionStatus.Connected;
        }

        return Task.CompletedTask;
    }
    
    private readonly Mqtt311Decoder _decoder = new();

    /// <summary>
    /// Simulates the "Server" side of this connection
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    private async Task DoByteWritesAsync(CancellationToken ct)
    {
        await foreach(var msg in _writesToTransport.Reader.ReadAllAsync(ct))
        {
            ReadOnlyMemory<byte> buffer = msg.buffer.Memory;
            if (_decoder.TryDecode(in buffer, out var msgs))
            {
                foreach(var m in msgs)
                {
                    await HandlePacket(m);
                }
            }
        }
    }

    public int MaxFrameSize { get; }
    public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> Writer { get; }
    public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> Reader { get; }

    private async ValueTask TryPush(MqttPacket packet)
    {
        switch (ProtocolVersion)
        {
            case MqttProtocolVersion.V3_1_1:
            {
                var estimatedSize = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(packet);
                var headerSize = MqttPacketSizeEstimator.GetPacketLengthHeaderSize(estimatedSize) + 1;
                var buffer = new Memory<byte>(new byte[estimatedSize + headerSize]);

                Mqtt311Encoder.EncodePacket(packet, ref buffer, estimatedSize);

                var unshared = new UnsharedMemoryOwner<byte>(buffer);
                
                // simulate reads back on the client here
                await _readsFromTransport.Writer.WriteAsync((unshared, estimatedSize + headerSize));
                break;
            }
            case MqttProtocolVersion.V5_0:
                throw new NotSupportedException();
        }
    }
    
    private readonly HashSet<string> _subscribedTopics = new();


    public async ValueTask HandlePacket(MqttPacket packet)
    {
        switch (packet.PacketType)
        {
            case MqttPacketType.Publish:
                var publish = (PublishPacket)packet;

                switch (publish.QualityOfService)
                {
                    case QualityOfService.AtLeastOnce:
                        var pubAck = publish.ToPubAck();
                        await TryPush(pubAck);
                        break;
                    case QualityOfService.ExactlyOnce:
                        var pubRec = publish.ToPubRec();
                        await TryPush(pubRec);
                        break;
                }

                // are there any subscribers to this topic?
                if (_subscribedTopics.Contains(publish.TopicName))
                {
                    // if so, we need to propagate this message to them
                    await TryPush(publish);
                }

                break;
            case MqttPacketType.PubAck:
            {
                // nothing to do here
                break;
            }
            case MqttPacketType.Connect:
                var connect = (ConnectPacket)packet;
                var connAck = new ConnAckPacket()
                {
                    SessionPresent = true,
                    ReasonCode = ConnAckReasonCode.Success,
                    MaximumPacketSize = connect.MaximumPacketSize
                };
                await TryPush(connAck);
                break;

            case MqttPacketType.PingReq:
                var pingResp = PingRespPacket.Instance;
                await TryPush(pingResp);
                break;
            case MqttPacketType.Subscribe:
            {
                var subscribe = (SubscribePacket)packet;
                foreach (var topic in subscribe.Topics)
                {
                    _subscribedTopics.Add(topic.Topic);
                }

                var subAck = subscribe.ToSubAckPacket(subscribe.Topics.Select(c =>
                {
                    // does realistic validation here
                    if (!MqttTopicValidator.ValidateSubscribeTopic(c.Topic).IsValid)
                        return MqttSubscribeReasonCode.TopicFilterInvalid;

                    return c.Options.QoS switch
                    {
                        QualityOfService.AtMostOnce => MqttSubscribeReasonCode.GrantedQoS0,
                        QualityOfService.AtLeastOnce => MqttSubscribeReasonCode.GrantedQoS1,
                        QualityOfService.ExactlyOnce => MqttSubscribeReasonCode.GrantedQoS2,
                        _ => MqttSubscribeReasonCode.UnspecifiedError
                    };
                }).ToArray());

                await TryPush(subAck);
                break;
            }
            case MqttPacketType.PubRec:
            {
                var pubRec = (PubRecPacket)packet;
                var pubRel = pubRec.ToPubRel();
                await TryPush(pubRel);
                break;
            }
            case MqttPacketType.PubRel:
            {
                var pubRel = (PubRelPacket)packet;
                var pubComp = pubRel.ToPubComp();
                await TryPush(pubComp);
                break;
            }
            case MqttPacketType.Unsubscribe:
            {
                var unsubscribe = (UnsubscribePacket)packet;
                foreach (var topic in unsubscribe.Topics)
                {
                    _subscribedTopics.Remove(topic);
                }

                var unsubAck = new UnsubAckPacket
                {
                    PacketId = unsubscribe.PacketId,
                    ReasonCodes = unsubscribe.Topics.Select(c =>
                    {
                        if (!MqttTopicValidator.ValidateSubscribeTopic(c).IsValid)
                            return MqttUnsubscribeReasonCode.TopicFilterInvalid;

                        return MqttUnsubscribeReasonCode.Success;
                    }).ToArray()
                };
                await TryPush(unsubAck);
                break;
            }
            case MqttPacketType.Disconnect:
                // shut it down
                _writesToTransport.Writer.Complete();
                break;
            default:
                throw new NotSupportedException($"Packet type {packet.PacketType} is not supported by this flow.");
        }
    }
}