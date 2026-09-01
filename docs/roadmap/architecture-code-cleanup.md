# Architecture and code cleanup roadmap

This roadmap reduces structural and naming debt without changing Terraria behavior or weakening runtime boundaries. Cleanup is accepted only when it removes an indirection, establishes one authoritative owner, makes a fixed ABI/policy explicit, or extracts a real lifecycle boundary.

## Non-negotiable boundaries

- `TerraRuntime.Transport` stays the mechanics-only process boundary for Vega-to-server sessions and sandbox supervisor-to-worker IPC.
- `TerraRuntime.Schematics` stays standalone and NativeAOT-safe; it must not gain dependencies on Core, World, Vega, or editor/runtime ownership code.
- `.wld` parsing and persistence remain in `TerraRuntime.World`.
- Every live world retains one authoritative writer. Refactoring must not expose mutable world state to arbitrary threads or callers.
- Source-backed Terraria constants are not configuration. Protocol/file layout constants are named ABI. Operational budgets are policy and need a rationale or measurement trail.

## C0: leaf cleanup

Goal: remove cheap structural debt before moving ownership boundaries.

- [ ] Delete transparent compatibility facades once all callers use the canonical catalog/service directly.
- [ ] Remove proxy-only `*Facts`, `*Helper`, `*Provider`, and `*Manager` types that own no invariant, lifecycle, cache, policy, or translation.
- [ ] Keep source-backed `*Facts` catalogs when they are the authoritative pinned data owner rather than a forwarding wrapper.
- [ ] Replace binary/protocol field offsets and I/O buffer literals with private named layout constants.
- [ ] Collapse duplicate local policy/constants only when their semantics and provenance are actually identical.
- [ ] Prefer one canonical item/tile/NPC definition catalog over compatibility views that merely reshape the same data.

Exit criteria: no known transparent compatibility facade remains on a production path without an explicit migration reason; touched binary formats contain no unexplained field-offset literals.

## C1: naming cleanup

Goal: shorten names where the namespace or owning type already carries the missing context.

Rules:

1. Namespace identifies the subsystem.
2. Type identifies one responsibility.
3. Method identifies the action.
4. `Runtime`, `World`, `Vanilla`, and version suffixes stay only when they disambiguate real semantic boundaries.
5. Public contracts are renamed only for a semantic improvement, not to make a diff look industrious.

Targets are internal/private high-churn types first. Avoid chains such as `RuntimeWorld...ManagerProviderFactsHelper` and avoid introducing aliases solely to preserve old names.

## C2: world composition cleanup

Goal: stop `TerrariaServerHost` from being the composition root for every per-world mutable subsystem.

- [ ] Introduce a concrete `WorldRuntime` lifecycle owner for one live world.
- [ ] Move per-world mutable state, authoritative loop ownership, save/cache workers, and world-scoped ingress capabilities behind that owner.
- [ ] Keep public listener/socket acceptance, OS signals, and process shutdown in the process host.
- [ ] Let the process host own a collection/registry of `WorldRuntime` instances without adding a generic `WorldRuntimeManager` facade.
- [ ] Preserve current single-world startup as the default composition while the multi-world roadmap advances.

Exit criteria: process composition can host two independent world-runtime instances without a singleton current-world assumption and without routing ordinary in-process gameplay through IPC.

## C3: launcher/composition boundary

Goal: remove the architectural oddity where the extensible host depends on the executable project.

- [ ] Extract shared AOT-compatible server/application composition into one non-executable assembly.
- [ ] Make NativeAOT and CoreCLR launchers depend on that composition assembly.
- [ ] Keep host-module/plugin concepts above TerraRuntime Core.

Exit criteria: launchers are thin entry points and no reusable project references the shipping executable for composition behavior.

## C4: operational-number audit

Classify every touched non-trivial number as one of:

- source-backed vanilla fact;
- protocol/file ABI layout;
- safety/bounds policy;
- measured operational tuning;
- test fixture data.

For operational queue capacities, cache ceilings, time budgets, retry counts, and batching limits, record why the value exists and where it should be measured. Do not move fixed protocol/source facts into configuration merely to eliminate a literal.

## Working rule

Refactor in coherent slices with tests/CI after each slice. Prefer deleting code over wrapping it again. A new abstraction must have at least one real responsibility beyond forwarding calls, otherwise the abstraction is the smell.
