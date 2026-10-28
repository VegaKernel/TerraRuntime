"""Strict physical PickAmmo boundary observations; no inferred item-use authority."""
from __future__ import annotations

import hashlib
from pathlib import Path
import re

from .capture import parse_json

SCENARIO = 'pick-ammo-random-conservation-v1'
OFFICIAL_ENTRY = 'Terraria.Player.PickAmmo'
RUNTIME_ENTRY = ('VanillaAmmoCycling1458.TrySelect+TryAdvanceSelectedAmmo/'
                 'VanillaProjectileWeaponCombatCatalog.PrepareAmmoConservation/'
                 'VanillaOrdinaryBowLaunch1458.TryResolve')
RUNTIME_DLL = 'TerraRuntime.Gameplay.dll'
SCOPE = ('Twenty-four Wooden Bow PickAmmo boundary calls on explicit projected effects and '
         '58 ordinary inventory slots; complete selector, launch binary32 bits and physical Main.rand state. '
         'Runtime stock debit is a detached caller projection. No ItemCheck/UpdateEquips, '
         'authoritative inventory admission, packet27, projectile birth or full player parity claim.')
MAX_BYTES = 2 * 1024 * 1024
TOP = {'schemaVersion', 'kind', 'engine', 'scenario', 'seed', 'context', 'input', 'frames', 'provenance', 'executedDlls'}
CONTEXT = dict(weaponType=39, prefix=0, steps=24, cyclingMode='Random', magicQuiver=True,
               ammoBox=True, ammoPotion=True, ammoCost75=True,
               effects='explicit-preprojected-not-UpdateEquips',
               inventoryProjection='58-ordinary-slots:type,stack,ammoFamily,consumable',
               debit='runtime-detached-caller-projection-not-authoritative-admission',
               floatEncoding='ieee754-binary32-signed-bits')


def _keys(value, expected, label):
    if not isinstance(value, dict) or set(value) != expected:
        raise ValueError('Incomplete/unknown ' + label)


def _int(value, low, high, label):
    if type(value) is not int or not low <= value <= high:
        raise ValueError('Invalid integer ' + label)


def _sha(value):
    return isinstance(value, str) and re.fullmatch('[0-9a-f]{64}', value) is not None


def _hashes(value):
    if not isinstance(value, dict) or not 1 <= len(value) <= 256:
        raise ValueError('Incomplete/bounded DLL closure')
    for name, digest in value.items():
        if (not isinstance(name, str) or len(name) > 256 or ':' in name or '\\' in name or
                any(part in ('', '.', '..') for part in name.split('/')) or not name.endswith('.dll') or not _sha(digest)):
            raise ValueError('Unsafe DLL identity/hash')


def _rng(value):
    _keys(value, {'algorithm', 'ownerAlias', 'inext', 'seedArray'}, 'physical RNG state')
    if value['algorithm'] != 'terraria1458-unifiedrandom-inext21' or value['ownerAlias'] != ['Main.rand']:
        raise ValueError('Unknown RNG owner/algorithm')
    _int(value['inext'], 0, 55, 'RNG cursor')
    if not isinstance(value['seedArray'], list) or len(value['seedArray']) != 56:
        raise ValueError('All 56 physical RNG slots are required')
    for item in value['seedArray']:
        _int(item, -0x80000000, 0x7fffffff, 'RNG slot')


def _state(value):
    _keys(value, {'inventory', 'cyclingOffset', 'cyclingCooldown', 'rng'}, 'inventory/selector/RNG state')
    _int(value['cyclingOffset'], -0x80000000, 0x7fffffff, 'cycling offset')
    _int(value['cyclingCooldown'], 0, 10000, 'cycling cooldown')
    if not isinstance(value['inventory'], list) or len(value['inventory']) != 58:
        raise ValueError('Exactly 58 physical ordinary slots required')
    for index, item in enumerate(value['inventory']):
        _keys(item, {'type', 'stack', 'ammoFamily', 'consumable'}, 'inventory item')
        _int(item['type'], 0, 6195, 'item type')
        _int(item['stack'], 0, 9999, 'item stack')
        _int(item['ammoFamily'], 0, 6195, 'ammo family')
        if type(item['consumable']) is not bool:
            raise ValueError('Invalid consumable Boolean')
        if item['type'] not in (0, 39, 40, 41, 47) or item['ammoFamily'] != (40 if item['type'] in (40, 41, 47) else 0):
            raise ValueError('Unrepresented inventory metadata')
        if item['consumable'] != (item['type'] in (40, 41, 47)) or (item['type'] == 0) != (item['stack'] == 0):
            raise ValueError('Unrepresented physical item/stock shape')
        if (item['type'] == 39) != (index == 0):
            raise ValueError('Weapon moved outside fixed carrier')
    _rng(value['rng'])


