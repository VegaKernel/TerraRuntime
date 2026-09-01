from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding='utf-8-sig')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected one patch anchor, found {count}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')


# Packet 56 is source-named UniqueTownNPCInfoSyncRequest even though the server also uses it as the response.
replace_once(
    'src/TerraRuntime.Protocol/TerrariaMessageId.cs',
    '    LiquidSet = 48,\n    PlayerSpawnSelf = 49,\n    SetNpcTalk = 40,\n    UpdateNpcHome = 60,',
    '    LiquidSet = 48,\n    PlayerSpawnSelf = 49,\n    SetNpcTalk = 40,\n    UniqueTownNpcInfoSyncRequest = 56,\n    UpdateNpcHome = 60,')

# Persistent roster owns both WorldFile.SaveNPCs shimmer type flags and townNpcVariationIndex.
replace_once(
    'src/TerraRuntime/RuntimeTownNpcStateStore.cs',
    '''internal readonly record struct RuntimeTownNpcHomeCommit(\n    short NpcSlot,\n    NpcTypeId NpcType,\n    int HomeTileX,\n    int HomeTileY,\n    TerrariaNpcHomeStatus Status)\n{\n    public TerrariaNpcHomeState ToWireState() => new(\n        NpcSlot,\n        checked((short)HomeTileX),\n        checked((short)HomeTileY),\n        (byte)Status);\n}\n''',
    '''internal readonly record struct RuntimeTownNpcHomeCommit(\n    short NpcSlot,\n    NpcTypeId NpcType,\n    int HomeTileX,\n    int HomeTileY,\n    TerrariaNpcHomeStatus Status)\n{\n    public TerrariaNpcHomeState ToWireState() => new(\n        NpcSlot,\n        checked((short)HomeTileX),\n        checked((short)HomeTileY),\n        (byte)Status);\n}\n\ninternal readonly record struct RuntimeTownNpcIdentityCommit(\n    short NpcSlot,\n    string GivenName,\n    int VariationIndex)\n{\n    public TerrariaTownNpcIdentityState ToWireState() => new(NpcSlot, GivenName, VariationIndex);\n}\n''')
replace_once(
    'src/TerraRuntime/RuntimeTownNpcStateStore.cs',
    '    private readonly int[] shimmeredTownNpcIndices;\n',
    '    private readonly SortedSet<int> shimmeredTownNpcTypes;\n')
replace_once(
    'src/TerraRuntime/RuntimeTownNpcStateStore.cs',
    '        shimmeredTownNpcIndices = source.ShimmeredTownNpcIndices.ToArray();\n',
    '        shimmeredTownNpcTypes = new SortedSet<int>(source.ShimmeredTownNpcIndices);\n')
replace_once(
    'src/TerraRuntime/RuntimeTownNpcStateStore.cs',
    '''    public bool TryUpdatePosition(short slot, in NpcSnapshot snapshot)\n    {\n        if (!townNpcsBySlot.TryGetValue(slot, out WorldTownNpc? npc))\n            return false;\n        townNpcsBySlot[slot] = npc with { X = snapshot.PositionX, Y = snapshot.PositionY };\n        return true;\n    }\n\n    public WorldNpcPersistence CaptureNpcPersistence() => new(\n        shimmeredTownNpcIndices.ToArray(),\n        townNpcsBySlot.Values.ToArray(),\n        persistentNpcs.ToArray());\n''',
    '''    public bool TryUpdatePosition(short slot, in NpcSnapshot snapshot)\n    {\n        if (!townNpcsBySlot.TryGetValue(slot, out WorldTownNpc? npc))\n            return false;\n        townNpcsBySlot[slot] = npc with { X = snapshot.PositionX, Y = snapshot.PositionY };\n        return true;\n    }\n\n    public bool TryToggleShimmerVariation(\n        short slot,\n        NpcTypeId type,\n        in NpcSnapshot snapshot,\n        out RuntimeTownNpcIdentityCommit commit)\n    {\n        if (!townNpcsBySlot.TryGetValue(slot, out WorldTownNpc? npc) ||\n            npc.NetId != type.Value ||\n            snapshot.Handle.Slot != slot ||\n            snapshot.Type != type.Value ||\n            !VanillaTownNpcShimmerCatalog1458.CanTogglePersistentTownVariant(type))\n        {\n            commit = default;\n            return false;\n        }\n\n        int current = npc.TownNpcVariationIndex ?? 0;\n        int next = current == 1 ? 0 : 1;\n        townNpcsBySlot[slot] = npc with\n        {\n            X = snapshot.PositionX,\n            Y = snapshot.PositionY,\n            TownNpcVariationIndex = next\n        };\n        if (next == 1)\n            shimmeredTownNpcTypes.Add(type.Value);\n        else\n            shimmeredTownNpcTypes.Remove(type.Value);\n\n        commit = new RuntimeTownNpcIdentityCommit(slot, npc.GivenName, next);\n        return true;\n    }\n\n    public WorldNpcPersistence CaptureNpcPersistence() => new(\n        shimmeredTownNpcTypes.ToArray(),\n        townNpcsBySlot.Values.ToArray(),\n        persistentNpcs.ToArray());\n''')
