# Семантика Expert и Master loot King Slime

TerraRuntime владеет source-backed gameplay-семантикой difficulty-only loot rules King Slime из TerrariaServer 1.4.5.8. Три разных способа доставки сохраняются отдельно, а не сплющиваются в обычные общие world-item drops.

## Учёт взаимодействия игрока

Boss bag и per-player Master rules Terraria используют `NPC.playerInteraction[playerSlot]`. TerraRuntime проецирует это состояние через `RuntimeNpcPlayerInteractionLedger`:

- после принятия точной NPC generation атака предметом/projectile игрока записывает interaction до результата последующего strike, как в исходном порядке packet 28;
- generation-valid invulnerable target всё равно может записать interaction игрока, даже если само изменение HP отвергнуто;
- stale NPC generations и некорректные damage requests interaction credit не получают;
- NPC-сторона привязана к точному generation-safe `NpcHandle`, поэтому переиспользованный NPC slot не наследует старые взаимодействия;
- исходной идентичностью игрока остаётся Terraria player slot;
- в момент смерти доставка повторно проверяет, какие записанные slots сейчас заняты активными игроками;
- подходящие slots обрабатываются в возрастающем порядке `0..254`, как в исходных циклах.

Environment/server/NPC damage не выдаёт игроку interaction credit.

## Expert boss bag

Закреплённое правило King Slime — `BossBag(3318)`. На сервере оно сводится к `DropLocalPerClientAndResetsNPCMoneyTo0` и использует raw RNG, а не `RollLuck`:

1. `Next(1)` для гарантированного chance rule;
2. `Next(1, 2)` для общего stack;
3. один no-broadcast item materialize-ится у NPC;
4. packet 90 отправляется каждому активному взаимодействовавшему игроку;
5. сервер превращает свою копию в air, но не позволяет переиспользовать этот world-item slot `54000` ticks.

`RuntimeKingSlimeDifficultyLootDeliverySink` теперь реализует эту transport boundary. Предмет materialize-ится тем же source-backed world-item materializer, затем резервируется неопубликованный точный slot, packet 90 кодируется с byte-for-byte payload формата packet 21 и отправляется только указанным playing player slots. `RuntimeWorldItemInstancedLeaseStore` удерживает reservation, поэтому обычный item allocator не может переиспользовать этот slot, пока существует instanced client copy.

Когда lease достигает нуля, `TerrariaWorldItemFrameEncoder.TryEncodeInstancedSlotRelease` формирует пятибайтовый packet 151 с освобождённым item slot. Оставшаяся runtime-интеграция — тикать эти leases в авторитетной item-update phase; wire contract и lease semantics уже конкретны.

## Master relic

`MasterModeCommonDrop(4929)` в Master mode сводится к raw-RNG `CommonDropNotScalingWithLuck`:

1. chance roll `Next(1)`;
2. stack roll `Next(1, 2)`;
3. немедленный обычный world-item materialization в точке смерти King Slime.

Materialization происходит до начала Master pet rule.

## Master pet item

`MasterModeDropOnAllPlayers(4797, 4)` не кладёт предмет прямо в inventory. TerrariaServer 1.4.5.8:

1. один раз выбирает общий stack через `Next(1, 2)`;
2. проходит активные взаимодействовавшие player slots по возрастанию;
3. для каждого slot делает raw `Next(4)`;
4. при успехе немедленно создаёт обычный world item в центре этого игрока до roll следующего игрока.

TerraRuntime сохраняет этот порядок, включая чередование RNG успешного item materialization и `Next(4)` следующего игрока.

## Граница финализации

`RuntimeKingSlimeDifficultyLootFinalizer` принимает только мёртвую generation King Slime в Expert/Master context. Он фиксирует активных взаимодействовавших игроков, выполняет difficulty rules в исходном порядке и despawn-ит точную NPC generation только после успешной доставки. Normal mode остаётся во владении существующей normal-loot transaction.

Rule semantics, packet-90/151 wire representation и leased-slot storage теперь явные. Открытой остаётся интеграция с live packet-28/playerInteraction combat/death ingress и авторитетный lease ticking. Остановка Slime Rain и first-kill Nerdy Slime world effects закрываются отдельным committed death-progression срезом.
