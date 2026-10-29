"""Schema3 exact state paths; a capability profile of the existing candidate engine."""
from __future__ import annotations

import copy
import re

from . import source_analysis as analysis
from .scalar_values import scalar, SCALARS, IDENTIFIER, SYMBOL

VECTOR = 'Microsoft.Xna.Framework.Vector2'


def argument(value, kind):
    return type(value) is dict and set(value) == {'x', 'y'} and all(scalar(v, 'float') for v in value.values()) \
        if kind == VECTOR else scalar(value, kind)


def validate_request(data):
    if type(data) is not dict or set(data) != {'schemaVersion', 'analysis', 'returnType', 'fields', 'cases'} or \
            type(data['schemaVersion']) is not int or data['schemaVersion'] != 3 or data['returnType'] not in (*SCALARS, 'void'):
        raise ValueError('Invalid schema3 state request')
    result = copy.deepcopy(data)
    result['analysis'] = analysis.validate_request(data['analysis'])
    request = result['analysis']
    if len(request['entries']) != 1 or request['maxDepth'] != 0 or request['maxMethods'] != 1:
        raise ValueError('State profile requires one bounded entry')
    parameters = request['entries'][0]['parameterTypes']
    if any(kind not in (*SCALARS, VECTOR) for kind in parameters):
        raise ValueError('Only scalar and readonly by-value Vector2 inputs are admitted')
    if type(data['fields']) is not list or not 1 <= len(data['fields']) <= 32:
        raise ValueError('Expected1..32 exact state capabilities')
    identities, fields = set(), {}
    for field in data['fields']:
        if type(field) is not dict or set(field) != {'path', 'index', 'target', 'type', 'access'} or \
                field['type'] not in SCALARS or field['access'] not in ('readOnly', 'readWrite') or \
                type(field['path']) is not list or not 1 <= len(field['path']) <= 2 or \
                any(type(s) is not str or not SYMBOL.fullmatch(s) for s in field['path']) or \
                type(field['target']) is not str or not IDENTIFIER.fullmatch(field['target']) or field['target'] == '__state' or \
                field['target'] in fields or field['index'] is not None and (type(field['index']) is not int or not 0 <= field['index'] <= 3):
            raise ValueError('Invalid state capability shape')
        if len(field['path']) == 2 and (field['path'][1] not in (VECTOR + '.X', VECTOR + '.Y') or field['index'] is not None or field['type'] != 'float'):
            raise ValueError('Only exact Vector2 scalar leaves are admitted')
        if field['index'] is not None and (len(field['path']) != 1 or field['path'][0].split('.')[-1] not in ('ai', 'localAI') or field['type'] != 'float'):
            raise ValueError('Only fixed ai/localAI float slots are admitted')
        identity = (tuple(field['path']), field['index'])
        if identity in identities:
            raise ValueError('Duplicate semantic state capability')
        identities.add(identity); fields[field['target']] = field['type']
    if type(data['cases']) is not list or not 1 <= len(data['cases']) <= 4096:
        raise ValueError('Expected1..4096 explicit state cases')
    for case in data['cases']:
        if type(case) is not dict or set(case) != {'state', 'arguments', 'rngSeed'} or type(case['state']) is not dict or \
                set(case['state']) != set(fields) or any(not scalar(case['state'][key], kind) for key, kind in fields.items()) or \
                type(case['arguments']) is not list or len(case['arguments']) != len(parameters) or \
                any(not argument(value, kind) for value, kind in zip(case['arguments'], parameters)) or not scalar(case['rngSeed'], 'int'):
            raise ValueError('Incomplete or mistyped state preimage')
    return result


