# Gameplay runtime и vanilla parity

[English](../en/gameplay.md) · [Документация](README.md) · [Архитектура](architecture.md) · [Gameplay decomposition roadmap](../roadmap/gameplay-decomposition-and-catalogs.md)

## 1. Назначение

TerraRuntime реализует gameplay Terraria как authoritative runtime systems, а не как набор побочных эффектов внутри packet handlers.

Цель: **наблюдаемая parity TerrariaServer 1.4.5.8**, а не повторение структуры исходников. Внутренняя реализация может отличаться полностью, если player-visible results, ordering и compatibility остаются корректными.

Этот документ специально различает готовый фундамент и ширину vanilla coverage. Наличие runtime store или AI dispatcher не означает, что реализованы все Terraria entities, способные использовать эту подсистему.

## 2. Базовый gameplay flow

```text
client/network input
   -> bounded protocol decode
   -> semantic ingress/command
   -> authoritative game loop
   -> validation и state transition
   -> runtime store/event
   -> replication projection
   -> recipient selection
   -> protocol encode
```

Gameplay владеет legality и authoritative outcomes. Networking владеет wire transport. Replication отвечает за преобразование authoritative state обратно к клиентам.

## 3. Authoritative ownership

Mutable gameplay state принадлежит game-loop thread.

Сюда входят runtime player state, world mutations, chests, world items, NPCs, projectiles и другое simulation state по мере перехода подсистем в authoritative model.

External threads и trusted host modules используют snapshots или command/operations surfaces. Mutable stores им не выдаются.

## 4. Identity и content type

TerraRuntime разделяет vanilla content identity и identity конкретной live runtime entity.

Примеры:

```text
NpcTypeId(1)            vanilla NPC type
NpcHandle(slot, gen)    конкретный live runtime NPC

ProjectileTypeId(1)     vanilla projectile type
projectile handle/slot  конкретный live runtime projectile
```

Generation/revision-aware handles не дают stale reference изменить другую entity после reuse slot.

Raw protocol IDs допустимы на wire boundary. Gameplay должен как можно раньше переходить к validated named domain IDs.

## 5. Version-pinned vanilla facts

Gameplay facts runtime привязаны к TerrariaServer 1.4.5.8.

Примеры текущих typed/named facts:

- NPC IDs и AI-style IDs;
- projectile IDs и AI-style IDs;
- verified widths/heights/defaults, реально используемые simulation;
- tile/item facts для реализованных mutation/drop paths;
- protocol-independent runtime handles и snapshots.

Catalog содержит только факты, которые нужны текущему behavior. TerraRuntime не копирует весь decompiled `SetDefaults` только ради красивого ощущения полноты.

## 6. Текущий статус parity

Таблица намеренно консервативная.

| Область | Состояние | Что это значит |
|---|---|---|
| Handshake / join / player slot | substantial | есть live official-world join probes; поддерживаются не все gameplay packets |
| Player spawn/state/movement | partial-to-substantial | есть authoritative ingress/state, normalization и replication foundation; полного anti-cheat movement model нет |
| Inventory/equipment | partial | есть typed commit/request paths и packet handling, но полная server-authoritative item-use/equipment semantics не завершена |
| World items | substantial foundation | есть runtime-owned store, allocation/reservation/update/replication paths и tests |
| Tiles | partial | есть verified mutation slices, dirt/stone behavior и replication; полной placement/framing/wiring/growth breadth нет |
| Chests | substantial slice | runtime chest state, live open/content path, replication и persistence реально проверяются; полная chest/item authority ещё растёт |
| Signs | partial | runtime replication/state paths есть; edit/validation parity уже vanilla пока уже |
| Projectiles | partial | lifecycle/store/ownership/AI-style physics/collision/replication есть для verified type families; полного catalog/combat/side effects нет |
| NPC lifecycle | partial | runtime store, generation-safe identity, definitions, targeting/check-active/spawn/motion primitives есть |
| NPC AI breadth | early partial | есть selected verified NPCs и AI families, но не весь vanilla roster |
| Combat/damage | early/partial | supporting structures существуют, полный vanilla PvE/PvP pipeline не завершён |
| Bosses | largely incomplete | broad boss parity предполагать нельзя |
| Loot/drops | early/partial | selected world/tile drop paths есть; полного NPC loot и RNG behavior нет |
| Housing/town NPCs | incomplete | target architecture есть, broad behavior ещё нет |
| Events/invasions/progression | incomplete | production parity пока нет |
| Wiring/liquids/growth | foundation/partial | world/liquid primitives есть; полной vanilla simulation нет |
| Vanilla world generation | incomplete | extensible worldgen framework есть; built-in flat generator не является vanilla WorldGen |

