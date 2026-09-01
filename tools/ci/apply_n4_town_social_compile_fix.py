#!/usr/bin/env python3
from pathlib import Path

codec = Path('src/TerraRuntime.Protocol.Multiplicity/TerrariaEmoteBubbleCodec.cs')
text = codec.read_text(encoding='utf-8-sig')
old = '''        Span<byte> scratch = stackalloc byte[CreatePayloadLength];
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
            payload = scratch[..checked((int)frame.Payload.Length)];
        }
'''
new = '''        byte[] payloadBytes = frame.Payload.ToArray();
        ReadOnlySpan<byte> payload = payloadBytes;
'''
if text.count(old) != 1:
    raise SystemExit(f'emote decoder span anchor missing or ambiguous: {text.count(old)}')
codec.write_text(text.replace(old, new, 1), encoding='utf-8')

social = Path('src/TerraRuntime/RuntimeTownNpcSocial1458.cs')
text = social.read_text(encoding='utf-8-sig')
old_player = '''    private bool TryGetTalkablePlayer(int slot, in NpcSnapshot source, float maximumDistance, out PlayerStateSnapshot player)
    {
        if ((uint)slot >= 255u || !players.TryGetPlayer(new PlayerSlotId(checked((byte)slot)), out player) || player.IsDead)
            return false;
'''
new_player = '''    private bool TryGetTalkablePlayer(int slot, in NpcSnapshot source, float maximumDistance, out PlayerStateSnapshot player)
    {
        player = default;
        if ((uint)slot >= 255u || !players.TryGetPlayer(new PlayerSlotId(checked((byte)slot)), out player) || player.IsDead)
            return false;
'''
if text.count(old_player) != 1:
    raise SystemExit(f'talkable-player anchor missing or ambiguous: {text.count(old_player)}')
text = text.replace(old_player, new_player, 1)
old_hitbox = '''    private static bool TryGetHitbox(in NpcSnapshot npc, out VanillaNpcHitboxSize hitbox) =>
        VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition) &&
        definition.TryResolveHitbox(npc.Simulation.Scale, out hitbox);
'''
new_hitbox = '''    private static bool TryGetHitbox(in NpcSnapshot npc, out VanillaNpcHitboxSize hitbox)
    {
        hitbox = default;
        return VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition) &&
               definition.TryResolveHitbox(npc.Simulation.Scale, out hitbox);
    }
'''
if text.count(old_hitbox) != 1:
    raise SystemExit(f'hitbox anchor missing or ambiguous: {text.count(old_hitbox)}')
text = text.replace(old_hitbox, new_hitbox, 1)
social.write_text(text, encoding='utf-8')
print('Town social compile compatibility fixes applied')
