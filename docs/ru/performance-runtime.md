# Performance, tick scheduling и work budgets

[English](../en/performance-runtime.md) · [Документация](README.md) · [Архитектура](architecture.md) · [Performance roadmap](../roadmap/performance-tick-stability.md)

## 1. Performance model

TerraRuntime считает performance correctness constraint вокруг bounded work, а не разрешением менять observable vanilla behavior ради красивого benchmark.

Baseline simulation model: один authoritative owner работает по fixed-rate tick schedule, а networking и bounded background work идут независимо.

Приоритет:

```text
correctness и vanilla-visible behavior
        -> bounded work / failure isolation
        -> measurement
        -> optimization
```

Optimization без measurement — hypothesis, а не завершённое performance change.

## 2. Default tick rate

`GameLoopOptions.DefaultTicksPerSecond` = **60**.

При 60 Hz nominal interval примерно **16.67 ms**.

Generic loop технически допускает configured positive tick rate до 1000 Hz, но Terraria runtime baseline остаётся 60 Hz. Повышение generic loop option само по себе не делает vanilla Terraria gameplay семантически корректным на большей simulation frequency.

Перед поддержкой alternate tick-rate mode нужно проверить game constants, timers, network cadence и vanilla reference behavior.

## 3. Dedicated authoritative thread

`AuthoritativeGameLoop<TState,TCommand>` работает на dedicated non-background thread:

```text
TerraRuntime Game Loop
```

Thread владеет mutable simulation state. Producers отправляют commands и не получают reference на state.

Эта ownership model убирает locks как обычный механизм gameplay mutation и делает per-tick work измеримой controlled sequence.

## 4. Tick phases generic loop

Текущий generic authoritative loop измеряет три top-level phases:

```text
Ingress  -> bounded staging commands из channel
Commands -> bounded/fair command apply
Update   -> authoritative state update
```

Runtime snapshot публикует last/worst timings и slowest phase.

Higher-level Terraria update code может дальше дробить work по subsystem phases. Architecture roadmap ожидает отдельную observability для liquids, items, NPC AI, projectiles, combat, spawning, housing, progression и synchronization по мере роста implementation.

## 5. Global command capacity

Default global command mailbox capacity:

```text
CommandCapacity = 8192
```

Channel bounded. External producers не могут создать unbounded retained command list, просто отправляя быстрее game loop.

`TryPost` может reject при исчерпании global или per-source pending capacity.

Bounded mailbox — invariant; exact production sizing продолжает подтверждаться realistic load.

## 6. Bounded ingress

Default command staging limit:

```text
MaxCommandIngressPerTick = 2048
```

Game loop не drains весь producer channel во внутренние source queues каждый tick. Поэтому ingress phase bounded даже при полном global mailbox.

Staging и command apply имеют разные budgets, потому что перестройка большого backlog тоже стоит времени.

## 7. Global apply budget

Default authoritative command execution limit:

```text
MaxCommandsPerTick = 1024
```

После exhaustion operation budget оставшиеся commands откладываются на следующие ticks.

Runtime показывает deferred work и command-budget exhaustion вместо скрытого unbounded loop.

## 8. Per-source fairness

Default per-source apply quota:

```text
MaxCommandsPerSourcePerTick = 128
```

Non-system source после достижения quota throttled до следующего прохода rotation.

Это не даёт одному busy connection/source съесть весь global command budget, пока остальные ждут.

System-owned work освобождён от per-source quota, но не должен превращаться в unbounded bypass path.

## 9. Per-source pending limit

Default retained pending commands одного external source:

```text
MaxPendingCommandsPerSource = 1024
```

Этот limit отделён от global `CommandCapacity`.

Он не даёт одному connection/source занять весь global mailbox ещё до per-tick fairness.

## 10. Optional command CPU budget

`MaxCommandCpuMillisecondsPerTick` может задавать optional CPU-time ceiling для command-application phase.

Hard operation-count budget остаётся active даже если platform не предоставляет thread CPU clock.

При доступном CPU timing и достижении configured budget command processing останавливается на этом tick, remaining work deferred.

Generic option не имеет default CPU budget value. Production value выбирается по measurement, а не на глаз.

## 11. CPU time и wall time

Game loop записывает оба значения:

```text
wall duration
thread CPU duration when available
```

Они отвечают на разные вопросы.

- High wall + high CPU обычно означает реальную работу authoritative thread.
- High wall + low CPU может указывать на scheduler/OS contention или blocking вне pure computation.

Нельзя диагностировать slow tick только по wall time, если CPU data доступны.

## 12. Missed tick policy

TerraRuntime **не** выполняет burst catch-up ticks после missed deadline.

Если текущий tick закончился позже следующего deadline, loop считает missed deadlines и переносит schedule anchor к current time.

```text
late tick
   -> count missed deadlines
   -> skip burst catch-up
   -> continue from now
```

Это защищает от spiral, где один expensive tick вызывает несколько immediate catch-up ticks, которые увеличивают backlog и latency.

## 13. Pending age

Loop отслеживает age oldest pending command.

Queue depth сам по себе может скрывать starvation. Stable queue count при растущем oldest age означает, что work ждёт всё дольше, даже если количество queued commands не растёт.

Поэтому scheduler diagnosis смотрит как минимум на:

```text
pending/deferred count
oldest pending age
```

## 14. Command rejection

`TryPost` резервирует per-source и global pending capacity до write в bounded channel.

