# AI3 and AI7 NPC definitions, Terraria 1.4.5.8

[Русский](../ru/npc-ai3-ai7-roster-1458.md) · [Source definition roster](npc-source-definition-roster-1458.md)

The complete source census contains **300 identities**: AI3 has **191 positive types and 27 signed NetIDs**; AI7 has **82 positive types and no signed variants**. The source is the locally decompiled official dedicated server, `NPC.SetDefaults`, `SetDefaultsFromNetId`, `ScaleStats` and special-world adjustments. Numeric original-assembly captures supply independent expectations. No decompiled bodies, localized game text or assets are distributed.

Definition support means source defaults and real runtime creation: body dimensions, scale, life, damage, defense, knockback, gravity/tile collision, allegiance and applicable difficulty/world transformations. It does not establish type-specific AI, attacks, transformations, spawn pools, rescue/capture, loot or full encounters. Explicit behavior and physics families remain the admission boundary; a shared `aiStyle` never assigns them automatically. `FullVanillaAiParity` remains false.

## Complete source identities

### AI3 positive types

`3`, `21`, `26`, `27`, `28`, `31`, `44`, `47`, `52`, `53`, `67`, `73`, `77`, `78`, `79`, `80`, `104`, `109`, `110`, `111`, `120`, `132`, `140`, `159`, `161`, `162`, `163`, `164`, `166`, `167`, `168`, `181`, `185`, `186`, `187`, `188`, `189`, `196`, `197`, `198`, `199`, `200`, `201`, `202`, `203`, `206`, `212`, `213`, `214`, `215`, `216`, `217`, `218`, `219`, `220`, `223`, `236`, `239`, `243`, `251`, `254`, `255`, `257`, `258`, `269`, `270`, `271`, `272`, `273`, `274`, `275`, `276`, `277`, `278`, `279`, `280`, `287`, `290`, `291`, `292`, `293`, `294`, `295`, `296`, `305`, `306`, `307`, `308`, `309`, `310`, `311`, `312`, `313`, `314`, `319`, `320`, `321`, `322`, `323`, `324`, `326`, `331`, `332`, `338`, `339`, `340`, `342`, `343`, `348`, `349`, `350`, `351`, `379`, `380`, `381`, `382`, `383`, `385`, `386`, `389`, `391`, `409`, `411`, `415`, `419`, `424`, `425`, `426`, `427`, `428`, `429`, `430`, `431`, `432`, `433`, `434`, `435`, `436`, `449`, `450`, `451`, `452`, `460`, `461`, `462`, `463`, `464`, `466`, `468`, `469`, `470`, `471`, `480`, `481`, `482`, `489`, `494`, `495`, `498`, `499`, `500`, `501`, `502`, `503`, `504`, `505`, `506`, `508`, `518`, `520`, `524`, `525`, `526`, `527`, `528`, `529`, `530`, `532`, `534`, `536`, `580`, `582`, `586`, `590`, `591`, `624`, `630`, `631`, `632`, `635`, `691`

### AI3 signed NetIDs

`-13`, `-14`, `-15`, `-26`, `-27`, `-28`, `-29`, `-30`, `-31`, `-32`, `-33`, `-34`, `-35`, `-36`, `-37`, `-44`, `-45`, `-46`, `-47`, `-48`, `-49`, `-50`, `-51`, `-52`, `-53`, `-54`, `-55`

### AI7 positive types

`17`, `18`, `19`, `20`, `22`, `37`, `38`, `46`, `54`, `107`, `108`, `124`, `142`, `148`, `149`, `160`, `178`, `207`, `208`, `209`, `227`, `228`, `229`, `230`, `299`, `300`, `303`, `337`, `353`, `361`, `362`, `364`, `366`, `367`, `368`, `369`, `441`, `443`, `445`, `447`, `453`, `538`, `539`, `540`, `550`, `588`, `593`, `602`, `608`, `610`, `614`, `616`, `617`, `625`, `633`, `637`, `638`, `639`, `640`, `641`, `642`, `643`, `644`, `645`, `646`, `647`, `648`, `649`, `650`, `651`, `652`, `656`, `663`, `670`, `678`, `679`, `680`, `681`, `682`, `683`, `684`, `687`

## Definition progress against the starting `main`

| Source family | Positive definitions before → after | Signed defaults before → after |
| --- | ---: | ---: |
| AI3 | 128 → 191 | 0 → 27 |
| AI7 | 39 → 82 | 0 → 0 |

This promotes **106 positive definitions and 27 signed variants** from classic metadata to source-backed creation defaults. Global queryable coverage stays 691 positive / 65 signed; creation-default coverage becomes **501 positive / 63 signed**. The remaining **192 metadata-only identities** (190 positive / 2 signed) keep their lifecycle fences. Existing explicit behavior admission is preserved.

The newly materialized positive IDs are:

- AI3: `44`, `104`, `140`, `181`, `185`, `196`, `197`, `198`, `201`, `202`, `203`, `236`, `379`, `380`, `381`, `382`, `383`, `385`, `386`, `389`, `391`, `409`, `411`, `415`, `419`, `424`, `425`, `426`, `427`, `428`, `429`, `449`, `450`, `451`, `452`, `464`, `470`, `471`, `480`, `481`, `482`, `489`, `508`, `518`, `520`, `524`, `525`, `526`, `527`, `528`, `529`, `530`, `532`, `534`, `536`, `580`, `582`, `586`, `590`, `631`, `632`, `635`, `691`.
- AI7: `37`, `148`, `149`, `230`, `299`, `300`, `303`, `337`, `361`, `362`, `364`, `366`, `367`, `368`, `443`, `445`, `447`, `453`, `538`, `539`, `540`, `593`, `602`, `608`, `610`, `616`, `617`, `625`, `639`, `640`, `641`, `642`, `643`, `644`, `645`, `646`, `647`, `648`, `649`, `650`, `651`, `652`, `687`.

