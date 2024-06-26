﻿// -----------------------------------------------------------------------
// <copyright file="FakeServerHandle.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using Akka.Event;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.IO;

internal interface IFakeServerHandle
{
    public Task<string> WhenClientIdAssigned { get; }

    public Task WhenTerminated { get; }
    MqttProtocolVersion ProtocolVersion { get; }
    ILoggingAdapter Log { get; }
    void HandleBytes(in ReadOnlyMemory<byte> bytes);
    void HandlePacket(MqttPacket packet);
    bool TryPush(MqttPacket outboundPacket);

    /// <summary>
    /// Encodes and writes the packets out to the transport
    /// </summary>
    void FlushPackets();

    void DisconnectFromServer();
}

internal class FakeMqtt311ServerHandle : IFakeServerHandle
{
    protected readonly TaskCompletionSource<string> ClientIdAssigned = new();
    private readonly Mqtt311Decoder _decoder = new();
    private readonly Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool> _pushMessage;
    private readonly Func<Task> _closingAction;
    private readonly HashSet<string> _subscribedTopics = [];
    private readonly TimeSpan _heartbeatDelay;
    private readonly TaskCompletionSource _terminated = new();
    private readonly List<(MqttPacket packet, PacketSize estimatedSize)> _pendingPackets = new();


    public FakeMqtt311ServerHandle(Func<(IMemoryOwner<byte> buffer, int estimatedSize), bool> pushMessage,
        Func<Task> closingAction, ILoggingAdapter log, TimeSpan? heartbeatDelay = null)
    {
        _pushMessage = pushMessage;
        _closingAction = closingAction;
        Log = log;
        _heartbeatDelay = heartbeatDelay ?? TimeSpan.Zero;
    }

    public bool TryPush(MqttPacket packet)
    {
        if (Log.IsDebugEnabled)
            Log.Debug("Sending packet of type {0} using {1}", packet.PacketType, ProtocolVersion);
        var estimatedSize = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(packet);

        _pendingPackets.Add((packet, estimatedSize));

        return true;
    }

    public virtual void FlushPackets()
    {
        if (_pendingPackets.Count == 0)
            return;
        Log.Info("Starting flush");
        var totalSize = _pendingPackets.Sum(c =>
            c.estimatedSize.TotalSize); // fixed length header
        var bufferPooled = new UnsharedMemoryOwner<byte>(new Memory<byte>(new byte[totalSize]));
        var buffer = bufferPooled.Memory[..totalSize];

        var encodedBytes = Mqtt311Encoder.EncodePackets(_pendingPackets, ref buffer);

        // assert that encodedBytes == totalSize
        if (encodedBytes != totalSize)
        {
            var errMsg = $"Expected to encode {totalSize} bytes, but only encoded {encodedBytes} bytes.";
            Log.Error(errMsg);
            throw new ArgumentOutOfRangeException(errMsg);
        }


        // simulate reads back on the client here
        var didWrite = _pushMessage((bufferPooled, encodedBytes));
        if (!didWrite)
        {
            Log.Error("Failed to write [{0}] packets [{1} bytes] to transport.", _pendingPackets.Count, totalSize);
        }
        else
        {
            Log.Debug("Successfully wrote N packets {0} [{1} bytes] to transport.", _pendingPackets.Count, totalSize);
        }

        bufferPooled.Dispose();
        _pendingPackets.Clear();
    }

    public void DisconnectFromServer()
    {
        // use this to tell the client we're disconnecting
        var pushed = TryPush(DisconnectPacket.Instance);
        if (!pushed)
        {
            Log.Warning("Failed to write DISCONNECT packet to transport.");
        }

        _closingAction();
        _terminated.TrySetResult();
    }

    public Task<string> WhenClientIdAssigned => ClientIdAssigned.Task;
    public Task WhenTerminated => _terminated.Task;
    public MqttProtocolVersion ProtocolVersion => MqttProtocolVersion.V3_1_1;
    public ILoggingAdapter Log { get; }

    public void HandleBytes(in ReadOnlyMemory<byte> bytes)
    {
        if (_decoder.TryDecode(bytes, out var packets))
        {
            if (Log.IsDebugEnabled)
                Log.Debug("Decoded {0} packets from transport.", packets.Count);
            foreach (var packet in packets)
            {
                HandlePacket(packet);
            }

            FlushPackets();
        }
        else
        {
            if (Log.IsDebugEnabled)
                Log.Debug("Didn't have enough bytes to decode a packet. Waiting for more.");
        }
    }

    public virtual void HandlePacket(MqttPacket packet)
    {
        if (Log.IsDebugEnabled)
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
                ClientIdAssigned.TrySetResult(connect.ClientId);
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

                // schedule a heartbeat response according to the delay interval
                if (_heartbeatDelay > TimeSpan.Zero)
                {
                    Task.Delay(_heartbeatDelay).ContinueWith(_ => TryPush(pingResp));
                }
                else
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
                _ = _closingAction();
                _terminated.TrySetResult();
                break;
            default:
                var ex = new NotSupportedException($"Packet type {packet.PacketType} is not supported by this flow.");
                Log.Error(ex, "Received unsupported packet type {0}", packet.PacketType);
                throw ex;
        }
    }
}