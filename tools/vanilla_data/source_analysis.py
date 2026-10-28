"""Bounded offline semantic inventories, never code translation or parity acceptance."""
from __future__ import annotations

import copy
import hashlib
from pathlib import Path
import re
import subprocess
import time

from . import capture as facts
from .pass_probe import dll_pins

ROOT = facts.ROOT
PROJECT = ROOT / 'tools/vanilla_data/SourceProbe'
ALIASES = dict(zip(('Boolean', 'Byte', 'SByte', 'Int16', 'UInt16', 'Int32', 'UInt32', 'Int64',
                   'UInt64', 'Single', 'Double', 'Decimal', 'Char', 'String', 'Object'),
                  ('bool', 'byte', 'sbyte', 'short', 'ushort', 'int', 'uint', 'long',
                   'ulong', 'float', 'double', 'decimal', 'char', 'string', 'object')))
SHA = re.compile(r'[0-9a-f]{64}\Z')


def _read_bounded(path, maximum):
    with path.open('rb') as stream:
        raw = stream.read(maximum + 1)
    if len(raw) > maximum:
        raise ValueError('Input exceeds byte limit: ' + str(path))
    return raw


def _digest_bounded(path, maximum):
    digest = hashlib.sha256()
    size = 0
    with path.open('rb') as stream:
        while chunk := stream.read(min(1024 * 1024, maximum + 1 - size)):
            size += len(chunk)
            if size > maximum:
                raise ValueError('Input exceeds byte limit: ' + str(path))
            digest.update(chunk)
    return digest.hexdigest()


def _text(value, name, maximum=1024):
    if type(value) is not str or not value.strip() or value != value.strip() or len(value) > maximum or '\0' in value:
        raise ValueError('Invalid ' + name)
    return value


def _relative(value):
    value = _text(value, 'relative source path')
    if '\\' in value or ':' in value or not value.endswith('.cs') or any(
            part in ('', '.', '..') or part != part.rstrip(' .') for part in value.split('/')):
        raise ValueError('Source paths must be canonical relative forward-slash .cs paths')
    return value


def _no_link(path, stop=None):
    current = path
    while True:
        if current.is_symlink() or (hasattr(current, 'is_junction') and current.is_junction()):
            raise ValueError('Linked input is unsupported: ' + str(current))
        if current == stop or current.parent == current:
            break
        current = current.parent


def validate_request(data):
    required = {'schemaVersion', 'sourceRoot', 'assembly', 'sources', 'entries'}
    if type(data) is not dict or not required <= data.keys() or data.keys() - required - {'maxDepth', 'maxMethods'}:
        raise ValueError('Invalid source-analysis request fields')
    if type(data['schemaVersion']) is not int or data['schemaVersion'] != 1:
        raise ValueError('Unsupported source-analysis schema')
    result = copy.deepcopy(data)
    for name, default, low, high in (('maxDepth', 1, 0, 4), ('maxMethods', 32, 1, 128)):
        value = data.get(name, default)
        if type(value) is not int or not low <= value <= high:
            raise ValueError('Invalid ' + name)
        result[name] = value
    root = Path(_text(data['sourceRoot'], 'sourceRoot'))
    assembly = Path(_text(data['assembly'], 'assembly'))
    _no_link(root); _no_link(assembly)
    if not root.is_dir() or not assembly.is_file():
        raise ValueError('Source root and reference assembly must exist')
    root = root.resolve(strict=True)
    result['sourceRoot'], result['assembly'] = str(root), str(assembly.resolve(strict=True))
    sources = data['sources']
    if type(sources) is not list or not 1 <= len(sources) <= 256:
        raise ValueError('Expected 1..256 explicit source files')
    names = [_relative(name) for name in sources]
    if len({name.casefold() for name in names}) != len(names):
        raise ValueError('Duplicate source paths')
    total = 0
    for name in names:
        path = root / name
        _no_link(path, root)
        if not path.is_file() or not path.resolve(strict=True).is_relative_to(root):
            raise ValueError('Missing or escaping source file: ' + name)
        size = path.stat().st_size
        total += size
        if size > 32 * 1024 * 1024 or total > 128 * 1024 * 1024:
            raise ValueError('Source byte budget exceeded')
    entries = data['entries']
    if type(entries) is not list or not 1 <= len(entries) <= 32:
        raise ValueError('Expected 1..32 method entries')
    identities = set()
    for entry in result['entries']:
        if type(entry) is not dict or set(entry) != {'path', 'type', 'method', 'parameterTypes'}:
            raise ValueError('Invalid method entry fields')
        if _relative(entry['path']) not in names:
            raise ValueError('Entry source is outside the explicit allowlist')
        _text(entry['type'], 'entry type', 512); _text(entry['method'], 'entry method', 256)
        parameters = entry['parameterTypes']
        if type(parameters) is not list or len(parameters) > 64:
            raise ValueError('Invalid parameter type list')
        for value in parameters:
            if not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_.,<>\[\]? *:]*', _text(value, 'parameter type', 512)):
                raise ValueError('Unsupported parameter type spelling')
        entry['parameterTypes'] = [re.sub(r'\bSystem\.(' + '|'.join(ALIASES) + r')\b',
            lambda match: ALIASES[match[1]], value) for value in parameters]
        identity = (entry['path'], entry['type'], entry['method'], tuple(entry['parameterTypes']))
        if identity in identities:
            raise ValueError('Duplicate method entry')
        identities.add(identity)
    return result


