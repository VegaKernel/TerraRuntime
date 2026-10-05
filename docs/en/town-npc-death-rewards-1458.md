# Town NPC death rewards

[Русский](../ru/town-npc-death-rewards-1458.md) · [NPC roadmap](../roadmap/npc-ai-parity.md)

The common death pipeline now owns the registered town-specific reward tables of TerrariaServer 1.4.5.8. Each successful rule materializes its item, natural prefix and initial velocity before the next rule or death advances the shared RNG. Global drops, dedicated HitEffect choices, credit, money and healing retain their existing source order. Ordinary lethal strikes and ordered Guide Doll batches use the same implementation.

| NPC | Specific reward | Source condition |
| --- | --- | --- |
| Guide `22` | `867` | Exact resident name Andrew |
| Steampunker `178` | `4372` | Exact resident name Whitney |
| Painter `227` | `5290`, then independent `3350` | Name Jim for the first; one in eight for the second |
| Stylist `353` | `3352` | One in eight |
| Tax Collector `441` | `3351` | One in eight |
| Tavernkeep `550` | `3821` | One in eight |
| Party Girl `208` | `3548`, stack `30..60` inclusive | One in four |
| Dye Trader `207` | `3349` | One in eight |
| Mechanic `124` | `4818` | One in eight |
| Princess `663` | `5065` | Hardmode, one in eight |
| Clothier `54` | `260` | Guaranteed |
| Travelling Merchant `368` | `2222` | Guaranteed; pure table evidence only |

The standalone server composition explicitly selects the original server's default English loot-name profile. This is independent of the operating system culture and the language of this documentation. Custom composition defaults to an unknown profile. A nonempty resident name requires an owned language profile; an independently known empty name proves the source HasGivenName predicate false without localization. Names belong to exact resident generations. Unknown names or unsupported profiles reject the whole dependent death or Doll batch before publication.

The twelve reward item defaults grant physical world-drop capability only. They do not grant weapon use, placement or item-use support. Existing source prefix machinery supplies sword, ranged, generic and magic families. The Travelling Merchant table does not admit its NPC definition, AI or visiting lifecycle.

Retained death plans validate world facts, resident names, players, inventory, RNG and allocation ownership. External name/player/world callbacks finish before the final direct NPC, progression and death-prelude checks. This prevents an unchanged name callback from changing Hardmode after an earlier comparison and publishing stale rewards.

Independent original evidence covers 3060 specific-table executions in English and Russian reference cultures, twelve item defaults with natural/valid-prefix checks, 1620 admitted whole StrikeNPC executions, 280 whole Doll batches, 14 independently captured migrations of previously refused reward victims and 31 packet 21/22 comparisons through the real replication writer. Russian reference captures verify the source predicate semantics; they do not claim a Russian runtime loot profile. Packet evidence establishes item publication, not complete town-chat or whole NPC wire cadence. Copied regressions detect wrong Painter ordering, name predicates, unknown locale admission, Princess Hardmode, stack endpoints and stale dependencies.

Full NPC parity remains open. Other locale profiles, unknown imported names, the Travelling Merchant lifecycle, remaining actor families and broader status, environment, scheduling and wire phases retain their explicit admission boundaries. See [source inventory](npc-source-inventory-1458.md) and [owned death/status producers](npc-owned-health-and-producers-1458.md).
