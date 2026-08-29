# Синхронизация, bootstrap и interest management

[English](../en/synchronization.md) · [Документация](README.md) · [Архитектура](architecture.md) · [Performance roadmap](../roadmap/performance-tick-stability.md)

## 1. Область документа

Synchronization является границей между authoritative runtime state и per-client state, который нужно передать по Terraria protocol 326.

Сюда входят initial join/bootstrap state, section delivery, player movement/event fanout, NPC/projectile/world-item/object replication, recipient selection и будущие interest-management suppression/resync semantics.

Synchronization не владеет gameplay mutation. Она наблюдает authoritative state/events и проектирует их клиентам.

## 2. Общий flow

```text
authoritative game state/event
        |
        v
replication registry / projection
        |
        v
recipient decision
        |
        +--> global/vanilla-like routing
        +--> future interest-managed routing
        |
        v
packet encode / reusable frame
        |
        v
bounded connection queue
        |
        v
socket writer
```

Recipient decision не должен мутировать underlying entity только ради удобства synchronization.

## 3. Initial bootstrap

Игрок не может перейти в normal gameplay сразу после TCP handshake. Runtime обязан передать достаточно world/entity state, чтобы официальный клиент безопасно завершил переход.

Bootstrap path включает verified классы traffic: world metadata, required initial tile sections, global post-section state, актуальные runtime entity bootstrap frames и final enter-world handoff.

Точный protocol ordering защищается live official-world probes, а не замораживается здесь как вечный рецепт packet numbers. Если ordering меняется на основании verified behavior 1.4.5.8, executable live probe является source of truth.

## 4. Bootstrap frame budget

Initial bootstrap явно bounded, чтобы valid world не мог заполнить production outbound queue только из-за join одного игрока.

`PlayerBootstrapFrameBudget` выводит structural maximum из fixed pre-enter frames, максимального initial tile-section count, global post-section frames и world-item bootstrap frames.

Live integration probe сейчас использует hard budget **1536 frames**, что намеренно ниже production outbound queue depth **4096 frames**.

Это safety invariant, а не утверждение, что обычный join должен приближаться к этим значениям.

## 5. Sections

World state делится на Terraria sections для initial и ongoing world synchronization.

Section work должен удовлетворять трём независимым требованиям:

1. correctness: клиент получает world region, нужный его текущему состоянию;
2. invalidation: mutated section не может бесконечно оставаться представлен stale encoded data;
3. budget: generation/compression uncached sections не может монополизировать simulation tick.

Поэтому long-term join pipeline использует global subsystem budgets, а не выдаёт полный section-generation budget каждому одновременно подключающемуся игроку.

## 6. Replication registries

TerraRuntime отделяет replication от authoritative storage.

Runtime уже имеет dedicated replication state/registries для нескольких domains, включая player events/movement, NPCs, projectiles, world items, chests, signs и tile manipulation.

Это позволяет runtime stores заниматься authoritative identity/state, а synchronization отслеживать, что нужно project/fanout клиентам.

## 7. Shared frame principle

Если нескольким recipients нужны одинаковые bytes, preferred architecture: encode один immutable frame и share его между recipient queues вместо повторного encode одинакового state для каждого игрока.

Оптимизация допустима только если bytes действительно recipient-independent. Packets с recipient-specific identity, slot remapping, visibility state или другими персональными полями требуют отдельной projection.

## 8. Владение interest management

Interest management принадлежит самому TerraRuntime.

External hosts вроде Vega получают только `IInterestManagementControl` с `IsEnabled` и `SetEnabled(bool)`.

Host может включать/выключать feature, но не управляет spatial layout, enter/leave radius, hysteresis, entity-specific recipient policy, forced resync deadlines, full-state-on-entry behavior или stale visibility recovery.

Эти правила остаются implementation details runtime, чтобы два разных host не создавали несовместимые networking semantics для одной версии TerraRuntime.

## 9. Текущий spatial foundation

`RuntimeInterestRouter` уже владеет section-based player spatial index и visibility tracker, когда известны dimensions мира.

Текущие default player radii:

```text
enter radius = 3 sections
leave radius = 4 sections
```

Больший leave radius даёт hysteresis, чтобы player возле boundary не прыгал visible/invisible при каждом пересечении края section.

Router обновляет membership на tracked player movement и удаляет membership при disconnect/removal.

Invalid, non-finite или иным образом unusable positions не должны оставлять stale spatial membership. Visibility infrastructure обязана fail-open, если location data нельзя считать надёжными.

## 10. Текущий статус suppression

Это критическое различие:

**interest-management state tracking уже существует, но default enabled policy сейчас `PassthroughInterestPolicy`.**

Она возвращает `true` для player observation. Поэтому включение interest management сегодня устанавливает ownership/control и ведёт spatial/visibility state, но **ещё не suppress'ит player movement updates**.

Это сделано специально. Packet suppression остаётся выключенным, пока runtime полностью не проверит enter transitions, leave transitions, full state on entry, out-of-range semantics, teleport/respawn handling, slot reuse и bounded forced resynchronization.