Если эта таблица расходится с executable evidence или более новым roadmap item, обновляется документ, а не сохраняется протухший процент ради красоты.

## 7. Players

Player networking переводится в runtime-owned commit requests/events до mutation.

Архитектура уже содержит dedicated ingress/commit shapes для таких областей, как:

- spawn;
- movement;
- vitals/state slices;
- appearance/equipment slices;
- event fanout/replication.

Movement имеет vanilla-oriented normalization и server-known state, но roadmap всё ещё включает более богатую history/tolerance модель exceptional movement: teleports, mounts, respawn transitions.

Runtime не должен reject'ить legal vanilla movement только потому, что будущая authoritative model придумана строже. Anti-cheat policy не имеет права становиться guessed gameplay.

## 8. Server-controlled players

Trusted hosts могут создавать connection-free runtime-owned players через `IServerPlayerOperations`.

Такие actors резервируют обычные Terraria player slots из generation-safe pool и принимают semantic intent, например horizontal movement. Host не может каждый tick напрямую выставлять final velocity/position, обходя runtime physics/ownership.

Эта boundary предназначена для server-controlled actors и integration scenarios, а не для выдачи mutable player internals плагинам.

## 9. Inventory и equipment

Inventory/equipment processing постепенно выносится из loose packet fields и raw slot numbers.

Target concepts:

- named inventory layout regions;
- validated item type/stack/prefix state;
- explicit equipment/loadout semantics;
- semantic item use вместо packet-handler side effects;
- server-known ownership для world items и transitions.

Текущую packet/commit infrastructure нельзя считать полной authoritative recipe/use/ammo/accessory logic.

## 10. World items

`RuntimeWorldItemStore` является authoritative runtime entity store, а не transparent client relay.

Реализованный foundation покрывает протестированные области вроде:

- slot allocation/reservation;
- updates и partial updates;
- runtime ingress/commands;
- replication registry integration;
- selected tile-drop integration.

World item identity отделена от item content type. Будущая pickup/stack/ownership validation строится на server-owned identity, а не на доверии к arbitrary client slot metadata.

## 11. Tiles и world mutation

World edits идут через semantic/runtime mutation paths, а не напрямую меняют tile из decoder.

Runtime уже имеет verified slices для tile kill/update/replication и world collision/query behavior. Selected dirt/stone cases закреплены official-source/reference workflows.

В broad vanilla scale пока не завершены:

- все placement rules;
- frame-important и multi-tile object behavior;
- все slope/platform interactions;
- wiring/actuation;
- growth/spread families;
- полный набор tool/item requirements и drops.

Tile mutation не завершена только потому, что итоговый tile ID выглядит правильным. Neighbor framing, object validity, drops, liquid interaction, persistence и network replication могут быть наблюдаемыми частями одного vanilla action.

## 12. Chests

Chest path является одним из более зрелых object slices.

Текущая архитектура включает runtime chest state, interaction/replication paths и authoritative persistence support. Live workflows проверяют open/content behavior на official-world data.

Важные invariants:

- chest identity/coordinates проверяются до mutation;
- malformed chest traffic изолируется, а не валит process;
- chest state захватывается authoritative owner для save;
- replication отделён от storage.

Полную server-authoritative inventory conservation/anti-dupe logic надо вводить только когда item ownership model достаточно сильна, чтобы не плодить false rejects легального vanilla traffic.

