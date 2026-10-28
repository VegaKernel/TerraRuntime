"""Actual Player.Update versus joined authoritative player-phase observations."""
from __future__ import annotations

import hashlib
from pathlib import Path

from .capture import parse_json
from .gameplay_trace import _keys, _int, _sha, _hashes, _rng, _differences

SCENARIO = 'owned-bow-player-update-v1'
OFFICIAL_ENTRY = 'Terraria.Player.Update'
RUNTIME_ENTRY = 'TerraRuntime.Application.ServerRuntimeState.Tick/PlayerAuthority.TickRemotePlayerPhase/RuntimePlayerInventoryStore'
RUNTIME_DLL = 'TerraRuntime.Application.dll'
SCOPE = ('Sixty-five actual headless local Player.Update frames versus a genuinely joined LocalOwnerProjection '
         'through commands and State.Tick: physical inventory, item clocks, heat and complete physical RNG; '
         'runtime funded quantities and paid receipts are independently checked. Ordinary empty scene only; '
         'no cracked/thin-ice mutation, packet27 delivery, projectile simulation, GUI, Native or complete Player.Update parity claim.')
MAX_BYTES = 2 * 1024 * 1024
CONTEXT = dict(steps=65, weaponType=39, ammoType=40, ammoSlot=54, initialAmmo=4,
               widthTiles=400, heightTiles=300, worldSurface=80, netMode=2, dedServ=True,
               gameMenu=True, myPlayer=0, fallStart=51,
               sourceCaller='original-headless-local-Player.Update-not-UpdateWorld_Players-or-GUI',
               runtimeCaller='real-joined-LocalOwnerProjection-command-then-State.Tick',
               runtimeStock='trusted-reserved-world-drop-then-real-owned-contact-pickup',
               controls='held:1,2,32,33;released:all-other-frames', terrain='empty-no-cracked-or-thin-ice',
               positionInput=[736, 822], projection='58x(type,stack,prefix,favoriteFlag);item-clocks;manaHeatBits;physical-Main.rand',
               publication='original-shoot-versus-runtime-paid-receipt-before-delayed-packet27')
TOP = {'schemaVersion', 'kind', 'engine', 'scenario', 'seed', 'context', 'input', 'frames', 'provenance', 'executedDlls'}


def _state(state):
    _keys(state, {'inventory', 'animation', 'animationMax', 'itemTime', 'itemTimeMax', 'release', 'reuse', 'manaHeatBits', 'rng'}, 'owned item state')
    inventory = state['inventory']
    if not isinstance(inventory, list) or len(inventory) != 58:
        raise ValueError('All 58 physical slots required')
    for index, item in enumerate(inventory):
        if not isinstance(item, list) or len(item) != 4:
            raise ValueError('Complete item identity/stack/prefix/flags required')
        for value in item:
            _int(value, 0, 9999, 'physical item')
        if index == 0:
            if item != [39, 1, 0, 0]:
                raise ValueError('Fixed bow carrier changed')
        elif index == 54:
            if item not in ([40, n, 0, 0] for n in range(1, 5)) and item != [0, 0, 0, 0]:
                raise ValueError('Unsupported arrow stock')
        elif item != [0, 0, 0, 0]:
            raise ValueError('Unsupported nonselected item')
    for name in ('animation', 'animationMax', 'itemTime', 'itemTimeMax', 'reuse'):
        _int(state[name], 0, 10000, name)
    if type(state['release']) is not bool:
        raise ValueError('Invalid release Boolean')
    _int(state['manaHeatBits'], 0, 0x7fffffff, 'heat bits')
    if state['manaHeatBits'] not in (0, 0x3f800000):
        raise ValueError('Unsupported explicit heat state')
    _rng(state['rng'])


