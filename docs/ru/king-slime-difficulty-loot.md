# Семантика Expert и Master loot King Slime

TerraRuntime теперь владеет source-backed gameplay-семантикой difficulty-only loot rules King Slime из TerrariaServer 1.4.5.8. Этот срез намеренно сохраняет три разных способа доставки вместо того, чтобы сплющивать их в обычные общие world-item drops.

## Учёт взаимодействия игрока

Boss bag и per-player Master rules Terraria используют `NPC.playerInteraction[playerSlot]`. TerraRuntime проецирует это состояние через `RuntimeNpcPlayerInteractionLedger`:

- после принятия точной NPC generation атака предметом/projectile игрока записывает interaction до результата последующего strike, как в исходном порядке packet 28;
- поэтому generation-valid invulnerable target всё равно может записать interaction игрока, даже если само изменение HP отвергнуто;
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

`IKingSlimeDifficultyLootDeliverySink` представляет это как один логический instanced item, упорядоченный список получателей и явный slot lease на `54000` ticks. Gameplay evaluator materialize-ит bag до последующих Master rules, чтобы RNG `Item.NewItem` оставался вплетён в исходный порядок.

Packet-90 encoder и конкретный leased-slot transport adapter **пока не входят** в этот срез. Контракт оставлен явным, чтобы production-код не мог подменить boss bag глобально видимым packet-21 world item и назвать это паритетом.

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

Этот блок закрывает authoritative gameplay rule semantics и interaction accounting. Открытыми остаются конкретный packet-90/leased-slot adapter и оставшиеся world effects смерти King Slime, включая остановку Slime Rain и first-kill unlock/spawn Nerdy Slime.
