# Town NPC arrival identity

TerraRuntime now materializes the persistent identity that TerrariaServer 1.4.5.8 assigns during `NPC.NewNPC` / `GiveTownUniqueDataToNPCsThatNeedIt`, instead of creating new residents with an empty name and an unset variation.

For ordinary legacy/transformable Town NPC profiles, vanilla first assigns `getNewNPCName(type)` and then the profile calls `getNewNPCName(type)` again while resolving its variant. TerraRuntime preserves both `WorldGen.genRand` consumptions and stores the second name. Cat, Dog, and Bunny preserve their different ordering: the default-pet name roll is consumed first, `Main.rand` chooses one of the six profile variants, and `WorldGen.genRand` then chooses the name from that variant-specific category. A persisted shimmer flag overrides `townNpcVariationIndex` to `1` only after those rolls, without rerolling the name.

Generated given names use the official Terraria 1.4.5.8 en-US `Town.json` category values in source order. This is deliberately scoped to the server-side given-name locale currently owned by TerraRuntime; there is not yet a runtime server-language setting. The arrival announcement itself does not bake English text into the packet.

After a successful move-in, the generated `GivenName` and `townNpcVariationIndex` are committed into `RuntimeTownNpcStateStore`, included in subsequent `.wld` snapshots, and published through packet 56 before the home-state packet. `WorldGen.SpawnTownNPC`'s arrival announcement is then emitted as packet 82 / `NetTextModule` with server author `255`, `ChatColors.NPCTravel` `(50,125,255)`, and the nested localization tree `Announcement.HasArrived(Game.NPCTitle(literal given name, NPCName.*))`. Nameless residents such as Santa use the localized NPC type directly, matching `NPC.GetFullNetName()`.

The pinned source contract covers the `getNewNPCName` categories, profile RNG order, pet variant order, shimmer override, `GetFullNetName`, final `SpawnTownNPC` announcement ordering, and server chat author semantics.
