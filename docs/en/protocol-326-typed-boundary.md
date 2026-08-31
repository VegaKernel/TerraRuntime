# Protocol 326 typed packet boundary

[Русский](../ru/protocol-326-typed-boundary.md) · [Networking and protocol](networking-protocol.md) · [Roadmap](../roadmap.md)

## Scope

TerraRuntime targets Terraria `1.4.5.8`, protocol `326`, with Multiplicity `2.7.2` isolated behind `TerraRuntime.Protocol.Multiplicity`.

The packet boundary follows three rules:

1. use Multiplicity packet/view types for wire layout instead of gameplay-side byte offsets;
2. keep decoded values in TerraRuntime-owned records before they cross into authoritative gameplay;
3. verify wire-sensitive behavior against official TerrariaServer behavior or captured traffic, not only against an encode/decode round trip.

## Typed coverage completed in this pass

The remaining manually indexed gameplay packet adapters were migrated to Multiplicity-backed layouts:

| Packet | Vanilla role | TerraRuntime path |
| --- | --- | --- |
| `17` | tile manipulation | `TileView` / `Tile` |
| `19` | door/trapdoor/tall-gate use | `DoorUseView` / `DoorUse` |
| `46` | sign read request | bounded `PacketReader` projection |
| `47` | sign state | bounded `PacketReader` decode / `SignNew` encode |
| `79` | object placement | `PlaceObjectView` / `PlaceObject` |

Fixed-size fragmented payloads use bounded stack storage. Variable-length fragmented sign payloads rent a buffer from `ArrayPool<byte>` and always return it after decoding. Sign text keeps strict UTF-8 validation at the TerraRuntime protocol boundary.

Multiplicity `PacketViewParser` is span-based in the pinned `2.7.2` baseline and has no `ReadOnlySequence<byte>` overload. Therefore a fragmented fixed-size frame cannot be inspected by a Multiplicity view without first becoming contiguous. TerraRuntime keeps that fallback bounded on the stack for small fixed payloads instead of allocating a temporary array. Single-segment frames continue to go directly to the corresponding Multiplicity view.

## Serialization path

Owned Multiplicity packets are serialized through `MultiplicityPacketSerializer`. `TerrariaPacket.GetLength()` determines the exact final frame size, and `FixedBufferWriteStream` lets Multiplicity's `TerrariaPacket.ToStream(Stream)` write directly into that final `byte[]`. The previous `ArrayBufferWriter<byte>` staging allocation and `WrittenSpan.ToArray()` copy are no longer present on the common owned-packet encode path.

The exact-size path is fail-closed. If a Multiplicity model writes fewer bytes than it declared, the candidate array is discarded so an uninitialized tail can never be published. If it writes past the declared length, `FixedBufferWriteStream` records the logical overflow without allocating a larger buffer, and the candidate is discarded. A successful frame is published only when the actual byte count exactly matches the declared frame length.

`ArrayBufferWriterStream` remains intentionally in the packet-10 compression path because DEFLATE output size is not known before compression. Once `DeflateStream` completes, however, `WorldSectionPacketEncoder` now frames `compressedWriter.WrittenSpan` directly into one exact final array through the span overload of `TerrariaFrameEncoder`. It no longer materializes a separate compressed array and then a second `ArrayBufferWriter`-backed framed copy.

The same exact-size Multiplicity serializer is used by sign, tile manipulation, door/object placement, player lifecycle/appearance/equipment/vitals/movement, NPC/projectile replication, chat, world-item bootstrap/live replication, chest synchronization and persisted town-NPC synchronization paths.

Multiplicity itself still owns `ToStream(Stream)` and may use its own `BinaryWriter` implementation internally. TerraRuntime does not duplicate those packet serializers merely to hide that implementation detail; the runtime-side contract is that no second complete frame buffer is staged around Multiplicity serialization.

## Vanilla boolean semantics

Official TerrariaServer `1.4.5.8` `MessageBuffer` reads packet `79` direction with `BinaryReader.ReadBoolean()`. Therefore:

```text
0      -> false
1..255 -> true
```

TerraRuntime previously rejected values greater than `1`. That stricter behavior was not vanilla-compatible and has been removed. The old `InvalidDirectionValue` enum value is retained only for source compatibility and is no longer emitted by the decoder.

Packet `19` direction follows the same non-zero rule on decode: any non-zero byte maps to direction `+1`, while zero maps to `-1`. Encoding remains canonical and writes only `0` or `1`.

## Independent wire evidence

The golden vectors in `Protocol326VanillaGoldenWireTests` are literal bytes transcribed from the locally decompiled official TerrariaServer `1.4.5.8` implementation. The relevant official switch cases are:

- `NetMessage.SendData`: packets `17`, `19`, `47`, `79`;
- `MessageBuffer.GetData`: packets `17`, `19`, `46`, `47`, `79`.

The decompiled official source remains local reference material and is not committed. The tests commit only independently derived wire vectors and behavioral assertions.

## Regression contract

Protocol changes in this area must preserve all of the following:

- exact golden bytes for packets `17`, `19`, `47` and `79`;
- packet `46` coordinate decode;
- vanilla non-zero boolean behavior for packet `79`;
- strict UTF-8 rejection for malformed sign text;
- segmented fixed-payload decode and pooled segmented sign/chat/chest decode where ownership permits it;
- exact-size serialization that rejects both under-write and over-write before publishing a frame;
- an allocation guard preventing the common Multiplicity serializer from regaining a second complete frame buffer;
- packet-10 framing directly from the completed DEFLATE writer into the final frame array;
- no gameplay dependency on Multiplicity concrete types outside the protocol boundary.
