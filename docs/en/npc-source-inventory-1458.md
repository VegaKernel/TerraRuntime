# NPC source inventory for 1.4.5.8

The independent dedicated-server inventory executes `NPC.SetDefaults` for every positive and negative source identity in classic mode. `NPCID.Count` is `697`: positive requests are `1..696`. `NPCID.NegativeIDCount` is `-66`: negative requests are `-1..-65`. The capture contains 761 original outputs, including the last identity `696`, whose source AI style is `127`.

Five positive identities (`76`, `146`, `403`, `404`, `408`) produce zero maximum life. Excluding these leaves 691 positive actor definitions. At checkpoint `2d71e8d8`, runtime admits 394 positive definitions and 36 signed variants; 297 positive definitions and 29 signed variants remain absent. Definition admission does **not** mean complete AI, encounter, loot, status, environment or synchronization support. These counts are an inventory, not a completion percentage. `FullVanillaAiParity` remains false.

The largest missing positive groups, classified by the actual source `aiStyle`, are:

| Source AI style | Missing positive definitions |
| --- | ---: |
| `3` | 63 |
| `7` | 43 |
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

The complete source-only capture is [the compressed fixture](../../tests/TerraRuntime.Tests/Fixtures/npc-definition-manifest-official-1458.json.gz). It contains numeric defaults and identities, with no game assets, display text or copied source bodies. [The inventory tests](../../tests/TerraRuntime.Tests/NpcDefinitionManifest1458Tests.cs) check the complete identity range, original AI style for every admitted positive definition and original classic stats for admitted signed variants. They automatically cover newly admitted identities without treating missing definitions as implemented.

New families still need original execution traces, owned whole transitions and rejection regressions before admission. The existing [NPC roadmap](../roadmap/npc-ai-parity.md) tracks that broader work. The source assembly was executed on Windows CoreCLR; this inventory does not establish Linux NativeAOT acceptance.
