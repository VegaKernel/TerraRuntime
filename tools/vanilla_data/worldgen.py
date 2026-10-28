"""Offline lexical worldgen catalog audit, not a world-generation/differential oracle."""
from __future__ import annotations

from functools import lru_cache
import hashlib
import importlib.util
import json
import math
import re
import struct
from pathlib import Path

from . import capture as facts
from .csharp import mask, require_unconditional

ROOT = Path(__file__).resolve().parents[2]
TARGET = 'src/TerraRuntime.WorldGeneration/Generation/Vanilla/PassCatalog1458.cs'
TOOL_INPUTS = ('tools/vanilla_data/worldgen.py', 'tools/vanilla_data/csharp.py',
              'tools/vanilla_data/capture.py', 'tools/vanilla_data/__init__.py',
              'tools/ci/probe_world_generation_init.py', 'tools/ci/verify_runtime_worldgen_catalog.py')
SOURCE_PATHS = ('Terraria/Main.cs', 'Terraria/WorldGen.cs', 'Terraria.ID/GenPassNameID.cs',
                'Terraria.GameContent.Generation/PassLegacy.cs',
                'Terraria.GameContent.Biomes/TerrainPass.cs', 'Terraria.GameContent.Biomes/JunglePass.cs',
                'Terraria.GameContent.Biomes/DitherSnakePass.cs')


def _module(name: str, relative: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / relative)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


_source = _module('worldgen_source_probe', 'tools/ci/probe_world_generation_init.py')
_runtime = _module('worldgen_runtime_check', 'tools/ci/verify_runtime_worldgen_catalog.py')


def _hash(value: str) -> str:
    return hashlib.sha256(value.encode('utf-8')).hexdigest()


@lru_cache(maxsize=8)
def _views(text: str):
    result = mask(text)
    require_unconditional(result[0])
    return result


def _balanced(code: str, start: int, opening: str, closing: str) -> int:
    if code[start] != opening:
        raise ValueError('Expected ' + opening)
    depth = 0
    for index in range(start, len(code)):
        if code[index] == opening:
            depth += 1
        elif code[index] == closing:
            depth -= 1
            if depth == 0:
                return index
    raise ValueError('Unterminated C# region')


def _methods(text: str, name: str) -> list[str]:
    code, _ = _views(text)
    pattern = r'(?m)^[ \t]*(?:public|private|internal|protected)(?: static)? [^\r\n{;]*\b' + re.escape(name) + r'\([^\r\n)]*\)'
    result = []
    for match in re.finditer(pattern, code):
        start = match.start() + len(match[0]) - len(match[0].lstrip())
        brace = code.find('{', match.end())
        if brace < 0:
            raise ValueError('Missing method body: ' + name)
        end = _balanced(code, brace, '{', '}')
        result.append(text[start:end + 1])
    return result


def _method(text: str, name: str) -> str:
    found = _methods(text, name)
    if len(found) != 1:
        raise ValueError(f'Expected one {name} method, got {len(found)}')
    return found[0]


def _string_names(text: str) -> dict[str, str]:
    code, comments = _views(text)
    pattern = r'\bpublic\s+(?:const\s+|static\s+readonly\s+|static\s+)string\s+(\w+)\s*='
    result = {}
    for match in re.finditer(pattern, code):
        literal = re.match(r'\s*("(?:\\.|[^"\\])*")\s*;', comments[match.end():])
        if literal is None or match[1] in result:
            raise ValueError('Unsupported/duplicate GenPassNameID declaration')
        result[match[1]] = json.loads(literal[1])
    if not result:
        raise ValueError('No GenPassNameID declarations')
    return result


def _name(expression: str, names: dict[str, str]) -> str:
    expression = _source.compact(expression)
    found = re.fullmatch(r'GenPassNameID\.(\w+)', expression)
    if found and found[1] in names:
        return names[found[1]]
    if re.fullmatch(r'"(?:\\.|[^"\\])*"', expression):
        return json.loads(expression)
    raise ValueError('Unresolved pass name expression: ' + expression)