replace_once(
    'src/TerraRuntime/RuntimeTownNpcStateStore.cs',
    '''    public RuntimeTownNpcHomeCommit[] CaptureHomeBaselines()\n    {\n        var result = new RuntimeTownNpcHomeCommit[townNpcsBySlot.Count];\n        int count = CopyHomeBaselines(result);\n        return count == result.Length ? result : result.AsSpan(0, count).ToArray();\n    }\n''',
    '''    public RuntimeTownNpcHomeCommit[] CaptureHomeBaselines()\n    {\n        var result = new RuntimeTownNpcHomeCommit[townNpcsBySlot.Count];\n        int count = CopyHomeBaselines(result);\n        return count == result.Length ? result : result.AsSpan(0, count).ToArray();\n    }\n\n    public RuntimeTownNpcIdentityCommit[] CaptureIdentityBaselines()\n    {\n        var result = new RuntimeTownNpcIdentityCommit[townNpcsBySlot.Count];\n        int index = 0;\n        foreach ((short slot, WorldTownNpc npc) in townNpcsBySlot)\n            result[index++] = new RuntimeTownNpcIdentityCommit(slot, npc.GivenName, npc.TownNpcVariationIndex ?? 0);\n        return result;\n    }\n''')

# Replicate packet 56 live and as a reconnect baseline after packet 23 slot materialization.
replace_once(
    'src/TerraRuntime/RuntimeNpcReplicationRegistry.cs',
    '    private readonly byte[]?[] townHomeBaselineFrames = new byte[RuntimeTownNpcStateStore.MaximumTownNpcs][];\n',
    '    private readonly byte[]?[] townHomeBaselineFrames = new byte[RuntimeTownNpcStateStore.MaximumTownNpcs][];\n    private readonly byte[]?[] townIdentityBaselineFrames = new byte[RuntimeTownNpcStateStore.MaximumTownNpcs][];\n')
replace_once(
    'src/TerraRuntime/RuntimeNpcReplicationRegistry.cs',
    '''    public bool TryPublishNpcTalk(ConnectionHandle connection, short npcSlot)\n    {\n''',
    '''    public void ConfigureTownIdentityBaselines(ReadOnlySpan<RuntimeTownNpcIdentityCommit> identities)\n    {\n        Array.Clear(townIdentityBaselineFrames, 0, townIdentityBaselineFrames.Length);\n        foreach (RuntimeTownNpcIdentityCommit identity in identities)\n        {\n            if ((uint)identity.NpcSlot >= (uint)townIdentityBaselineFrames.Length)\n                continue;\n            TerrariaTownNpcIdentityState state = identity.ToWireState();\n            if (TerrariaTownNpcIdentityCodec.TryEncode(in state, out byte[] encoded) != TerrariaTownNpcIdentityEncodeResult.Encoded)\n                continue;\n            Volatile.Write(ref townIdentityBaselineFrames[identity.NpcSlot], encoded);\n        }\n    }\n\n    public bool TryPublishTownIdentity(in RuntimeTownNpcIdentityCommit identity)\n    {\n        if ((uint)identity.NpcSlot >= (uint)townIdentityBaselineFrames.Length)\n            return false;\n        TerrariaTownNpcIdentityState state = identity.ToWireState();\n        if (TerrariaTownNpcIdentityCodec.TryEncode(in state, out byte[] encoded) != TerrariaTownNpcIdentityEncodeResult.Encoded)\n            return false;\n        Volatile.Write(ref townIdentityBaselineFrames[identity.NpcSlot], encoded);\n        Broadcast(encoded);\n        return true;\n    }\n\n    public bool TryPublishNpcTalk(ConnectionHandle connection, short npcSlot)\n    {\n''')