def _tool_inputs():
    paths = list(PROJECT.glob('*.cs')) + [PROJECT / 'SourceProbe.csproj']
    paths += [ROOT / name for name in ('Directory.Build.props', 'global.json', 'tools/vanilla.py',
        'tools/vanilla_data/source_analysis.py', 'tools/vanilla_data/cli.py',
        'tools/vanilla_data/capture.py', 'tools/vanilla_data/reference.json', 'tools/vanilla_data/pass_probe.py',
        'tools/vanilla_data/scalar_candidate.py', 'tools/vanilla_data/scalar_values.py')]
    return {path.relative_to(ROOT).as_posix(): facts.digest(path) for path in sorted(paths)}


def _inputs(request):
    root, assembly = Path(request['sourceRoot']), Path(request['assembly'])
    references = sorted(assembly.parent.glob('*.dll'))
    if len(references) > 128:
        raise ValueError('Reference count exceeded')
    references.append(assembly)
    for path in references:
        _no_link(path)
        if not path.is_file() or path.stat().st_size > 64 * 1024 * 1024:
            raise ValueError('Invalid or oversized reference input')
    source = {name: _digest_bounded(root / name, 32 * 1024 * 1024) for name in request['sources']}
    physical = {path.name: _digest_bounded(path, 64 * 1024 * 1024) for path in references}
    return {'sources': source, 'references': physical, 'tools': _tool_inputs()}


