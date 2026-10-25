# NPC source inventory for 1.4.5.8

The independent dedicated-server inventory calls `NPC.SetDefaults` for `1..696` and `SetDefaultsFromNetId` for `-1..-65`: 761 original outputs. `NPCID.Count = 697`; the last identity `696` uses AI style `127`. Five zero-life identities (`76`, `146`, `403`, `404`, `408`) leave 691 positive definitions.

Queryable coverage is **691 positive / 65 signed**. Source-backed creation defaults cover **501 positive / 63 signed**; **190 positive / 2 signed** remain classic metadata only. The [AI3/AI7 block](npc-ai3-ai7-roster-1458.md) closes both definition rosters (191+27 and 82+0). These counts are definition/default coverage, never complete AI or encounter claims. `FullVanillaAiParity` remains false.

The largest remaining positive metadata groups are:

| Source AI style | Remaining positive metadata-only defaults |
| --- | ---: |
| `107` | 20 |
| `24` | 11 |
| `0` | 8 |
| `66` | 8 |
| `114` | 7 |
| `13` | 6 |
| `22` | 6 |
| `75` | 6 |
| `18` | 5 |
| `26` | 5 |
| `39` | 5 |
| `40` | 5 |

The [compressed source manifest](../../tests/TerraRuntime.Tests/Fixtures/npc-definition-manifest-official-1458.json.gz) contains only numeric facts. [Inventory tests](../../tests/TerraRuntime.Tests/NpcDefinitionManifest1458Tests.cs) preserve complete identity-range, original-style and signed-stat checks. Family defaults and runtime creation have additional grouped source regressions. New autonomous behavior still requires original traces, owned transitions and rejection tests. See the [NPC roadmap](../roadmap/npc-ai-parity.md). Executing an original Linux assembly on Windows does not establish Linux NativeAOT acceptance.
