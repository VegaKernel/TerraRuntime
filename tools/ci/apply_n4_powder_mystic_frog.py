from pathlib import Path


def replace_once(path: str, old: str, new: str):
    p = Path(path)
    text = p.read_text()
    if old not in text:
        raise SystemExit(f'anchor not found in {path}: {old[:100]!r}')
    if text.count(old) != 1:
        raise SystemExit(f'anchor not unique in {path}: {text.count(old)}')
    p.write_text(text.replace(old, new, 1))

p = 'src/TerraRuntime.World/WorldFileProgressionHeaderPatcher.cs'
replace_once(p,
'''        bool needsTownState = mutations.UnlockSlimeBlueSpawn ||
            mutations.UnlockTruffleSpawn ||
            mutations.RescuedTownNpcs != RuntimeTownRescueFacts1458.None;
''',
'''        bool needsTownState = mutations.UnlockSlimeBlueSpawn ||
            mutations.UnlockTruffleSpawn ||
            mutations.UnlockSlimeYellowSpawn ||
            mutations.RescuedTownNpcs != RuntimeTownRescueFacts1458.None;
''')
replace_once(p,
'''        if (mutations.UnlockTruffleSpawn && !townState.PersistedTruffle)
            patchedHeader[townState.TruffleOffset] = 1;
        PatchTownRescueFact''',
'''        if (mutations.UnlockTruffleSpawn && !townState.PersistedTruffle)
            patchedHeader[townState.TruffleOffset] = 1;
        if (mutations.UnlockSlimeYellowSpawn && !townState.PersistedSlimeYellow)
            patchedHeader[townState.SlimeYellowOffset] = 1;
        PatchTownRescueFact''')
replace_once(p,
'''        int SlimeBlueOffset,
        bool PersistedSlimeBlue,
        int TruffleOffset,
        bool PersistedTruffle);
''',
'''        int SlimeBlueOffset,
        bool PersistedSlimeBlue,
        int TruffleOffset,
        bool PersistedTruffle,
        int SlimeYellowOffset,
        bool PersistedSlimeYellow);
''')
replace_once(p,
'''        int truffleOffset = reader.Offset;
        if (!reader.TryReadBool(out bool truffle)) return false;

        state = new TownStateOffsets1458(
''',
'''        int truffleOffset = reader.Offset;
        if (!reader.TryReadBool(out bool truffle)) return false;
        // arms dealer, nurse, princess, combat book II, peddler satchel, green/old/purple/rainbow/red slimes.
        if (!reader.TrySkipBools(10)) return false;
        int slimeYellowOffset = reader.Offset;
        if (!reader.TryReadBool(out bool slimeYellow)) return false;

        state = new TownStateOffsets1458(
''')
replace_once(p,
'''            savedBartenderOffset, savedBartender,
            slimeBlueOffset, slimeBlue,
            truffleOffset, truffle);
''',
'''            savedBartenderOffset, savedBartender,
            slimeBlueOffset, slimeBlue,
            truffleOffset, truffle,
            slimeYellowOffset, slimeYellow);
''')

p = 'src/TerraRuntime/ServerRuntimeState.cs'
replace_once(p,
'''    private readonly RuntimeTownNpcRescueService1458? _townRescue;
    private readonly RuntimeTownCommerceResolver1458? _townCommerce;
''',
'''    private readonly RuntimeTownNpcRescueService1458? _townRescue;
    private readonly RuntimePurificationPowderNpcInteraction1458? _purificationPowderNpcInteractions;
    private readonly RuntimeMysticFrogCatchService1458? _mysticFrogCatch;
    private readonly RuntimeTownCommerceResolver1458? _townCommerce;
''')
replace_once(p,
'''        _townRescue = townNpcs is not null && _worldProgression is not null
            ? new RuntimeTownNpcRescueService1458(_npcs, townNpcs, _worldProgression)
            : null;
        _townCommerce = worldTiles is not null''',
'''        _townRescue = townNpcs is not null && _worldProgression is not null
            ? new RuntimeTownNpcRescueService1458(_npcs, townNpcs, _worldProgression)
            : null;
        _mysticFrogCatch = worldTiles is not null
            ? new RuntimeMysticFrogCatchService1458(_npcs, worldTiles, this)
            : null;
        _purificationPowderNpcInteractions = townNpcs is not null && _worldProgression is not null && _townRescue is not null
            ? new RuntimePurificationPowderNpcInteraction1458(
                _npcs, _projectiles, townNpcs, _townRescue, _worldProgression, townSpawnWorldFacts?.InfectedSeed ?? false)
            : null;
        _townCommerce = worldTiles is not null''')
replace_once(p,
'''                progression.SetTruffleSpawnBaseline(facts.UnlockedTruffleSpawn);
                RuntimeTownRescueFacts1458 rescuedBaseline''',
'''                progression.SetTruffleSpawnBaseline(facts.UnlockedTruffleSpawn);
                progression.SetSlimeYellowSpawnBaseline(facts.UnlockedSlimeYellowSpawn);
                RuntimeTownRescueFacts1458 rescuedBaseline''')
replace_once(p,
'''            LastProjectileTick = _projectileExecutor.Tick(_projectileStepper);
            AppliedProjectileReflections += _projectileReflections.Tick();
''',
'''            LastProjectileTick = _projectileExecutor.Tick(_projectileStepper);
            _purificationPowderNpcInteractions?.Tick();
            AppliedProjectileReflections += _projectileReflections.Tick();
''')
replace_once(p,
'''        // Terraria 1.4.5.8 Mystic Frog (687) teleports instead of becoming an item. That special transform is
        // deliberately left to its own N4 special-NPC slice; packet 70 must not incorrectly despawn it here.
        if (VanillaNpcCatchCatalog1458.IsMysticFrog(npcType))
            return;
''',
'''        if (VanillaNpcCatchCatalog1458.IsMysticFrog(npcType))
        {
            _mysticFrogCatch?.TryApply(npc.Handle, out _);
            return;
        }
''')

p = 'docs/roadmap/n4-rescue-catchability-1458.md'
replace_once(p,
'''## Explicit next boundary

This slice does not claim the Purification Powder projectile side effects. Demon Tax Collector (`534 -> 441`) and Mystic Frog powder transformation remain in the projectile-special-interaction slice. Packet-70 Mystic Frog capture is fail-closed here because Terraria teleports the frog instead of producing the ordinary caught item.
''',
'''## Projectile-special NPC interactions

The follow-up slice now owns Purification Powder (`projectile 10`) NPC side effects: Demon Tax Collector `534 -> 441` reuses the generation-safe rescue transaction and journals `savedTaxCollector`; Mystic Frog `687 -> 683` becomes the Yellow Town Slime and journals `unlockedSlimeYellowSpawn`. The powder hitbox is source-pinned at 64x64, expanding to 106x106 in infected-seed worlds.

Packet 70 now owns the Mystic Frog special path too. It searches the source-shaped 15-tile teleport range with an 8-tile player telefrag exclusion and preserves the NPC generation on a successful teleport; if no legal tile is found after the vanilla 100 attempts, the frog is authoritatively despawned without producing a captured item. Teleport/smoke visuals remain presentation-only and do not alter the authoritative gameplay transaction.
''')

print('applied N4 powder/Mystic Frog integration')