def validate(data: dict, engine: str) -> None:
    _keys(data, TOP, 'gameplay observation envelope')
    if type(data['schemaVersion']) is not int or data['schemaVersion'] != 1:
        raise ValueError('Unknown gameplay schema')
    if data['kind'] != 'gameplay-behavior-observation' or data['engine'] != engine or engine not in ('official', 'runtime'):
        raise ValueError('Expected official/runtime gameplay roles')
    if data['scenario'] != SCENARIO:
        raise ValueError('Unsupported gameplay scenario')
    _int(data['seed'], -0x80000000, 0x7fffffff, 'seed')
    _keys(data['context'], set(CONTEXT), 'gameplay context')
    if any(type(data['context'][key]) is not type(value) or data['context'][key] != value for key, value in CONTEXT.items()):
        raise ValueError('Unsupported bounded gameplay context')
    _state(data['input'])
    if not isinstance(data['frames'], list) or len(data['frames']) != 24:
        raise ValueError('Exactly twenty-four sequential frames required')
    previous = data['input']
    for index, frame in enumerate(data['frames']):
        _keys(frame, {'step', 'before', 'selectedSlot', 'usedAmmoItemId', 'canShoot', 'conserved', 'launch', 'state'}, 'gameplay frame')
        if type(frame['step']) is not int or frame['step'] != index:
            raise ValueError('Missing/duplicate/reordered frame')
        _state(frame['before'])
        if frame['before'] != previous:
            raise ValueError('Frame preimage does not continue preceding physical state')
        _int(frame['selectedSlot'], 0, 57, 'selected physical slot')
        if type(frame['usedAmmoItemId']) is not int or frame['usedAmmoItemId'] not in (40, 41, 47):
            raise ValueError('Unsupported selected ammo')
        if type(frame['canShoot']) is not bool or type(frame['conserved']) is not bool:
            raise ValueError('Invalid result Boolean')
        launch = frame['launch']
        _keys(launch, {'projectile', 'damage', 'speedBits', 'knockBackBits'}, 'launch result')
        _int(launch['projectile'], 0, 1196, 'projectile')
        _int(launch['damage'], 0, 10000, 'damage')
        for name in ('speedBits', 'knockBackBits'):
            _int(launch[name], -0x80000000, 0x7fffffff, name)
            if launch[name] & 0x7f800000 == 0x7f800000:
                raise ValueError('Nonfinite launch float')
        _state(frame['state'])
        previous = frame['state']
    provenance = data['provenance']
    required = {'entry', 'control', 'probeSha256', 'officialAssemblySha256', 'dependenciesSha256'}
    if engine == 'runtime':
        required.add('runtimeAssemblySha256')
    _keys(provenance, required, 'gameplay execution provenance')
    if provenance['entry'] != (OFFICIAL_ENTRY if engine == 'official' else RUNTIME_ENTRY):
        raise ValueError('Unexpected executed gameplay entry')
    if provenance['control'] not in ('normal', 'omit-step', 'extra-rng') or engine == 'official' and provenance['control'] != 'normal':
        raise ValueError('Unknown mutation control')
    for name in required - {'entry', 'control', 'dependenciesSha256'}:
        if not _sha(provenance[name]):
            raise ValueError('Invalid provenance hash')
    _hashes(provenance['dependenciesSha256'])
    _hashes(data['executedDlls'])
    if not {'GameplayProbe.dll', RUNTIME_DLL}.issubset(data['executedDlls']):
        raise ValueError('Missing probe/runtime DLL identity')
    if provenance['probeSha256'] != data['executedDlls']['GameplayProbe.dll']:
        raise ValueError('Probe/closure identity differs')
    if engine == 'runtime' and provenance['runtimeAssemblySha256'] != data['executedDlls'][RUNTIME_DLL]:
        raise ValueError('Runtime/closure identity differs')


def load_with_hash(path: str | Path, engine: str) -> tuple[dict, str]:
    path = Path(path)
    if path.stat().st_size > MAX_BYTES:
        raise ValueError('Gameplay observation exceeds size bound')
    with path.open('rb') as stream:
        raw = stream.read(MAX_BYTES + 1)
    if len(raw) > MAX_BYTES:
        raise ValueError('Gameplay observation exceeds size bound')
    data = parse_json(raw.decode('utf-8'))
    validate(data, engine)
    return data, hashlib.sha256(raw).hexdigest()


def _differences(a, b, path=''):
    if type(a) is not type(b):
        return [{'field': path, 'expected': a, 'actual': b}]
    if isinstance(a, dict):
        return [difference for key in a for difference in _differences(a[key], b[key], path + '.' + key)]
    if isinstance(a, list):
        return [difference for index, (x, y) in enumerate(zip(a, b)) for difference in _differences(x, y, f'{path}[{index}]')]
    return [] if a == b else [{'field': path, 'expected': a, 'actual': b}]


def compare(official: dict, runtime: dict) -> dict:
    validate(official, 'official')
    validate(runtime, 'runtime')
    report = dict(schemaVersion=1, kind='gameplay-behavior-comparison', scenario=SCENARIO,
                  scope=SCOPE, accepted=False, classification='incomparable-input')
    for key in ('seed', 'context', 'input'):
        if official[key] != runtime[key]:
            report['firstDifference'] = _differences(official[key], runtime[key], key)[0]
            return report
    differences = _differences(official['frames'], runtime['frames'], 'frames')
    report.update(accepted=not differences, classification='match' if not differences else 'behavior-mismatch',
                  framesCompared=24, differentFields=len(differences), firstDifference=differences[0] if differences else None)
    return report
