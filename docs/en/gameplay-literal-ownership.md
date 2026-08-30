# Intentional literal ownership

[Русский](../ru/gameplay-literal-ownership.md) · [Domain-literal CI gate](gameplay-domain-literal-gate.md) · [Gameplay decomposition roadmap](../roadmap/gameplay-decomposition-and-catalogs.md)

Not every number is a gameplay identity. TerraRuntime keeps the remaining intentional raw values at explicit owners and rejects their migration into unrelated gameplay code.

| Owner | Intentional raw values | Boundary rule |
|---|---|---|
| `Vanilla*Ids` and definition catalogs | version-pinned content numbers, counts and selected metadata | gameplay consumes typed IDs or definitions, never copied literals |
| packet/frame codecs and protocol projections | message fields, bit layouts, sentinel values and primitive capacities | decode/validate before gameplay; encode only from semantic state |
| `WorldFile*`, prepared-state and snapshot codecs | `.wld` field order, raw enums, section markers and format limits | persistence primitives remain inside the named adapter |
| `Vanilla*WorldGeneration*1458` passes | source-order RNG bounds, pass-local tile aliases, dimensions and thresholds | values belong to the pinned generation pass and may not become general gameplay constants |
| behavior/physics owners | ticks, pixels, speeds, probabilities and local arithmetic | use named constants or parameter records when observable/non-obvious; tests pin the controlled branch |
| tests and verification tools | exact fixtures, invalid sentinels and official reference values | literals are evidence/input, not runtime identity APIs |

The lexical audit currently has no production-source suppressions. If an exceptional literal requires one later, the same source line must name the matched rule and give a reviewable reason; the suppression is itself part of the ownership record.

`Generation`, `Packet`, `Protocol`, `Codec`, `WorldFile`, `Snapshot` and similar filename markers are audit boundary declarations, not blanket exemptions. A file with such a name still must not perform unrelated gameplay decisions on raw identities.

The existing textual gate is high-signal and its self-tests cover every enforced pattern. A Roslyn analyzer remains optional unless syntax evolution or recurring false negatives make lexical enforcement insufficient.
