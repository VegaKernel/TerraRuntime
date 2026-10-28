"""Content-pinned, explicit-context official capture format shared by all adapters."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

from .catalog import NPC_FIELDS, NPC_FLOATS, NPC_BOOLS, ITEM_FIELDS, single

ROOT = Path(__file__).resolve().parents[2]
PIN_PATH = Path(__file__).with_name('reference.json')
INPUTS = ('tools/vanilla_data/ReferenceProbe/Program.cs',
          'tools/vanilla_data/ReferenceProbe/ReferenceProbe.csproj',
          'tools/vanilla.py', 'tools/vanilla_data/cli.py',
          'tools/vanilla_data/capture.py', 'tools/vanilla_data/catalog.py',
          'tools/vanilla_data/__init__.py', 'tools/vanilla_data/csharp.py',
          'tools/vanilla_data/reference.json', 'Directory.Build.props', 'global.json')


def digest(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def canonical(value) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False).encode('utf-8')


def data_digest(value) -> str:
    return hashlib.sha256(canonical(value)).hexdigest()


def _pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f'Duplicate JSON key: {key}')
        result[key] = value
    return result


def parse_json(text: str):
    def invalid(value):
        raise ValueError('Non-finite JSON number: ' + value)
    return json.loads(text, object_pairs_hook=_pairs, parse_constant=invalid)


def read_json(path: Path):
    return parse_json(path.read_text(encoding='utf-8-sig'))


def pins(root: Path = ROOT) -> dict:
    return {name: digest(root / name) for name in INPUTS}


def reference() -> dict:
    return read_json(PIN_PATH)


def validate(data: dict, *, provenance: bool = True, root: Path = ROOT) -> None:
    admitted = reference()
    if not isinstance(data, dict):
        raise ValueError('Capture must be a JSON object')
    if type(data.get('schemaVersion')) is not int or data['schemaVersion'] != 1 or data.get('gameVersion') != admitted['gameVersion'] or data.get('assemblySha256') != admitted['assemblySha256']:
        raise ValueError('Capture version/official assembly differs from the admitted reference')
    bounds = {name: admitted[name] for name in ('npcPositiveCount', 'npcNegativeCount', 'itemCount')}
    if data.get('bounds') != bounds:
        raise ValueError('Capture identity bounds differ from the admitted reference')
    if data.get('dependenciesSha256') != admitted['dependenciesSha256']:
        raise ValueError('Official dependency hashes differ from the admitted reference')
    profile = data.get('profile', {})
    if not isinstance(profile, dict):
        raise ValueError('Capture profile must be a JSON object')
    required = dict(name='classic-constructor-coreclr', seed=1458, gameMenu=True,
                    netMode=2, hardMode=False, getGoodWorld=False, remixWorld=False)
    if any(type(profile.get(name)) is not type(value) or profile[name] != value for name, value in required.items()):
        raise ValueError('Capture context differs from the admitted constructor profile')
    if any(not isinstance(profile.get(name), str) or not profile[name].strip() for name in ('hostOs', 'framework')):
        raise ValueError('Capture host/framework metadata is incomplete')
    if not isinstance(data.get('datasets'), dict) or set(data['datasets']) != {'npc-defaults', 'item-placement'}:
        raise ValueError('Capture datasets are incomplete or unknown')
    for dataset, fields, key, expected in (
        ('npc-defaults', ('requested',) + NPC_FIELDS, 'requested',
         set(range(1, bounds['npcPositiveCount'] + 1)) | set(range(-bounds['npcNegativeCount'], 0))),
        ('item-placement', ITEM_FIELDS, 'id', set(range(1, bounds['itemCount']))),
    ):
        rows = data['datasets'][dataset]
        if not isinstance(rows, list) or len(rows) != len(expected):
            raise ValueError(f'{dataset}: missing/extra rows')
        seen = set()
        for row in rows:
            if not isinstance(row, dict) or set(row) != set(fields):
                raise ValueError(f'{dataset}: row schema changed')
            for name in fields:
                value = row[name]
                boolean = name in NPC_BOOLS if dataset == 'npc-defaults' else name == 'consumable'
                floating = dataset == 'npc-defaults' and name in NPC_FLOATS
                if boolean:
                    valid = type(value) is bool
                elif floating:
                    valid = type(value) in (int, float)
                    if valid:
                        single(value)
                else:
                    valid = type(value) is int
                if not valid:
                    raise ValueError(f'{dataset}: invalid {name}')
            if row[key] in seen:
                raise ValueError(f'{dataset}: duplicate identity {row[key]}')
            seen.add(row[key])
            if dataset == 'npc-defaults' and row['netId'] != row['requested']:
                raise ValueError('NPC requested/net identity changed')
        if seen != expected:
            raise ValueError(f'{dataset}: identity coverage changed')
    if provenance:
        if data.get('captureInputs') != pins(root):
            raise ValueError('Capture tool/reference inputs changed; recapture explicitly')
        if data.get('dataSha256') != data_digest(data['datasets']):
            raise ValueError('Capture data hash differs; reference data changed')
        if not isinstance(data.get('probeSha256'), str) or len(data['probeSha256']) != 64 or any(char not in '0123456789abcdef' for char in data['probeSha256']):
            raise ValueError('Executed probe hash is missing/invalid')


def load(path: Path, root: Path = ROOT) -> dict:
    data = read_json(path)
    validate(data, root=root)
    return data
