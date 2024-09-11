// -----------------------------------------------------------------------
// <copyright file="MqttDecoderSource.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipelines;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Streams;

internal sealed class MqttDecoderSource : GraphStage<SourceShape<ImmutableList<MqttPacket>>>
{
    private readonly PipeReader _reader;
    private readonly MqttProtocolVersion _protocolVersion;

    public MqttDecoderSource(PipeReader reader, MqttProtocolVersion protocolVersion = MqttProtocolVersion.V3_1_1)
    {
        _reader = reader;
        _protocolVersion = protocolVersion;
        Out = new Outlet<ImmutableList<MqttPacket>>($"MqttDecoderSink{_protocolVersion}.Out");
        Shape = new SourceShape<ImmutableList<MqttPacket>>(Out);
    }

    protected override Attributes InitialAttributes => DefaultAttributes.Select;

    public Outlet<ImmutableList<MqttPacket>> Out { get; }

    public override SourceShape<ImmutableList<MqttPacket>> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private class Logic : OutGraphStageLogic
    {
        private readonly PipeReader _pipeReader;
        private readonly MqttProtocolVersion _protocolVersion;
        private readonly MqttDecoderSource _graphStage;
        private readonly Mqtt311Decoder _mqtt311Decoder;
        private readonly Action<ImmutableList<MqttPacket>> _onReadReady;
        private readonly CancellationTokenSource _shutdownCts = new();

        // ImmutableLists are returned by the decoder, and we queue them to preserve order before emission
        private ImmutableQueue<ImmutableList<MqttPacket>> _packets = ImmutableQueue<ImmutableList<MqttPacket>>.Empty;
        
        public Logic(MqttDecoderSource graphStage) : base(graphStage.Shape)
        {
            _graphStage = graphStage;
            _pipeReader = graphStage._reader;
            _protocolVersion = graphStage._protocolVersion;
            _mqtt311Decoder = new Mqtt311Decoder();
            _onReadReady = GetAsyncCallback<ImmutableList<MqttPacket>>(HandleReadResult);
            SetHandler(graphStage.Out, this);
        }

        private async Task ReadLoop()
        {
            while (!_shutdownCts.IsCancellationRequested)
            {
                var result = await _pipeReader.ReadAsync(_shutdownCts.Token);

                if (result.IsCompleted)
                {
                    CompleteStage();
                }

                if (result.IsCanceled)
                {
                    continue;
                }

                var buffer = result.Buffer;
            
                if (!buffer.IsEmpty)
                {
                    // consume this entire sequence by copying it into a new buffer
                    // have to copy because there's no guarantee we can safely release a shared buffer
                    // once we hand the message over to the end-user.
                    var newMemory = new Memory<byte>(new byte[buffer.Length]);
                    buffer.CopyTo(newMemory.Span);
                    _pipeReader.AdvanceTo(buffer.End);
            
                    if (_mqtt311Decoder.TryDecode(newMemory, out var decoded))
                    {
                        Log.Debug("Decoded [{0}] packets totaling [{1}] bytes", decoded.Count, newMemory.Length);
                        _onReadReady(decoded);
                    }
                }
            }
        }

        public override void PostStop()
        {
            _shutdownCts.Cancel();
            base.PostStop();
        }

        public override void PreStart()
        {
            // start the read loop asynchronously
            _  =  ReadLoop();
            base.PreStart();
        }

        public override void OnPull()
        {
            if (_packets.IsEmpty) return;
            
            // aggregate all the packets we've decoded so far
            var builder = ImmutableList.CreateBuilder<MqttPacket>();
            foreach (var packetSet in _packets)
            {
                builder.AddRange(packetSet);
            }
            _packets = ImmutableQueue<ImmutableList<MqttPacket>>.Empty;
            Push(_graphStage.Out, builder.ToImmutable());
        }

        private void HandleReadResult(ImmutableList<MqttPacket> decoded)
        {
            
            if (IsAvailable(_graphStage.Out))
            {
                Push(_graphStage.Out, decoded);
            }
            else
            {
                _packets = _packets.Enqueue(decoded);
            }
        }
    }
}