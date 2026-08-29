# TerraRuntime documentation

[Русский](../ru/README.md) · [Repository README](../../README.md) · [Roadmap](../roadmap.md)

This directory contains the English TerraRuntime documentation. The Russian version is maintained in parallel under `docs/ru/` and must describe the same actual code state.

## Start here

- [Project guide](project-guide.md) — TerraRuntime purpose, repository map, build, startup, lifecycle, networking/gameplay flow, worlds, persistence, and operations.
- [Architecture](architecture.md) — subsystem boundaries, state ownership, data flow, threading model, NativeAOT/CoreCLR profiles, persistence, and extension boundaries.
- [Host integration interfaces](host-interfaces.md) — public `TerraRuntime.HostContracts`, trusted host module lifecycle, and safe runtime interaction rules.

## Subsystem and engineering guides

- [Networking and protocol](networking-protocol.md) — framing, connection policy, Multiplicity boundary, queues, rate accounting, stop reasons and join traffic.
- [World loading and persistence](world-persistence.md) — `.wld`, `.runtime-world`, live save shadow, atomic replacement, liquid state and recovery.
- [Gameplay and vanilla parity](gameplay.md) — players, items, tiles, chests, NPCs, projectiles, combat and explicit parity gaps.
- [Synchronization and interest management](synchronization.md) — bootstrap, sections, replication registries, spatial tracking, hysteresis and current passthrough policy.
- [Operations and Terminal UI](operations-tui.md) — no-argument startup, CLI defaults, TUI lifecycle, fallback console, telemetry and dashboard extension rules.
- [Observability and logging](observability-logging.md) — bounded runtime telemetry/log buffers, current host-log behavior and the incomplete async structured logging target.
- [World generation](world-generation.md) — provider/pass/workspace/RNG contracts, trusted-host registration and current non-vanilla flat baseline.
- [Security and trust boundaries](security.md) — admission limits, rate/size bounds, failure isolation, persistence safety and incomplete hardening work.
- [Testing, verification and evidence](testing-evidence.md) — roadmap checkbox policy, independent evidence, official-source/live-world probes, NativeAOT/CoreCLR gates and performance proof rules.

## Normative project references

- [Main roadmap](../roadmap.md) — current implementation state and mandatory next work.
- [Documentation roadmap](../roadmap/documentation.md) — permanent bilingual documentation discipline and coverage plan.
- [NativeAOT baseline](../native-aot-baseline.md) — NativeAOT compatibility and shipping gates.
- [Reference policy](../reference-policy.md) — source hierarchy used to reconstruct vanilla behavior.

## Documentation freshness rule

Documentation is part of implementation, not a separate final phase.

When a code change affects observable behavior, architecture, a public interface, CLI, configuration format, deployment layout, persistence, lifecycle, threading/ownership rules, or an extension surface, the corresponding documentation in both `docs/ru/` and `docs/en/` must be updated in the same change.

A change is not complete when code and documentation describe different TerraRuntime versions.

For a new subsystem, choose its canonical documentation location first. Do not create one Markdown file per class: documentation is organized by user and architecture concepts rather than mirroring the source tree mechanically.

## What must be documented

Every significant subsystem should describe:

1. purpose and responsibility boundary;
2. source of truth and owner of mutable state;
3. inputs, outputs, and primary data types;
4. execution order and lifecycle;
5. public or host-facing interfaces with usage examples;
6. limits, safety guarantees, and failure behavior;
7. persistence/networking implications when applicable;
8. observability and diagnostics;
9. known vanilla divergences and unimplemented behavior;
10. links to relevant tests, roadmap items, or decision documents when a design choice needs separate justification.

## Language policy

Russian and English documentation are equal first-class views. Type names, package names, packet IDs, CLI keys, and other machine identifiers are not translated. Meaning, constraints, and examples must remain equivalent in both versions.
