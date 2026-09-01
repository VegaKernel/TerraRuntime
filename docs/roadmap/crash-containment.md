# Crash containment and fault isolation roadmap

This roadmap defines how TerraRuntime, the extensible host, Vega and ordinary server plugins contain failures without silently continuing after an authoritative state corruption.

The goal is not `catch (Exception) { continue; }`. Recoverable extension/subsystem failures must be isolated and logged with enough context to diagnose them. Failures that invalidate authoritative world/runtime invariants must stop the affected world or process in a controlled way instead of pretending that state is still trustworthy.

> Checkbox policy: `[x]` means implementation plus executable tests/CI or equivalent proof exists on the production branch. A catch block or log message alone is not completion.

## Fault classes and ownership

| Fault source | Owner | Default disposition |
|---|---|---|
| ordinary `ServerPlugins/*.dll` callback | Vega | isolate callback, record fault, circuit-break/disable offending plugin after threshold |
| ordinary plugin startup/reload/unload | Vega | preserve previous good revision when possible; failed revision becomes `Failed`/`Degraded` |
| plugin background task / timer / IO continuation | Vega | cancel/contain task, attribute full exception to plugin, count toward circuit breaker |
| plugin command | Vega | return a failed command result; never let exception escape into host dispatch |
| plugin player/world/event hook | Vega | isolate the single plugin callback; other subscribers and runtime continue |
| trusted `HostModules/*.dll` startup/attach/callback | TerraRuntime extensible host | isolate module when policy allows; otherwise controlled host shutdown |
| optional TUI / telemetry / diagnostics | TerraRuntime or Vega owner | disable/degrade optional subsystem; server keeps running |
| one network connection / decoder session | TerraRuntime | close only the offending connection and retain server |
| bounded background worker/cache job | TerraRuntime | capture failed completion, discard unpublished result, retain authoritative state |
| world generator/provider failure before publication | TerraRuntime | abort generation transaction, clean temporary state, retain running server |
| save publication failure | TerraRuntime | retain last known-good world image / backup, report failure, do not publish partial file |
| authoritative game-loop invariant violation after partial mutation | TerraRuntime | fail closed: structured critical log and controlled world/server shutdown; never continue unknown state |
| process-corrupting/runtime-fatal failure (`FailFast`, stack overflow, native corruption, OOM edge cases) | OS/process supervisor | same-process containment is not guaranteed; rely on external restart and last known-good persisted state |

## Audit findings

### Vega ordinary-plugin boundary

- plugin load/start already catches activation failures, runs cleanup, attempts collectible `AssemblyLoadContext` release and records `Failed` state;
- hot replacement already stages a new revision before replacing the active one;
- plugin stop/dispose cleanup aggregates failures instead of immediately crashing the process;
- PluginSdk player lifecycle callbacks are currently subscribed directly to shared events;
- PluginSdk commands are currently registered as raw `ICommandHandler` instances and `CommandDispatcher` invokes `ExecuteAsync` directly;
- scheduled game-thread work is caught by the shared dispatcher, but the catch occurs too late to provide a uniform plugin callback contract/circuit-breaker policy;
- periodic/IO helpers catch some background exceptions, but the callback applied on the game thread still needs the same plugin execution guard;
- player snapshot refresh has a broad outer catch, which protects the Terraria update loop but can misattribute a plugin event exception as a snapshot failure;
- there is no single per-plugin failure counter, rolling time window, degraded/faulted state transition or automatic callback suppression after repeated faults.

### TerraRuntime trusted-host boundary

- `TrustedHostModuleLoader` rolls back failed runtime attachment scopes and unloads failed load contexts;
- host modules are optional/degradable by default, while `TERRARUNTIME_REQUIRED_HOST_MODULES` can require selected DLLs or all modules through `*`;
- optional startup, attach, detach and stop failures are attributed to the owning module and contained without exposing partial loader-owned registrations;
- required startup/attach/detach failures remain fail-closed and preserve full `Exception.ToString()` diagnostics;
- dashboard and world-generator registrations are loader-owned scopes, so one retiring module cannot unregister another module's resources and failed cleanup cannot keep registrations published;
- contained host-module faults are retained in a bounded fault history and surfaced through the `Host Module Health` terminal dashboard;
- the permanent `Runtime Crash Containment` CI gate runs deliberate load/start/attach/detach/stop fault injection alongside the existing host loader/world-generation integration suites;
- trusted/custom world-generator execution now uses the C5 transactional publication boundary and generic structural validation instead of inheriting vanilla-completeness requirements from canonical dimensions.

