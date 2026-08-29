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
│   ├── host-interfaces.md
│   ├── networking-protocol.md
│   ├── world-persistence.md
│   ├── gameplay.md
│   ├── synchronization.md
│   ├── performance-runtime.md
│   ├── operations-tui.md
│   ├── observability-logging.md
│   ├── world-generation.md
│   ├── security.md
│   └── testing-evidence.md
├── en/
│   ├── README.md
│   ├── project-guide.md
│   ├── architecture.md
│   ├── host-interfaces.md
│   ├── networking-protocol.md
│   ├── world-persistence.md
│   ├── gameplay.md
│   ├── synchronization.md
│   ├── performance-runtime.md
│   ├── operations-tui.md
│   ├── observability-logging.md
│   ├── world-generation.md
│   ├── security.md
│   └── testing-evidence.md
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
- performance/tick scheduling or work-budget semantics;
- persistence, `.wld`, `.runtime-world`, save/recovery behavior;
- security, rate-limit or failure behavior;
- TUI/operations integration;
- logging/observability contracts;
- test/evidence policy that changes what qualifies as verified support;
- world generation extension contracts;
- known supported/unsupported vanilla behavior.

A mismatch between code and documentation is a defect.

## Presentation standard

Architecture/process diagrams use GitHub-rendered Mermaid instead of ASCII or pseudographic boxes/arrows.

Use the diagram type that matches the concept:

- `flowchart` for ownership, dependency and data-flow structure;
- `sequenceDiagram` for protocol/lifecycle ordering;
- `stateDiagram-v2` for connection, entity or persistence states;
- another GitHub-supported Mermaid form only when it expresses the concept more clearly.

Plain fenced text/code blocks remain valid for literal CLI commands, directory trees, byte layouts, protocol envelopes and code samples. They must not be used to imitate a diagram.

Quantitative values with units are written as LaTeX math where applicable. Examples:

- `$60\,\mathrm{Hz}$`;
- `$16.67\,\mathrm{ms}$`;
- `$16\,\mathrm{MiB}$`;
- `$4\,\text{sections/tick}$`.

Identifiers remain code literals rather than math: packet IDs, protocol versions, enum values, type names, configuration keys and file-format identifiers such as `packet 47`, protocol `326`, Terraria `1.4.5.8` and `MaxCommandsPerTick`.

Derived quantities should use equations when the relationship matters, with symbols defined near the formula. Do not invent decimal precision beyond the source measurement.

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

### Subsystem and engineering guides

Complex domains receive standalone guides once their behavior and boundaries are substantial enough to maintain as a coherent concept. The current baseline covers protocol/networking, world persistence, gameplay/parity, synchronization/interest management, performance/tick scheduling, operations/TUI, observability/logging, world generation, security and the project's testing/evidence discipline.

### Roadmaps

Describe target state, acceptance criteria and incomplete work. They must not masquerade as documentation of already implemented behavior.

## RU/EN parity

- Russian and English are equal first-class documentation languages.
- Machine identifiers remain unchanged across languages.
- Examples must describe the same API version and semantics.
- A feature must not be documented as supported in one language and experimental/missing in the other.
- When exact sentence-by-sentence translation harms clarity, semantic equivalence is required instead.

## CI documentation gate

`tools/ci/check_documentation.py` runs in the main `build-test` CI job before restore/build/test for repository-wide structural/link validation.

The checker validates structural invariants rather than attempting machine translation:

- `docs/en/` and `docs/ru/` must contain the same set of Markdown pages;
- the required baseline pages listed by the checker must exist in both languages;
- repository-local Markdown links in `docs/**/*.md`, root `README.md` and `AGENTS.md` must resolve to an existing path;
- relative links may not escape the repository root.

The dedicated `.github/workflows/documentation.yml` workflow checks out full Git history and invokes the same checker with `--changed-base`. That adds a change-set invariant: every changed `docs/en/<page>.md` requires the matching changed `docs/ru/<page>.md` in the same push/PR diff, and vice versa.

