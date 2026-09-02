# Protocol 326 typed packet boundary

[Русский](../ru/protocol-326-typed-boundary.md) · [Networking and protocol](networking-protocol.md) · [Roadmap](../roadmap.md)

## Scope

TerraRuntime targets Terraria `1.4.5.8`, protocol `326`, with Multiplicity `3.0.0` isolated behind `TerraRuntime.Protocol.Multiplicity`.

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

Multiplicity `PacketViewParser` is span-based in the pinned `3.0.0` baseline and has no `ReadOnlySequence<byte>` overload. Therefore a fragmented fixed-size frame cannot be inspected by a Multiplicity view without first becoming contiguous. TerraRuntime keeps that fallback bounded on the stack for small fixed payloads instead of allocating a temporary array. Single-segment frames continue to go directly to the corresponding Multiplicity view.

## Serialization path

Multiplicity `3.0.0` owns the exact-size packet-buffer mechanics that TerraRuntime previously carried locally. Owned packet models now call `TerrariaPacket.TrySerialize(...)` / `ToArray()` directly; segmented payload decode calls `TerrariaPacket.TryDeserializePayload(..., ReadOnlySequence<byte>, ...)` directly. The removed TerraRuntime shims were `MultiplicityPacketSerializer`, `FixedBufferWriteStream` and `MultiplicityPacketDeserializer`.

The upstream v3 path is fail-closed: serialization publishes an exact final array only when the model writes exactly its declared length, and the `ReadOnlySequence<byte>` decode path borrows single-segment input while using a bounded `ArrayPool<byte>` lease for multi-segment input. TerraRuntime therefore no longer owns a second implementation of those packet-buffer rules.

`DeflateStream` is a different case because packet `10` compressed output size is unknown before compression. `WorldSectionPacketEncoder` keeps a small write-only `IBufferWriter<byte>` stream bridge private to that encoder, then frames the completed compressed span directly into the exact final Terraria frame. It is not a general Multiplicity adapter anymore.

The adapter still owns protocol/domain projection, validation and wire evidence. It does not reimplement generic packet serialization or segmented-buffer coalescing already supplied by Multiplicity. The same 3.0 audit moved packet-28/40/56/60 encoding onto Multiplicity-owned `NpcStrike`, `NpcTalk`, `UpdateNPCName` and `UpdateNPCHome` models, plus `DamageNPCAck`, while retaining allocation-free fixed-size ingress parsing where replacing it with an owned model would add work on an untrusted hot path.

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
- an integration allocation guard on Multiplicity v3 exact-size serialization so a second complete frame buffer cannot return unnoticed;
- packet-10 framing directly from the completed DEFLATE writer into the final frame array;
- no gameplay dependency on Multiplicity concrete types outside the protocol boundary.
