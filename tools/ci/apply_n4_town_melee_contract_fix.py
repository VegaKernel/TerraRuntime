#!/usr/bin/env python3
from pathlib import Path

p = Path('tools/ci/check_town_combat_source.py')
text = p.read_text(encoding='utf-8-sig')
old_boundary = 'state15 = slice_between(ai7, "else if (ai[0] == 15f)", "else if (ai[0] == 16f)", "AI_007 state 15")'
new_boundary = 'state15 = slice_between(ai7, "else if (ai[0] == 15f)", "else if (ai[0] == 24f)", "AI_007 state 15")'
if text.count(old_boundary) != 1:
    raise SystemExit('state15 source-contract boundary anchor missing or ambiguous')
text = text.replace(old_boundary, new_boundary, 1)

lines = text.splitlines()
indices = [i for i, line in enumerate(lines) if '"state 15 hostile immunity gate"' in line]
if len(indices) != 1:
    raise SystemExit('state15 hostile immunity contract anchor missing or ambiguous')
i = indices[0]
lines[i:i+1] = [
    '    require(state15, r"nPC2\\.immune\\[[^\\]]+\\]\\s*==\\s*0", "state 15 server immunity slot gate")',
    '    require(state15, r"!nPC2\\.dontTakeDamage.*?!nPC2\\.friendly.*?nPC2\\.damage\\s*>\\s*0", "state 15 hostile damageability gate")',
    '    require(state15, r"itemRectangle\\.Intersects\\(nPC2\\.Hitbox\\)", "state 15 melee hitbox intersection")',
]
text = '\n'.join(lines) + '\n'
p.write_text(text, encoding='utf-8')
print('Town melee source-contract boundary and immunity checks hardened')