def _registrations(method: str) -> list[dict]:
    code, comments = _views(method)
    rows = []

    def block(start, end, guards):
        cursor = start
        while cursor < end:
            if code[cursor].isspace():
                cursor += 1
                continue
            keyword = re.match(r'(if|AddGenerationPass)\b', code[cursor:])
            if keyword is None:
                raise ValueError('Unsupported statement outside a pass delegate: ' + comments[cursor:cursor + 60].strip())
            opening = cursor + keyword.end()
            while code[opening].isspace(): opening += 1
            closing = _balanced(code, opening, '(', ')')
            arguments = comments[opening + 1:closing]
            after = closing + 1
            while code[after].isspace(): after += 1
            if keyword[1] == 'if':
                # This is a recorded registration predicate, not a secret-seed evaluator.
                condition = _source.compact(arguments)
                _validate_condition(condition)
                stop = _balanced(code, after, '{', '}')
                block(after + 1, stop, guards + [condition])
                cursor = stop + 1
            else:
                if code[after] != ';':
                    raise ValueError('Pass registration must end in a semicolon')
                values = _source.split_top_level_arguments(arguments)
                if not 1 <= len(values) <= 3:
                    raise ValueError('Unknown AddGenerationPass overload')
                rows.append({'expression': values[0], 'arguments': values, 'registrationGuards': list(guards),
                             'implementationSha256': _hash(values[-1]) if len(values) > 1 else None})
                cursor = after + 1

    opening = code.index('{')
    block(opening + 1, _balanced(code, opening, '{', '}'), [])
    if not rows:
        raise ValueError('No pass registrations')
    return rows


def _validate_condition(condition: str) -> None:
    tokens = re.findall(r'[A-Za-z_]\w*(?:\.\w+)*|&&|\|\||!|\(|\)', condition)
    if re.sub(r'[A-Za-z_]\w*(?:\.\w+)*|&&|\|\||!|\(|\)|\s', '', condition):
        raise ValueError('Unsupported outer registration guard: ' + condition)
    cursor = 0
    def atom():
        nonlocal cursor
        if cursor >= len(tokens): raise ValueError('Incomplete registration guard')
        token = tokens[cursor]; cursor += 1
        if token == '!': atom()
        elif token == '(':
            expression()
            if cursor >= len(tokens) or tokens[cursor] != ')': raise ValueError('Unclosed registration guard')
            cursor += 1
        elif re.fullmatch(r'[A-Za-z_]\w*(?:\.\w+)*', token) is None:
            raise ValueError('Unsupported registration guard operand')
    def expression():
        nonlocal cursor
        atom()
        while cursor < len(tokens) and tokens[cursor] in ('&&', '||'):
            cursor += 1; atom()
    expression()
    if cursor != len(tokens): raise ValueError('Trailing registration guard tokens')


def _weight_map(text: str) -> dict[str, float]:
    code, comments = _views(text)
    declaration = re.search(r'\b_weightMap\s*=\s*new\s+Dictionary<string,\s*float>\s*\{', code)
    if declaration is None:
        raise ValueError('PassLegacy weight map not found')
    opening = code.index('{', declaration.start())
    closing = _balanced(code, opening, '{', '}')
    body = comments[opening + 1:closing]
    result, position = {}, 0
    pattern = r'\{\s*("(?:\\.|[^"\\])*")\s*,\s*(\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)f\s*\}'
    for match in re.finditer(pattern, body):
        if body[position:match.start()].strip() != (',' if result else ''):
            raise ValueError('Unsupported weight-map entry')
        name = json.loads(match[1])
        if name in result:
            raise ValueError('Duplicate weight-map name')
        result[name] = struct.unpack('<f', struct.pack('<f', float(match[2])))[0]
        position = match.end()
    if not result or body[position:].strip() not in ('', ','):
        raise ValueError('Incomplete weight map')
    get_weight = _source.compact(_views(_method(text, 'GetWeight'))[1])
    pattern = r'private static float GetWeight\(string name\) \{ if \(!_weightMap\.TryGetValue\(name, out var value\)\) \{ return 1f; \} return value; \}'
    if re.fullmatch(pattern, get_weight) is None:
        raise ValueError('Unsupported PassLegacy.GetWeight semantics')
    return result


