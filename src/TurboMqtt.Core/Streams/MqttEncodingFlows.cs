// -----------------------------------------------------------------------
// <copyright file="MqttEncodingFlows.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using Akka;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using TurboMqtt.Core.IO;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Streams;

public static class MqttEncodingFlows
{
    /// <summary>
    /// Creates an MQTT 3.1.1 encoding flow that tries to group as many packets as possible into a single frame.
    /// </summary>
    /// <param name="memoryPool">Shared memory pool - buffers will get released by the <see cref="IMqttTransport"/> upon flush.</param>
    /// <param name="maxFrameSize">The maximum frame size.</param>
    /// <returns>An Akka.Streams graph - still needs to be connected to a source and a sink in order to run.</returns>
    public static IGraph<FlowShape<MqttPacket, (IMemoryOwner<byte> buffer, int readableBytes)>, NotUsed>
        Mqtt311Encoding(MemoryPool<byte> memoryPool, int maxFrameSize)
    {
        var g = (Flow.Create<MqttPacket>()
            .Select(c => (c, MqttPacketSizeEstimator.EstimateMqtt3PacketSize(c)))
            .Via(new PacketSizeFilter(
                maxFrameSize)) // drops any packets bigger than the maximum frame size with a warning (accounts for header too)
            .GroupedWeightedWithin(maxFrameSize, int.MaxValue, TimeSpan.FromMicroseconds(20), p => p.Item2)
            .Via(new Mqtt311EncoderFlow(memoryPool)));

        return g;
    }
}

/// <summary>
/// Accepts a range of MqttPackets and uses a shared memory pool for encode them into an <see cref="IMemoryOwner{T}"/>
/// </summary>
/// <remarks>
/// The stages above this one guarantee that 
/// </remarks>
internal sealed class Mqtt311EncoderFlow : GraphStage<FlowShape<IEnumerable<(MqttPacket packet, int predictedSize)>, (
    IMemoryOwner<byte> buffer, int readableBytes)>>
{
    private readonly MemoryPool<byte> _memoryPool;

    public Mqtt311EncoderFlow(MemoryPool<byte> memoryPool)
    {
        _memoryPool = memoryPool;
        In = new Inlet<IEnumerable<(MqttPacket packet, int predictedSize)>>("Mqtt311EncoderFlow.In");
        Out = new Outlet<(IMemoryOwner<byte> buffer, int readableBytes)>("Mqtt311EncoderFlow.Out");
        Shape =
            new FlowShape<IEnumerable<(MqttPacket packet, int predictedSize)>, (IMemoryOwner<byte> buffer, int
                readableBytes)>(In, Out);
    }

    public Inlet<IEnumerable<(MqttPacket packet, int predictedSize)>> In { get; }
    public Outlet<(IMemoryOwner<byte> buffer, int readableBytes)> Out { get; }

    protected override Attributes InitialAttributes => DefaultAttributes.Select;

    public override
        FlowShape<IEnumerable<(MqttPacket packet, int predictedSize)>, (IMemoryOwner<byte> buffer, int readableBytes)>
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

        public Logic(Mqtt311EncoderFlow flow, Attributes inheritedAttributes) : base(flow.Shape)
        {
            _flow = flow;
            _memoryPool = flow._memoryPool;
            var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>();
            _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;
            SetHandler(flow.In, this);
            SetHandler(flow.Out, this);
        }

        public override void OnPush()
        {
            // DOH! have to allocate a list here if we haven't already
            var packets = Grab(_flow.In).ToList();

            // sum the size of the payloads and their headers
            var totalBytes = packets.Select(c =>
                c.predictedSize + MqttPacketSizeEstimator.GetPacketLengthHeaderSize(c.predictedSize) + 1).Sum();

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