def validate_observation(data):
    if type(data) is not dict or type(data.get('schemaVersion')) is not int or data['schemaVersion'] != 1 or \
            data.get('kind') != 'source-semantic-inventory' or data.get('analysisOnly') is not True:
        raise ValueError('Invalid semantic observation envelope')
    for name in ('accepted', 'sourceClosureComplete'):
        if type(data.get(name)) is not bool:
            raise ValueError('Invalid observation ' + name)
    for name in ('entries', 'methods', 'errors', 'frontiers', 'limits'):
        if type(data.get(name)) is not list:
            raise ValueError('Missing observation ' + name)
    for name in ('errors', 'frontiers', 'limits'):
        if any(type(value) is not str or not value.strip() for value in data[name]):
            raise ValueError('Malformed observation ' + name)
    if not 0 <= len(data['entries']) <= 32 or not 0 <= len(data['methods']) <= 128:
        raise ValueError('Invalid entry/method observations')
    if any(type(name) is not str or not name.strip() for name in data['entries']) or len(set(data['entries'])) != len(data['entries']):
        raise ValueError('Invalid or duplicate entry identities')
    if any(type(method) is not dict or type(method.get('id')) is not str or not method['id'].strip() for method in data['methods']):
        raise ValueError('Invalid method identity')
    if len({method['id'] for method in data['methods']}) != len(data['methods']):
        raise ValueError('Duplicate method observations')
    for method in data['methods']:
        if type(method.get('qualified')) is not bool or any(type(method.get(name)) is not list for name in
                ('diagnostics', 'calls', 'accesses', 'uncertainties')):
            raise ValueError('Incomplete method inventory')
        for key in ('path', 'type', 'name'):
            _text(method.get(key), 'method ' + key, 4096)
        if type(method.get('parameterTypes')) is not list or any(type(value) is not str or not value for value in method['parameterTypes']):
            raise ValueError('Incomplete method parameter identity')
        if type(method.get('depth')) is not int or not 0 <= method['depth'] <= 4 or \
                type(method.get('line')) is not int or method['line'] < 1:
            raise ValueError('Invalid method location/depth')
        for call in method['calls']:
            if type(call) is not dict or call.get('dispatch') not in ('direct', 'virtual', 'delegate', 'dynamic') or \
                    call.get('origin') not in ('source', 'metadata-or-nonmethod-body', 'unresolved') or \
                    type(call.get('rng')) is not bool or type(call.get('refArguments')) is not bool or \
                    (call.get('symbol') is not None and type(call['symbol']) is not str):
                raise ValueError('Malformed call inventory')
        if any(type(value) is not dict for name in ('diagnostics', 'accesses') for value in method[name]):
            raise ValueError('Malformed diagnostic/access inventory')
        if any(type(value) is not str or not value.strip() for value in method['uncertainties']):
            raise ValueError('Malformed method uncertainties')
        required_frontiers = {value + ': ' + method['id'] for value in method['uncertainties']}
        for call in method['calls']:
            if call['dispatch'] in ('virtual', 'delegate'):
                required_frontiers.add(call['dispatch'] + ': ' + str(call['symbol']))
            elif call['origin'] == 'metadata-or-nonmethod-body':
                required_frontiers.add(call['origin'] + ': ' + str(call['symbol']))
        if not required_frontiers <= set(data['frontiers']):
            raise ValueError('Opaque effect frontier omitted from observation')
        if method['qualified'] and (method['diagnostics'] or any(call.get('dispatch') == 'dynamic' or
                call.get('origin') == 'unresolved' for call in method['calls'])):
            raise ValueError('Invalid method qualification')
    qualified = bool(data['entries']) and bool(data['methods']) and not data['errors'] and all(method['qualified'] for method in data['methods'])
    if data['accepted'] and not set(data['entries']) <= {method['id'] for method in data['methods']}:
        raise ValueError('Accepted inventory omits an entry')
    if data['accepted'] != qualified or data['sourceClosureComplete'] != (qualified and not data['frontiers']):
        raise ValueError('Observation acceptance is inconsistent')
    for name in ('sourceSha256', 'referenceSha256'):
        if type(data.get(name)) is not dict or not data[name] or any(
                type(key) is not str or type(value) is not str or not SHA.fullmatch(value) for key, value in data[name].items()):
            raise ValueError('Invalid observation input pins')
    if type(data.get('compilationErrorCount')) is not int or data['compilationErrorCount'] < 0 or \
            not isinstance(data.get('compiler'), str):
        raise ValueError('Missing compiler diagnostics/identity')
    return data


