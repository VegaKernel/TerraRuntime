"""Complete bounded Dryad-animation observations; independent of the AI3 schema."""
from __future__ import annotations

import hashlib
from pathlib import Path
import re
import struct

from .capture import parse_json
from .npc_trace import _keys, _int, _sha, _float, _vector, _rng, _first_difference

SCENARIO = 'ai007-dryad-stardew-v1'
OFFICIAL_ENTRY = 'Terraria.NPC.UpdateNPC/AI_007_TownEntities/FindFrame'
RUNTIME_ENTRY = 'TerraRuntime.Application.ServerRuntimeState.Tick/RuntimeTownNpcSchedule1458'
RUNTIME_DLL = 'TerraRuntime.Application.dll'
SCOPE = ('One explicit Dryad state24 continuation through480 animation frames and16 idle frames; '
         'actual original NPC.UpdateNPC and runtime town phase, represented motion/combat/frame state, '
         'emote events and full physical Main.rand. No animation initiation, general AI7, '
         'magic combat, multiplayer traffic, natural world, Native or Linux OS parity claim.')
MAX_BYTES = 8 * 1024 * 1024
FRAME_COUNT = 496
TOP = {'schemaVersion', 'kind', 'engine', 'scenario', 'seed', 'context', 'input', 'frames', 'provenance', 'executedDlls'}
CONTEXT = dict(frames=496, widthTiles=100, heightTiles=80, worldSurface=20, floorY=30, floorType=1,
               netMode=2, dedServ=True, gameMenu=False, dayTime=True, hardMode=False, expert=False, master=False,
               players='all256inactive', peers='oneDryad', home={'x': 40, 'y': 30, 'homeless': False},
               carrier='actual NPC.UpdateNPC versus actual ServerRuntimeState.Tick; no player/clock; no animation initiation')
VECTORS = {'position': 2, 'oldPosition': 2, 'velocity': 2, 'oldVelocity': 2, 'ai': 4, 'localAi': 4}
FLOATS = {'scale', 'rotation', 'knockBackResist'}
INTS = {'type', 'netId', 'direction', 'directionY', 'spriteDirection', 'target', 'life', 'lifeMax', 'timeLeft',
        'defense', 'damage', 'frameIndex', 'homeX', 'homeY', 'breath', 'lifeRegenCounter', 'friendlyRegenerationCounter'}
BOOLS = {'homeless', 'justHit', 'collideX', 'collideY', 'wet', 'noGravity', 'noTileCollide'}


def _double(value):
    if not isinstance(value, str) or re.fullmatch('[0-9a-f]{16}', value) is None:
        raise ValueError('Expected canonical sixteen-hex binary64 frame clock')
    if int(value, 16) & 0x7ff0000000000000 == 0x7ff0000000000000:
        raise ValueError('Nonfinite frame clock')
    return struct.unpack('>d', bytes.fromhex(value))[0]


def _state(value):
    _keys(value, {'npc', 'rng'}, 'town NPC/RNG state')
    npc = value['npc']
    _keys(npc, set(VECTORS) | FLOATS | INTS | BOOLS | {'frameCounter'}, 'town NPC state projection')
    for name, count in VECTORS.items(): _vector(npc[name], count)
    for name in FLOATS: _float(npc[name])
    for name in INTS: _int(npc[name], -0x80000000, 0x7fffffff, name)
    for name in BOOLS:
        if type(npc[name]) is not bool: raise ValueError('Expected Boolean ' + name)
    if (npc['type'], npc['netId']) != (20, 20): raise ValueError('Only actual Dryad identity is represented')
    for name in ('direction', 'directionY', 'spriteDirection'): _int(npc[name], -1, 1, name)
    _int(npc['target'], 0, 255, 'target')
    _int(npc['lifeMax'], 1, 2_000_000, 'maximum life')
    _int(npc['life'], 0, npc['lifeMax'], 'life')
    _int(npc['timeLeft'], 0, 1_000_000, 'lifetime')
    _int(npc['frameIndex'], 0, 64, 'frame index')
    _int(npc['friendlyRegenerationCounter'], 0, 180, 'friendly regeneration')
    _int(npc['breath'], -1000, 200, 'breath')
    if _float(npc['scale']) <= 0 or _float(npc['knockBackResist']) < 0 or _double(npc['frameCounter']) < 0:
        raise ValueError('Invalid physical scale/knockback/frame clock')
    _rng(value['rng'])


