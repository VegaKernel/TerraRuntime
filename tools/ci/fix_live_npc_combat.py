from pathlib import Path

ROOT = Path('.')


def replace_once(path: str, old: str, new: str) -> None:
    p = ROOT / path
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected one match, got {count}\n--- needle ---\n{old[:800]}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')


# Keep stackalloc storage inside the lexical lifetime of each decoding branch.
replace_once(
    'src/TerraRuntime.Protocol.Multiplicity/TerrariaNpcDamageCodec.cs',
    '''        Span<byte> scratch = stackalloc byte[PayloadLength];
        ReadOnlySpan<byte> payload;
        if (frame.Payload.IsSingleSegment)
        {
            payload = frame.Payload.FirstSpan;
        }
        else
        {
            int offset = 0;
            foreach (ReadOnlyMemory<byte> segment in frame.Payload)
            {
                segment.Span.CopyTo(scratch[offset..]);
                offset += segment.Length;
            }
            payload = scratch;
        }

        float knockBack = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(payload[4..8]));
        state = new TerrariaNpcDamageState(
            payload[0],
            payload[1],
            BinaryPrimitives.ReadInt16LittleEndian(payload[2..4]),
            knockBack,
            payload[8],
            payload[9]);
        return state.IsStructurallyValid
            ? TerrariaNpcDamageDecodeResult.Decoded
            : TerrariaNpcDamageDecodeResult.InvalidState;''',
    '''        if (frame.Payload.IsSingleSegment)
        {
            state = DecodePayload(frame.Payload.FirstSpan);
        }
        else
        {
            Span<byte> scratch = stackalloc byte[PayloadLength];
            int offset = 0;
            foreach (ReadOnlyMemory<byte> segment in frame.Payload)
            {
                segment.Span.CopyTo(scratch[offset..]);
                offset += segment.Length;
            }
            state = DecodePayload(scratch);
        }

        return state.IsStructurallyValid
            ? TerrariaNpcDamageDecodeResult.Decoded
            : TerrariaNpcDamageDecodeResult.InvalidState;''')
replace_once(
    'src/TerraRuntime.Protocol.Multiplicity/TerrariaNpcDamageCodec.cs',
    '''    public static TerrariaNpcDamageEncodeResult TryEncode(
        in TerrariaNpcDamageState state,''',
    '''    private static TerrariaNpcDamageState DecodePayload(ReadOnlySpan<byte> payload)
    {
        float knockBack = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(payload[4..8]));
        return new TerrariaNpcDamageState(
            payload[0],
            payload[1],
            BinaryPrimitives.ReadInt16LittleEndian(payload[2..4]),
            knockBack,
            payload[8],
            payload[9]);
    }

    public static TerrariaNpcDamageEncodeResult TryEncode(
        in TerrariaNpcDamageState state,''')

# The combat bridge consumes the Multiplicity packet codec explicitly.
replace_once(
    'src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs',
    '''using TerraRuntime.Protocol;
using TerraRuntime.World;''',
    '''using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;''')

# RuntimeNpcSpawnIntentApplier is deliberately Core-internal. Reuse the same public definitions/store semantics
# locally rather than widening an implementation-only Core type just to cross the assembly boundary.
replace_once(
    'src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs',
    '''            if (TryCreateNerdySlimeSpawnIntent(in kingSlime, out NpcAiSpawnIntent intent) &&
                RuntimeNpcSpawnIntentApplier.TryApply(npcs, in intent, out NpcSnapshot nerdy))''',
    '''            if (TryCreateNerdySlimeSpawnIntent(in kingSlime, out NpcAiSpawnIntent intent) &&
                TryApplySpawnIntent(in intent, out NpcSnapshot nerdy))''')
replace_once(
    'src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs',
    '''    private void ReleaseReservations(Span<WorldItemDropReservation> reservations)
    {''',
    '''    private bool TryApplySpawnIntent(in NpcAiSpawnIntent intent, out NpcSnapshot spawned)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(intent.Type, out VanillaNpcDefinition definition) ||
            !float.IsFinite(intent.VelocityX) ||
            !float.IsFinite(intent.VelocityY) ||
            !intent.InitialAi.IsFinite)
        {
            spawned = default;
            return false;
        }

        var update = new NpcStateUpdate(
            Type: intent.Type.Value,
            NetId: checked((short)intent.Type.Value),
            PositionX: intent.BottomX - definition.Width * 0.5f,
            PositionY: intent.BottomY - definition.Height,
            VelocityX: intent.VelocityX,
            VelocityY: intent.VelocityY,
            Target: intent.Target,
            Ai: intent.InitialAi,
            Simulation: NpcSimulationState.Initial with
            {
                TimeLeft = VanillaNpcSpawnFacts.NewNpcTimeLeft
            });
        return npcs.TrySpawnVanilla(in update, out spawned);
    }

    private void ReleaseReservations(Span<WorldItemDropReservation> reservations)
    {''')

# TerrariaFrameDecoder is static.
replace_once(
    'tests/TerraRuntime.Tests/TerrariaNpcDamageCodecTests.cs',
    '''        var decoder = new TerrariaFrameDecoder();
        Assert.Equal(TerrariaFrameReadResult.Frame, decoder.TryRead(ref sequence, out TerrariaFrame frame));''',
    '''        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));''')