def validate(data, engine):
    _keys(data, TOP, 'owned gameplay envelope')
    if type(data['schemaVersion']) is not int or data['schemaVersion'] != 1:
        raise ValueError('Unknown owned schema')
    if data['kind'] != 'gameplay-behavior-observation' or engine not in ('official', 'runtime') or data['engine'] != engine or data['scenario'] != SCENARIO:
        raise ValueError('Unknown owned profile/role')
    _int(data['seed'], -0x80000000, 0x7fffffff, 'seed')
    _keys(data['context'], set(CONTEXT), 'owned gameplay context')
    if any(type(data['context'][key]) is not type(value) or data['context'][key] != value for key, value in CONTEXT.items()):
        raise ValueError('Unknown owned caller/context')
    if any(type(value) is not int for value in data['context']['positionInput']):
        raise ValueError('Invalid position input')
    _state(data['input'])
    if not isinstance(data['frames'], list) or len(data['frames']) != 65:
        raise ValueError('Exactly 65 actual frames required')
    previous = data['input']
    for index, frame in enumerate(data['frames']):
        _keys(frame, {'step', 'held', 'before', 'after', 'proof'}, 'owned frame')
        if type(frame['step']) is not int or frame['step'] != index or type(frame['held']) is not bool or frame['held'] != (index in (1, 2, 32, 33)):
            raise ValueError('Reordered/missing control frame')
        _state(frame['before'])
        _state(frame['after'])
        if frame['before'] != previous:
            raise ValueError('Discontinuous actual owned preimage')
        previous = frame['after']
        proof = frame['proof']
        stock = frame['after']['inventory'][54][1]
        if engine == 'runtime':
            _keys(proof, {'kind', 'connectionAssigned', 'knownPhase', 'knownClocks', 'fundedAmmo', 'paidUses'}, 'actual runtime ownership')
            if proof['kind'] != 'owned-authority':
                raise ValueError('Missing authoritative owner')
            for name in ('connectionAssigned', 'knownPhase', 'knownClocks'):
                if type(proof[name]) is not bool or not proof[name]:
                    raise ValueError('Retired/unknown authority')
            _int(proof['fundedAmmo'], 0, 4, 'funded ammo')
            _int(proof['paidUses'], 0, 2, 'paid receipt count')
            if proof['fundedAmmo'] != stock or proof['paidUses'] != 4 - stock:
                raise ValueError('Physical/funded/paid custody differs')
        else:
            _keys(proof, {'kind', 'active', 'dead', 'life', 'projectileCount'}, 'actual original lifecycle')
            if proof['kind'] != 'original-player' or type(proof['active']) is not bool or not proof['active'] or type(proof['dead']) is not bool or proof['dead']:
                raise ValueError('Original lifecycle left profile')
            _int(proof['life'], 1, 100, 'life')
            _int(proof['projectileCount'], 0, 2, 'original projectile count')
            if proof['projectileCount'] != 4 - stock:
                raise ValueError('Original birth/debit projection differs')
    provenance = data['provenance']
    keys = {'entry', 'control', 'probeSha256', 'officialAssemblySha256', 'dependenciesSha256'}
    if engine == 'runtime':
        keys.add('runtimeAssemblySha256')
    _keys(provenance, keys, 'owned execution provenance')
    if provenance['entry'] != (OFFICIAL_ENTRY if engine == 'official' else RUNTIME_ENTRY):
        raise ValueError('Unexpected original/runtime entry')
    if provenance['control'] not in ('normal', 'omit-step', 'extra-rng') or engine == 'official' and provenance['control'] != 'normal':
        raise ValueError('Unknown execution control')
    for name in keys - {'entry', 'control', 'dependenciesSha256'}:
        if not _sha(provenance[name]):
            raise ValueError('Invalid execution hash')
    _hashes(provenance['dependenciesSha256'])
    _hashes(data['executedDlls'])
    if not {'GameplayProbe.dll', RUNTIME_DLL}.issubset(data['executedDlls']):
        raise ValueError('Missing actual probe/owner DLL')
    if provenance['probeSha256'] != data['executedDlls']['GameplayProbe.dll'] or engine == 'runtime' and provenance['runtimeAssemblySha256'] != data['executedDlls'][RUNTIME_DLL]:
        raise ValueError('Actual DLL/closure hash differs')


def load_with_hash(path: str | Path, engine: str):
    path = Path(path)
    if path.stat().st_size > MAX_BYTES:
        raise ValueError('Owned observation exceeds hard bound')
    with path.open('rb') as stream:
        raw = stream.read(MAX_BYTES + 1)
    if len(raw) > MAX_BYTES:
        raise ValueError('Owned observation exceeds hard bound')
    data = parse_json(raw.decode('utf-8'))
    validate(data, engine)
    return data, hashlib.sha256(raw).hexdigest()


def compare(official, runtime):
    validate(official, 'official')
    validate(runtime, 'runtime')
    report = dict(schemaVersion=1, kind='owned-gameplay-comparison', scenario=SCENARIO, scope=SCOPE,
                  accepted=False, classification='incomparable-input')
    for name in ('seed', 'context', 'input'):
        if official[name] != runtime[name]:
            report['firstDifference'] = _differences(official[name], runtime[name], name)[0]
            return report
    # Engine-specific ownership/birth evidence is validated above, never silently
    # equated: paid server receipts precede the client's delayed packet27 birth.
    differences = [difference for index, (a, b) in enumerate(zip(official['frames'], runtime['frames']))
                   for name in ('before', 'after') for difference in _differences(a[name], b[name], f'frames[{index}].{name}')]
    report.update(accepted=not differences, classification='match' if not differences else 'behavior-mismatch',
                  framesCompared=65, differentFields=len(differences), firstDifference=differences[0] if differences else None)
    return report