For direct work on `main`, paired language edits should therefore be committed atomically where possible. A sequence of single-file pushes is intentionally considered incomplete until a push contains the complete bilingual pair in its checked change set.

The gate does **not** claim to prove semantic translation equivalence. Review remains responsible for meaning and factual parity between the two language versions.

Run the structural/link check locally from the repository root:

```text
python3 tools/ci/check_documentation.py
```

The `--changed-base <sha>` form is used by CI when a concrete push/PR base is available.

## Initial implementation

- [x] Create `docs/ru/` and `docs/en/` entry points.
- [x] Create bilingual project guides.
- [x] Create bilingual architecture guides.
- [x] Create bilingual trusted-host interface guides with usage examples.
- [x] Add same-change documentation discipline to `AGENTS.md`.
- [x] Link the bilingual documentation from the main repository documentation surface.

## Documentation coverage

- [x] Dedicated protocol/networking guide: framing, connection states, Multiplicity boundary, inbound/outbound queues and rejection categories.
- [x] Dedicated world/persistence guide: `.wld` support, save pipeline, atomic recovery, runtime cache and warm-start behavior.
- [x] Dedicated gameplay guide: players, inventory/items, NPCs, projectiles, combat and authoritative validation, with explicit parity status.
- [x] Dedicated synchronization guide: sections, bootstrap/join, interest management and resync invariants.
- [x] Dedicated performance/tick-runtime guide: $60\,\mathrm{Hz}$ schedule, command mailbox/ingress/apply budgets, per-source fairness, missed-deadline policy and measurement discipline.
- [x] Dedicated operations/TUI guide: startup modes, dashboard model, telemetry and safe administrative operations.
- [x] Dedicated observability/logging guide: bounded current read models and telemetry, TUI consumption, and explicit separation from the incomplete async structured logging target.
- [x] Dedicated worldgen guide: provider contracts, plan/pass lifecycle, workspace model and vanilla-worldgen status.
- [x] Dedicated security guide: trust boundaries, budgets, rate limits, malformed input handling and failure isolation.
- [x] Dedicated testing/evidence guide: source hierarchy, roadmap checkbox policy, independent compatibility evidence, official/live probes, runtime publish gates and performance proof rules.
- [x] Add documentation-link validation in CI.
- [x] Add lightweight RU/EN structural parity validation without machine translation or line-by-line equality.
- [x] Enforce paired RU/EN page changes in the same push/PR diff through the dedicated Documentation workflow.
- [x] Adopt Mermaid + LaTeX as the documentation presentation standard and migrate the main architecture guide.
- [ ] Migrate remaining legacy pseudographic subsystem diagrams to Mermaid and normalize dimensional numeric values to LaTeX.

## Continuing work

Documentation does not become "finished" after the baseline pages exist. The permanent work is to keep those pages synchronized with implementation and split out new stable subsystem guides only when a concept becomes too large for the existing structure.

Useful future improvements, when justified by real maintenance needs, include source/API link generation for stable public contracts without mirroring every class into Markdown, additional executable examples for host integrations, version/support matrices when TerraRuntime supports more than one Terraria/protocol baseline, documentation coverage for new gameplay domains as bosses/events/housing/wiring/progression become authoritative, and CI checks for machine-readable support tables if those tables later become canonical project data.

## Definition of done for documentation work

Documentation work is complete when:

- both language versions exist and agree semantically;
- examples compile conceptually against the current public signatures;
- implemented behavior and target behavior are clearly distinguished;
- ownership/threading/failure rules are explicit where relevant;
- diagrams use Mermaid rather than pseudographic boxes/arrows;
- dimensional quantitative values use LaTeX units where applicable;
- links are relative and repository-safe;
- `python3 tools/ci/check_documentation.py` passes;
- the dedicated Documentation workflow passes for a change that touches bilingual pages;
- the associated roadmap/status is updated when support changed.
