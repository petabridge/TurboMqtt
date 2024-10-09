// -----------------------------------------------------------------------
// <copyright file="MqttDecodingFlows.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Collections.Immutable;
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

public static class MqttDecodingFlows
{
    /// <summary>
    /// Creates an MQTT 3.1.1 decoding flow that tries to parse multiple packets from individual byte frames.
    /// </summary>
    /// <remarks>
    /// Allocates additional byte arrays internally in order to be safe - since the contents of the shared buffer
    /// are being spread across multiple packets that all might be handled separately, this is the only safe spot
    /// to dispose of the <see cref="IMemoryOwner{T}"/> buffer.
    /// </remarks>
    /// <returns>An Akka.Streams graph - still needs to be connected to a source and a sink in order to run.</returns>
    public static IGraph<FlowShape<(IMemoryOwner<byte> buffer, int readableBytes), ImmutableList<MqttPacket>>, NotUsed> Mqtt311Decoding()
    {
        var g = Flow.Create<(IMemoryOwner<byte> buffer, int readableBytes)>()
            .Via(new Mqtt311DecoderFlow()); // flatten the IEnumerable<MqttPacket> into a stream of MqttPacket

        return g;
    }

    public static Source<ImmutableList<MqttPacket>, NotUsed> Mqtt311DecoderSource(PipeReader reader)
    {
        var g = new MqttDecoderSource(reader);
        return Source.FromGraph(g);
    }
}

/// <summary>
/// Accepts memory buffers and tries to decode them into a series of <see cref="MqttPacket"/> instances.
/// </summary>
/// <remarks>
/// Will have to allocate additional byte arrays in order to be safe if we're using shared <see cref="IMemoryOwner{T}"/> instances.
/// </remarks>
internal sealed class Mqtt311DecoderFlow : GraphStage<FlowShape<(
    IMemoryOwner<byte> buffer, int readableBytes), ImmutableList<MqttPacket>>>
{
    public Mqtt311DecoderFlow()
    {
        In = new Inlet<(IMemoryOwner<byte> buffer, int readableBytes)>("Mqtt311DecoderFlow.In");
        Out = new Outlet<ImmutableList<MqttPacket>>("Mqtt311DecoderFlow.Out");
        Shape = new FlowShape<(IMemoryOwner<byte> buffer, int readableBytes), ImmutableList<MqttPacket>>(In, Out);
    }

    public override FlowShape<(IMemoryOwner<byte> buffer, int readableBytes), ImmutableList<MqttPacket>> Shape { get; }

    public Inlet<(IMemoryOwner<byte> buffer, int readableBytes)> In { get; }
    public Outlet<ImmutableList<MqttPacket>> Out { get; }
    
    protected override Attributes InitialAttributes => DefaultAttributes.Select;
    
    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Mqtt311DecoderFlowLogic(this, inheritedAttributes);
    }
    
    private sealed class Mqtt311DecoderFlowLogic :  InAndOutGraphStageLogic
    {
        private readonly Mqtt311DecoderFlow _flow;
        private readonly Decider _decider;
        private readonly Mqtt311Decoder _decoder = new();
        
        protected override object LogSource => Akka.Event.LogSource.Create("Mqtt311DecoderFlow");
        
        public Mqtt311DecoderFlowLogic(Mqtt311DecoderFlow flow, Attributes inheritedAttributes) : base(flow.Shape)
        {
            _flow = flow;
            var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>();
            _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;
            SetHandler(_flow.In, this);
            SetHandler(_flow.Out, this);
        }

        public override void OnPush()
        {
            var (buffer, readableBytes) = Grab(_flow.In);
            
            var safeBytes = buffer.Memory[..readableBytes];

            // optimize for the case where we don't need to copy the buffer
            // UnsharedMemoryOwner is what IMqttTransport returns internally for reads
            if (buffer is not UnsharedMemoryOwner<byte>)
            {
                safeBytes = new Memory<byte>(new byte[readableBytes]);
                buffer.Memory[..(readableBytes-1)].CopyTo(safeBytes);
            }
            buffer.Dispose();
            
            var decoded = _decoder.TryDecode(safeBytes, out var packets);
            if (decoded)
            {
                Log.Debug("Decoded [{0}] packets totaling [{1}] bytes", packets.Count, readableBytes);
                Push(_flow.Out, packets);
            }
        }

        public override void OnPull()
        {
            Pull(_flow.In);
        }
    }
}