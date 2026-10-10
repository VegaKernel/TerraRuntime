"""Generate the 1.4.5.8 contextual prefix-acceptance appendix from independent numeric cells.

Inputs are the pinned 200-cell numeric fixture and existing generated normal prefix table.
No Terraria assembly, runtime code generation, or full ItemDefaults are required.
Usage: python tools/ci/generate_item_prefix_world_table.py cells.json normal-table.cs output.cs
"""
import hashlib
import json
import re
import struct
import sys
from pathlib import Path


def require(condition, message):
    if not condition:
        raise ValueError(message)


def normal_records(text):
    def span(name):
        match = re.search(rf'{name}\s*=>\s*\[([^\]]+)\]', text, re.S)
        require(match is not None, f'missing normal table span {name}')
        return [int(token, 16) if token.startswith('0x') else int(token)
                for token in re.findall(r'0x[0-9A-Fa-f]+|\d+', match[1])]
    data = bytes(span('Records'))
    require(len(data) == 886 * 8, 'normal table must contain the pinned 886 records')
    prefixes, starts = span('FamilyPrefixes'), span('FamilyStarts')
    records = {}
    for offset in range(0, len(data), 8):
        item, damage, animation, mana, flags, reserved = struct.unpack_from('<HhBBBB', data, offset)
        require(item not in records and reserved == 0, 'duplicate or malformed normal record')
        family = flags & 7
        end = starts[family + 1] if family + 1 < len(starts) else len(prefixes)
        records[item] = (damage, animation, mana, bool(flags & 8), prefixes[starts[family]:end])
    return records


