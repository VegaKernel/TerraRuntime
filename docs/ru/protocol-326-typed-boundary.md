# Typed boundary протокола 326

[English](../en/protocol-326-typed-boundary.md) · [Сеть и протокол](networking-protocol.md) · [Roadmap](../roadmap.md)

## Область

TerraRuntime ориентирован на Terraria `1.4.5.8`, protocol `326`; Multiplicity `3.0.0` изолирован внутри `TerraRuntime.Protocol.Multiplicity`.

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

В закреплённой версии Multiplicity `3.0.0` `PacketViewParser` работает со span и не имеет overload для `ReadOnlySequence<byte>`. Поэтому fragmented fixed-size frame нельзя передать Multiplicity view без предварительного получения contiguous data. Для маленьких fixed payload TerraRuntime оставляет этот fallback bounded через stack storage, не создавая временный heap array. Single-segment frames по-прежнему передаются напрямую соответствующему Multiplicity view.

### Player-buff snapshot packet `50`

Packet `50` (`PlayerBuff`) также проходит через typed bounded boundary. Official TerrariaServer `1.4.5.8` записывает один byte игрока, все ненулевые active buff types из `44` buff slots игрока и завершающий `ushort 0`. Duration в wire payload отсутствует. TerraRuntime принимает не более `44` ненулевых version-pinned buff IDs плюс terminator; malformed length, внутренний zero, отсутствующий terminator или buff ID вне диапазона отклоняются как malformed protocol.

На server ingress заявленный клиентом player byte не считается доверенным. Identity заменяется на connection-owned generation `PlayerHandle` до того, как owned snapshot типов баффов попадает в authoritative queue. Committed snapshot остаётся только presentation/synchronization state: его можно удерживать для peer relay, late-join baseline и cross-world transfer, но он не превращается в authoritative combat buff с выдуманной remaining duration. Реально наблюдённый пустой snapshot намеренно отличается от игрока, для которого packet `50` ещё никогда не наблюдался.

Такое разделение соответствует official dedicated-server boundary: hostile projectile `Damage_EVP` пропускается в server mode, а пострадавший client применяет эти PvE status effects локально и позднее сообщает только список active types через packet `50`. Packet `55` является отдельным targeted PvP buff-delivery path и не используется как fallback для восстановления duration packet `50`.

### Targeted PvP buff packet `55`

`TerrariaPlayerPvpBuffCodec1458` фиксирует packet `55` с точным payload `[target player byte][buff ushort][duration int32]`. Decode отвергает wrong ID, неверную payload length, неизвестный buff ID и nonpositive duration. Egress дополнительно требует source-pinned 1.4.5.8 allow-set `Main.pvpBuff` до отправки frame. Authoritative projectile path использует этот codec только после server-resolved admitted `StatusPvP` proc и только для exact playing target generation; это не broadcast snapshot и не grant combat authority входящему client packet `55`.

## Serialization path

В Multiplicity `3.0.0` перенесена exact-size packet-buffer механика, которую TerraRuntime раньше держал у себя. Owned packet models теперь напрямую используют `TerrariaPacket.TrySerialize(...)` / `ToArray()`, а segmented payload decode напрямую вызывает `TerrariaPacket.TryDeserializePayload(..., ReadOnlySequence<byte>, ...)`. Удалены локальные прокладки `MultiplicityPacketSerializer`, `FixedBufferWriteStream` и `MultiplicityPacketDeserializer`.

Upstream v3 path работает fail-closed: serialization публикует exact final array только при точном совпадении фактически записанного и declared length, а `ReadOnlySequence<byte>` decode заимствует single-segment input и использует bounded lease из `ArrayPool<byte>` для multi-segment input. Поэтому TerraRuntime больше не содержит вторую реализацию этих packet-buffer правил.

`DeflateStream` для packet `10` остаётся отдельным случаем, потому что размер compressed output заранее неизвестен. Маленький write-only bridge `IBufferWriter<byte> -> Stream` теперь приватен внутри `WorldSectionPacketEncoder`; после compression готовый span сразу оформляется в exact final Terraria frame. Общим helper адаптера этот bridge больше не является.

Adapter по-прежнему отвечает за protocol/domain projection, validation и wire evidence, но не дублирует generic packet serialization и segmented-buffer coalescing, уже принадлежащие Multiplicity. Тот же аудит 3.0 перевёл encoding packet 28/40/56/60 на owned models Multiplicity `NpcStrike`, `NpcTalk`, `UpdateNPCName`, `UpdateNPCHome` и `DamageNPCAck`; allocation-free fixed-size ingress parsing сохранён там, где materialization owned model добавила бы работу на недоверенном hot path.

## Vanilla semantics boolean-поля

Official TerrariaServer `1.4.5.8` в `MessageBuffer` читает direction packet `79` через `BinaryReader.ReadBoolean()`. Следовательно:

```text
0      -> false
1..255 -> true
```

Раньше TerraRuntime отклонял значения больше `1`. Это было строже vanilla и не соответствовало official behavior. Такое отклонение убрано. Старое значение enum `InvalidDirectionValue` оставлено только ради source compatibility и decoder его больше не возвращает.

Direction packet `19` при decode использует ту же non-zero semantics: любой ненулевой byte даёт направление `+1`, ноль даёт `-1`. Encoder остаётся canonical и пишет только `0` или `1`.

Client text module может передавать команду без аргументов, например `CommandName = "Help"` с пустым `ChatMessage`. Это корректный command traffic: он не должен классифицироваться как malformed chat или отключать клиента. Module с двумя пустыми полями остаётся невалидным.

## Independent wire evidence

Golden vectors в `Protocol326VanillaGoldenWireTests` являются literal bytes, вручную выведенными из локально декомпилированного official TerrariaServer `1.4.5.8`. Проверялись следующие official switch cases:

- `NetMessage.SendData`: packets `17`, `19`, `47`, `50`, `79`;
- `MessageBuffer.GetData`: packets `17`, `19`, `46`, `47`, `50`, `55`, `79`;
- `Projectile.Damage` / `Damage_EVP` / `StatusPlayer` и `Player.AddBuff` для разграничения authority packet `50`/`55`.

Декомпилированный official source остаётся только локальным reference material и в repository не коммитится. В tests коммитятся лишь independently derived wire vectors и behavioral assertions.

## Regression contract

Изменения этого protocol участка обязаны сохранять:

- exact golden bytes для packets `17`, `19`, `47`, `79`;
- decode координат packet `46`;
- vanilla non-zero boolean behavior packet `79`;
- strict UTF-8 rejection для malformed sign text;
- segmented fixed-payload decode и pooled segmented sign/chat/chest decode там, где ownership это допускает;
- argumentless command text modules остаются валидными при непустом `CommandName`;
- exact-size serialization с rejection как under-write, так и over-write до публикации frame;
- allocation guard, не позволяющий общему Multiplicity serializer снова получить второй полный frame buffer;
- packet-10 framing напрямую из завершённого DEFLATE writer в финальный frame array;
- отсутствие зависимости gameplay от concrete Multiplicity types вне protocol boundary.