def validate_observation(request, data):
    count = len(request['cases'])
    entry = request['analysis']['entries'][0]
    identity = entry['type'] + '.' + entry['method'] + '(' + ', '.join(entry['parameterTypes']) + ')'
    if type(data) is not dict or type(data.get('schemaVersion')) is not int or data['schemaVersion'] != 3 or \
            data.get('kind') != 'source-state-candidate-verification' or data.get('entry') != identity or \
            data.get('returnType') != request['returnType'] or type(data.get('cases')) is not int or data['cases'] != count or \
            type(data.get('originalMethodCalls')) is not int or data['originalMethodCalls'] != count or \
            any(data.get(flag) is not True for flag in ('accepted', 'candidateOnly', 'originalCarrierCompiled', 'methodQualified',
                'originalRngCursorAndAll56SlotsUnchanged', 'allUnmappedDirectActorFieldsAndArraysUnchanged', 'directArrayAliasesRejected')) or \
            data.get('wholeOriginalTypeCompiled') is not False or type(data.get('compilationErrorCount')) is not int or data['compilationErrorCount'] < 0:
        raise ValueError('State observation identity or scope differs')
    fields = {field['target']: field for field in request['fields']}
    if type(data.get('observations')) is not list or len(data['observations']) != count:
        raise ValueError('Missing complete state observations')
    for index, (case, row) in enumerate(zip(request['cases'], data['observations'])):
        if type(row) is not dict or type(row.get('index')) is not int or row['index'] != index or \
                type(row.get('rngSeed')) is not int or row['rngSeed'] != case['rngSeed'] or \
                row.get('initial') != case['state'] or row.get('arguments') != case['arguments'] or \
                type(row.get('initial')) is not dict or set(row['initial']) != set(fields) or \
                any(not scalar(row['initial'][key], field['type']) for key, field in fields.items()) or \
                type(row['arguments']) is not list or len(row['arguments']) != len(entry['parameterTypes']) or \
                any(not argument(value, kind) for value, kind in zip(row['arguments'], entry['parameterTypes'])):
            raise ValueError('State observation preimage differs')
        for key in ('original', 'carrier', 'candidate'):
            value = row.get(key)
            if (value is not None if request['returnType'] == 'void' else not scalar(value, request['returnType'])):
                raise ValueError('Missing or mistyped state result')
        if row['original'] != row['carrier'] or row['original'] != row['candidate']:
            raise ValueError('Original/carrier/candidate return mismatch')
        for key in ('originalState', 'carrierState', 'candidateState'):
            state = row.get(key)
            if type(state) is not dict or set(state) != set(fields) or any(not scalar(state[name], field['type']) for name, field in fields.items()):
                raise ValueError('Incomplete state result')
            if any(state[name] != case['state'][name] for name, field in fields.items() if field['access'] == 'readOnly'):
                raise ValueError('Readonly state capability changed')
        if row['originalState'] != row['carrierState'] or row['originalState'] != row['candidateState']:
            raise ValueError('Original/carrier/candidate state mismatch')
        for prefix in ('actor',):
            before, after = row.get(prefix + 'Before'), row.get(prefix + 'After')
            if any(type(v) is not dict or set(v) != {'sha256', 'fields'} or type(v['fields']) is not int or not 1 <= v['fields'] <= 4096 or \
                   type(v['sha256']) is not str or not analysis.SHA.fullmatch(v['sha256']) for v in (before, after)) or before != after:
                raise ValueError('Unmapped actor fingerprint changed or absent')
        for key in ('rngBefore', 'rngAfter'):
            value = row.get(key)
            if type(value) is not dict or set(value) != {'cursor', 'slots'} or type(value['cursor']) is not int or not 0 <= value['cursor'] < 56 or \
                    type(value['slots']) is not list or len(value['slots']) != 56 or any(not scalar(v, 'int') for v in value['slots']):
                raise ValueError('Missing physical RNG')
        if row['rngBefore'] != row['rngAfter']:
            raise ValueError('Original RNG changed')
    controls = data.get('mutants')
    if type(controls) is not list or not 1 <= len(controls) <= 128:
        raise ValueError('Missing state mutation controls')
    names = set()
    for control in controls:
        if type(control) is not dict or type(control.get('mutation')) is not str or \
                not re.fullmatch(r'omit-operation-[0-9]+|invert-result|perturb-result', control['mutation']) or control['mutation'] in names or \
                type(control.get('cases')) is not int or control['cases'] != count or type(control.get('differences')) is not int or \
                not 1 <= control['differences'] <= count or type(control.get('errors')) is not int or control['errors'] != 0:
            raise ValueError('Ineffective, duplicate or failed state mutation control')
        names.add(control['mutation'])
    return data