def _typed(expression: str, sources: dict, names: dict) -> tuple[str, float, str]:
    found = re.fullmatch(r'new (\w+)\((.*)\)', expression)
    if found is None or found[1] not in sources:
        raise ValueError('Unsupported typed pass: ' + expression)
    class_name = found[1]
    source = sources[class_name]
    code, comments = _views(source)
    constructor = re.search(r'\bpublic\s+' + re.escape(class_name) + r'\(([^)]*)\)\s*:\s*base\s*\(', code)
    if constructor is None:
        raise ValueError('Missing typed pass constructor')
    opening = constructor.end() - 1
    closing = _balanced(code, opening, '(', ')')
    values = _source.split_top_level_arguments(comments[opening + 1:closing])
    body = closing + 1
    while code[body].isspace(): body += 1
    end = _balanced(code, body, '{', '}')
    if code[body + 1:end].strip() or len(values) != 2 or not re.fullmatch(r'\d+(?:\.\d+)?', values[1]):
        raise ValueError('Unsupported typed constructor body/weight')
    parameters = [part.strip().split() for part in constructor[1].split(',') if part.strip()]
    arguments = _source.split_top_level_arguments(found[2])
    name_expression = values[0]
    if parameters:
        if len(parameters) != 1 or parameters[0][0] != 'string' or len(parameters[0]) != 2 or len(arguments) != 1 or name_expression != parameters[0][1]:
            raise ValueError('Unsupported typed constructor arguments')
        name_expression = arguments[0]
    elif arguments:
        raise ValueError('Unexpected typed constructor arguments')
    return _name(name_expression, names), float(values[1]), class_name + '.ctor'


def _filter(method: str, names: dict[str, str]) -> list[str]:
    code, _ = _views(method)
    conditions = [_source.compact(value) for value in _source.extract_invocations(code, 'if')]
    if len(conditions) != 2 or conditions[0] != '!enabled':
        raise ValueError('Unknown special-seed filter conditions')
    second = re.fullmatch(r'enabled && \((name == GenPassNameID\.\w+(?: \|\| name == GenPassNameID\.\w+)*)\)', conditions[1])
    if second is None or re.search(r'\bbool\s+enabled\s*=\s*SecretSeed\.dualDungeons\.Enabled\s*;', code) is None or len(_source.extract_invocations(code, 'Disable')) != 1:
        raise ValueError('Unsupported special-seed disable predicate')
    return [_name(value, names) for value in re.findall(r'GenPassNameID\.\w+', second[1])]


def _tool_pins() -> dict:
    return {name: facts.digest(ROOT / name) for name in TOOL_INPUTS}


def _payload(reference: dict) -> dict:
    return {name: value for name, value in reference.items() if name != 'dataSha256'}