### TerraRuntime core/subsystems

- bounded worker jobs already convert thrown exceptions into failed completions rather than killing the worker loop;
- several TUI/console paths already catch local failures, but the policy is not yet expressed as a unified optional-subsystem health model;
- network, save and authoritative game-loop boundaries need explicit fault-disposition tests so future code cannot accidentally broaden a local failure into process termination;
- world generation now proves that provider/pass exception or cancellation after partial workspace mutation discards the candidate and publishes no partial `.wld` or temporary file;
- top-level fatal logging must preserve `Exception.ToString()`/structured exception data, including type, stack trace and inner exceptions;
- no catch-all may resume the authoritative loop after a failure that may have partially mutated world state.

## C0 — Policy, diagnostics and architecture

- [x] classify plugin, host-module, optional-subsystem, connection, worker, generation/save and authoritative-core faults by owner and disposition in this roadmap;
- [ ] define a shared structured fault record: component/module/plugin, version, operation/callback, world/session where applicable, thread, UTC timestamp, exception type/message/stack/inner chain and correlation id;
- [ ] define health states (`Healthy`, `Degraded`, `Faulted`, `Disabled`, `RestartRequired`) without conflating them with process liveness;
- [ ] define which failures are safe to continue, safe to retry, require module disable, require world shutdown, or require process shutdown;
- [ ] add operator-facing documentation explaining that same-process managed plugins are not a hostile-code security sandbox.

## C1 — Vega PluginSdk execution guard

- [ ] introduce one per-plugin execution guard owned by the plugin registration scope;
- [ ] wrap dynamically registered plugin commands so thrown exceptions become `plugin.callback_failed` results and never escape `CommandDispatcher`;
- [ ] wrap `Players.OnJoined` and `Players.OnLeft` callbacks independently so one plugin cannot abort shared player-session projection/event delivery;
- [ ] wrap game-thread `Post`, periodic timer actions and IO result-apply callbacks at the plugin boundary, with plugin/operation attribution;
- [ ] preserve normal cancellation semantics instead of counting host-requested cancellation as a plugin fault;
- [ ] log the full exception object through `IOperationsLogger`, not only `Exception.Message`;
- [ ] add regression tests with deliberately throwing command, join/leave, scheduled and apply callbacks proving the exception is contained and attributed.

## C2 — Vega circuit breaker and automatic retirement

- [ ] maintain per-plugin rolling fault counters and last-fault metadata;
- [ ] add configurable thresholds/window with conservative defaults and no hot-path allocation-heavy bookkeeping;
- [ ] transition repeatedly failing plugins through `Degraded` to `Faulted`/`Disabled`;
- [ ] suppress new callbacks once the breaker opens;
- [ ] cancel plugin lifetime tasks, unregister commands/events/hooks, call `StopAsync`/`DisposeAsync`, unload collectible `AssemblyLoadContext` and verify collection;
- [ ] if cleanup or unload fails, expose `RestartRequired` instead of claiming successful isolation;
- [ ] preserve the active old revision if a hot-replacement candidate faults during staging/start;
- [ ] expose fault count, last callback, last exception summary and breaker state in module snapshots/TUI/API;
- [ ] test one faulty plugin alongside a healthy plugin and prove healthy callbacks keep running before and after retirement of the faulty plugin.

## C3 — Vega callback coverage audit