## 13. NPC lifecycle

NPC используют runtime-owned store и generation-safe handles.

Текущий foundation включает:

- allocation/lifecycle state;
- version-pinned definition lookup;
- target selection primitives;
- gravity/world motion;
- spawn cadence primitives;
- check-active/despawn behavior slices;
- replication projection;
- trusted-host actor control через semantic intent.

Текущий verified definition catalog содержит **Blue Slime**, **Demon Eye** и **Zombie**. Это явный coverage slice, а не намёк, будто defaults остальных NPC можно угадать по соседям.

## 14. NPC AI

AI декомпозируется по behavior/family вместо гигантского `switch(type)` внутри packet handler.

Selected implementation уже включает AI-specific/family primitives для verified NPC slice, в том числе slime/fighter/flying-style работу, используемую Blue Slime, Zombie и Demon Eye paths.

Правила расширения AI:

1. constants и state ordering проверяются по TerrariaServer 1.4.5.8;
2. reusable behavior выделяется только если entities действительно разделяют это правило;
3. RNG ordering сохраняется там, где он наблюдаем;
4. state transitions получают deterministic tests;
5. official server/client evidence используется, когда local tests могут разделять одну и ту же ошибочную гипотезу.

Boss orchestration не надо насильно запихивать в abstractions, придуманные для трёх простых early-game NPC.

## 15. Trusted-host NPC actors

`INpcActorOperations` позволяет trusted host получить lease на существующий runtime NPC и передавать semantic `NpcActorIntent`.

Runtime всё равно владеет:

- final movement;
- gravity;
- collision;
- lifetime и entity identity;
- authoritative application order.

Controller IDs и explicit release позволяют безопасно завершать module/plugin lifecycle. Host не может таскать direct mutable NPC objects через reload boundaries.

## 16. Projectiles

Projectile support уже вышел из relay-only design.

Текущая архитектура включает:

- runtime projectile store;
- ownership/provenance facts;
- lifecycle handling;
- definition catalog;
- behavior state executor/stepper;
- world physics/collision;
- tile-cut integration для supported cases;
- packet projection и replication.

Текущие verified AI-style identities:

```text
Arrow     aiStyle 1
Thrown    aiStyle 2
Boomerang aiStyle 3
```

Definition catalog содержит растущий verified набор этих families, включая разные arrows, bullets/lasers, bones, shuriken/throwing-knife-style projectiles и boomerang support.

Но это **не** полная projectile parity Terraria. Unsupported irreversible side effects, child spawning, immunity, penetration, specialized AI, damage и kill effects должны оставаться explicit boundaries, а не guessed behavior.

## 17. Combat

Combat является отдельной semantic subsystem, а не просто полями projectile/NPC packet.

Target model включает явные concepts:

- damage source/provenance;
- attacker и target;
- base/final damage;
- defense interaction;
- knockback;
- critical hits;
- immunity/cooldowns;
- death reason/result;
- PvP/environment/NPC/projectile categories.

Authoritative должны становиться только verified portions. Пока complete conservation/damage rules отсутствуют, server не должен придумывать строгие rejection rules, ломающие legal vanilla behavior.

## 18. Drops и loot

Selected tile/world-item drop paths реализованы и протестированы, но полный vanilla loot намного шире.

NPC loot parity потребует rule/data structures, сохраняющих:

- conditions;
- probabilities;
- stack ranges;
- progression/event dependencies;
- RNG ordering.

Declarative loot table полезна только если она воспроизводит verified sequence. Изменение RNG call order при тех же процентах всё равно может изменить наблюдаемое vanilla behavior.

## 19. Buffs, prefixes и item metadata

Архитектура движется к typed IDs и version-pinned metadata вместо scattered raw integers.

Эти системы остаются большой future work. Новая authoritative validation не должна принимать arbitrary unvalidated bytes как domain state, но также не должна reject'ить values, чья vanilla legality ещё не проверена.

## 20. Wiring, liquids и growth

Liquids уже имеют explicit tile state и runtime work queue, который может сохраняться в warm snapshots.