def generate(fixture_path, normal_path):
    # Hash textual inputs canonically so Windows CRLF and Linux LF checkouts reproduce the same output.
    fixture_text = Path(fixture_path).read_text(encoding='utf-8-sig')
    raw = fixture_text.replace('\r\n', '\n').replace('\r', '\n').encode('utf-8')
    fixture = json.loads(raw.decode('utf-8'))
    require(fixture.get('format') == 'Terraria1458PrefixWorldAcceptance200', 'wrong fixture format')
    for field in ['sourceManifestSha256', 'sourceFixtureSha256']:
        require(re.fullmatch('[0-9a-f]{64}', fixture.get(field, '')) is not None, 'missing source provenance')
    normal_text = Path(normal_path).read_text(encoding='utf-8-sig')
    normal_bytes = normal_text.replace('\r\n', '\n').replace('\r', '\n').encode('utf-8')
    normal = normal_records(normal_bytes.decode('utf-8'))
    rows = fixture.get('cells', [])
    require(len(rows) == 200, 'exactly 200 source cells required')
    by_item = {}
    for row in rows:
        item, bits = row.get('item'), row.get('bits')
        require(type(item) is int and 0 < item < 6196 and type(bits) is int and 0 <= bits <= 7,
                'noncanonical item/context')
        require(bits not in by_item.setdefault(item, {}), 'duplicate source context cell')
        require(type(row.get('canPrefix')) is bool and type(row.get('noKnockBack')) is bool,
                'capability flags must be booleans')
        for name, low, high in [('damage', -32768, 32767), ('animation', 0, 255), ('mana', 0, 255)]:
            require(type(row.get(name)) is int and low <= row[name] <= high, 'invalid acceptance scalar')
        family = row.get('family')
        require((not row['canPrefix'] and family is None) or
                (row['canPrefix'] and isinstance(family, list) and len(family) > 0 and
                 all(type(value) is int and 0 < value < 98 for value in family)), 'invalid source family')
        by_item[item][bits] = row
    require(len(by_item) == 25, 'exactly 25 source variant identities required')
    conditions = {0xAA: 'world.RemixWorld', 0xCC: 'world.GetGoodWorld',
                  0x88: 'world.RemixWorld && world.GetGoodWorld', 0xF0: 'world.SkyblockWorld'}
    models = []
    for item, cells in sorted(by_item.items()):
        require(set(cells) == set(range(8)), 'missing source context')
        baseline = cells[0]
        require(baseline.get('variant') == 'None', 'normal world must have no selected variant')
        selected = [row for row in cells.values() if row.get('variant') != 'None']
        require(selected and len({row.get('variant') for row in selected}) == 1, 'ambiguous variant identity')
        mask = sum(1 << bits for bits, row in cells.items() if row.get('variant') != 'None')
        require(mask in conditions, 'unsupported source selector priority')
        fields = ('damage', 'animation', 'mana', 'noKnockBack', 'family', 'canPrefix')
        key = lambda row: tuple(json.dumps(row[field], sort_keys=True) for field in fields)
        require(len({key(row) for row in selected}) == 1, 'inconsistent selected acceptance records')
        require(all(key(row) == key(baseline) for row in cells.values() if row.get('variant') == 'None'),
                'inconsistent default acceptance records')
        active = selected[0]
        for row in cells.values():
            require(row['canPrefix'] == baseline['canPrefix'] and row['family'] == baseline['family'] and
                    row['noKnockBack'] == baseline['noKnockBack'], 'variant capability/family/zeroKB changed')
        require((item in normal) == baseline['canPrefix'], 'normal table capability mismatch')
        if baseline['canPrefix']:
            require(normal[item] == (baseline['damage'], baseline['animation'], baseline['mana'],
                                     baseline['noKnockBack'], baseline['family']), 'normal table metadata mismatch')
        models.append((item, conditions[mask], active, baseline['canPrefix']))
    require(sum(model[3] for model in models) == 18, 'expected 18 prefixable and seven nonprefixable variants')
    lines = ['// <auto-generated>',
             '// Generated by tools/ci/generate_item_prefix_world_table.py; do not edit by hand.',
             '// Official 1.4.5.8 ItemVariants selection and Item.Prefix acceptance; numeric metadata only.',
             '// Independent 200-cell fixture canonical UTF-8/LF SHA256: ' + hashlib.sha256(raw).hexdigest(),
             '// Normal prefix table canonical UTF-8/LF SHA256: ' + hashlib.sha256(normal_bytes).hexdigest(),
             '// Original capture manifest SHA256: ' + fixture['sourceManifestSha256'],
             '// </auto-generated>', 'using TerraRuntime.Contracts.Gameplay;', '',
             'namespace TerraRuntime.Gameplay.Items;', '',
             '/// <summary>Known source world flags; null at the call boundary means unknown provenance.</summary>',
             'public readonly record struct VanillaItemPrefixWorld1458(bool RemixWorld, bool GetGoodWorld, bool SkyblockWorld);', '',
             'internal static class PrefixWorldRecords1458', '{',
             '    internal static bool HasVariant(int item) => item is ' + ' or '.join(str(model[0]) for model in models) + ';',
             '', '    internal static bool IsVariantActive(int item, in VanillaItemPrefixWorld1458 world) => item switch', '    {']
    lines += [f'        {item} => {condition},' for item, condition, _, _ in models]
    lines += ['        _ => false', '    };', '',
              '    internal static bool TryGet(ItemTypeId item, in VanillaItemPrefixWorld1458 world, out VanillaItemPrefixRecord1458 record)',
              '    {', '        // Seven source variant identities cannot receive prefixes in any of the eight contexts.',
              '        if (!VanillaItemPrefixTable1458.TryGet(item, out record)) return false;',
              '        if (!IsVariantActive(item.Value, in world)) return true;',
              '        record = item.Value switch', '        {']
    lines += [f'            {item} => record with {{ Damage = {row["damage"]}, UseAnimation = {row["animation"]}, Mana = {row["mana"]} }},'
              for item, _, row, capable in models if capable]
    lines += ['            _ => record', '        };', '        return true;', '    }', '}']
    return '\n'.join(lines) + '\n'


if __name__ == '__main__':
    try:
        if len(sys.argv) != 4:
            raise ValueError('usage: generator cells.json normal-table.cs output.cs')
        output = generate(sys.argv[1], sys.argv[2])
        Path(sys.argv[3]).write_text(output, encoding='utf-8', newline='\n')
    except (ValueError, KeyError, TypeError, IndexError) as error:
        print(f'Invalid source fixture: {error}', file=sys.stderr)
        sys.exit(2)
