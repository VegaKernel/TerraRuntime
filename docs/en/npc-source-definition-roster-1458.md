# Source NPC definition roster 1.4.5.8

[Русский](../ru/npc-source-definition-roster-1458.md) · [NPC runtime boundary](npc-behavior-runtime-boundary.md)

Queryable definitions cover 691 of 696 positive source identities and all 65 signed variants. IDs `76`, `146`, `403`, `404`, `408` have zero source maximum life and remain excluded. Identity `664` has nonzero life and a zero-size body: its metadata is queryable, while geometry resolution fails.

Source-backed creation defaults now cover **501 positive identities and 63 signed variants**. The [AI3/AI7 block](npc-ai3-ai7-roster-1458.md) adds 106 positive definitions and 27 signed variants to the starting `main`'s 395 positive / 36 signed defaults. It also resolves applicable difficulty and world transformations for the complete AI3/AI7 roster through the existing NPC store. Definition support does not grant unverified autonomous AI.

The 133 additions carry `CreationDefaultsOnly`; `SupportsRuntimeLifecycle` preserves the previous 431 explicit registrations. Creation/defaults and packet projection use real source data, while unproved AI/status/combat/death/role paths stay fenced.

The remaining **192 metadata-only identities** (190 positive, 2 signed) expose classic source defaults with `DefinitionOnly = true`, `BehaviorFamily.None` and `PhysicsFamily.None`. Their exact post-scale hitboxes are retained without inventing raw dimensions. Vanilla creation refuses these rows before context callbacks, RNG or allocation. Explicit manual retention/removal and deliberately registered custom behavior keep their existing ownership.

For metadata-only imports, packet `23` still requires explicit current maximum life and a valid live hitbox override. Lookup never fills unknown imported facts. The existing original-byte evidence also covers release-owner serialization for Gold NPC IDs `442..448`; wire support alone does not admit capture/release gameplay.

The complete source inventory has 761 original `SetDefaults` / `SetDefaultsFromNetId` outputs. Only numeric facts are retained. The complete family census, additions, source transforms and shared staircase correction are documented in the [AI3/AI7 roster](npc-ai3-ai7-roster-1458.md). `FullVanillaAiParity` remains false; explicit behavior and physics admission, type-specific side effects and broader runtime parity remain separate work.