Этот foundation не является полной vanilla liquid simulation. Wiring/actuation и growth/spread также остаются incomplete.

Эти подсистемы order-sensitive и способны затронуть большие части world, поэтому их реализация должна сочетать:

- exact behavioral verification;
- global bounded per-tick work;
- deterministic owner-thread commits;
- dirty/replication tracking;
- save compatibility.

## 21. Progression, events, town NPCs и bosses

Это сейчас крупнейшие parity gaps.

Нельзя выводить их поддержку из того, что world header fields читаются или generic NPC infrastructure существует. World может загрузить progression metadata, пока runtime ещё не воспроизводит все transitions и gameplay consequences этой metadata.

По мере перехода этих систем в authoritative state каждой потребуется explicit persistence, synchronization и official behavior evidence.

## 22. World generation

TerraRuntime имеет world-generation **framework** с provider registration, planning, ordered passes, isolated workspace execution и final validation.

Built-in generator сейчас является deterministic flat dirt/stone baseline. Он прямо не является approximation vanilla Terraria WorldGen.

Поэтому vanilla worldgen остаётся incomplete, хотя архитектура custom/pluggable world generation уже развита существенно.

## 23. Replication

Gameplay mutation и network replication являются разными responsibilities.

Runtime replication registries существуют для нескольких entity/object classes, включая player-related events, NPCs, projectiles, world items, chests, signs и tile manipulation.

Разделение важно, потому что:

- одна mutation может иметь много recipients;
- recipients могут меняться из-за interest management;
- один authoritative state можно encode'ить один раз и share'ить, если bytes идентичны;
- persistence не должен зависеть от того, что последний раз отправили клиенту.

## 24. Философия validation

Server становится более authoritative только там, где правило доказано.

Правильный порядок:

```text
verify vanilla rule
   -> represent semantic state
   -> implement authoritative transition
   -> add regression evidence
   -> reject impossible client action
```

Плохой порядок:

```text
предположить, что legitimate client "должен" делать
   -> reject всё остальное
   -> потом обнаружить, что vanilla это разрешает
```

False-positive anti-cheat является gameplay bug.

## 25. Evidence hierarchy

Gameplay changes используют project-wide source hierarchy:

1. locally decompiled TerrariaServer 1.4.5.8 для current vanilla behavior/constants;
2. Multiplicity для protocol 326 wire representation;
3. terrustia как independent implementation cross-check;
4. TShock/OTAPI только для history/exploit lessons.

Real official-client/server traffic, generated worlds и differential probes обязательны там, где local unit test не способен независимо доказать behavior.

## 26. Test strategy

В зависимости от subsystem evidence должен включать:

- deterministic state-transition unit tests;
- definition/catalog tests;
- runtime store lifecycle/slot-reuse tests;
- collision/world-query tests;
- replication tests;
- malformed/illegal input tests;
- official-source contract workflows;
- live official-world/client probes;
- persistence/restart tests для state, переживающего save.

Green build сам по себе не является parity evidence.

## 27. Добавление нового NPC/projectile behavior

Перед новым behavior slice:

1. определить точный Terraria 1.4.5.8 type и AI/default facts;
2. решить, относится ли он к существующей verified behavior family или требует отдельной strategy;
3. добавить только metadata, используемую текущим behavior;
4. реализовать state transitions без protocol-library dependencies;
5. независимо проверить world collision/physics assumptions;
6. добавить lifecycle/replication handling;
7. добавить deterministic regression tests;
8. явно описать unsupported side effects;
9. обновить RU и EN parity documentation в том же change.

## 28. Самые большие текущие gameplay gaps

Главный остаток теперь не basic store architecture, а ширина vanilla rules:

- множество NPC AI families;
- bosses;
- полный combat/damage semantics;
- полная item use/inventory authority;
- loot;
- housing/town NPC behavior;
- invasions/events;
- wiring/liquids/growth;
- world progression;
- vanilla world generation.

Это должно оставаться явной работой, а не прятаться под глобальной надписью «gameplay implemented».
