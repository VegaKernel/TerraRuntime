# Typed boundary протокола 326

[English](../en/protocol-326-typed-boundary.md) · [Сеть и протокол](networking-protocol.md) · [Roadmap](../roadmap.md)

## Область

TerraRuntime ориентирован на Terraria `1.4.5.8`, protocol `326`; Multiplicity `2.7.2` изолирован внутри `TerraRuntime.Protocol.Multiplicity`.

Для packet boundary действуют три правила:

1. wire layout берётся из packet/view types Multiplicity, а не из разбросанных byte offsets в gameplay code;
2. до перехода в authoritative gameplay decoded data превращаются в TerraRuntime-owned records;
3. wire-sensitive behavior проверяется по official TerrariaServer или real-client capture, а не только внутренним encode/decode round trip.

## Typed coverage, закрытый этим проходом

Оставшиеся gameplay packet adapters с ручной индексацией payload переведены на Multiplicity-backed layout:

| Packet | Vanilla роль | TerraRuntime path |
| --- | --- | --- |
| `17` | tile manipulation | `TileView` / `Tile` |
| `19` | door/trapdoor/tall-gate use | `DoorUseView` / `DoorUse` |
| `46` | sign read request | bounded `PacketReader` projection |
| `47` | sign state | bounded `PacketReader` decode / `SignNew` encode |
| `79` | object placement | `PlaceObjectView` / `PlaceObject` |

Fragmented fixed-size payloads собираются в bounded stack storage. Для variable-length fragmented sign payload арендуется buffer из `ArrayPool<byte>` и всегда возвращается после decode. Для sign text сохранена strict UTF-8 validation на protocol boundary TerraRuntime.

## Serialization path

Owned Multiplicity packets сериализуются через `MultiplicityPacketSerializer`. Он соединяет `TerrariaPacket.ToStream(Stream)` с существующим `ArrayBufferWriterStream`, поэтому оставшиеся protocol encoders больше не используют промежуточный `MemoryStream`, а wire re-serialization по-прежнему принадлежит Multiplicity.

Serializer проверяет non-negative payload length, попадание полного frame в signed `Int16` envelope Multiplicity и совпадение фактически записанного числа bytes с declared frame length packet model.

Тот же serializer теперь используется для sign, tile manipulation, door/object placement, world-item bootstrap/live replication, chest synchronization и persisted town-NPC synchronization paths, где раньше был `MemoryStream` staging.

## Vanilla semantics boolean-поля

Official TerrariaServer `1.4.5.8` в `MessageBuffer` читает direction packet `79` через `BinaryReader.ReadBoolean()`. Следовательно:

```text
0      -> false
1..255 -> true
```

Раньше TerraRuntime отклонял значения больше `1`. Это было строже vanilla и не соответствовало official behavior. Такое отклонение убрано. Старое значение enum `InvalidDirectionValue` оставлено только ради source compatibility и decoder его больше не возвращает.

Direction packet `19` при decode использует ту же non-zero semantics: любой ненулевой byte даёт направление `+1`, ноль даёт `-1`. Encoder остаётся canonical и пишет только `0` или `1`.

## Independent wire evidence

Golden vectors в `Protocol326VanillaGoldenWireTests` являются literal bytes, вручную выведенными из локально декомпилированного official TerrariaServer `1.4.5.8`. Проверялись следующие official switch cases:

- `NetMessage.SendData`: packets `17`, `19`, `47`, `79`;
- `MessageBuffer.GetData`: packets `17`, `19`, `46`, `47`, `79`.

Декомпилированный official source остаётся только локальным reference material и в repository не коммитится. В tests коммитятся лишь independently derived wire vectors и behavioral assertions.

## Regression contract

Изменения этого protocol участка обязаны сохранять:

- exact golden bytes для packets `17`, `19`, `47`, `79`;
- decode координат packet `46`;
- vanilla non-zero boolean behavior packet `79`;
- strict UTF-8 rejection для malformed sign text;
- segmented fixed-payload decode и pooled segmented sign decode;
- отсутствие зависимости gameplay от concrete Multiplicity types вне protocol boundary.
