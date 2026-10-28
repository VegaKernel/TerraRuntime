"""Strict bounded NPC AI observations; movement floats compare by their physical bits."""
from __future__ import annotations

import hashlib
from pathlib import Path
import re
import struct

from .capture import parse_json

SCENARIO = 'ai003-ice-golem-airborne-v1'
OFFICIAL_ENTRY = 'Terraria.NPC.AI_003_Fighters'
RUNTIME_ENTRY = 'TerraRuntime.Core.Npcs.RuntimeNpcAiStateExecutor.Tick/VanillaNpcTargetingAiStepper'
RUNTIME_DLL = 'TerraRuntime.Core.dll'
SCOPE = ('Sixteen type243 Ice Golem AI+explicit caller justHit-retirement frames on identical airborne input; '
         'retained motion, attack timers, target and full physical Main.rand state. '
         'No NPC.Update, collision/gravity, projectile-spawn, world lifecycle, AI7, Native or full NPC parity claim.')
MAX_BYTES = 2 * 1024 * 1024
HIT_FRAMES = [2, 5, 9, 13]
TOP = {'schemaVersion', 'kind', 'engine', 'scenario', 'seed', 'context', 'input', 'frames', 'provenance', 'executedDlls'}
CONTEXT = {'frames', 'widthTiles', 'heightTiles', 'worldSurface', 'netMode', 'dedServ', 'gameMenu', 'dayTime',
           'hardMode', 'gameMode', 'expert', 'master', 'goodWorld', 'remix', 'eclipse', 'invasionType',
           'tiles', 'player', 'carrier', 'justHitFrames'}
CARRIER = 'AI+caller justHit retirement; fixed position/oldPosition; retained velocity/AI/localAI; justHit pulses before each call'
RETIREMENT = 'NPC.Update tail justHit=false after AI; runtime retires in admitted proposal'
VECTORS = {'position': 2, 'oldPosition': 2, 'velocity': 2, 'oldVelocity': 2, 'ai': 4, 'localAi': 4}
FLOATS = {'scale', 'rotation', 'knockBackResist'}
INTS = {'type', 'netId', 'aiStyle', 'width', 'height', 'direction', 'directionY', 'spriteDirection',
        'target', 'life', 'lifeMax', 'timeLeft', 'alpha'}
BOOLS = {'active', 'justHit', 'collideX', 'collideY', 'wet', 'noGravity', 'noTileCollide'}


def _keys(value, expected, label):
    if not isinstance(value, dict) or set(value) != expected:
        raise ValueError('Incomplete/unknown ' + label)


def _int(value, low, high, label):
    if type(value) is not int or not low <= value <= high:
        raise ValueError('Invalid integer ' + label)


def _sha(value):
    return isinstance(value, str) and re.fullmatch('[0-9a-f]{64}', value) is not None


def _float(value):
    if not isinstance(value, str) or re.fullmatch('[0-9a-f]{8}', value) is None:
        raise ValueError('Expected canonical eight-hex binary32 bits')
    if int(value, 16) & 0x7f800000 == 0x7f800000:
        raise ValueError('Nonfinite NPC float')
    return struct.unpack('>f', bytes.fromhex(value))[0]


def _vector(value, size):
    if not isinstance(value, list) or len(value) != size:
        raise ValueError('Incomplete float vector')
    for item in value:
        _float(item)


def _rng(value):
    _keys(value, {'algorithm', 'owner', 'inext', 'seedArray'}, 'physical RNG state')
    if value['algorithm'] != 'terraria1458-unifiedrandom-inext21' or value['owner'] != 'Main.rand':
        raise ValueError('Unknown RNG owner/algorithm')
    _int(value['inext'], 0, 55, 'RNG cursor')
    if not isinstance(value['seedArray'], list) or len(value['seedArray']) != 56:
        raise ValueError('All 56 physical RNG slots are required')
    for item in value['seedArray']:
        _int(item, -0x80000000, 0x7fffffff, 'RNG slot')