Если reservation/channel write не проходит, command rejected и rejection telemetry увеличивается.

Producer получает explicit failure result и не должен считать, что любой submitted work гарантированно попадёт в authoritative state.

## 15. Source scheduling

Staged commands группируются по `GameCommandSourceId` и вращаются через ready source queues.

Это даёт deterministic bounded fairness без одного OS thread на каждого player/source.

Scheduling structure — implementation detail. Semantic guarantees: bounded global work, per-source fairness и required ordering.

## 16. Networking остаётся asynchronous

Socket read/write work не выполняется game-loop thread.

Networking может receive/encode asynchronously, но authoritative mutation проходит bounded command boundary.

Game loop также не ждёт slow client's TCP receive window. Outbound work заканчивается bounded per-connection queue и отдельным writer.

## 17. Background workers

CPU-heavy или blocking work может выполняться вне game loop при ясном ownership.

Workers получают immutable snapshots/isolated buffers и возвращают explicit completion data через controlled commit path.

Нельзя заменять дизайн ownership/capacity unbounded `Task.Run` fan-out.

## 18. Disk I/O и saving

Persistence организован так, чтобы disk serialization/write не выполнялся в authoritative hot path.

Game loop делает bounded snapshot/shadow synchronization, затем передаёт detached data background save coordinator.

Current tile save shadow синхронизирует bounded section count на tick вместо копирования complete tile array одной паузой.

## 19. Join/bootstrap performance

Join — burst workload и не должен budget'иться как ordinary steady-state movement.

Initial world sections/entity state могут породить много frames и дорогой serialization/compression work.

Join work использует **global subsystem budgets**. Полный section-generation budget на каждого joining player умножил бы worst-case tick cost на число concurrent joins.

Bootstrap frame count также hard-bounded ниже production outbound queue capacity live integration checks.

## 20. Synchronization scaling

Unconditional player-to-player movement broadcast при росте игроков стремится к O(players²).

TerraRuntime уже имеет spatial/visibility tracking foundation для runtime-owned interest management, но actual default movement suppression остаётся passthrough до proof enter/leave/full-resync semantics.

Performance rule: fail-open correctness first. Нельзя снижать bandwidth ценой stale/permanently missing remote state.

## 21. Dirty/revision-driven work

Target runtime избегает full-world/full-entity scans, если work можно вести от mutations/revisions.

Примеры:

- dirty world sections;
- replication registries entities/objects;
- persistence dirty-section tracking;
- cached/prepared startup state.

Caches и dirty flags требуют explicit invalidation rules. Быстрый stale cache является correctness regression.

## 22. Allocation discipline

Hot paths избегают avoidable heap churn, но allocation removal измеряется, а не превращается в ритуал.

Preferred tools: spans, pooled/owned buffers при justification, immutable frame sharing, compact value types.

Нельзя вводить `unsafe`, custom allocators или broad pooling без evidence material improvement workload и без проверки RSS/paging/complexity.

## 23. GC discipline

GC configuration меняется только по production-like measurements.

NativeAOT standalone runtime не может зависеть от JIT-specific assumptions вроде tiered compilation/dynamic PGO.

CoreCLR extensible host может использовать CoreCLR features, но runtime-core design продолжает проходить NativeAOT production gate.

`GC.TryStartNoGCRegion` не является baseline architecture assumption.

## 24. Performance telemetry

Current/target runtime telemetry включает:

- tick wall/CPU duration;
- worst/last phase duration;
- command processed/deferred/rejected counts;
- command budget exhaustion count;
- oldest pending command age;
- missed tick deadlines;
- inbound/outbound queue depths;
- slow-client events;
- entity counts;
- save snapshot/write state;
- startup/cache timings;
- allocation/GC metrics where safely available.

Telemetry aggregate/bounded и не должна сама создавать hot-path allocation problem.

## 25. Benchmark matrix

Performance roadmap использует полезные connection/load checkpoints:

```text
1
8
24
64
128
255
```

`24` players — первый meaningful realistic optimization baseline; `255` connections — stress/scalability target.

Idle, normal-play, join-burst, slow-reader и save workloads находят разные bottlenecks и не сводятся в один benchmark score.

## 26. Before/after rule

Meaningful optimization фиксирует before/after на одном hardware/environment, world и workload.

Нужно сохранять достаточно context для reproduction: relevant runtime config и player/connection count.

Если optimization не улучшает intended metric materially либо ухудшает memory/latency/correctness, complexity надо revert, а не хранить ради теоретической красоты.

## 27. Текущие ограничения

Performance work продолжается. Важные incomplete areas:

- final measurement-derived queue limits для всех workloads;
- complete per-subsystem global budgets;
- actual production interest-management suppression/resync;
- complete packet allocation/throughput baselines;
- broad 24-player/255-connection soak/stress coverage;
- complete startup/save/GC profiling больших worlds;
- optimized section cache/dirty synchronization на final scale.

## 28. Checklist performance/scheduler change

Performance/scheduler change не завершён, пока по необходимости:

- mutable state остаётся у одного authoritative owner;
- producer/per-source work bounded;
- fairness нельзя обойти одним external source;
- missed-tick behavior deliberate и tested;
- CPU/wall measurements не смешиваются;
- before/after measurement поддерживает performance claim;
- NativeAOT constraints остаются valid;
- changed observable behavior имеет explicit compatibility decision;
- эта страница и `docs/en/performance-runtime.md` обновлены вместе.
