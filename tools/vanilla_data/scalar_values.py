"""Schema2 read-only scalar-return contracts; execution stays in adapt-scalar."""
from __future__ import annotations

import copy
import re

from . import source_analysis as analysis

SCALARS = ('bool', 'int', 'float')
IDENTIFIER = re.compile(r'[A-Za-z_][A-Za-z0-9_]{0,127}\Z')
SYMBOL = re.compile(r'[A-Za-z_][A-Za-z0-9_.]{0,511}\Z')
CLASS = re.compile(r'[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+\Z')


def scalar(value, kind):
    if kind == 'bool':
        return type(value) is bool
    if kind == 'int':
        return type(value) is int and -(2**31) <= value < 2**31
    return kind == 'float' and type(value) is dict and set(value) == {'bits'} and \
        type(value['bits']) is int and 0 <= value['bits'] < 2**32 and value['bits'] & 0x7f800000 != 0x7f800000


def validate_request(data):
    if type(data) is not dict or set(data) != {'schemaVersion', 'analysis', 'returnType', 'fields', 'cases'} or \
            type(data['schemaVersion']) is not int or data['schemaVersion'] != 2 or data['returnType'] not in SCALARS:
        raise ValueError('Invalid schema2 scalar-return request')
    result = copy.deepcopy(data)
    result['analysis'] = analysis.validate_request(data['analysis'])
    request = result['analysis']
    if len(request['entries']) != 1 or request['maxDepth'] != 0 or request['maxMethods'] != 1:
        raise ValueError('Value candidate requires one closed entry')
    parameters = request['entries'][0]['parameterTypes']
    references = [i for i, kind in enumerate(parameters) if kind not in SCALARS]
    if len(references) != 1 or not CLASS.fullmatch(parameters[references[0]]):
        raise ValueError('Exactly one explicit class receiver and scalar by-value parameters required')
    reference = references[0]
    fields = data['fields']
    if type(fields) is not list or not 1 <= len(fields) <= 32:
        raise ValueError('Expected1..32 read-only field capabilities')
    identities, targets, receivers = set(), {}, set()
    for field in fields:
        if type(field) is not dict or set(field) != {'symbol', 'target', 'type', 'receiver', 'access'} or \
                field['type'] not in SCALARS or field['access'] != 'readOnly' or \
                type(field['symbol']) is not str or not SYMBOL.fullmatch(field['symbol']) or \
                type(field['target']) is not str or not IDENTIFIER.fullmatch(field['target']) or field['target'] == '__state' or \
                type(field['receiver']) is not str or not IDENTIFIER.fullmatch(field['receiver']) or field['receiver'] == '__state' or \
                field['symbol'] in identities or field['target'] in targets:
            raise ValueError('Invalid, duplicate or unknown read-only receiver field capability')
        identities.add(field['symbol']); targets[field['target']] = field['type']
        if field['receiver'] != 'this':
            receivers.add(field['receiver'])
    if len(receivers) != 1:
        raise ValueError('Exact non-null reference receiver must have mapped observed fields')
    receiver = next(iter(receivers))
    cases = data['cases']
    if type(cases) is not list or not 1 <= len(cases) <= 4096:
        raise ValueError('Expected1..4096 explicit value cases')
    for case in cases:
        if type(case) is not dict or set(case) != {'state', 'arguments', 'rngSeed'} or \
                type(case['state']) is not dict or set(case['state']) != set(targets) or \
                any(not scalar(case['state'][name], kind) for name, kind in targets.items()) or \
                type(case['arguments']) is not list or len(case['arguments']) != len(parameters) or \
                not scalar(case['rngSeed'], 'int'):
            raise ValueError('Incomplete, nonfinite or mistyped value preimage')
        for index, (value, kind) in enumerate(zip(case['arguments'], parameters)):
            if index == reference:
                if type(value) is not dict or value != {'receiver': receiver}:
                    raise ValueError('Reference argument differs from explicit field receiver')
            elif not scalar(value, kind):
                raise ValueError('Value argument differs from exact scalar type/domain')
    return result