def _state(value):
    _keys(value, {'npc', 'rng'}, 'NPC/RNG state')
    npc = value['npc']
    _keys(npc, set(VECTORS) | FLOATS | INTS | BOOLS, 'NPC state projection')
    for name, size in VECTORS.items():
        _vector(npc[name], size)
    for name in FLOATS:
        _float(npc[name])
    for name in INTS:
        _int(npc[name], -0x80000000, 0x7fffffff, name)
    for name in BOOLS:
        if type(npc[name]) is not bool:
            raise ValueError('Expected explicit Boolean NPC ' + name)
    if (npc['type'], npc['netId'], npc['aiStyle'], npc['width'], npc['height']) != (243, 243, 3, 30, 114):
        raise ValueError('Unsupported type/body AI profile')
    for name in ('direction', 'directionY', 'spriteDirection'):
        _int(npc[name], -1, 1, name)
    _int(npc['target'], 0, 255, 'target')
    _int(npc['lifeMax'], 1, 2_000_000, 'maximum life')
    _int(npc['life'], 0, npc['lifeMax'], 'life')
    _int(npc['timeLeft'], 0, 1_000_000, 'lifetime')
    _int(npc['alpha'], 0, 255, 'alpha')
    if _float(npc['scale']) <= 0 or _float(npc['knockBackResist']) < 0:
        raise ValueError('Invalid scale/knockback')
    _rng(value['rng'])


def validate(data: dict, engine: str) -> None:
    _keys(data, TOP, 'NPC observation envelope')
    if type(data['schemaVersion']) is not int or data['schemaVersion'] != 1:
        raise ValueError('Unknown NPC observation schema')
    if data['kind'] != 'npc-behavior-observation' or data['engine'] != engine or engine not in ('official', 'runtime'):
        raise ValueError('Expected official/runtime NPC roles')
    if data['scenario'] != SCENARIO:
        raise ValueError('Unsupported NPC scenario')
    _int(data['seed'], -0x80000000, 0x7fffffff, 'seed')
    context = data['context']
    _keys(context, CONTEXT, 'NPC context')
    for name in ('frames', 'widthTiles', 'heightTiles', 'worldSurface', 'netMode', 'gameMode', 'invasionType'):
        _int(context[name], 0, 4096, name)
    if (context['frames'], context['widthTiles'], context['heightTiles'], context['netMode'], context['gameMode'],
            context['invasionType']) != (16, 256, 128, 2, 0, 0):
        raise ValueError('Unsupported bounded NPC context')
    if not 0 < context['worldSurface'] < context['heightTiles']:
        raise ValueError('Invalid world surface')
    for name in ('dedServ', 'gameMenu', 'dayTime', 'hardMode', 'expert', 'master', 'goodWorld', 'remix', 'eclipse'):
        if type(context[name]) is not bool or context[name] != (name == 'dedServ'):
            raise ValueError('Unsupported Boolean context ' + name)
    if context['tiles'] != 'all-empty' or context['carrier'] != CARRIER or context['justHitFrames'] != HIT_FRAMES:
        raise ValueError('Unsupported carrier or per-frame context')
    if any(type(value) is not int for value in context['justHitFrames']):
        raise ValueError('Invalid justHit frame indices')
    player = context['player']
    _keys(player, {'slot', 'position', 'width', 'height', 'active', 'dead', 'ghost', 'aggro', 'frozen'}, 'player context')
    _vector(player['position'], 2)
    for name in ('slot', 'width', 'height', 'aggro'):
        _int(player[name], 0, 255, 'player ' + name)
    if (player['slot'], player['width'], player['height'], player['aggro']) != (7, 20, 42, 0):
        raise ValueError('Unsupported player profile')
    for name in ('active', 'dead', 'ghost', 'frozen'):
        if type(player[name]) is not bool or player[name] != (name == 'active'):
            raise ValueError('Unsupported player flag')
    _state(data['input'])
    if not isinstance(data['frames'], list) or len(data['frames']) != 16:
        raise ValueError('Exactly sixteen sequential frames required')
    previous = data['input']
    for index, frame in enumerate(data['frames']):
        _keys(frame, {'index', 'justHit', 'before', 'after'}, 'NPC frame')
        if type(frame['index']) is not int or frame['index'] != index or type(frame['justHit']) is not bool or frame['justHit'] != (index in HIT_FRAMES):
            raise ValueError('Unknown/reordered frame context')
        _state(frame['before'])
        _state(frame['after'])
        expected = {'npc': dict(previous['npc'], justHit=frame['justHit']), 'rng': previous['rng']}
        if frame['before'] != expected:
            raise ValueError('Frame preimage does not continue preceding physical state')
        previous = frame['after']
    provenance = data['provenance']
    required = {'entry', 'control', 'officialAssemblySha256', 'dependenciesSha256', 'probeSha256', 'methodIlSha256',
                'rawAfterAiJustHit', 'carrierRetirement'}
    if engine == 'runtime':
        required |= {'runtimeAssemblySha256', 'runtimeRng'}
    _keys(provenance, required, 'NPC execution provenance')
    if provenance['entry'] != (OFFICIAL_ENTRY if engine == 'official' else RUNTIME_ENTRY):
        raise ValueError('Unexpected executed NPC entry')
    if provenance['control'] not in ('normal', 'omit-step', 'extra-rng') or engine == 'official' and provenance['control'] != 'normal':
        raise ValueError('Unknown mutation control')
    raw = provenance['rawAfterAiJustHit']
    if not isinstance(raw, list) or len(raw) != 16 or any(type(value) is not bool for value in raw):
        raise ValueError('All raw direct-AI justHit observations are required')
    expected_raw = [index in HIT_FRAMES for index in range(16)] if engine == 'official' or provenance['control'] == 'omit-step' else [False] * 16
    if raw != expected_raw or provenance['carrierRetirement'] != RETIREMENT:
        raise ValueError('Unexpected direct-AI pulse behavior or carrier operation')
    for key in required:
        if key.endswith('Sha256') and key != 'dependenciesSha256' and not _sha(provenance[key]):
            raise ValueError('Invalid provenance hash')
    if engine == 'runtime' and (not isinstance(provenance['runtimeRng'], str) or not 1 <= len(provenance['runtimeRng']) <= 256):
        raise ValueError('Missing runtime RNG provenance')
    for values in (provenance['dependenciesSha256'], data['executedDlls']):
        if not isinstance(values, dict) or not 1 <= len(values) <= 256:
            raise ValueError('Incomplete/bounded DLL closure')
        for name, digest in values.items():
            if (not isinstance(name, str) or len(name) > 256 or name.startswith('/') or '\\' in name or
                    any(part in ('', '.', '..') for part in name.split('/')) or ':' in name or not _sha(digest)):
                raise ValueError('Unsafe DLL identity/hash')
    if RUNTIME_DLL not in data['executedDlls'] or 'NpcProbe.dll' not in data['executedDlls']:
        raise ValueError('Missing NPC probe/runtime identity')