def analyze(request_path: Path, output_dir: Path):
    if request_path.stat().st_size > 1024 * 1024:
        raise ValueError('Request exceeds 1 MiB')
    raw_request = _read_bounded(request_path, 1024 * 1024)
    request = validate_request(facts.parse_json(raw_request.decode('utf-8-sig')))
    admitted = facts.reference()
    assembly = Path(request['assembly'])
    # The output is evidence, never an input or source rewrite destination.
    output_dir = output_dir.resolve()
    if output_dir.exists() or output_dir.is_relative_to(Path(request['sourceRoot'])) or output_dir.is_relative_to(assembly.parent):
        raise ValueError('Choose a new output directory outside original inputs')
    before = _inputs(request)
    if before['references'].get(assembly.name) != admitted['assemblySha256'] or any(
            before['references'].get(name) != expected for name, expected in admitted['dependenciesSha256'].items()):
        raise ValueError('Official reference differs from the admitted original input snapshot')
    output_dir.mkdir(parents=True, exist_ok=False)
    def save(name, value):
        with (output_dir / name).open('xb') as stream:
            stream.write(facts.canonical(value) + b'\n')
    save('request.json', request); save('input-before.json', before)
    started = time.perf_counter(); errors = []; commands = []; pins = {}; observation = None; observation_hash = None
    def execute(name, command, timeout):
        with (output_dir / (name + '.log')).open('xb') as stream:
            try:
                process = subprocess.run(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT, timeout=timeout)
                commands.append({'name': name, 'command': command, 'exitCode': process.returncode})
                if process.returncode == 0:
                    return True
                errors.append(name + ': exit ' + str(process.returncode))
            except (OSError, subprocess.TimeoutExpired) as error:
                errors.append(name + ': ' + str(error))
        return False
    binary = PROJECT / 'bin/Release/net11.0/SourceProbe.dll'
    if execute('build', ['dotnet', 'build', str(PROJECT / 'SourceProbe.csproj'), '-c', 'Release', '-warnaserror', '-v', 'quiet'], 120):
        pins = dll_pins(binary.parent)
        required = {'SourceProbe.dll', 'Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll'}
        if not required <= pins.keys():
            errors.append('Fresh backend lacks compiler DLL closure')
        elif execute('probe', ['dotnet', str(binary), str(output_dir / 'request.json'), str(output_dir / 'observation.json')], 90):
            try:
                path = output_dir / 'observation.json'
                raw = _read_bounded(path, 32 * 1024 * 1024)
                observation_hash = hashlib.sha256(raw).hexdigest()
                observation = validate_observation(facts.parse_json(raw.decode('utf-8-sig')))
                if observation['sourceSha256'] != before['sources'] or {
                        name: value for name, value in observation['referenceSha256'].items() if '::' not in name} != before['references']:
                    raise ValueError('Observed source/reference pins differ from runner')
                if not set(observation['entries']) <= {method['id'] for method in observation['methods']}:
                    # A rejected entry selection/budget can legitimately omit entries.
                    if observation['accepted']:
                        raise ValueError('Accepted observation omits an entry')
                if observation['accepted']:
                    wanted = {(entry['path'], entry['type'], entry['method'], tuple(entry['parameterTypes'])) for entry in request['entries']}
                    observed = {(method['path'], method['type'], method['name'], tuple(method['parameterTypes']))
                        for method in observation['methods'] if method['id'] in observation['entries']}
                    if observed != wanted or len(observation['entries']) != len(wanted):
                        raise ValueError('Observation entry identities differ from the executed request')
                    if any(method['path'] not in request['sources'] or type(method['depth']) is not int or
                            not 0 <= method['depth'] <= request['maxDepth'] for method in observation['methods']) or \
                            len(observation['methods']) > request['maxMethods']:
                        raise ValueError('Observed methods differ from admitted source/budgets')
            except (ValueError, OSError, KeyError, TypeError) as error:
                errors.append('Observation: ' + str(error))
    try:
        inputs_unchanged = _inputs(request) == before and _read_bounded(request_path, 1024 * 1024) == raw_request
        dlls_after = dll_pins(binary.parent) if pins else {}
        if not inputs_unchanged: errors.append('Input membership/content changed')
        if pins and dlls_after != pins: errors.append('Executed backend DLL closure changed')
        if facts.read_json(output_dir / 'request.json') != request: errors.append('Executed request changed')
        if observation_hash and _digest_bounded(output_dir / 'observation.json', 32 * 1024 * 1024) != observation_hash:
            errors.append('Observation bytes changed after validation')
    except (OSError, ValueError) as error:
        inputs_unchanged = False; dlls_after = {}; errors.append('Input recheck: ' + str(error))
    result = {'schemaVersion': 1, 'operation': 'source-analyze', 'accepted': not errors and observation is not None and observation['accepted'],
        'analysisOnly': True, 'sourceClosureComplete': not errors and observation is not None and observation['sourceClosureComplete'],
        'methods': len(observation['methods']) if observation else 0, 'inputsUnchanged': inputs_unchanged,
        'inputSha256': facts.data_digest(before), 'dllsBefore': pins, 'dllsAfter': dlls_after,
        'validationErrors': errors, 'analysisErrors': observation['errors'] if observation else [],
        'compilationErrorCount': observation['compilationErrorCount'] if observation else None,
        'observationSha256': observation_hash,
        'commands': commands, 'wallSeconds': time.perf_counter() - started,
        'scope': 'Explicit method-span semantic inventory only; no automatic translation, full-project compilation, effect equivalence or vanilla parity.'}
    save('result.json', result)
    return result