replace_once(
    'src/TerraRuntime/RuntimeNpcReplicationRegistry.cs',
    '''        for (int slot = 0; slot < townHomeBaselineFrames.Length; slot++)\n        {\n            byte[]? encoded = Volatile.Read(ref townHomeBaselineFrames[slot]);\n            if (encoded is null)\n                continue;\n\n            if (endpoint.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)\n                Interlocked.Increment(ref baselineFrameCount);\n            else\n                Interlocked.Increment(ref rejectedFrames);\n        }\n''',
    '''        for (int slot = 0; slot < townIdentityBaselineFrames.Length; slot++)\n        {\n            byte[]? encoded = Volatile.Read(ref townIdentityBaselineFrames[slot]);\n            if (encoded is null)\n                continue;\n\n            if (endpoint.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)\n                Interlocked.Increment(ref baselineFrameCount);\n            else\n                Interlocked.Increment(ref rejectedFrames);\n        }\n\n        for (int slot = 0; slot < townHomeBaselineFrames.Length; slot++)\n        {\n            byte[]? encoded = Volatile.Read(ref townHomeBaselineFrames[slot]);\n            if (encoded is null)\n                continue;\n\n            if (endpoint.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)\n                Interlocked.Increment(ref baselineFrameCount);\n            else\n                Interlocked.Increment(ref rejectedFrames);\n        }\n''')

# Bootstrap reconnect baselines from loaded .wld town names/variations.
replace_once(
    'src/TerraRuntime/TerrariaServerHost.cs',
    '        npcReplication.ConfigureTownHomeBaselines(townNpcStore.CaptureHomeBaselines());\n',
    '        npcReplication.ConfigureTownHomeBaselines(townNpcStore.CaptureHomeBaselines());\n        npcReplication.ConfigureTownIdentityBaselines(townNpcStore.CaptureIdentityBaselines());\n')

# Wire shimmer lifecycle into the authoritative tick immediately after world motion has refreshed liquid contact.
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '    private readonly RuntimeTownNpcSchedule1458? _townSchedule;\n',
    '    private readonly RuntimeTownNpcSchedule1458? _townSchedule;\n    private readonly RuntimeTownNpcShimmerService1458? _townShimmer;\n')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        if (worldTiles is not null && townNpcs is not null && _housingValidator is not null)\n        {\n            _townSchedule = new RuntimeTownNpcSchedule1458(townNpcs, _npcs, worldTiles);\n''',
    '''        if (worldTiles is not null && townNpcs is not null && _housingValidator is not null)\n        {\n            _townSchedule = new RuntimeTownNpcSchedule1458(townNpcs, _npcs, worldTiles);\n            _townShimmer = new RuntimeTownNpcShimmerService1458(_npcs, townNpcs, worldTiles, npcReplication);\n''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        LastNpcAiTick = _npcAiExecutor.Tick(_npcAiStepper);\n        TickTownNpcLifecycle();\n''',
    '''        LastNpcAiTick = _npcAiExecutor.Tick(_npcAiStepper);\n        _townShimmer?.Tick();\n        TickTownNpcLifecycle();\n''')

# Keep Core docs project-independent.
p = Path('src/TerraRuntime.Core/Npcs/VanillaTownNpcShimmerCatalog1458.cs')
text = p.read_text()
text = text.replace(' because\n/// <see cref="RuntimeTownNpcStateStore"/> owns the persistent .wld town roster rather than transient NPC lifecycle.',
                    '; the runtime persistent-town roster owns the .wld lifecycle instead.')
p.write_text(text)

print('N4 town shimmer integration patches applied')