## Runtime ownership

Definitions feed the existing `RuntimeNpcStore` creation paths and `RuntimeNpcStateOwnershipPolicy`. The world supplies the existing spawn context and shared RNG. Source dimensions are materialized before later visual-scale changes; signed variants preserve their own integer rounding and source type. AI7 includes residents, critters and special actors; membership alone does not grant resident housing, commerce or combat.

| Definition state | Identities | Creation/defaults | Vanilla runtime lifecycle |
| --- | ---: | --- | --- |
| Existing explicit admission | 431 | supported | existing bounded slices |
| `CreationDefaultsOnly` | 133 | supported | fenced pending independent behavior evidence |
| `DefinitionOnly` | 192 | classic metadata/manual retention | fenced |

`SupportsRuntimeLifecycle` requires both markers to be false. AI/prepasses, status, targeting, combat, death/loot and role/interaction boundaries use that explicit gate. This preserves the previous 431 registrations, including the narrow Gnome contact/danger definition; it does not derive lifecycle from AI style or family metadata. Newly created actors can be retained and projected with correct defaults while their unproved lifecycle stays disabled. Explicit registered custom behavior retains its separate owner.


`NpcSimulationState.StairFall` is nullable: omission in an update retains committed history, fresh slot creation owns `false`, and source `SetDefaults`/Transform preserves history. The existing revision/snapshot comparisons include this state. `VanillaNpcDefinition.SourceNetId` retains signed creation identity independently of the positive type. Host arithmetic follows separately captured original Windows and Linux defaults; their integer truncation is not assumed identical.

## Shared staircase descent

The previous outer motion pass used platform descent as the slope-descent input and discarded the collision result's staircase state. Official `NPC.Collision_MoveSlopesAndStairFall` retains `stairFall` across updates and treats ordinary platform collision separately from slope collision. AI7 further applies its home, day/eclipse and head-tile rules. The shared motion boundary now owns this retained state for the admitted AI3/AI7 motion paths, through the existing collision implementation and NPC lifecycle.

This is a bounded shared-motion correction. It does not prove every type-specific AI branch, all outer-update ordering or graphical official-client parity. See the [NPC roadmap](../roadmap/npc-ai-parity.md).

## Evidence and remaining boundaries

The retained numeric fixtures contain 153,600 independently executed `SetDefaults` contexts per platform: 300 identities × 32 Hardmode/Good/Tenth/Remix/Plantera combinations × two player counts × eight effective strengths from `0.5` to `4`. Original Windows x86 Framework and Linux-assembly/CoreCLR captures each repeated byte-for-byte; the grouped test compares all 4,300,800 retained context fields. Runtime tests exercise 600 classic births and 4,608 contextual births before publication. Staircase evidence contains 2,560 original states plus actual two-update AI3/AI7 movement. Omission controls detect missing Tenth geometry, signed Good adjustments and the previous staircase behavior.

The defaults oracle must initialize the official `Main.npcCatchable` table before calling `SetDefaults`: its tail marks catchable NPCs friendly before `ScaleStats`. An earlier capture with the constructor's empty table was rejected after the independent NewNPC fixtures exposed the setup error. Final family captures use the original initialization facts, independently of TerraRuntime's catch catalog; this does not prove capture/release or the temporary catch-immunity lifecycle.

The existing named `VanillaNpcNetVariantCatalog` still contains 36 entries; the additional 27 variants resolve through the ground definition owner and `SourceNetId`. The 63 supported signed defaults therefore measure real definition/creation support, not the named-catalog `Count`. Unrepresented source fields such as complete `npcSlots`, trap/lava/cold flags and visual state, unsupported natural-spawn/attack/transform/rescue/capture/loot paths, wet/special AI7 scheduling, conveyor/graphics offsets and complete outer-update ordering remain separate gaps. Definition support is bounded to the retained runtime contract; it is not an assertion that every field in vanilla `NPC` is represented.

## Local validation

Release rebuild passes with zero warnings/errors. Grouped checks pass **42,151/42,151**. Mandatory fast passes **25,000/25,001**; the single unfiltered full run passes **1,563,293/1,563,294** in $785.281\,\mathrm{s}$. Both retain the same pre-existing canonical-liquid skip, with zero failures or runner errors. All 4,995 discovered methods participate in the full run.

Fresh Windows NativeAOT publication and all five server smoke paths pass. The exact current component sources and corrected numeric fixtures also pass **11 typed invocations / 5,093,946 assertions** in managed and fresh Windows NativeAOT execution. Documentation, project-reference, domain-literal and CI-tool checks pass. The only post-full source edit removes one empty EOF line from the staircase test; an exact byte bridge and a fresh component run preserve the evidence. No shipping code changed after the full run.

Genuine Linux NativeAOT publish/smoke remains unverified because this Windows host has no Linux/WSL distribution. No GitHub CI or graphical official-client acceptance is claimed. Numeric execution of the original Linux assembly on Windows is source evidence, not Linux NativeAOT acceptance.