def validate_observation(request, data):
    entry = request['analysis']['entries'][0]
    identity = entry['type'] + '.' + entry['method'] + '(' + ', '.join(entry['parameterTypes']) + ')'
    count = len(request['cases'])
    if type(data) is not dict or type(data.get('schemaVersion')) is not int or data['schemaVersion'] != 2 or \
            data.get('kind') != 'source-value-candidate-verification' or data.get('accepted') is not True or \
            data.get('candidateOnly') is not True or data.get('entry') != identity or data.get('returnType') != request['returnType'] or \
            type(data.get('cases')) is not int or data['cases'] != count or type(data.get('originalMethodCalls')) is not int or data['originalMethodCalls'] != count or \
            data.get('originalCarrierCompiled') is not True or data.get('wholeOriginalTypeCompiled') is not False or data.get('methodQualified') is not True or \
            data.get('allDirectActorFieldsAndArraysUnchanged') is not True or data.get('originalRngCursorAndAll56SlotsUnchanged') is not True or \
            type(data.get('compilationErrorCount')) is not int or data['compilationErrorCount'] < 0:
        raise ValueError('Value observation identity, counts or ownership scope differs')
    fields = {field['target']: field['type'] for field in request['fields']}
    rows = data.get('observations')
    if type(rows) is not list or len(rows) != count:
        raise ValueError('Missing complete value observations')
    for index, (case, row) in enumerate(zip(request['cases'], rows)):
        if type(row) is not dict or type(row.get('index')) is not int or row['index'] != index or type(row.get('rngSeed')) is not int or \
                row['rngSeed'] != case['rngSeed'] or row.get('initial') != case['state'] or row.get('arguments') != case['arguments']:
            raise ValueError('Value observation preimage differs')
        if type(row['initial']) is not dict or set(row['initial']) != set(fields) or any(not scalar(row['initial'][name], kind) for name, kind in fields.items()) or \
                type(row['arguments']) is not list or len(row['arguments']) != len(entry['parameterTypes']):
            raise ValueError('Value observation preimage types differ')
        for value, kind, expected in zip(row['arguments'], entry['parameterTypes'], case['arguments']):
            if kind in SCALARS:
                if not scalar(value, kind):
                    raise ValueError('Value observation scalar argument type differs')
            elif type(value) is not dict or set(value) != {'receiver'} or value != expected:
                raise ValueError('Value observation reference argument differs')
        for key in ('original', 'carrier', 'candidate'):
            if not scalar(row.get(key), request['returnType']):
                raise ValueError('Missing or nonfinite raw-bit result')
        if row['original'] != row['carrier'] or row['original'] != row['candidate']:
            raise ValueError('Original/carrier/candidate value mismatch')
        for key in ('originalState', 'carrierState', 'candidateState'):
            value = row.get(key)
            if type(value) is not dict or set(value) != set(fields) or any(not scalar(value[name], kind) for name, kind in fields.items()) or value != case['state']:
                raise ValueError('Read-only complete state changed')
        for key in ('actor', 'receiver'):
            before, after = row.get(key + 'Before'), row.get(key + 'After')
            if any(type(value) is not dict or set(value) != {'sha256', 'fields'} or type(value['fields']) is not int or not 1 <= value['fields'] <= 4096 or \
                   type(value['sha256']) is not str or not analysis.SHA.fullmatch(value['sha256']) for value in (before, after)) or before != after:
                raise ValueError('Missing or changed actor field fingerprint')
        for key in ('rngBefore', 'rngAfter'):
            value = row.get(key)
            if type(value) is not dict or set(value) != {'cursor', 'slots'} or type(value['cursor']) is not int or not 0 <= value['cursor'] < 56 or \
                    type(value['slots']) is not list or len(value['slots']) != 56 or any(not scalar(v, 'int') for v in value['slots']):
                raise ValueError('Missing physical RNG state')
        if row['rngBefore'] != row['rngAfter']:
            raise ValueError('Original RNG changed')
    controls = data.get('mutants')
    if type(controls) is not list or not 1 <= len(controls) <= 32:
        raise ValueError('Missing value mutation controls')
    names = set()
    for control in controls:
        if type(control) is not dict or type(control.get('mutation')) is not str or \
                not re.fullmatch(r'(?:omit-operation-[0-9]+|swap-category-precedence)', control['mutation']) or control['mutation'] in names or \
                type(control.get('cases')) is not int or control['cases'] != count or type(control.get('differences')) is not int or not 1 <= control['differences'] <= count or \
                type(control.get('errors')) is not int or control['errors'] != 0:
            raise ValueError('Missing, duplicate or ineffective value control')
        names.add(control['mutation'])
    return data