def validate(data, engine):
    _keys(data, TOP, 'town observation envelope')
    if type(data['schemaVersion']) is not int or data['schemaVersion'] != 1:
        raise ValueError('Unsupported town schema')
    if data['kind'] != 'npc-behavior-observation' or data['engine'] != engine or engine not in ('official', 'runtime'):
        raise ValueError('Expected official/runtime NPC role')
    if data['scenario'] != SCENARIO: raise ValueError('Unsupported town scenario')
    _int(data['seed'], -0x80000000, 0x7fffffff, 'seed')
    # Exact fixture context, including JSON types (bool must not stand in for int).
    _keys(data['context'], set(CONTEXT), 'town fixture context')
    for key, expected in CONTEXT.items():
        value = data['context'][key]
        if type(value) is not type(expected) or value != expected: raise ValueError('Unsupported context ' + key)
    _keys(data['context']['home'], {'x', 'y', 'homeless'}, 'home context')
    for key, expected in CONTEXT['home'].items():
        if type(data['context']['home'][key]) is not type(expected): raise ValueError('Invalid home context type')
    _state(data['input'])
    if not isinstance(data['frames'], list) or len(data['frames']) != FRAME_COUNT:
        raise ValueError('All496 sequential frames required')
    previous = data['input']; next_emote = 0
    for index, frame in enumerate(data['frames']):
        _keys(frame, {'index', 'before', 'after', 'emotes'}, 'town frame')
        if type(frame['index']) is not int or frame['index'] != index: raise ValueError('Missing/reordered frame')
        _state(frame['before']); _state(frame['after'])
        if frame['before'] != previous: raise ValueError('Frame preimage breaks retained state continuity')
        events = frame['emotes']
        if not isinstance(events, list) or len(events) > 4: raise ValueError('Invalid emote event count')
        for event in events:
            _keys(event, {'id', 'slot', 'lifetime', 'emote'}, 'emote observation')
            for name in event: _int(event[name], 0, 65535, 'emote ' + name)
            if event['id'] != next_emote or event['slot'] != 0 or event['lifetime'] == 0 or event['emote'] > 255:
                raise ValueError('Invalid emitted emote identity/value')
            next_emote += 1
        previous = frame['after']
    provenance = data['provenance']
    expected = {'entry', 'control', 'officialAssemblySha256', 'dependenciesSha256', 'probeSha256'}
    if engine == 'runtime': expected.add('runtimeAssemblySha256')
    _keys(provenance, expected, 'town provenance')
    if provenance['entry'] != (OFFICIAL_ENTRY if engine == 'official' else RUNTIME_ENTRY): raise ValueError('Wrong executed town entry')
    if provenance['control'] not in ('normal', 'omit-step', 'extra-rng') or engine == 'official' and provenance['control'] != 'normal':
        raise ValueError('Unknown control')
    for key in expected:
        if key.endswith('Sha256') and key != 'dependenciesSha256' and not _sha(provenance[key]): raise ValueError('Invalid provenance hash')
    for closure in (provenance['dependenciesSha256'], data['executedDlls']):
        if not isinstance(closure, dict) or not 1 <= len(closure) <= 256: raise ValueError('Invalid DLL closure')
        for name, digest in closure.items():
            if not isinstance(name, str) or len(name) > 256 or ':' in name or '\\' in name or any(p in ('', '.', '..') for p in name.split('/')) or not _sha(digest):
                raise ValueError('Unsafe DLL identity/hash')
    if 'NpcProbe.dll' not in data['executedDlls'] or RUNTIME_DLL not in data['executedDlls']: raise ValueError('Missing executed town probe/runtime')


def load_with_hash(path, engine):
    path = Path(path)
    if path.stat().st_size > MAX_BYTES: raise ValueError('Town observation exceeds size bound')
    with path.open('rb') as stream: raw = stream.read(MAX_BYTES + 1)
    if len(raw) > MAX_BYTES: raise ValueError('Town observation exceeds size bound')
    data = parse_json(raw.decode('utf-8')); validate(data, engine)
    return data, hashlib.sha256(raw).hexdigest()


def compare(official, runtime):
    validate(official, 'official'); validate(runtime, 'runtime')
    for name in ('scenario', 'seed', 'context', 'input'):
        if official[name] != runtime[name]:
            return dict(accepted=False, classification='incomparable-input', firstDifference=name, scenario=SCENARIO, scope=SCOPE)
    for index, (a, b) in enumerate(zip(official['frames'], runtime['frames'])):
        for field in ('after', 'emotes'):
            if field == 'emotes' and len(a[field]) != len(b[field]):
                difference = (f'frames[{index}].emotes.count', len(a[field]), len(b[field]))
            else: difference = _first_difference(a[field], b[field], f'frames[{index}].{field}')
            if difference:
                path, expected, actual = difference
                classification = 'rng-state-mismatch' if '.rng.' in path else 'emote-mismatch' if '.emotes' in path else 'npc-state-mismatch'
                return dict(accepted=False, classification=classification, firstDifference=path, expected=expected, actual=actual, frame=index, scenario=SCENARIO, scope=SCOPE)
    return dict(accepted=True, classification='match', comparedFrames=FRAME_COUNT, scenario=SCENARIO, scope=SCOPE)
