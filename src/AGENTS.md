# TerraRuntime source architecture and naming rules

These rules apply to all production source under `src/` and supplement the repository-root `AGENTS.md`. The living cleanup checklist is `docs/roadmap/architecture-code-cleanup.md`.

## Project ownership

Use project boundaries to express real dependency, lifecycle, trust or deployment boundaries. Do not create a project merely because a folder is large.

Target ownership:

- `TerraRuntime.Contracts`: stable low-level identities and DTO/contracts.
- `TerraRuntime.Gameplay`: protocol-neutral gameplay/content rules and source-backed catalogs.
- `TerraRuntime.Core`: authoritative execution mechanics, ownership, scheduling, command ingress and bounded worker/lifecycle primitives.
- `TerraRuntime.World`: `.wld`, persistence representation and world storage semantics.
- `TerraRuntime.Schematics`: standalone `.trschem` model/codec/file API.
- `TerraRuntime.Protocol*`: wire semantics and protocol-library boundary.
- `TerraRuntime.Network`: bounded connection/network mechanics.
- `TerraRuntime.Transport`: process IPC mechanics only.
- server/application composition: concrete `WorldRuntime`, listener/process lifecycle and assembly composition.
- Vega/plugins/modules: policy and extension composition above TerraRuntime.

Keep references acyclic. Do not solve a move by introducing `Core <-> Gameplay`, `World <-> Core` or similar cycles.

This is a pre-1.0 codebase. When an ownership move makes a namespace wrong, migrate the namespace and callers directly. Do not add compatibility aliases, forwarding wrappers or obsolete facades unless a concrete external compatibility commitment exists.

## Naming

Let namespaces carry subsystem context. A type name should identify one responsibility, not repeat the full route through the architecture.

Avoid stacked names such as `WorldRuntimeManagerProviderFactory`, `RuntimeWorldGameplayServiceManager`, `...FactsHelper` or `...FactoryProvider`.

Use role suffixes only when they describe real ownership:

- `Store`: owns mutable/retained state;
- `Registry`: owns keyed registration/lookup and its lifecycle or snapshot semantics;
- `Catalog`: owns authoritative immutable/source-backed definitions;
- `Router`: owns routing decisions;
- `Queue`: owns queued work/backpressure semantics;
- `Pool`: owns bounded reusable/concurrent resources;
- `Supervisor`: owns child-process/task lifecycle and failure containment;
- `Coordinator`: owns a real multi-step transaction/workflow across collaborators;
- `Session`: owns one bounded interaction/lifetime;
- `Clock`: owns time state/rules;
- `Cache`: owns cached derived data and invalidation;
- `Codec`, `Parser`, `Reader`, `Writer`: own representation translation.

`Manager` is a last resort. Prefer the resource or lifecycle actually owned.

`Provider` is valid only for meaningful contextual/external resolution. It is not a synonym for “class with one method”.

`Factory` is valid only when object creation has real variants, policy, pooling or non-trivial construction. Do not wrap a single `new T(...)` path in a factory for hypothetical extensibility.

`Helper` must not be a public architectural type. Put behavior on the owning type or give an internal algorithm a precise domain name.

`Facts` is reserved for an authoritative source-backed fact owner. A class forwarding to a catalog is not a Facts type.

`Service` is not a universal escape hatch. Use it only for a cohesive operation-oriented API whose responsibility cannot be named more precisely.

Do not create an `IThing`/`Thing` pair by habit. An interface needs an existing reason: multiple real implementations, a trust/process/thread boundary, or an independently substitutable contract used by tests/composition. “Maybe later” is not a reason.

Keep `Vanilla` when it communicates a source-pinned vanilla contract, vanilla-versus-extension distinction or vanilla content identity. Remove it when it merely says “this Terraria runtime contains Terraria behavior”.

Keep version suffixes such as `1458` only when the type is deliberately pinned to that version and a different version could materially differ or coexist.

## Decomposition

Split by ownership and reason-to-change, not by line count.

Review triggers for handwritten production code:

- roughly 600+ lines in one file: inspect responsibilities;
- roughly 1,000+ lines: require a clear cohesion justification or decomposition plan;
- roughly 100+ lines in one method: inspect control flow/responsibility;
- roughly 200+ lines in one method: require an explicit reason when source-order fidelity makes splitting worse;
- roughly 12+ independent constructor collaborators: inspect composition ownership.

These are review triggers, not CI limits. A large source-backed catalog can remain one cohesive file. A much smaller class with unrelated state, protocol parsing, persistence and gameplay mutation should be split.

Prefer a few concrete collaborators that own state/lifecycle over many tiny classes that only forward calls. Do not create one class per method.

## Abstraction admission rule

Before adding a new abstraction, state what it owns. At least one must be true:

- invariant;
- lifecycle/resource ownership;
- mutable or retained state;
- cache/invalidation;
- admission/policy decision;
- trust/thread/process boundary;
- representation translation;
- non-trivial algorithm;
- multiple existing implementations that genuinely need one contract.

If none apply, use the existing owner directly.

## Constants

Do not treat every numeric literal as configuration.

Classify important values as source-backed vanilla facts, protocol/file ABI layout, safety/bounds policy, measured operational tuning, local mathematics or test fixture data. Name field offsets/masks and important bounds near the code that owns their semantics. Keep source-backed values close to the verified catalog/rule. Do not create a giant unrelated `Constants` class.

## Refactoring workflow

When a refactor closes an item in `docs/roadmap/architecture-code-cleanup.md`, update its checkbox in the same coherent slice. Delete the old path/facade when callers migrate instead of leaving permanent forwarding layers.
