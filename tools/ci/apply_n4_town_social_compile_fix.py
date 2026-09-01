#!/usr/bin/env python3
from pathlib import Path

p = Path('src/TerraRuntime.Protocol.Multiplicity/TerrariaEmoteBubbleCodec.cs')
text = p.read_text(encoding='utf-8-sig')
old = '''        Span<byte> scratch = stackalloc byte[CreatePayloadLength];
        ReadOnlySpan<byte> payload;
        if (frame.Payload.IsSingleSegment)
        {
            payload = frame.Payload.FirstSpan;
        }
        else
        {
            int offset = 0;
            foreach (ReadOnlyMemory<byte> segment in frame.Payload)
            {
                segment.Span.CopyTo(scratch[offset..]);
                offset += segment.Length;
            }
            payload = scratch[..checked((int)frame.Payload.Length)];
        }
'''
new = '''        byte[] payloadBytes = frame.Payload.ToArray();
        ReadOnlySpan<byte> payload = payloadBytes;
'''
if text.count(old) != 1:
    raise SystemExit(f'emote decoder span anchor missing or ambiguous: {text.count(old)}')
p.write_text(text.replace(old, new, 1), encoding='utf-8')
print('Town social packet-91 decoder uses array-backed payload')
