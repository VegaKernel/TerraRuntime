# Ankh-line equipment immunity — Terraria 1.4.5.8

The isolated equipment candidate adds functional immunity for sixteen accessories through the existing effective-equipment selection, retained player combat snapshot and authoritative 44-slot buff state. It adds no inventory, timer or RNG owner.

| Items | Buff immunity |
|---|---|
|885 /886 /887 /888 /889 /890 /891 /892 /893|Bleeding /Broken Armor /Poisoned /Darkness /Slow /Silenced /Cursed /Weak /Confused|
|3781|Stoned|
|901 /902 /903 /904 /5354|Weak+Broken Armor /Bleeding+Poisoned /Slow+Confused /Silenced+Cursed /Darkness+Stoned|
|1612 Ankh Charm|All ten above|

Functional slots, inherited favorite equipment, slot usability and existing accessory prefixes use the common selector. A packet 5 equipment report updates its existing inventory/profile owner; accepted Player.Update projection owns the derived immunity. Reports do not immediately replace previously accepted combat fields. Wearing an accessory consumes no quantity and creates no new weapon, ammunition or resource entitlement.

The NPC contact caller retains its source proc RNG before checking immunity. Immune offers do not refresh or insert a buff. Existing equipment-prefix damage, defense, crit, mana and attack-speed fields keep their source ordering. Unknown immunity bits fail validation.

The first sixteen-accessory component qualified late removal of already-present Poisoned `20` and Cursed `23`. The [six current-debuff continuations](scalar-debuff-player-1458.md) add Darkness, Bleeding, Slow, Weak, Silenced and Broken Armor to this ordinary phase. UpdateBuffs and regeneration already observed the frame's effects before equipment immunity removes a timer; current effects survive until the next update. Forward DelBuff compaction deliberately permits an adjacent immune slot to survive one more update; slot `43` retains the original special behavior. The same buff slots carry food/contact provenance; no parallel duration ledger exists.

Active Confused `31` and Stoned `156` retain whole-phase fences. In particular, source Confused input reads a previous-frame flag, while Stoned needs additional hurt/fall/control effects. The six represented continuations have explicit ordinary-world/caller boundaries; this is not complete player simulation. Ankh Shield `1613` still has its separate component scope because its fireWalk writer is unrepresented by the ordinary phase. Hand Warmer `1921` and Shimmer Cloak `5355` remain outside this candidate.

Original evidence executes initialized unmodified ResetEffects, UpdateEquips, AddBuff, StatusFromNPC and Player.Update. The new immutable Linux-managed-on-Windows-CoreCLR fixture has 435 observation rows, repeated byte-identically across three seeds. Whole-update cure rows explicitly use the remote 255 caller role and compare the buff/effect projection, without claiming unknown wire regeneration clocks or complete player-state parity. Original game bodies are not included.

The first component's 454 focused cases pass. Its unchanged source-backed 387-case test DLL gives 384 assertion failures and 3 no-gear control passes on old production; the same DLL passes all 387 on fixed production. A cleanup-only private Application omission causes 30 assertion failures with 21 unaffected controls on that component's exact final test DLL. Nineteen neighboring tests also passed. The six-effect follow-up preserves all 454 and records its larger checks separately. Aggregate fast/full/Native acceptance and publication are pending. No GUI-client or Linux Native result is implied.