def load_with_hash(path: str | Path, engine: str) -> tuple[dict, str]:
    path = Path(path)
    if path.stat().st_size > MAX_BYTES:
        raise ValueError('NPC observation exceeds size bound')
    with path.open('rb') as stream:
        raw = stream.read(MAX_BYTES + 1)
    if len(raw) > MAX_BYTES:
        raise ValueError('NPC observation exceeds size bound')
    data = parse_json(raw.decode('utf-8'))
    validate(data, engine)
    return data, hashlib.sha256(raw).hexdigest()


def _first_difference(a, b, path=''):
    if type(a) is not type(b):
        return path, a, b
    if isinstance(a, dict):
        for key in a:
            difference = _first_difference(a[key], b[key], path + '.' + key)
            if difference is not None:
                return difference
    elif isinstance(a, list):
        for index, (x, y) in enumerate(zip(a, b)):
            difference = _first_difference(x, y, path + f'[{index}]')
            if difference is not None:
                return difference
    elif a != b:
        return path, a, b
    return None


def compare(official: dict, runtime: dict) -> dict:
    validate(official, 'official')
    validate(runtime, 'runtime')
    for name in ('scenario', 'seed', 'context', 'input'):
        if official[name] != runtime[name]:
            return {'accepted': False, 'classification': 'incomparable-input', 'firstDifference': name,
                    'scenario': SCENARIO, 'scope': SCOPE}
    for index, (a, b) in enumerate(zip(official['frames'], runtime['frames'])):
        difference = _first_difference(a['after'], b['after'], f'frames[{index}].after')
        if difference is not None:
            path, expected, actual = difference
            return {'accepted': False, 'classification': 'rng-state-mismatch' if '.rng.' in path else 'npc-state-mismatch',
                    'firstDifference': path, 'expected': expected, 'actual': actual,
                    'frame': index, 'scenario': SCENARIO, 'scope': SCOPE}
    return {'accepted': True, 'classification': 'match', 'comparedFrames': 16, 'scenario': SCENARIO, 'scope': SCOPE}