- [ ] route every PluginSdk event/hook registration through the guard, not just player lifecycle;
- [ ] audit command, chat, packet, world, NPC, chest, tile, projectile, account, permissions, region/protection and future extension surfaces;
- [ ] audit all fire-and-forget tasks and event subscriptions for ownership/cancellation/exception observation;
- [ ] ensure config validators/change callbacks cannot tear down shared reload loops;
- [ ] ensure diagnostics/debug providers are guarded and time-bounded where appropriate;
- [ ] add an architecture test that rejects new raw plugin callback registrations outside the approved guard/scope boundary.

## C4 — TerraRuntime trusted host-module containment

- [x] replace message-only startup failure output with structured/full exception logging;
- [x] introduce explicit host-module policy: required module versus optional/degradable module;
- [x] on optional module startup failure, unload only that module and continue without exposing partial registrations;
- [x] on required module startup failure, perform controlled shutdown with distinct exit code and complete diagnostics;
- [x] guard runtime attach/detach and future host callbacks with per-module attribution while retaining current rollback semantics;
- [x] ensure one optional host module cannot unregister/retire another module's scoped resources;
- [x] expose host-module health in TUI/operations telemetry;
- [x] test a deliberately throwing host-module fixture during load, start, attach, detach and stop.

## C5 — TerraRuntime subsystem containment

- [ ] network: prove decoder/connection exceptions terminate only the owning connection and release all per-connection resources;
- [ ] worker pool/cache: prove thrown work is surfaced as failed completion, unpublished data is discarded and worker capacity is recovered;
- [x] world generation: provider execution is isolated in a candidate transaction; exception/cancellation after partial mutation discards the candidate, leaves no partial `.wld`/temporary publication, custom canonical-size generators use generic structural validation, and process-level acceptance proves the published world loads in TerraRuntime and TerrariaServer 1.4.5.8;
- [ ] save/recovery: preserve last known-good file/backup on encoder, fsync, rename or validation failure;
- [ ] TUI: contain render/provider failures and allow headless/plain-console server operation to continue;
- [ ] telemetry/log sinks: prevent a broken optional sink from blocking the game loop; retain a minimal stderr fallback;
- [ ] background services: every long-lived task must be owned, cancellable and observed; no unobserved fire-and-forget exceptions;
- [ ] add fault-injection tests for each boundary.

## C6 — Authoritative-core fail-closed policy

- [ ] define invariants whose violation makes continuation unsafe (partial world mutation, entity ownership corruption, generation/version mismatch after commit, save publication ambiguity, etc.);
- [ ] add a controlled fatal path that records full context and stops accepting new mutations/connections;
- [ ] do not perform an emergency world save after an invariant violation unless the state has an explicitly validated safe checkpoint;
- [ ] flush bounded diagnostics and terminate with a distinct fatal exit code suitable for a service supervisor;
- [ ] add process-level smoke tests proving graceful shutdown ordering and last-known-good recovery on next start;
- [ ] document which failures cannot be reliably caught in-process and require systemd/Docker/Windows-service restart policy.

## C7 — Production acceptance

- [ ] fault-injection plugin that throws from each public callback family without taking down the server;
- [ ] repeated-fault test opens the breaker and unloads only the offending plugin;
- [ ] healthy plugin continues receiving callbacks while a faulty neighbor is disabled;
- [ ] collectible load context is demonstrably reclaimed after fault retirement;
- [ ] optional host-module fault leaves TerraRuntime serving when policy permits;
- [ ] required host-module fault exits cleanly with full diagnostics;
- [ ] network/client fault storm does not destabilize the authoritative loop;
- [ ] worldgen/save injected failures leave no published partial world and preserve recovery path;
- [ ] authoritative invariant fault stops instead of continuing corrupt state;
- [ ] soak test combines plugin exceptions, reconnects, slow clients, saves and hot reload while checking memory/thread/registration leaks.

## Current implementation order

TerraRuntime is completed first. C4 is closed and C5 proceeds subsystem by subsystem: world generation, network, worker/cache jobs, save/recovery, TUI/telemetry and background-service ownership. C6 then defines the authoritative fail-closed path and C7 provides process-level fault acceptance. Vega C1-C3 remain documented but are intentionally deferred until the runtime containment layers are complete.
