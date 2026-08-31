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

## Serialization path

Owned Multiplicity packets are serialized through `MultiplicityPacketSerializer`, which bridges `TerrariaPacket.ToStream(Stream)` to the existing `ArrayBufferWriterStream` adapter. This removes `MemoryStream` staging from the remaining protocol encoders while retaining Multiplicity as the owner of packet re-serialization.

The serializer checks that the Multiplicity model reports a non-negative payload length, that the complete frame stays inside Multiplicity's signed `Int16` frame envelope, and that the actual written byte count matches the model's declared frame length.

The same serializer is now used by sign, tile manipulation, door/object placement, world-item bootstrap/live replication, chest synchronization and persisted town-NPC synchronization paths that previously staged through `MemoryStream`.

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
- segmented fixed-payload decode and pooled segmented sign decode;
- no gameplay dependency on Multiplicity concrete types outside the protocol boundary.
