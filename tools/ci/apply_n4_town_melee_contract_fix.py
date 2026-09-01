#!/usr/bin/env python3
from pathlib import Path
p = Path('tools/ci/check_town_combat_source.py')
text = p.read_text(encoding='utf-8-sig')
old = 'state15 = slice_between(ai7, "else if (ai[0] == 15f)", "else if (ai[0] == 16f)", "AI_007 state 15")'
new = 'state15 = slice_between(ai7, "else if (ai[0] == 15f)", "else if (ai[0] == 24f)", "AI_007 state 15")'
if text.count(old) != 1:
    raise SystemExit('state15 source-contract boundary anchor missing or ambiguous')
p.write_text(text.replace(old, new, 1), encoding='utf-8')
print('Town melee source-contract state15 boundary fixed')
