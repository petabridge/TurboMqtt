// -----------------------------------------------------------------------
// <copyright file="OpenTelemetryFlows.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Akka;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;
using TurboMqtt.Telemetry;

namespace TurboMqtt.Streams;

/// <summary>
/// INTERNAL API
/// </summary>
internal static class OpenTelemetryFlows
{
    public static IGraph<FlowShape<MqttPacket, MqttPacket>, NotUsed> MqttPacketRateTelemetryFlow(
        MqttProtocolVersion version, string clientId, OpenTelemetrySupport.Direction direction)
    {
        var g = new MqttPacketRateTelemetryFlow(version, clientId, direction);
        return g;
    }

    public static
        IGraph<FlowShape<(IMemoryOwner<byte> buffer, int readableBytes), (IMemoryOwner<byte> buffer, int readableBytes)>
            , NotUsed> MqttBitRateTelemetryFlow(
            MqttProtocolVersion version, string clientId, OpenTelemetrySupport.Direction direction)
    {
        var g = new MqttBitRateTelemetryFlow(version, clientId, direction);
        return g;
    }
}

/// <summary>
/// Used to measure the inbound and outbound bit rate of MQTT packets.
/// </summary>
internal sealed class MqttBitRateTelemetryFlow : GraphStage<FlowShape<(
    IMemoryOwner<byte> buffer, int readableBytes), (IMemoryOwner<byte> buffer, int readableBytes)>>
{
    private readonly string _clientId;
    private readonly MqttProtocolVersion _version;
    private readonly OpenTelemetrySupport.Direction _direction;

    public MqttBitRateTelemetryFlow(MqttProtocolVersion version, string clientId, 
        OpenTelemetrySupport.Direction direction)
    {
        _clientId = clientId;
        _version = version;
        _direction = direction;
        Shape =
            new FlowShape<(IMemoryOwner<byte> buffer, int readableBytes), (IMemoryOwner<byte> buffer, int readableBytes
                )>(In, Out);
    }

    public Inlet<(IMemoryOwner<byte> buffer, int readableBytes)> In { get; } = new("MqttBitRateTelemetryFlow.in");
    public Outlet<(IMemoryOwner<byte> buffer, int readableBytes)> Out { get; } = new("MqttBitRateTelemetryFlow.out");

    public override
        FlowShape<(IMemoryOwner<byte> buffer, int readableBytes), (IMemoryOwner<byte> buffer, int readableBytes)> Shape
    {
        get;
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private sealed class Logic : InAndOutGraphStageLogic
    {
        private readonly MqttBitRateTelemetryFlow _flow;
        private readonly Counter<long> _bitRateCounter;
        private readonly TagList _defaultTags;

        public Logic(MqttBitRateTelemetryFlow flow) : base(flow.Shape)
        {
            _flow = flow;
            _bitRateCounter = OpenTelemetrySupport.CreateBitRateCounter(flow._direction);
            _defaultTags = OpenTelemetrySupport.CreateTags(flow._clientId, flow._version);
            
            SetHandler(_flow.In, this);
            SetHandler(_flow.Out, this);
        }

        public override void OnPush()
        {
            var msg = Grab(_flow.In);

            // record the number of bytes
            _bitRateCounter.Add(msg.readableBytes, _defaultTags);

            Push(_flow.Out, msg);
        }

        public override void OnPull()
        {
            Pull(_flow.In);
        }
    }
}

/// <summary>
/// Uses OTEL metrics to measure the flow of inbound MQTT packets.
/// </summary>
internal sealed class MqttPacketRateTelemetryFlow : GraphStage<FlowShape<MqttPacket, MqttPacket>>
{
    private readonly string _clientId;
    private readonly MqttProtocolVersion _version;
    private readonly OpenTelemetrySupport.Direction _direction;


    public MqttPacketRateTelemetryFlow(MqttProtocolVersion version, string clientId,
        OpenTelemetrySupport.Direction direction)
    {
        _version = version;
        _clientId = clientId;
        _direction = direction;
        Shape = new FlowShape<MqttPacket, MqttPacket>(In, Out);
    }

    public override FlowShape<MqttPacket, MqttPacket> Shape { get; }
    public Inlet<MqttPacket> In { get; } = new("MqttPacketRateTelemetryFlow.in");
    public Outlet<MqttPacket> Out { get; } = new("MqttPacketRateTelemetryFlow.out");

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private class Logic : InAndOutGraphStageLogic
    {
        private readonly MqttPacketRateTelemetryFlow _flow;
        private readonly Counter<long> _msgCounter;
        private readonly TagList _defaultTags;

        public Logic(MqttPacketRateTelemetryFlow flow) : base(flow.Shape)
        {
            _flow = flow;

            _msgCounter = OpenTelemetrySupport.CreateMessagesCounter(flow._direction);
            _defaultTags = OpenTelemetrySupport.CreateTags(flow._clientId, flow._version);
            
            SetHandler(_flow.In, this);
            SetHandler(_flow.Out, this);
        }

        public override void OnPush()
        {
            var msg = Grab(_flow.In);
            
            var newTags = _defaultTags;
            newTags.Add(OpenTelemetrySupport.PacketTypeTag, msg.PacketType);

            // record the packet type and QoS level for publish packets
            if (msg.PacketType == MqttPacketType.Publish)
            {
                newTags.Add(OpenTelemetrySupport.QoSLevelTag, msg.QualityOfService);
            }
            
            _msgCounter.Add(1,
                newTags);

            Push(_flow.Out, msg);
        }

        public override void OnPull()
        {
            Pull(_flow.In);
        }
    }
}