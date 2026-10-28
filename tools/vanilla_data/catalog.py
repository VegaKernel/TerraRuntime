"""Strict readers for the two existing production data layouts, not gameplay execution."""
from __future__ import annotations

import math
import re
import struct
from pathlib import Path

from .csharp import mask, require_unconditional

TARGETS = {
    'npc-defaults': 'src/TerraRuntime.Gameplay/Npcs/VanillaNpcSourceDefaultRoster1458.cs',
    'item-placement': 'src/TerraRuntime.Gameplay/Items/VanillaItemPlacementTable1458.cs',
}
NPC_FIELDS = ('netId', 'type', 'aiStyle', 'width', 'height', 'lifeMax', 'damage', 'defense',
              'scale', 'knockBackResist', 'alpha', 'noGravity', 'noTileCollide', 'dontTakeDamage', 'hidden')
NPC_FLOATS = {'scale', 'knockBackResist'}
NPC_BOOLS = {'noGravity', 'noTileCollide', 'dontTakeDamage', 'hidden'}
ITEM_FIELDS = ('id', 'createTile', 'createWall', 'placeStyle', 'consumable')
KEYS = {'npc-defaults': 'netId', 'item-placement': 'id'}


def single(value: float) -> float:
    value = struct.unpack('<f', struct.pack('<f', value))[0]
    if not math.isfinite(value):
        raise ValueError('Non-finite Single value')
    return value


def _array(text: str, pattern: str) -> tuple[int, int]:
    matches = list(re.finditer(pattern, text, re.S))
    if len(matches) != 1:
        raise ValueError('Expected exactly one data array with the admitted layout')
    return matches[0].span('body')


NPC_ARRAY = r'private\s+static\s+readonly\s+Row\[\]\s+Rows\s*=\s*\[(?P<body>.*?)\];'
ITEM_ARRAY = r'private\s+static\s+ReadOnlySpan<byte>\s+Records\s*=>\s*\[(?P<body>.*?)\];'


def read_npcs(text: str) -> list[dict]:
    code, uncommented = mask(text)
    require_unconditional(code)
    signatures = list(re.finditer(r'private\s+readonly\s+record\s+struct\s+Row\((.*?)\);', code, re.S))
    signature = signatures[0] if len(signatures) == 1 else None
    names = ['NetId', 'Type', 'AiStyle', 'Width', 'Height', 'Life', 'Damage', 'Defense',
             'Scale', 'KnockBack', 'Alpha', 'NoGravity', 'NoTileCollide', 'DontTakeDamage', 'Hidden']
    types = ['short'] + ['int'] * 7 + ['float'] * 2 + ['int'] + ['bool'] * 4
    if signature is None or [part.split() for part in signature[1].split(',')] != [[kind, name] for kind, name in zip(types, names)]:
        raise ValueError('NPC record column order changed')
    start, end = _array(code, NPC_ARRAY)
    body = uncommented[start:end]
    rows, position = [], 0
    for match in re.finditer(r'new\s*\(([^()]*)\)', body):
        separator = body[position:match.start()].strip()
        if separator != (',' if rows else ''):
            raise ValueError('Unsupported NPC row syntax')
        tokens = [token.strip() for token in match[1].split(',')]
        if len(tokens) != len(NPC_FIELDS):
            raise ValueError(f'NPC row has {len(tokens)} columns; expected {len(NPC_FIELDS)}')
        row = {}
        for name, token in zip(NPC_FIELDS, tokens):
            if name in NPC_BOOLS:
                if token not in ('true', 'false'):
                    raise ValueError(f'Unsupported Boolean literal {token}')
                row[name] = token == 'true'
            elif name in NPC_FLOATS:
                if not re.fullmatch(r'-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?f', token):
                    raise ValueError(f'Unsupported Single literal {token}')
                row[name] = single(float(token[:-1]))
            else:
                if not re.fullmatch(r'-?\d+', token):
                    raise ValueError(f'Unsupported integer literal {token}')
                row[name] = int(token)
        rows.append(row)
        position = match.end()
    if body[position:].strip() not in ('', ',') or not rows:
        raise ValueError('Empty/incomplete NPC data array')
    index_rows('npc-defaults', rows)
    return rows


def read_items(text: str) -> list[dict]:
    code, uncommented = mask(text)
    require_unconditional(code)
    start, end = _array(code, ITEM_ARRAY)
    body = uncommented[start:end]
    tokens = re.findall(r'0x[0-9a-fA-F]{2}\b', body)
    if not re.fullmatch(r'\s*0x[0-9a-fA-F]{2}(?:\s*,\s*0x[0-9a-fA-F]{2})*\s*,?\s*', body):
        raise ValueError('Unsupported placement byte array syntax')
    payload = bytes(int(token, 16) for token in tokens)
    if not payload or len(payload) % 8:
        raise ValueError('Placement payload is empty or truncated')
    rows = []
    for identity, tile, wall, style in struct.iter_unpack('<HhhH', payload):
        rows.append(dict(zip(ITEM_FIELDS, (identity, tile, wall, style & 0x7fff, bool(style & 0x8000)))))
    if [row['id'] for row in rows] != sorted(row['id'] for row in rows):
        raise ValueError('Placement identities must be sorted')
    for name, expected in {'RecordBytes': 8, 'Count': len(rows),
                           'TilePlacingCount': sum(row['createTile'] >= 0 for row in rows),
                           'WallPlacingCount': sum(row['createWall'] >= 0 for row in rows)}.items():
        matches = list(re.finditer(r'\bconst int ' + name + r'\s*=\s*(\d+)\s*;', code))
        if len(matches) != 1 or int(matches[0][1]) != expected:
            raise ValueError(f'Placement {name} does not match the actual byte array')
    index_rows('item-placement', rows)
    return rows


