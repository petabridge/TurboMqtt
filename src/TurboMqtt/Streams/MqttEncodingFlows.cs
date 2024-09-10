// -----------------------------------------------------------------------
// <copyright file="MqttEncodingFlows.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.IO.Pipelines;
using Akka;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using TurboMqtt.IO;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Streams;

public static class MqttEncodingFlows
{
    /// <summary>
    /// Creates an MQTT 3.1.1 encoding flow that tries to group as many packets as possible into a single frame.
    /// </summary>
    /// <param name="memoryPool">Shared memory pool - buffers will get released by the <see cref="IMqttTransport"/> upon flush.</param>
    /// <param name="maxFrameSize">The maximum frame size.</param>
    /// <param name="maxPacketSize">The maximum packet size allowed on this MQTT connection</param>
    /// <returns>An Akka.Streams graph - still needs to be connected to a source and a sink in order to run.</returns>
    public static IGraph<FlowShape<MqttPacket, (IMemoryOwner<byte> buffer, int readableBytes)>, NotUsed>
        Mqtt311Encoding(MemoryPool<byte> memoryPool, int maxFrameSize, int maxPacketSize)
    {
        var g = (Flow.Create<MqttPacket>()
            .Select(c => (c, MqttPacketSizeEstimator.EstimateMqtt3PacketSize(c)))
            .Via(new PacketSizeFilter(
                maxPacketSize)) // drops any packets bigger than the maximum frame size with a warning (accounts for header too)
            .BatchWeighted(maxFrameSize, tuple => tuple.Item2.TotalSize, tuple => new List<(MqttPacket packet, PacketSize readableBytes)>(){ tuple },
                (list, tuple) =>
                {
                    list.Add(tuple);
                    return list;
                }) // group packets into a single frame
            .Via(new Mqtt311EncoderFlow(memoryPool)));

        return g;
    }
}

internal sealed class MqttEncoderSink : GraphStage<SinkShape<List<(MqttPacket packet, PacketSize predictedSize)>>>
{
    private readonly PipeWriter _writer;
    private readonly MqttProtocolVersion _protocolVersion;

    public MqttEncoderSink(PipeWriter writer, MqttProtocolVersion protocolVersion = MqttProtocolVersion.V3_1_1)
    {
        _writer = writer;
        _protocolVersion = protocolVersion;
        In = new Inlet<List<(MqttPacket packet, PacketSize predictedSize)>>($"MqttEncoderFlow{_protocolVersion}.In");
        Shape = new SinkShape<List<(MqttPacket packet, PacketSize predictedSize)>>(In);
    }
    
    public Inlet<List<(MqttPacket packet, PacketSize predictedSize)>> In { get; }

    public override SinkShape<List<(MqttPacket packet, PacketSize predictedSize)>> Shape { get; }
    protected override Attributes InitialAttributes => DefaultAttributes.Select;
    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        throw new NotImplementedException();
    }

    private sealed class Logic : InGraphStageLogic
    {
        private readonly PipeWriter _pipeWriter;
        
        public Logic(MqttEncoderSink encoderSink) : base(encoderSink.Shape)
        {
            _pipeWriter = encoderSink._writer;
            SetHandler(encoderSink.In, this);
        }

        public override void OnPush()
        {
            
        }
    }
}

/// <summary>
/// Accepts a range of MqttPackets and uses a shared memory pool for encode them into an <see cref="IMemoryOwner{T}"/>
/// </summary>
/// <remarks>
/// The stages above this one guarantee that the total size of the packets will not exceed the maximum frame size.
/// </remarks>
internal sealed class Mqtt311EncoderFlow : GraphStage<FlowShape<List<(MqttPacket packet, PacketSize predictedSize)>, (
    IMemoryOwner<byte> buffer, int readableBytes)>>
{
    private readonly MemoryPool<byte> _memoryPool;

    public Mqtt311EncoderFlow(MemoryPool<byte> memoryPool)
    {
        _memoryPool = memoryPool;
        In = new Inlet<List<(MqttPacket packet, PacketSize predictedSize)>>("Mqtt311EncoderFlow.In");
        Out = new Outlet<(IMemoryOwner<byte> buffer, int readableBytes)>("Mqtt311EncoderFlow.Out");
        Shape =
            new FlowShape<List<(MqttPacket packet, PacketSize predictedSize)>, (IMemoryOwner<byte> buffer, int
                readableBytes)>(In, Out);
    }

    public Inlet<List<(MqttPacket packet, PacketSize predictedSize)>> In { get; }
    public Outlet<(IMemoryOwner<byte> buffer, int readableBytes)> Out { get; }

    protected override Attributes InitialAttributes => DefaultAttributes.Select;

    public override
        FlowShape<List<(MqttPacket packet, PacketSize predictedSize)>, (IMemoryOwner<byte> buffer, int readableBytes)>
        Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this, inheritedAttributes);
    }

    private sealed class Logic : InAndOutGraphStageLogic
    {
        private readonly Mqtt311EncoderFlow _flow;
        private readonly Decider _decider;
        private readonly MemoryPool<byte> _memoryPool;
        
        protected override object LogSource => Akka.Event.LogSource.Create("Mqtt311EncoderFlow");

        public Logic(Mqtt311EncoderFlow flow, Attributes inheritedAttributes) : base(flow.Shape)
        {
            _flow = flow;
            _memoryPool = flow._memoryPool;
            var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>();
            _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;
            SetHandler(flow.In, this);
            SetHandler(flow.Out, this);
        }

        // TODO: error handling
        
        public override void OnPush()
        {
            // DOH! have to allocate a list here if we haven't already
            var packets = Grab(_flow.In).ToList();

            // sum the size of the payloads and their headers
            var totalBytes = packets.Select(c =>
                c.predictedSize.TotalSize).Sum();

            var memoryOwner = _memoryPool.Rent(totalBytes);
            var writeableBuffer = memoryOwner.Memory;

            var bytesWritten = Mqtt311Encoder.EncodePackets(packets, ref writeableBuffer);

            System.Diagnostics.Debug.Assert(bytesWritten == totalBytes, "bytesWritten == predictedBytes");

            Log.Debug("Encoded {0} messages using {1} bytes", packets.Count, bytesWritten);

            Push(_flow.Out, (memoryOwner, bytesWritten));
        }

        public override void OnPull()
        {
            Pull(_flow.In);
        }
    }
}