def capture(source_root: Path) -> dict:
    source_root = Path(source_root)
    tool_before = _tool_pins()
    raw = {name: (source_root / name).read_bytes() for name in SOURCE_PATHS}
    sources = {name: value.decode('utf-8-sig').replace('\r\n', '\n').replace('\r', '\n') for name, value in raw.items()}
    source_pins = {name: hashlib.sha256(value).hexdigest() for name, value in raw.items()}
    main_code, main = _views(sources['Terraria/Main.cs'])
    version = re.search(r'\bpublic\s+static\s+string\s+versionNumber\s*=', main_code)
    release = re.search(r'\bpublic\s+const\s+int\s+curRelease\s*=\s*326\s*;', main_code)
    if version is None or re.match(r'\s*"v1\.4\.5\.8"\s*;', main[version.end():]) is None or release is None:
        raise ValueError('Expected official source version1.4.5.8/release326')
    source = sources['Terraria/WorldGen.cs']
    add = _method(source, 'AddPasses')
    filtering = _method(source, 'DisablePassesForSpecialSeeds')
    names = _string_names(sources['Terraria.ID/GenPassNameID.cs'])
    weights = _weight_map(sources['Terraria.GameContent.Generation/PassLegacy.cs'])
    typed = {name: sources['Terraria.GameContent.Biomes/' + name + '.cs'] for name in ('TerrainPass', 'JunglePass', 'DitherSnakePass')}
    registrations = _registrations(add)
    # Cross-check the recursive registration walk with the existing balanced-call
    # reader over lexical code (quoted/commented lookalikes are not invocations).
    legacy = [_source.first_argument(value) for value in _source.extract_invocations(_views(add)[0], 'AddGenerationPass')]
    expressions = [row['expression'] for row in registrations]
    if len(legacy) != len(expressions) or any(left and left != right for left, right in zip(legacy, expressions)):
        raise ValueError('Independent lexical registration readers disagree')
    wrappers = _methods(source, 'AddGenerationPass')
    if len(wrappers) != 3:
        raise ValueError('Unknown registration overload census')
    expected_calls = {'newPassLegacy(name,method)', 'newPassLegacy(name,method,weight)', 'pass'}
    actual_calls = set()
    for wrapper in wrappers:
        wrapper_code, wrapper_comments = _views(wrapper)
        opening = wrapper_code.index('{')
        closing = _balanced(wrapper_code, opening, '{', '}')
        call = re.fullmatch(r'\s*_generator\.Append\((.*?)\)\s*;\s*', wrapper_comments[opening + 1:closing], re.S)
        if call is None:
            raise ValueError('Unsupported registration overload body')
        actual_calls.add(re.sub(r'\s', '', call[1]))
    if actual_calls != expected_calls:
        raise ValueError('Registration overload semantics changed')
    for index, row in enumerate(registrations):
        values = row.pop('arguments')
        if len(values) == 1:
            name, weight, weight_source = _typed(row['expression'], typed, names)
        else:
            name = _name(values[0], names)
            if len(values) == 3:
                if not re.fullmatch(r'\d+(?:\.\d+)?', values[1]):
                    raise ValueError('Unknown explicit legacy pass weight')
                weight, weight_source = float(values[1]), 'explicit-registration'
            else:
                weight = weights.get(name, 1.0)
                weight_source = 'PassLegacy._weightMap' if name in weights else 'PassLegacy.GetWeight-fallback'
        row.update(index=index, name=name, weight=weight, weightSource=weight_source)
    result = {
        'schemaVersion': 1, 'kind': 'worldgen-catalog', 'gameVersion': '1.4.5.8',
        'profile': 'lexical-registration-order-before-special-seed-filtering',
        'sourceInputs': source_pins, 'toolInputs': tool_before,
        'fingerprints': {'AddPassesSha256': _hash(add),
                         'RegistrationExpressionSequenceSha256': _hash('\n'.join(expressions)),
                         'ResolvedPassNameSequenceSha256': _hash('\n'.join(row['name'] for row in registrations)),
                         'DisablePassesForSpecialSeedsSha256': _hash(filtering)},
        'passes': registrations,
        'specialSeedFiltering': {'predicate': 'SecretSeed.dualDungeons.Enabled', 'disabledNames': _filter(filtering, names)},
        'coverage': {'sourceRegistrationCount': len(registrations), 'sourceWeightsKnown': len(registrations),
                     'runtimeWeightComparison': 'not-represented-by-runtime-catalog',
                     'seedGateComparison': 'recorded-and-fingerprinted-not-executed',
                     'worldCellRngComparison': 'not-executed'},
    }
    if {name: facts.digest(source_root / name) for name in SOURCE_PATHS} != source_pins:
        raise ValueError('Source tree changed during capture')
    if _tool_pins() != tool_before:
        raise ValueError('Worldgen tool inputs changed during capture')
    result['dataSha256'] = facts.data_digest(_payload(result))
    return result


