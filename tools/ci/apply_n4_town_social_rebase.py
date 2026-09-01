#!/usr/bin/env python3
from pathlib import Path
import subprocess

SOURCE_REF = 'origin/work/n4-town-social-emotes-1458'


def read(path):
    return Path(path).read_text(encoding='utf-8-sig')


def write(path, text):
    Path(path).write_text(text, encoding='utf-8')


def replace_once(path, old, new, label):
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected one anchor, found {count}')
    write(path, text.replace(old, new, 1))


def copy_from_source(path):
    data = subprocess.check_output(['git', 'show', f'{SOURCE_REF}:{path}'])
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)

for path in [
    'src/TerraRuntime.Protocol.Multiplicity/TerrariaEmoteBubbleCodec.cs',
    'src/TerraRuntime/RuntimeTownNpcSocial1458.cs',
    'tests/TerraRuntime.Tests/RuntimeTownNpcSocial1458Tests.cs',
    'tools/ci/check_town_social_emotes_source.py',
]:
    copy_from_source(path)

replace_once(
    'src/TerraRuntime.Protocol/TerrariaMessageId.cs',
    '    LoadNetModule = 82,\n    FinishedConnectingToServer = 129,',
    '    LoadNetModule = 82,\n    EmoteBubble = 91,\n    FinishedConnectingToServer = 129,',
    'packet 91 message id')

replace_once(
    'src/TerraRuntime/RuntimeNpcReplicationRegistry.cs',
    'internal sealed class RuntimeNpcReplicationRegistry : INpcStateCommitSink, IRuntimePlayerEventSink',
    'internal sealed class RuntimeNpcReplicationRegistry : INpcStateCommitSink, IRuntimePlayerEventSink, IRuntimeTownNpcEmoteSink1458',
    'emote sink interface')
replace_once(
    'src/TerraRuntime/RuntimeNpcReplicationRegistry.cs',
    '''    public bool TryPublishTownHome(in RuntimeTownNpcHomeCommit home)\n    {''',
    '''    public bool TryPublishEmoteBubble(in TerrariaEmoteBubbleState state)\n    {\n        if (TerrariaEmoteBubbleCodec.TryEncode(in state, out byte[] encoded) != TerrariaEmoteBubbleEncodeResult.Encoded)\n        {\n            Interlocked.Increment(ref unsupportedCommits);\n            return false;\n        }\n        Broadcast(encoded);\n        return true;\n    }\n\n    public bool TryPublishTownHome(in RuntimeTownNpcHomeCommit home)\n    {''',
    'packet 91 replication')

server = 'src/TerraRuntime/ServerRuntimeState.cs'
replace_once(
    server,
    '    private readonly RuntimeTownNpcSchedule1458? _townSchedule;\n    private readonly RuntimeTownNpcCombat1458? _townCombat;',
    '    private readonly RuntimeTownNpcSchedule1458? _townSchedule;\n    private readonly RuntimeTownNpcSocial1458? _townSocial;\n    private readonly RuntimeTownNpcCombat1458? _townCombat;',
    'town social field')
replace_once(
    server,
    '            _townSchedule = new RuntimeTownNpcSchedule1458(townNpcs, _npcs, worldTiles);\n            _townShimmer = new RuntimeTownNpcShimmerService1458(_npcs, townNpcs, worldTiles, npcReplication);',
    '            _townSchedule = new RuntimeTownNpcSchedule1458(townNpcs, _npcs, worldTiles);\n            _townSocial = new RuntimeTownNpcSocial1458(townNpcs, _npcs, worldTiles, this, npcReplication, _townSchedule);\n            _townShimmer = new RuntimeTownNpcShimmerService1458(_npcs, townNpcs, worldTiles, npcReplication);',
    'town social construction')
replace_once(
    server,
    '        if (_townMoveIn is null && _townSchedule is null && _townCombat is null)\n            return;',
    '        if (_townMoveIn is null && _townSchedule is null && _townSocial is null && _townCombat is null)\n            return;',
    'town lifecycle guard')
replace_once(
    server,
    '        _townCombat?.Tick();\n    }',
    '        _townSocial?.Tick();\n        _townCombat?.Tick();\n    }',
    'town social tick')

for path, heading, body in [
    ('docs/en/town-npc-combat.md', '## AI_007 social/emote vertical', '''\n\n## AI_007 social/emote vertical\n\nTown social state is now server-owned alongside combat. The runtime covers ordinary conversation pairs (3/4), RPS pairs (16/17), passive idle states (2/11), player-facing states (6/7/18/19), and source-shaped Town Pet idle states (20..23). RPS bubbles are emitted as protocol-326 packet 91 with vanilla NPC anchor tag 0 and the source frame cadence 40/100/160. Chair state 5 remains owned by the schedule service. NPC-picked free-form conversation bubbles still depend on Terraria's broader `PickNPCEmote` content graph and are not claimed by this slice.\n'''),
    ('docs/ru/town-npc-combat.md', '## Social/emote-вертикаль AI_007', '''\n\n## Social/emote-вертикаль AI_007\n\nСоциальное состояние Town NPC теперь также принадлежит серверу. Runtime поддерживает обычные разговорные пары (3/4), RPS-пары (16/17), пассивные idle-состояния (2/11), реакции на игрока (6/7/18/19) и source-shaped idle-состояния Town Pet (20..23). RPS-пузыри отправляются настоящим packet 91 protocol-326 с vanilla NPC anchor 0 и исходным cadence на кадрах 40/100/160. Chair state 5 остаётся во владении schedule-сервиса. Свободные NPC-picked реплики через полный граф `PickNPCEmote` этим блоком пока не заявляются.\n'''),
]:
    text = read(path)
    if heading not in text:
        write(path, text.rstrip() + body)

roadmap = 'docs/roadmap/npc-ai-parity.md'
text = read(roadmap)
old = '  - source-backed AI_007 shelter/home/chair scheduling, shimmer state 25, projectile combat for Merchant/Nurse/Arms Dealer/Guide, and melee state 15 for Dye Trader/Tax Collector/Stylist are authoritative; social/emote and remaining special town branches remain open;'
new = '  - source-backed AI_007 shelter/home/chair scheduling, shimmer state 25, projectile combat for Merchant/Nurse/Arms Dealer/Guide, melee state 15 for Dye Trader/Tax Collector/Stylist, and social/emote states 2/3/4/6/7/11/16/17/18/19/20..23 are authoritative; support/magic projectile and remaining special town branches remain open;'
if new not in text:
    if text.count(old) != 1:
        raise SystemExit(f'roadmap N4 anchor missing or ambiguous: {text.count(old)}')
    write(roadmap, text.replace(old, new, 1))

print('Town social/emote block transplanted onto fresh main')