def index_rows(dataset: str, rows: list[dict]) -> dict[int, dict]:
    result = {}
    fields = NPC_FIELDS if dataset == 'npc-defaults' else ITEM_FIELDS
    for row in rows:
        if set(row) != set(fields):
            raise ValueError('Data row fields differ from the admitted schema')
        for name in fields:
            value = row[name]
            is_bool = name in NPC_BOOLS if dataset == 'npc-defaults' else name == 'consumable'
            is_float = dataset == 'npc-defaults' and name in NPC_FLOATS
            if is_bool:
                valid = type(value) is bool
            elif is_float:
                valid = type(value) in (int, float) and math.isfinite(value)
            else:
                valid = type(value) is int
            if not valid:
                raise ValueError(f'Invalid {dataset} {name}: {value!r}')
        identity = row[KEYS[dataset]]
        if identity in result:
            raise ValueError(f'Duplicate {dataset} identity {identity}')
        result[identity] = row
    return result


def expected_rows(capture: dict, dataset: str) -> list[dict]:
    rows = []
    if dataset == 'npc-defaults':
        for source in capture['datasets'][dataset]:
            if source['lifeMax'] == 0:
                continue  # Explicit nonlive identities stay outside this existing metadata table.
            rows.append({name: single(source[name]) if name in NPC_FLOATS else source[name] for name in NPC_FIELDS})
    else:
        rows = [dict(row) for row in capture['datasets'][dataset]
                if row['createTile'] >= 0 or row['createWall'] >= 0]
    return sorted(rows, key=lambda row: row[KEYS[dataset]])


def read_actual(dataset: str, path: Path) -> list[dict]:
    text = path.read_text(encoding='utf-8-sig')
    return read_npcs(text) if dataset == 'npc-defaults' else read_items(text)


def compare(dataset: str, expected: list[dict], actual: list[dict]) -> dict:
    left, right = index_rows(dataset, expected), index_rows(dataset, actual)
    differences = []
    for identity in sorted(left.keys() | right.keys()):
        if identity not in left or identity not in right:
            differences.append({'id': identity, 'kind': 'extra' if identity not in left else 'missing'})
            continue
        for name in left[identity]:
            if left[identity][name] != right[identity][name]:
                differences.append({'id': identity, 'field': name, 'expected': left[identity][name],
                                    'actual': right[identity][name]})
    return {'accepted': not differences, 'dataset': dataset, 'expectedRows': len(left),
            'actualRows': len(right), 'differences': differences}


def emit(dataset: str, rows: list[dict], template: str) -> str:
    index_rows(dataset, rows)
    rows = sorted(rows, key=lambda row: row[KEYS[dataset]])
    code, _ = mask(template)
    require_unconditional(code)
    if dataset == 'npc-defaults':
        start, end = _array(code, NPC_ARRAY)
        def literal(name, value):
            if name in NPC_BOOLS:
                return str(value).lower()
            if name in NPC_FLOATS:
                value = single(value)
                for precision in range(1, 10):
                    candidate = format(value, f'.{precision}g')
                    if single(float(candidate)) == value:
                        return candidate + 'f'
                raise ValueError('Could not render Single literal')
            return str(value)
        body = '\n' + ''.join('        new(' + ', '.join(literal(name, row[name]) for name in NPC_FIELDS) + '),\n' for row in rows) + '    '
    else:
        start, end = _array(code, ITEM_ARRAY)
        payload = bytearray()
        for row in rows:
            if not 0 < row['id'] <= 0xffff or not -1 <= row['createTile'] <= 0x7fff or not -1 <= row['createWall'] <= 0x7fff or not 0 <= row['placeStyle'] <= 0x7fff:
                raise ValueError('Placement row does not fit the existing record layout')
            payload.extend(struct.pack('<HhhH', row['id'], row['createTile'], row['createWall'],
                                       row['placeStyle'] | (0x8000 if row['consumable'] else 0)))
        lines = [', '.join(f'0x{value:02X}' for value in payload[index:index + 16]) for index in range(0, len(payload), 16)]
        body = '\n' + ',\n'.join('        ' + line for line in lines) + '\n    '
    result = template[:start] + body + template[end:]
    if dataset == 'item-placement':
        for name, value in {'Count': len(rows), 'TilePlacingCount': sum(row['createTile'] >= 0 for row in rows),
                            'WallPlacingCount': sum(row['createWall'] >= 0 for row in rows)}.items():
            current_code, _ = mask(result)
            matches = list(re.finditer(r'\bconst int ' + name + r'\s*=\s*(\d+)\s*;', current_code))
            if len(matches) != 1:
                raise ValueError('Missing/ambiguous placement count declaration: ' + name)
            a, b = matches[0].span(1)
            result = result[:a] + str(value) + result[b:]
    return result