replace_once(
    'tests/TerraRuntime.Tests/TerrariaNpcDamageCodecTests.cs',
    '''        var decoder = new TerrariaFrameDecoder();
        Assert.Equal(TerrariaFrameReadResult.Frame, decoder.TryRead(ref sequence, out TerrariaFrame frame));''',
    '''        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));''')

# The implementation now closes the explicit N3 integration gap; keep status/docs in lockstep with code.
roadmap = ROOT / 'docs/roadmap/npc-ai-parity.md'
text = roadmap.read_text(encoding='utf-8')
old = '- [ ] connect the Expert/Master difficulty-loot finalizer to live packet-28/playerInteraction combat ingress and advance leased slots from the authoritative item-update phase;'
new = '- [x] connect the Expert/Master difficulty-loot path to live packet-28/playerInteraction combat ingress and advance leased slots from the authoritative item-update phase;'
if text.count(old) != 1:
    raise RuntimeError('npc-ai-parity roadmap integration checkbox changed unexpectedly')
text = text.replace(old, new, 1)
old_para = 'The remaining integration gap is the live packet-28 combat/death ingress plus authoritative per-tick lease advancement, not the wire or rule semantics themselves.'
new_para = 'Live packet-28 combat/death ingress now records source-ordered player interaction, executes the implemented King Slime difficulty loot before death effects, and advances instanced-item leases from the authoritative item phase; packet 151 is emitted when an exact lease expires.'
if text.count(old_para) != 1:
    raise RuntimeError('npc-ai-parity King Slime status paragraph changed unexpectedly')
roadmap.write_text(text.replace(old_para, new_para, 1), encoding='utf-8')

for path, old1, new1, old2, new2 in (
    (
        'docs/en/king-slime-difficulty-loot.md',
        'When a lease reaches zero, `TerrariaWorldItemFrameEncoder.TryEncodeInstancedSlotRelease` emits the five-byte packet 151 contract carrying the released item slot. The remaining runtime integration task is to advance these leases from the authoritative item-update phase; the encoder and lease semantics themselves are concrete.',
        'When a lease reaches zero, `TerrariaWorldItemFrameEncoder.TryEncodeInstancedSlotRelease` emits the five-byte packet 151 contract carrying the released item slot. Production advances these leases once per authoritative item phase, after NPC and projectile phases, so a Boss Bag created during NPC death consumes its first lease tick in the same world update just like the source item loop.',
        'The rule semantics, packet-90/151 wire representation and leased-slot storage are now explicit. The remaining integration boundary is live packet-28/playerInteraction combat/death ingress plus authoritative lease ticking; King Slime\'s Slime Rain stop and first-kill Nerdy Slime world effects are covered by the separate committed death-progression slice.',
        'The rule semantics, packet-90/151 wire representation, leased-slot storage and live runtime boundary are now connected. Packet 28 enters the bounded gameplay ingress, records `playerInteraction` before strike resolution, executes the implemented Expert/Master loot before King Slime death effects, then relays the strike and death sync in source order. Slime Rain stop, first-kill Nerdy Slime and progression remain part of the same authoritative death transaction.'
    ),
    (
        'docs/ru/king-slime-difficulty-loot.md',
        'Когда lease достигает нуля, `TerrariaWorldItemFrameEncoder.TryEncodeInstancedSlotRelease` формирует пятибайтовый packet 151 с освобождённым item slot. Оставшаяся runtime-интеграция — тикать эти leases в авторитетной item-update phase; wire contract и lease semantics уже конкретны.',
        'Когда lease достигает нуля, `TerrariaWorldItemFrameEncoder.TryEncodeInstancedSlotRelease` формирует пятибайтовый packet 151 с освобождённым item slot. Production теперь продвигает leases один раз за авторитетную item phase после NPC и projectile phases, поэтому Boss Bag, созданный при смерти NPC, расходует первый lease tick в том же world update, как в исходном item loop.',
        'Rule semantics, packet-90/151 wire representation и leased-slot storage теперь явные. Открытой остаётся интеграция с live packet-28/playerInteraction combat/death ingress и авторитетный lease ticking. Остановка Slime Rain и first-kill Nerdy Slime world effects закрываются отдельным committed death-progression срезом.',
        'Rule semantics, packet-90/151 wire representation, leased-slot storage и live runtime boundary теперь соединены. Packet 28 входит через bounded gameplay ingress, записывает `playerInteraction` до strike resolution, выполняет реализованный Expert/Master loot до death effects King Slime, затем relay-ит strike и death sync в исходном порядке. Остановка Slime Rain, first-kill Nerdy Slime и progression остаются частью той же авторитетной death transaction.'
    )
):
    p = ROOT / path
    text = p.read_text(encoding='utf-8')
    if text.count(old1) != 1 or text.count(old2) != 1:
        raise RuntimeError(f'{path}: expected stale integration text exactly once')
    p.write_text(text.replace(old1, new1, 1).replace(old2, new2, 1), encoding='utf-8')

print('live NPC combat build/status fixes applied')