def validate(reference: dict) -> None:
    if not isinstance(reference, dict):
        raise ValueError('Worldgen reference must be an object')
    if type(reference.get('schemaVersion')) is not int or reference['schemaVersion'] != 1 or reference.get('kind') != 'worldgen-catalog' or reference.get('gameVersion') != '1.4.5.8' or reference.get('profile') != 'lexical-registration-order-before-special-seed-filtering':
        raise ValueError('Unsupported worldgen catalog reference')
    if reference.get('toolInputs') != _tool_pins():
        raise ValueError('Worldgen tool inputs changed; recapture explicitly')
    if reference.get('dataSha256') != facts.data_digest(_payload(reference)):
        raise ValueError('Worldgen capture data changed')
    passes = reference.get('passes')
    if not isinstance(passes, list) or not passes or not all(isinstance(row, dict) for row in passes) or [row.get('index') for row in passes] != list(range(len(passes))):
        raise ValueError('Worldgen registration indexes incomplete')
    if not isinstance(reference.get('sourceInputs'), dict) or set(reference['sourceInputs']) != set(SOURCE_PATHS):
        raise ValueError('Worldgen source pin set changed')
    fingerprint_names = {'AddPassesSha256', 'RegistrationExpressionSequenceSha256',
                         'ResolvedPassNameSequenceSha256', 'DisablePassesForSpecialSeedsSha256'}
    if not isinstance(reference.get('fingerprints'), dict) or set(reference['fingerprints']) != fingerprint_names:
        raise ValueError('Worldgen method fingerprint set changed')
    for value in list(reference['sourceInputs'].values()) + list(reference['fingerprints'].values()):
        if not isinstance(value, str) or re.fullmatch(r'[0-9a-f]{64}', value) is None:
            raise ValueError('Invalid worldgen fingerprint')
    expected_fields = {'expression', 'registrationGuards', 'implementationSha256', 'index', 'name', 'weight', 'weightSource'}
    for row in passes:
        if not isinstance(row, dict) or set(row) != expected_fields or type(row['index']) is not int or not isinstance(row['name'], str) or not row['name'] or not isinstance(row['expression'], str):
            raise ValueError('Malformed worldgen registration')
        if type(row['weight']) not in (int, float) or not math.isfinite(row['weight']) or row['weight'] < 0:
            raise ValueError('Invalid worldgen weight')
        if row['weightSource'] not in {'TerrainPass.ctor', 'JunglePass.ctor', 'DitherSnakePass.ctor',
                                       'explicit-registration', 'PassLegacy._weightMap', 'PassLegacy.GetWeight-fallback'}:
            raise ValueError('Unknown worldgen weight source')
        if not isinstance(row['registrationGuards'], list) or not all(isinstance(value, str) for value in row['registrationGuards']):
            raise ValueError('Malformed worldgen registration guards')
        for guard in row['registrationGuards']:
            _validate_condition(guard)
        body_hash = row['implementationSha256']
        if body_hash is not None and (not isinstance(body_hash, str) or re.fullmatch(r'[0-9a-f]{64}', body_hash) is None):
            raise ValueError('Invalid registration body fingerprint')
    filtering = reference.get('specialSeedFiltering')
    if not isinstance(filtering, dict) or filtering.get('predicate') != 'SecretSeed.dualDungeons.Enabled' or not isinstance(filtering.get('disabledNames'), list) or not filtering['disabledNames'] or not all(isinstance(name, str) and name for name in filtering['disabledNames']):
        raise ValueError('Malformed special-seed filtering metadata')
    coverage = reference.get('coverage')
    if not isinstance(coverage, dict) or type(coverage.get('sourceRegistrationCount')) is not int or type(coverage.get('sourceWeightsKnown')) is not int or coverage != {'sourceRegistrationCount': len(passes), 'sourceWeightsKnown': len(passes),
        'runtimeWeightComparison': 'not-represented-by-runtime-catalog',
        'seedGateComparison': 'recorded-and-fingerprinted-not-executed', 'worldCellRngComparison': 'not-executed'}:
        raise ValueError('Worldgen coverage metadata changed')
    expressions = '\n'.join(row['expression'] for row in passes)
    names = '\n'.join(row['name'] for row in passes)
    if _hash(expressions) != reference['fingerprints']['RegistrationExpressionSequenceSha256'] or _hash(names) != reference['fingerprints']['ResolvedPassNameSequenceSha256']:
        raise ValueError('Worldgen registration digest disagrees with its rows')


def _runtime_array(text: str, name: str) -> list[str]:
    code, comments = _views(text)
    declarations = list(re.finditer(r'private\s+static\s+readonly\s+string\[\]\s+' + re.escape(name) + r'\s*=\s*\[', code))
    if len(declarations) != 1:
        raise ValueError('Expected one runtime array: ' + name)
    opening = declarations[0].end() - 1
    closing = _balanced(code, opening, '[', ']')
    body = comments[opening + 1:closing]
    values, position = [], 0
    for found in re.finditer(r'"(?:\\.|[^"\\])*"', body):
        if body[position:found.start()].strip() != (',' if values else ''):
            raise ValueError('Unsupported runtime pass-name expression')
        values.append(json.loads(found[0]))
        position = found.end()
    if body[position:].strip() not in ('', ',') or not values:
        raise ValueError('Incomplete runtime pass-name array')
    # Existing checker remains the canonical simple-array consumer.
    if values != _runtime.extract_array(comments[declarations[0].start():closing + 2], name):
        raise ValueError('Runtime array readers disagree')
    return values