Проект предпочитает временно отправить лишнее state, чем навсегда скрыть от клиента обязательное состояние.

## 11. Fail-open behavior

Interest management является performance optimization и не должен становиться correctness dependency.

При disabled feature `ShouldRelayPlayerMovement(...)` возвращает true. При недостаточном state или неполной доказанности feature routing должен оставаться vanilla-like/broad.

Synchronization optimization, способная оставить remote player/NPC/projectile замороженным навсегда, неприемлема даже при красивых bandwidth numbers.

## 12. Enter/leave semantics

Target visibility model stateful:

```text
not visible -> visible
    отправить complete state, нужный для начала observation

visible -> still visible
    отправлять deltas/normal replication

visible -> not visible
    применить verified out-of-range/despawn semantics

not visible -> still not visible
    suppress ordinary deltas
```

Hysteresis и forced resync защищают от boundary flicker и long-lived stale state.

## 13. Teleports, respawn и slot reuse

Spatial visibility должна немедленно реагировать на discontinuous identity/location changes: teleport через много sections, respawn в новой точке, disconnect/slot reuse, переход invalid position в valid, создание/despawn server-controlled player.

Generation-safe entity identity не даёт stale visibility state примениться к новой entity, переиспользовавшей старый numeric slot.

## 14. Player movement relay

Movement является одним из первых high-frequency кандидатов для interest-managed routing, потому что unconditional fanout при росте игроков становится примерно O(players²).

Текущее поведение `RuntimeInterestRouter.ShouldRelayPlayerMovement`:

```text
interest disabled -> relay
interest enabled + current passthrough policy -> relay
```

Future suppression надо вводить только вместе с transition/full-resync tests, а не заменять predicate на сырой distance comparison.

## 15. Visibility NPC/projectile/item

Та же общая архитектура подходит другим dynamic entities, но каждому классу нужны собственные semantics.

Для каждой family надо ответить: какое state нужно при first observation, что значит verified out-of-range/despawn для официального клиента, как выражается death/despawn, может ли entity вернуться в range без slot reuse, какой forced resync interval нужен и какие updates обязаны остаться global.

Нельзя слепо применять player movement policy к NPC или projectiles.

## 16. Dirty-state replication

Target runtime не должен сканировать каждую entity для каждого клиента каждый tick.

Preferred direction:

```text
authoritative mutation
   -> dirty/revision/event state
   -> один synchronization pass
   -> recipient filtering
   -> encode/fanout
```

Runtime уже имеет revision/replication registries как foundation этого подхода.

## 17. Bootstrap и steady state

Bootstrap и normal gameplay имеют очень разную форму traffic.

Bootstrap bursty, section-heavy, ordering-sensitive и требует global work budgets плюс queue headroom. Steady state в основном incremental, latency-sensitive и должен сильнее опираться на dirty/revision tracking и recipient selection.

Нельзя выбирать один queue/budget assumption, измерив только одну из этих фаз.

## 18. Slow clients

Synchronization заканчивается bounded connection queue. Клиент, который не способен принимать outbound data, не может создавать неограниченный retained frame backlog.

Поэтому slow-client policy закрывает перегруженное connection, а не блокирует authoritative simulation.

Interest management в будущем снизит bandwidth/queue pressure, но queue bounds остаются обязательными даже при включённой filtering.

## 19. Observability

Полезная synchronization telemetry включает или должна включать per-connection outbound depth, slow-client disconnects, bootstrap frame count, deferred section work, spatial-index membership changes, invalid-position removals, visibility transitions, suppressed/relayed counts, forced resync counts и oldest pending synchronization work.

Telemetry должна быть bounded и не должна форматировать строки на каждый high-frequency movement update.

## 20. Evidence

Relevant evidence: spatial-index tests, visibility/hysteresis tests, interest-control tests, bootstrap frame-budget tests, entity bootstrap frame tests, replication registry tests, slow-client/process tests и live `Vanilla World Load` join/movement probes.

Когда actual suppression будет включён, live tests обязаны доказать сразу оба свойства: updates действительно отсутствуют while hidden и complete correct state возвращается при восстановлении visibility.

## 21. Текущие ограничения

Пока не завершены:

- actual player movement suppression под default policy;
- полное enter/leave outbound wiring;
- full-state-on-entry для всех entity types;
- forced resync policy;
- generalized NPC/projectile/item interest routing;
- final radii/queue sizing на основе measurement;
- полное section-cache invalidation/per-client delivery accounting.

Текущий spatial tracker является foundation, а не заявлением о реальном bandwidth reduction.

## 22. Checklist изменения synchronization

Synchronization change не завершён, пока по необходимости authoritative state остаётся отделённым от replication state, join work bounded globally, queue growth bounded, enter/leave/full-resync behavior протестирован до включения suppression, invalid/unknown position fail-open, slot reuse не наследует stale visibility, protocol-sensitive transitions проверены с official client и эта страница обновлена вместе с `docs/en/synchronization.md`.
