#!/usr/bin/env python3
from pathlib import Path
p=Path('tools/ci/check_town_social_emotes_source.py')
text=p.read_text(encoding='utf-8-sig')
old="require(npc, r'ai\\[0\\] == 16f \\|\\| ai\\[0\\] == 17f.*?frameCounter == 40\\.0.*?num98 = 45;.*?frameCounter == 100\\.0.*?num98 = 45;.*?frameCounter != 160\\.0.*?num98 = 75;', 'RPS bubble frame cadence')\nrequire(npc, r'num108 = Utils\\.SelectRandom<int>\\(Main\\.rand, 38, 37, 36\\).*?EmoteBubble\\.NewBubble\\(num108.*?EmoteBubble\\.NewBubble\\(num109', 'RPS explicit emotes')"
new="require(npc, r'else if \\(CanTalk && \\(ai\\[0\\] == 16f \\|\\| ai\\[0\\] == 17f\\)\\)', 'RPS FindFrame state owner')\nrequire(npc, r'frameCounter == 40\\.0', 'RPS bubble frame 40')\nrequire(npc, r'frameCounter == 100\\.0', 'RPS bubble frame 100')\nrequire(npc, r'frameCounter != 160\\.0', 'RPS bubble frame 160')\nrequire(npc, r'Utils\\.SelectRandom<int>\\(Main\\.rand, 38, 37, 36\\)', 'RPS explicit emote set')\nrequire(npc, r'EmoteBubble\\.NewBubble\\([^;]+\\);.*?EmoteBubble\\.NewBubble\\([^;]+\\);', 'RPS paired explicit bubbles')"
if text.count(old)!=1:
    raise SystemExit('RPS source-contract anchor missing or ambiguous')
p.write_text(text.replace(old,new,1),encoding='utf-8')
print('Town social RPS source checker hardened without ILSpy temporary names')
