// -----------------------------------------------------------------------
// <copyright file="MqttEncodingFlowsOptimized.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Runtime.CompilerServices;
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

/// <summary>
/// Optimized MQTT encoding flows with performance improvements
/// </summary>
public static class MqttEncodingFlowsOptimized
{
    /// <summary>
    /// Creates an optimized MQTT 3.1.1 encoding flow with better batching and memory management
    /// </summary>
    public static IGraph<FlowShape<MqttPacket, (IMemoryOwner<byte> buffer, int readableBytes)>, NotUsed>
        Mqtt311EncodingOptimized(MemoryPool<byte> memoryPool, int maxFrameSize, int maxPacketSize)
    {
        return Flow.Create<MqttPacket>()
            .Select(c => (packet: c, size: MqttPacketSizeEstimator.EstimateMqtt3PacketSize(c)))
            .Via(new PacketSizeFilter(maxPacketSize))
            .Via(new OptimizedBatchingStage(maxFrameSize))
            .Via(new Mqtt311EncoderFlowOptimized(memoryPool));
    }
}

/// <summary>
/// Optimized batching stage that minimizes allocations
/// </summary>
internal sealed class OptimizedBatchingStage : GraphStage<FlowShape<(MqttPacket packet, PacketSize size), 
    ArraySegment<(MqttPacket packet, PacketSize size)>>>
{
    private readonly int _maxFrameSize;
    
    public OptimizedBatchingStage(int maxFrameSize)
    {
        _maxFrameSize = maxFrameSize;
        In = new Inlet<(MqttPacket packet, PacketSize size)>("OptimizedBatchingStage.In");
        Out = new Outlet<ArraySegment<(MqttPacket packet, PacketSize size)>>("OptimizedBatchingStage.Out");
        Shape = new FlowShape<(MqttPacket packet, PacketSize size), 
            ArraySegment<(MqttPacket packet, PacketSize size)>>(In, Out);
    }
    
    public Inlet<(MqttPacket packet, PacketSize size)> In { get; }
    public Outlet<ArraySegment<(MqttPacket packet, PacketSize size)>> Out { get; }
    public override FlowShape<(MqttPacket packet, PacketSize size), 
        ArraySegment<(MqttPacket packet, PacketSize size)>> Shape { get; }
    
    protected override Attributes InitialAttributes => DefaultAttributes.Select;
    
    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }
    
    private sealed class Logic : InAndOutGraphStageLogic
    {
        private readonly OptimizedBatchingStage _stage;
        private readonly (MqttPacket packet, PacketSize size)[] _buffer;
        private int _bufferCount;
        private int _currentBatchSize;
        
        public Logic(OptimizedBatchingStage stage) : base(stage.Shape)
        {
            _stage = stage;
            // Pre-allocate buffer for batching
            _buffer = new (MqttPacket packet, PacketSize size)[128];
            
            SetHandler(stage.In, this);
            SetHandler(stage.Out, this);
        }
        
        public override void OnPush()
        {
            var element = Grab(_stage.In);
            
            // Check if adding this packet would exceed max frame size
            if (_currentBatchSize + element.size.TotalSize > _stage._maxFrameSize && _bufferCount > 0)
            {
                // Emit current batch
                EmitBatch();
            }
            
            // Add to batch
            _buffer[_bufferCount++] = element;
            _currentBatchSize += element.size.TotalSize;
            
            // If batch is full or we've reached frame size, emit
            if (_bufferCount >= _buffer.Length || _currentBatchSize >= _stage._maxFrameSize)
            {
                EmitBatch();
            }
            else if (!HasBeenPulled(_stage.In))
            {
                Pull(_stage.In);
            }
        }
        
        public override void OnPull()
        {
            if (_bufferCount > 0)
            {
                EmitBatch();
            }
            else if (!HasBeenPulled(_stage.In))
            {
                Pull(_stage.In);
            }
        }
        
        public override void OnUpstreamFinish()
        {
            if (_bufferCount > 0)
            {
                EmitBatch();
            }
            CompleteStage();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EmitBatch()
        {
            var segment = new ArraySegment<(MqttPacket packet, PacketSize size)>(_buffer, 0, _bufferCount);
            Push(_stage.Out, segment);
            _bufferCount = 0;
            _currentBatchSize = 0;
        }
    }
}

/// <summary>
/// Optimized encoder flow using the new high-performance encoder
/// </summary>
internal sealed class Mqtt311EncoderFlowOptimized : GraphStage<FlowShape<ArraySegment<(MqttPacket packet, PacketSize size)>, 
    (IMemoryOwner<byte> buffer, int readableBytes)>>
{
    private readonly MemoryPool<byte> _memoryPool;
    
    public Mqtt311EncoderFlowOptimized(MemoryPool<byte> memoryPool)
    {
        _memoryPool = memoryPool;
        In = new Inlet<ArraySegment<(MqttPacket packet, PacketSize size)>>("Mqtt311EncoderFlowOptimized.In");
        Out = new Outlet<(IMemoryOwner<byte> buffer, int readableBytes)>("Mqtt311EncoderFlowOptimized.Out");
        Shape = new FlowShape<ArraySegment<(MqttPacket packet, PacketSize size)>, 
            (IMemoryOwner<byte> buffer, int readableBytes)>(In, Out);
    }
    
    public Inlet<ArraySegment<(MqttPacket packet, PacketSize size)>> In { get; }
    public Outlet<(IMemoryOwner<byte> buffer, int readableBytes)> Out { get; }
    
    protected override Attributes InitialAttributes => DefaultAttributes.Select;
    
    public override FlowShape<ArraySegment<(MqttPacket packet, PacketSize size)>, 
        (IMemoryOwner<byte> buffer, int readableBytes)> Shape { get; }
    
    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this, inheritedAttributes);
    }
    
    private sealed class Logic : InAndOutGraphStageLogic
    {
        private readonly Mqtt311EncoderFlowOptimized _flow;
        private readonly MemoryPool<byte> _memoryPool;
        private readonly Decider _decider;
        
        protected override object LogSource => Akka.Event.LogSource.Create("Mqtt311EncoderFlowOptimized");
        
        public Logic(Mqtt311EncoderFlowOptimized flow, Attributes inheritedAttributes) : base(flow.Shape)
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
            var packets = Grab(_flow.In);
            
            // Calculate total size needed
            var totalBytes = 0;
            for (var i = 0; i < packets.Count; i++)
            {
                totalBytes += packets.Array![packets.Offset + i].size.TotalSize;
            }
            
            // Rent buffer from pool
            var memoryOwner = _memoryPool.Rent(totalBytes);
            var buffer = memoryOwner.Memory.Span;
            
            // Use optimized encoder
            var bytesWritten = Mqtt311EncoderOptimized.EncodePackets(
                new ReadOnlySpan<(MqttPacket packet, PacketSize size)>(
                    packets.Array, packets.Offset, packets.Count), 
                buffer);
            
            System.Diagnostics.Debug.Assert(bytesWritten == totalBytes, 
                $"Bytes written ({bytesWritten}) != predicted bytes ({totalBytes})");
            
            Log.Debug("Encoded {0} messages using {1} bytes", packets.Count, bytesWritten);
            
            Push(_flow.Out, (memoryOwner, bytesWritten));
        }
        
        public override void OnPull()
        {
            Pull(_flow.In);
        }
    }
}