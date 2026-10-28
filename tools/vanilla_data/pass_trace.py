"""Compare bounded, explicit pass observations; never infer equal input from equal seed."""
from __future__ import annotations

import base64
import hashlib
import math
from pathlib import Path
import re
import struct

from .capture import parse_json

FIELDS = ('type', 'wall', 'frameX', 'frameY', 'flags', 'liquidAmount',
          'tileColor', 'wallColor', 'shape', 'liquidKind', 'reserved')
CELL = struct.Struct('<HHhhHBBBBBB')
PROJECTION = {'order': 'y-major/x-fast', 'fields': list(FIELDS),
              'recordBytes': CELL.size, 'byteOrder': 'little-endian'}
MAX_CELLS = 262_144
MAX_FILE_BYTES = 16 * 1024 * 1024
CONTEXT = {'width', 'height', 'worldSurface', 'seed', 'netMode', 'dedServ', 'gameMenu',
           'skyblock', 'getGoodWorld', 'dontStarve', 'remix', 'predecessorState'}
SCOPE = ('Supplied bounded synthetic pass observations, complete named WorldTile16 projection '
         'and reported RNG state. No natural-prefix/full-Tile-memory/whole-world parity claim.')


def _sha(value: str) -> bool:
    return isinstance(value, str) and re.fullmatch('[0-9a-f]{64}', value) is not None


def _rng(value: dict) -> None:
    if not isinstance(value, dict) or set(value) != {'algorithm', 'ownerAlias', 'inext', 'seedArray'}:
        raise ValueError('Incomplete RNG observation')
    if value['algorithm'] != 'terraria1458-unifiedrandom-inext21' or value['ownerAlias'] != ['Main.rand', 'WorldGen.genRand']:
        raise ValueError('Unsupported RNG owner/algorithm projection')
    if type(value['inext']) is not int or not 0 <= value['inext'] <= 55:
        raise ValueError('Invalid physical RNG cursor')
    if not isinstance(value['seedArray'], list) or len(value['seedArray']) != 56 or any(
        type(cell) is not int or not -0x80000000 <= cell <= 0x7fffffff for cell in value['seedArray']):
        raise ValueError('Expected all 56 physical RNG state slots')


def _state(value: dict, count: int) -> bytes:
    if not isinstance(value, dict) or set(value) != {'cellsBase64', 'cellsSha256', 'rng'}:
        raise ValueError('Incomplete input/output state')
    if not isinstance(value['cellsBase64'], str) or len(value['cellsBase64']) > (MAX_CELLS * CELL.size + 2) // 3 * 4:
        raise ValueError('Invalid/oversized cell payload')
    payload = base64.b64decode(value['cellsBase64'], validate=True)
    if len(payload) != count * CELL.size or base64.b64encode(payload).decode('ascii') != value['cellsBase64']:
        raise ValueError('Cell coverage or canonical Base64 encoding differs')
    if not _sha(value['cellsSha256']) or hashlib.sha256(payload).hexdigest() != value['cellsSha256']:
        raise ValueError('Cell payload hash differs')
    for cell in CELL.iter_unpack(payload):
        flags, tile_color, wall_color, shape, liquid_kind, reserved = cell[4], cell[6], cell[7], cell[8], cell[9], cell[10]
        if flags & ~0xfff or tile_color > 31 or wall_color > 31 or shape > 5 or liquid_kind > 3 or reserved != 0:
            raise ValueError('Unsupported cell state in the named WorldTile16 projection')
        if flags & 0x800 and shape < 2:
            raise ValueError('Independent half-brick-with-slope flag requires a slope')
    _rng(value['rng'])
    return payload


