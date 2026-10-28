"""Offline, bounded semantic scalar adaptation and actual-original differential."""
from __future__ import annotations

import copy
import json
from pathlib import Path
import re
import subprocess
import time

from . import capture as facts
from . import source_analysis as analysis
from .pass_probe import dll_pins

ROOT = facts.ROOT
IDENTIFIER = re.compile(r'[A-Za-z_][A-Za-z0-9_]*\Z')


def _scalar(value, kind):
    return type(value) is bool if kind == 'bool' else type(value) is int and -(2**31) <= value < 2**31


def validate_request(data):
    if type(data) is not dict or set(data) != {'schemaVersion', 'analysis', 'fields', 'cases'} or \
            type(data['schemaVersion']) is not int or data['schemaVersion'] != 1:
        raise ValueError('Invalid scalar candidate request envelope')
    result = copy.deepcopy(data)
    result['analysis'] = analysis.validate_request(data['analysis'])
    request = result['analysis']
    if len(request['entries']) != 1 or request['maxDepth'] != 0 or request['maxMethods'] != 1:
        raise ValueError('Scalar candidate requires one entry, maxDepth0 and maxMethods1')
    parameters = request['entries'][0]['parameterTypes']
    if any(kind not in ('int', 'bool') for kind in parameters):
        raise ValueError('Only by-value int/bool parameters are admitted')
    fields = data['fields']
    if type(fields) is not list or not 1 <= len(fields) <= 32:
        raise ValueError('Expected1..32 explicit exact field-symbol mappings')
    symbols, targets = set(), {}
    for field in fields:
        if type(field) is not dict or set(field) != {'symbol', 'target', 'type'} or field['type'] not in ('int', 'bool') or \
                type(field['symbol']) is not str or len(field['symbol']) > 512 or not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_.]*', field['symbol']) or \
                type(field['target']) is not str or len(field['target']) > 128 or not IDENTIFIER.fullmatch(field['target']) or \
                field['symbol'] in symbols or field['target'] in targets:
            raise ValueError('Invalid, duplicate, aliased or non-scalar field mapping')
        symbols.add(field['symbol']); targets[field['target']] = field['type']
    cases = data['cases']
    if type(cases) is not list or not 1 <= len(cases) <= 4096:
        raise ValueError('Expected1..4096 explicit scalar cases')
    for case in cases:
        if type(case) is not dict or set(case) != {'state', 'arguments', 'rngSeed'} or \
                type(case['state']) is not dict or set(case['state']) != set(targets) or \
                any(not _scalar(case['state'][name], kind) for name, kind in targets.items()) or \
                type(case['arguments']) is not list or len(case['arguments']) != len(parameters) or \
                any(not _scalar(value, kind) for value, kind in zip(case['arguments'], parameters)) or \
                not _scalar(case['rngSeed'], 'int'):
            raise ValueError('Case fields, arguments, seed or exact JSON scalar types differ')
    return result


def validate_observation(request, data):
    entry = request['analysis']['entries'][0]
    identity = entry['type'] + '.' + entry['method'] + '(' + ', '.join(entry['parameterTypes']) + ')'
    count = len(request['cases'])
    if type(data) is not dict or data.get('kind') != 'source-scalar-candidate-verification' or \
            data.get('accepted') is not True or data.get('candidateOnly') is not True or data.get('entry') != identity or \
            type(data.get('cases')) is not int or data['cases'] != count or type(data.get('originalMethodCalls')) is not int or data['originalMethodCalls'] != count or \
            data.get('originalCarrierCompiled') is not True or data.get('wholeOriginalTypeCompiled') is not False or \
            data.get('methodQualified') is not True or data.get('originalRngCursorAndAll56SlotsUnchanged') is not True or \
            type(data.get('compilationErrorCount')) is not int or data['compilationErrorCount'] < 0:
        raise ValueError('Scalar verification identity, scope or counts differ')
    fields = {field['target']: field['type'] for field in request['fields']}
    rows = data.get('observations')
    if type(rows) is not list or len(rows) != count:
        raise ValueError('Missing complete scalar observations')
    for index, (case, row) in enumerate(zip(request['cases'], rows)):
        if type(row) is not dict or type(row.get('index')) is not int or row['index'] != index or \
                type(row.get('rngSeed')) is not int or row['rngSeed'] != case['rngSeed'] or row.get('initial') != case['state'] or row.get('arguments') != case['arguments'] or \
                type(row.get('initial')) is not dict or any(not _scalar(row['initial'][name], kind) for name, kind in fields.items()) or \
                type(row.get('arguments')) is not list or any(not _scalar(value, kind) for value, kind in zip(row['arguments'], entry['parameterTypes'])):
            raise ValueError('Observation preimage differs from explicit case')
        for key in ('original', 'carrier', 'candidate'):
            value = row.get(key)
            if type(value) is not dict or set(value) != set(fields) or any(not _scalar(value[name], kind) for name, kind in fields.items()):
                raise ValueError('Missing or mistyped full state projection')
        if row['original'] != row['carrier'] or row['original'] != row['candidate']:
            raise ValueError('Original, carrier and candidate complete states differ')
        for key in ('rngBefore', 'rngAfter'):
            value = row.get(key)
            if type(value) is not dict or set(value) != {'cursor', 'slots'} or type(value['cursor']) is not int or \
                    not 0 <= value['cursor'] < 56 or type(value['slots']) is not list or len(value['slots']) != 56 or \
                    any(not _scalar(slot, 'int') for slot in value['slots']):
                raise ValueError('Missing full physical original RNG')
        if row['rngBefore'] != row['rngAfter']:
            raise ValueError('Unowned RNG effect observed')
    mutants = data.get('mutants')
    if type(mutants) is not list or len(mutants) != len(fields) or \
            {row.get('mutation') for row in mutants if type(row) is dict} != {'omit-field-write:' + name for name in fields} or \
            any(type(row.get('differences')) is not int or not 1 <= row['differences'] <= count or row.get('cases') != count for row in mutants):
        raise ValueError('Missing effective write omission control for each mapped state field')
    return data


