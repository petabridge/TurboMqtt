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
    
    private readonly CancellationTokenSource _shutdownTokenSource = new();

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

    public Task<ConnectionTerminatedReason> WhenTerminated => _terminationSource.Task;
    
    private readonly TaskCompletionSource<bool> _waitForPendingWrites = new();
    public Task<bool> WaitForPendingWrites => _waitForPendingWrites.Task;

    public async Task CloseAsync(CancellationToken ct = default)
    {
        Status = ConnectionStatus.Disconnected;
        _writesToTransport.Writer.TryComplete();
        await _waitForPendingWrites.Task;
        await _shutdownTokenSource.CancelAsync();
        _readsFromTransport.Writer.TryComplete();
        _terminationSource.TrySetResult(ConnectionTerminatedReason.Normal);
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        if (Status == ConnectionStatus.NotStarted)
        {
            Status = ConnectionStatus.Connected;
            
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            DoByteWritesAsync(_shutdownTokenSource.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
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
        Log.Debug("Starting to read from transport.");
        while(!_writesToTransport.Reader.Completion.IsCompleted)
        {
            if (!_writesToTransport.Reader.TryRead(out var msg))
            {
                try
                {
                    await _writesToTransport.Reader.WaitToReadAsync(ct);
                }
                catch(OperationCanceledException)
                {
                    
                }
                continue;
            }
            
            try
            {
                ReadOnlyMemory<byte> buffer = msg.buffer.Memory.Slice(0, msg.readableBytes);
                Log.Debug("Received {0} bytes from transport.", buffer.Length);
                if (_decoder.TryDecode(in buffer, out var msgs))
                {
                    Log.Debug("Decoded {0} packets from transport.", msgs.Count);
                    foreach (var m in msgs)
                    {
                        HandlePacket(m);
                    }
                }
                else
                {
                    Log.Debug("Didn't have enough bytes to decode a packet. Waiting for more.");
                }
            }
            finally
            {
                // have to free the shared buffer
                msg.buffer.Dispose();
            }
        }

        // should signal that all pending writes have finished
        _waitForPendingWrites.TrySetResult(true);
    }

    public int MaxFrameSize { get; }
    public ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> Writer { get; }
    public ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> Reader { get; }
    
    private void TryPush(MqttPacket packet)
    {
        switch (ProtocolVersion)
        {
            case MqttProtocolVersion.V3_1_1:
            {
                Log.Debug("Sending packet of type {0} using {1}", packet.PacketType, ProtocolVersion);
                var estimatedSize = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(packet);
                var headerSize = MqttPacketSizeEstimator.GetPacketLengthHeaderSize(estimatedSize) + 1;
                var buffer = new Memory<byte>(new byte[estimatedSize + headerSize]);

                Mqtt311Encoder.EncodePacket(packet, ref buffer, estimatedSize);

                var unshared = new UnsharedMemoryOwner<byte>(buffer);
                
                // simulate reads back on the client here
                var didWrite = _readsFromTransport.Writer.TryWrite((unshared, estimatedSize + headerSize));
                if (!didWrite)
                {
                    Log.Error("Failed to write packet of type {0} to transport.", packet.PacketType);
                    unshared.Dispose();
                }
                else
                {
                    Log.Debug("Successfully wrote packet of type {0} [{1} bytes] to transport.", packet.PacketType, estimatedSize + headerSize);
                }
                
                break;
            }
            case MqttProtocolVersion.V5_0:
            default:
                throw new NotSupportedException();
        }
    }
    
    private readonly HashSet<string> _subscribedTopics = new();


    public void HandlePacket(MqttPacket packet)
    {
        Log.Debug("Received packet of type {0}", packet.PacketType);
        switch (packet.PacketType)
        {
            case MqttPacketType.Publish:
                var publish = (PublishPacket)packet;

                switch (publish.QualityOfService)
                {
                    case QualityOfService.AtLeastOnce:
                        var pubAck = publish.ToPubAck();
                        TryPush(pubAck);
                        break;
                    case QualityOfService.ExactlyOnce:
                        var pubRec = publish.ToPubRec();
                        TryPush(pubRec);
                        break;
                }

                // are there any subscribers to this topic?
                if (_subscribedTopics.Contains(publish.TopicName))
                {
                    // if so, we need to propagate this message to them
                    TryPush(publish);
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
                TryPush(connAck);
                break;

            case MqttPacketType.PingReq:
                var pingResp = PingRespPacket.Instance;
                TryPush(pingResp);
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

                TryPush(subAck);
                break;
            }
            case MqttPacketType.PubRec:
            {
                var pubRec = (PubRecPacket)packet;
                var pubRel = pubRec.ToPubRel();
                TryPush(pubRel);
                break;
            }
            case MqttPacketType.PubRel:
            {
                var pubRel = (PubRelPacket)packet;
                var pubComp = pubRel.ToPubComp();
                TryPush(pubComp);
                break;
            }
            case MqttPacketType.PubComp:
            {
                // nothing to do here
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
                TryPush(unsubAck);
                break;
            }
            case MqttPacketType.Disconnect:
                // shut it down
                _ = CloseAsync();
                break;
            default:
                var ex = new NotSupportedException($"Packet type {packet.PacketType} is not supported by this flow.");
                Log.Error(ex, "Received unsupported packet type {0}", packet.PacketType);
                throw ex; 
        }
    }
}