def validate(data: dict, engine: str) -> tuple[bytes, bytes]:
    if not isinstance(data, dict) or type(data.get('schemaVersion')) is not int or data['schemaVersion'] != 1:
        raise ValueError('Unsupported pass observation schema')
    if data.get('kind') != 'worldgen-pass-observation' or data.get('engine') != engine:
        raise ValueError('Expected the named official/runtime observation roles')
    for key in ('pass', 'scenario'):
        if not isinstance(data.get(key), str) or not data[key] or len(data[key]) > 128:
            raise ValueError('Missing/oversized pass/scenario identity')
    if data.get('projection') != PROJECTION:
        raise ValueError('Cell projections differ or omit represented fields')
    context = data.get('context')
    if not isinstance(context, dict) or set(context) != CONTEXT:
        raise ValueError('Explicit pass context is incomplete/unknown')
    for name in ('width', 'height'):
        if type(context[name]) is not int or context[name] <= 0:
            raise ValueError('Invalid cell dimensions')
    count = context['width'] * context['height']
    if count > MAX_CELLS:
        raise ValueError('Pass observation exceeds the bounded-cell profile')
    if type(context['seed']) is not int or not -0x80000000 <= context['seed'] <= 0x7fffffff:
        raise ValueError('Invalid seed')
    if type(context['worldSurface']) not in (int, float) or not math.isfinite(context['worldSurface']) or not 0 <= context['worldSurface'] < context['height']:
        raise ValueError('Invalid world surface')
    if type(context['netMode']) is not int or context['netMode'] != 2:
        raise ValueError('Only the explicit dedicated fixture role is represented')
    for name in ('dedServ', 'gameMenu', 'skyblock', 'getGoodWorld', 'dontStarve', 'remix'):
        if type(context[name]) is not bool:
            raise ValueError('Expected explicit Boolean world context: ' + name)
    if context['predecessorState'] != 'explicit-synthetic-not-natural-prefix':
        raise ValueError('Only the explicit synthetic predecessor-state profile is represented')
    if not isinstance(data.get('provenance'), dict) or not data['provenance']:
        raise ValueError('Execution provenance is missing')
    return _state(data.get('input'), count), _state(data.get('output'), count)


def load_with_hash(path: Path, engine: str) -> tuple[dict, str]:
    if path.stat().st_size > MAX_FILE_BYTES:
        raise ValueError('Pass observation file exceeds its byte bound')
    with path.open('rb') as stream:
        raw = stream.read(MAX_FILE_BYTES + 1)
    if len(raw) > MAX_FILE_BYTES:
        raise ValueError('Pass observation file exceeds its byte bound')
    data = parse_json(raw.decode('utf-8-sig'))
    validate(data, engine)
    return data, hashlib.sha256(raw).hexdigest()


def load(path: Path, engine: str) -> dict:
    return load_with_hash(path, engine)[0]


def _rng_difference(expected: dict, actual: dict) -> dict | None:
    for name in ('algorithm', 'ownerAlias', 'inext'):
        if expected[name] != actual[name]:
            return {'field': name, 'expected': expected[name], 'actual': actual[name]}
    for index, (left, right) in enumerate(zip(expected['seedArray'], actual['seedArray'])):
        if left != right:
            return {'field': 'seedArray', 'index': index, 'expected': left, 'actual': right}
    return None


def compare(official: dict, runtime: dict) -> dict:
    original_input, original_output = validate(official, 'official')
    runtime_input, runtime_output = validate(runtime, 'runtime')
    input_differences = []
    for name in ('pass', 'scenario', 'projection', 'context'):
        if official[name] != runtime[name]:
            input_differences.append(name)
    if original_input != runtime_input:
        input_differences.append('input.cells')
    before_rng = _rng_difference(official['input']['rng'], runtime['input']['rng'])
    if before_rng is not None:
        input_differences.append('input.rng')
    report = {'schemaVersion': 1, 'operation': 'worldgen-compare-pass', 'accepted': False,
              'comparable': not input_differences, 'pass': official['pass'], 'scenario': official['scenario'],
              'inputDifferences': input_differences, 'scope': SCOPE}
    if input_differences:
        report['classification'] = 'incomparable-input'
        return report
    first, different_cells, different_fields = None, 0, 0
    width = official['context']['width']
    for index, (left, right) in enumerate(zip(CELL.iter_unpack(original_output), CELL.iter_unpack(runtime_output))):
        if left == right:
            continue
        fields = [{'field': name, 'expected': a, 'actual': b}
                  for name, a, b in zip(FIELDS, left, right) if a != b]
        different_cells += 1
        different_fields += len(fields)
        if first is None:
            first = {'index': index, 'x': index % width, 'y': index // width, 'fields': fields}
    rng = _rng_difference(official['output']['rng'], runtime['output']['rng'])
    report.update(accepted=different_cells == 0 and rng is None,
                  classification='equal-projection' if different_cells == 0 and rng is None else 'behavior-difference',
                  cellsCompared=official['context']['width'] * official['context']['height'],
                  differentCells=different_cells, differentFields=different_fields, firstCellDifference=first,
                  firstRngDifference=rng, beforeCellsSha256=official['input']['cellsSha256'],
                  officialAfterCellsSha256=official['output']['cellsSha256'],
                  runtimeAfterCellsSha256=runtime['output']['cellsSha256'])
    return report