def adapt(request_path, output_dir):
    request_path, output_dir = Path(request_path), Path(output_dir)
    raw = analysis._read_bounded(request_path, 1024 * 1024)
    request = validate_request(facts.parse_json(raw.decode('utf-8-sig')))
    analysis._no_link(output_dir)
    if output_dir.exists() or not output_dir.resolve().is_relative_to((ROOT / '.cache').resolve()):
        raise ValueError('Choose a NEW candidate output directory below this worktree .cache')
    before = analysis._inputs(request['analysis'])
    admitted = facts.reference()
    if before['references'].get(Path(request['analysis']['assembly']).name) != admitted['assemblySha256'] or any(
            before['references'].get(name) != digest for name, digest in admitted['dependenciesSha256'].items()):
        raise ValueError('Original reference differs from admitted official inputs')
    output_dir = output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=False)
    def save(name, value):
        with (output_dir / name).open('xb') as stream:
            stream.write(facts.canonical(value) + b'\n')
    save('request.json', request); save('input-before.json', before)
    commands, errors = [], []
    started = time.monotonic()
    def execute(name, command, timeout):
        with (output_dir / (name + '.log')).open('xb') as log:
            try:
                p = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, timeout=timeout)
                commands.append({'name': name, 'command': command, 'exitCode': p.returncode})
                if p.returncode == 0:
                    return True
                errors.append(name + ': exit' + str(p.returncode))
            except (OSError, subprocess.TimeoutExpired) as error:
                errors.append(name + ': ' + str(error))
        return False
    binary = analysis.PROJECT / 'bin/Release/net11.0/SourceProbe.dll'
    observation = None; pins = {}; observation_hash = None
    if execute('build', ['dotnet', 'build', str(analysis.PROJECT / 'SourceProbe.csproj'), '-c', 'Release', '-warnaserror', '-v', 'quiet'], 120):
        pins = dll_pins(binary.parent)
        if not {'SourceProbe.dll', 'Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll'} <= pins.keys():
            errors.append('Missing Roslyn/backend closure')
        elif execute('probe', ['dotnet', str(binary), '--adapt-scalar', str(output_dir / 'request.json'), str(output_dir / 'observation.json')], 120):
            observation_hash = facts.digest(output_dir / 'observation.json')
            try:
                observation = validate_observation(request, facts.read_json(output_dir / 'observation.json'))
                if observation.get('sourceSha256') != before['sources'] or \
                        {k: v for k, v in observation.get('referenceSha256', {}).items() if '::' not in k} != before['references']:
                    raise ValueError('Observed source/reference pins differ')
                for name, key in [('candidate.cs', 'candidateSha256'), ('original-carrier.cs', 'originalCarrierSha256'), ('candidate.dll', 'assemblySha256')]:
                    if facts.digest(output_dir / name) != observation.get(key): raise ValueError('Generated artifact differs: ' + name)
                for mutant in observation['mutants']:
                    name = 'omit-' + mutant['mutation'].removeprefix('omit-field-write:') + '.dll'
                    if facts.digest(output_dir / name) != mutant.get('assemblySha256'): raise ValueError('Mutant artifact differs')
                runtime = observation.get('runtimeReference', {})
                if analysis._digest_bounded(Path(runtime['path']), 64 * 1024 * 1024) != runtime['sha256']:
                    raise ValueError('Runtime compilation reference differs')
            except (ValueError, OSError, KeyError, TypeError) as error:
                errors.append('Observation: ' + str(error))
    try:
        unchanged = analysis._inputs(request['analysis']) == before and analysis._read_bounded(request_path, 1024 * 1024) == raw
        if not unchanged: errors.append('Source/reference/tool/request input drift')
        after = dll_pins(binary.parent) if pins else {}
        if after != pins: errors.append('Executed backend/Roslyn closure drift')
        if facts.read_json(output_dir / 'request.json') != request: errors.append('Executed request drift')
        if observation_hash and facts.digest(output_dir / 'observation.json') != observation_hash: errors.append('Observation drift')
    except (OSError, ValueError) as error:
        unchanged = False; after = {}; errors.append('Recheck: ' + str(error))
    artifacts = {p.name: facts.digest(p) for p in sorted(output_dir.iterdir()) if p.is_file()}
    result = {'schemaVersion': 1, 'operation': 'source-adapt-scalar', 'accepted': not errors and observation is not None,
              'candidateOnly': True, 'inputsUnchanged': unchanged, 'dllsBefore': pins, 'dllsAfter': after,
              'cases': observation.get('cases') if observation else None,
              'compilationErrorCount': observation.get('compilationErrorCount') if observation else None,
              'originalCarrierCompiled': observation is not None, 'wholeOriginalTypeCompiled': False,
              'validationErrors': errors, 'commands': commands, 'artifacts': artifacts,
              'wallSeconds': time.monotonic() - started,
              'scope': 'Cache-only automatic semantic bool/int state adaptation and compiled actual-original sampled differential. No whole AI translation, caller/state ownership, numeric/API/RNG mapping or runtime admission.'}
    save('result.json', result)
    return result
