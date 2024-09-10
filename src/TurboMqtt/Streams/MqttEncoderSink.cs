// -----------------------------------------------------------------------
// <copyright file="MqttEncoderSink.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.IO.Pipelines;
using Akka;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Streams;

internal sealed class MqttEncoderSink : GraphStage<SinkShape<List<(MqttPacket packet, PacketSize predictedSize)>>>
{
    private readonly PipeWriter _writer;
    private readonly MqttProtocolVersion _protocolVersion;

    public MqttEncoderSink(PipeWriter writer, MqttProtocolVersion protocolVersion = MqttProtocolVersion.V3_1_1)
    {
        _writer = writer;
        _protocolVersion = protocolVersion;
        In = new Inlet<List<(MqttPacket packet, PacketSize predictedSize)>>($"MqttEncoderSink{_protocolVersion}.In");
        Shape = new SinkShape<List<(MqttPacket packet, PacketSize predictedSize)>>(In);
    }
    
    public Inlet<List<(MqttPacket packet, PacketSize predictedSize)>> In { get; }

    public override SinkShape<List<(MqttPacket packet, PacketSize predictedSize)>> Shape { get; }
    protected override Attributes InitialAttributes => DefaultAttributes.Select;
    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private sealed class Logic : InGraphStageLogic
    {
        private readonly PipeWriter _pipeWriter;
        private readonly MqttProtocolVersion _protocolVersion;
        private readonly MqttEncoderSink _graphStage;
        private readonly Action<FlushResult> _flushCallback;
        
        public Logic(MqttEncoderSink encoderSink) : base(encoderSink.Shape)
        {
            _pipeWriter = encoderSink._writer;
            _protocolVersion = encoderSink._protocolVersion;
            _graphStage = encoderSink;
            _flushCallback = GetAsyncCallback<FlushResult>(OnFlushComplete);
            SetHandler(encoderSink.In, this);
        }

        private void OnFlushComplete(FlushResult obj)
        {
            if (obj.IsCompleted)
            {
                Log.Info("PipeWriter completed - terminating stage");
                CompleteStage();
                return;
            }

            if (obj.IsCanceled)
            {
                Log.Debug("Pending flush was cancelled - we'll catch it on next attempt");
            }
        }

        protected override object LogSource => Akka.Event.LogSource.Create($"Mqtt{_protocolVersion}EncoderFlow");

        public override void OnPush()
        {
            var packets = Grab(_graphStage.In);
            var totalBytes = packets.Sum(c => c.predictedSize.TotalSize);

            var buffer = _pipeWriter.GetMemory(totalBytes);
            // TODO: handle switching between MQTT 3.1.1 and 5
            var bytesWritten = Mqtt311Encoder.EncodePackets(packets, ref buffer);

            System.Diagnostics.Debug.Assert(bytesWritten == totalBytes, "bytesWritten == predictedBytes");

            Log.Debug("Encoded {0} messages using {1} bytes", packets.Count, bytesWritten);

            DoFlush().GetAwaiter().GetResult();
            return;

            async Task DoFlush()
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                _pipeWriter.Advance(totalBytes);
                _flushCallback(await _pipeWriter.FlushAsync(cts.Token));
            }
        }
        
        public override void PreStart()
        {
            Pull(_graphStage.In);
            base.PreStart();
        }
        
        public override void OnUpstreamFailure(Exception e)
        {
            base.OnUpstreamFailure(e);
            _pipeWriter.Complete(e);
        }
    }
}