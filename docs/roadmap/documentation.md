# Bilingual documentation roadmap

This document defines TerraRuntime's permanent RU/EN documentation discipline. It is normative together with `docs/roadmap.md` and `AGENTS.md`.

## Goal

Maintain complete, current, practical documentation for operators, contributors and trusted-host integrators in both Russian and English while the code evolves.

Documentation is not a release-polish phase. It is produced in the same development flow as the implementation it describes.

## Canonical layout

```text
docs/
├── ru/
│   ├── README.md
│   ├── project-guide.md
│   ├── architecture.md
│   └── host-interfaces.md
├── en/
│   ├── README.md
│   ├── project-guide.md
│   ├── architecture.md
│   └── host-interfaces.md
├── roadmap.md
└── roadmap/
```

More subsystem guides are added when a concept becomes large enough to deserve a stable standalone document. Do not create one documentation page per source class.

## Mandatory same-change policy

A code change must update both RU and EN documentation in the same change when it affects:

- observable behavior;
- architecture or dependency boundaries;
- authoritative state ownership or threading;
- public/host-facing contracts;
- CLI, startup, deployment or directory layout;
- configuration;
- networking or synchronization semantics visible to integrators/operators;
- persistence, `.wld`, `.runtime-world`, save/recovery behavior;
- security, rate-limit or failure behavior;
- TUI/operations integration;
- world generation extension contracts;
- known supported/unsupported vanilla behavior.

A mismatch between code and documentation is a defect.

## Required content for each subsystem

Every significant subsystem guide must eventually answer:

1. What problem does the subsystem solve?
2. Which project/package owns it?
3. Which thread/component owns mutable state?
4. What are the main inputs, outputs and data types?
5. What is the lifecycle/execution order?
6. Which public or host-facing interfaces exist?
7. How should external code use those interfaces?
8. What must external code never do?
9. What bounds, budgets and failure guarantees exist?
10. What persistence and networking effects exist?
11. What telemetry/diagnostics exist?
12. What vanilla behavior is verified?
13. What remains incomplete or deliberately unsupported?
14. Which tests/probes provide evidence?

## Documentation layers

### Project guide

User/contributor-oriented description of how the server is built, started and operated and how major runtime flows behave.

### Architecture

System-level source of truth for dependency direction, ownership, threading, data flow, persistence and extension boundaries.

### Host/API guides

Practical descriptions of supported external integration contracts, lifecycle, status/error semantics and examples.

### Subsystem guides

Added for complex domains such as protocol, world persistence, synchronization/interest management, NPCs/projectiles/combat, worldgen, security and observability as they mature.

### Roadmaps

Describe target state, acceptance criteria and incomplete work. They must not masquerade as documentation of already implemented behavior.

## RU/EN parity

- Russian and English are equal first-class documentation languages.
- Machine identifiers remain unchanged across languages.
- Examples must describe the same API version and semantics.
- A feature must not be documented as supported in one language and experimental/missing in the other.
- When exact sentence-by-sentence translation harms clarity, semantic equivalence is required instead.

## Initial implementation

- [x] Create `docs/ru/` and `docs/en/` entry points.
- [x] Create bilingual project guides.
- [x] Create bilingual architecture guides.
- [x] Create bilingual trusted-host interface guides with usage examples.
- [x] Add same-change documentation discipline to `AGENTS.md`.
- [x] Link the bilingual documentation from the main repository documentation surface.

## Next documentation coverage

- [ ] Dedicated protocol/networking guide: framing, connection states, Multiplicity boundary, inbound/outbound queues and rejection categories.
- [ ] Dedicated world/persistence guide: `.wld` support matrix, save pipeline, atomic recovery, runtime cache and warm-start behavior.
- [ ] Dedicated gameplay guide: players, inventory/items, NPCs, projectiles, combat and authoritative validation, with explicit parity status.
- [ ] Dedicated synchronization guide: sections, bootstrap/join, interest management and resync invariants.
- [ ] Dedicated operations/TUI guide: startup modes, dashboard model, telemetry and safe administrative operations.
- [ ] Dedicated worldgen guide: provider contracts, plan/pass lifecycle, workspace model and vanilla-worldgen status.
- [ ] Dedicated security guide: trust boundaries, budgets, rate limits, malformed input handling and failure isolation.
- [ ] Add diagrams/examples when they clarify an actual interaction path; do not add decorative architecture art that cannot be kept current.
- [ ] Add documentation-link validation in CI once the bilingual tree stabilizes.
- [ ] Consider a lightweight RU/EN parity check for required mirrored pages without attempting machine translation or line-by-line equality.

## Definition of done for documentation work

Documentation work is complete when:

- both language versions exist and agree semantically;
- examples compile conceptually against the current public signatures;
- implemented behavior and target behavior are clearly distinguished;
- ownership/threading/failure rules are explicit where relevant;
- links are relative and repository-safe;
- the associated roadmap status is updated when support changed.