def _check_text(reference: dict, text: str) -> dict:
    code, comments = _views(text)
    differences = []
    for name, expected in reference['fingerprints'].items():
        declarations = list(re.finditer(r'\bpublic\s+const\s+string\s+' + name + r'\s*=', code))
        if len(declarations) != 1:
            raise ValueError('Missing/duplicate runtime fingerprint: ' + name)
        tail = comments[declarations[0].end():]
        literal = re.match(r'\s*"([0-9a-fA-F]{64})"\s*;', tail)
        if literal is None:
            raise ValueError('Invalid runtime fingerprint: ' + name)
        if literal[1].lower() != expected:
            differences.append({'field': name, 'expected': expected, 'actual': literal[1].lower()})
    for array, expected in [('PassNames', [row['name'] for row in reference['passes']]),
                            ('DualDungeonDisabledPassNames', reference['specialSeedFiltering']['disabledNames'])]:
        actual = _runtime_array(text, array)
        if actual != expected:
            at = next((index for index, values in enumerate(zip(expected, actual)) if values[0] != values[1]), min(len(expected), len(actual)))
            differences.append({'field': array, 'firstDifference': at, 'expectedCount': len(expected), 'actualCount': len(actual),
                                'expected': expected[at] if at < len(expected) else None, 'actual': actual[at] if at < len(actual) else None})
    return {'accepted': not differences, 'differences': differences}


def check(reference: dict, repo: Path = ROOT, source_root: Path | None = None) -> dict:
    validate(reference)
    path = Path(repo) / TARGET
    try:
        result = _check_text(reference, path.read_text(encoding='utf-8-sig'))
    except ValueError as error:
        result = {'accepted': False, 'error': str(error)}
    result.update(operation='worldgen-check', runtimeCatalogSha256=facts.digest(path), coverage=reference['coverage'],
                  sourceFreshness='not-rechecked-without-source-root' if source_root is None else 'checked',
                  scope='Registration metadata/fingerprints only; no generated-world, cell/RNG or AI parity claim')
    if source_root is not None:
        changed = [name for name, expected in reference['sourceInputs'].items()
                   if not (Path(source_root) / name).is_file() or facts.digest(Path(source_root) / name) != expected]
        result['sourceDifferences'] = changed
        if changed:
            result['accepted'] = False
    return result


def mutate(reference: dict, repo: Path = ROOT) -> dict:
    baseline = check(reference, repo)
    if not baseline['accepted']:
        return {'accepted': False, 'operation': 'worldgen-mutate', 'baseline': baseline}
    path = Path(repo) / TARGET
    before = path.read_bytes()
    text = path.read_text(encoding='utf-8-sig')
    names = [row['name'] for row in reference['passes']]
    mutations = {
        'swap-first-two-passes': text.replace(json.dumps(names[0]) + ',', '"__SWAP__",', 1)
            .replace(json.dumps(names[1]) + ',', json.dumps(names[0]) + ',', 1).replace('"__SWAP__",', json.dumps(names[1]) + ',', 1),
        'change-final-pass-name': text.replace(json.dumps(names[-1]) + ',', '"Changed final pass",', 1),
        'change-source-body-fingerprint': text.replace(reference['fingerprints']['AddPassesSha256'], '0' * 64, 1),
        'remove-filter-entry': _remove_filter_entry(text, reference['specialSeedFiltering']['disabledNames'][0]),
        'comment-out-pass-array': text.replace('private static readonly string[] PassNames', '// private static readonly string[] PassNames', 1),
    }
    results = []
    for name, mutant in mutations.items():
        if mutant == text:
            raise ValueError('Worldgen mutation did not change its input: ' + name)
        try:
            detected = not _check_text(reference, mutant)['accepted']
        except ValueError:
            detected = True
        results.append({'mutation': name, 'detected': detected})
    if path.read_bytes() != before:
        raise ValueError('Runtime catalog changed during in-memory mutations')
    return {'accepted': all(row['detected'] for row in results), 'operation': 'worldgen-mutate',
            'mutants': results, 'runtimeCatalogSha256': facts.digest(path),
            'scope': 'Named bounded metadata mutants only; production input untouched'}


def _remove_filter_entry(text: str, name: str) -> str:
    code, _ = _views(text)
    declaration = re.search(r'private\s+static\s+readonly\s+string\[\]\s+DualDungeonDisabledPassNames\s*=', code)
    if declaration is None:
        raise ValueError('Missing filter array')
    start = declaration.start()
    suffix = text[start:].replace('        ' + json.dumps(name) + ',\n', '', 1)
    return text[:start] + suffix
