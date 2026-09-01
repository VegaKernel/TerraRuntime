#!/usr/bin/env python3
from pathlib import Path
p=Path('tools/ci/check_town_social_emotes_source.py')
text=p.read_text(encoding='utf-8-sig')
old="require(npc, r'ai\\[0\\] == 16f \\|\\| ai\\[0\\] == 17f.*?frameCounter == 40\\.0.*?num98 = 45;.*?frameCounter == 100\\.0.*?num98 = 45;.*?frameCounter != 160\\.0.*?num98 = 75;', 'RPS bubble frame cadence')\nrequire(npc, r'num108 = Utils\\.SelectRandom<int>\\(Main\\.rand, 38, 37, 36\\).*?EmoteBubble\\.NewBubble\\(num108.*?EmoteBubble\\.NewBubble\\(num109', 'RPS explicit emotes')"
new="rps_frame = npc[npc.index('else if (CanTalk && (ai[0] == 16f || ai[0] == 17f))'):npc.index('else if (velocity.X == 0f)', npc.index('else if (CanTalk && (ai[0] == 16f || ai[0] == 17f))'))]\nrequire(rps_frame, r'frameCounter == 40\\.0.*?num98 = 45;', 'RPS bubble frame 40')\nrequire(rps_frame, r'frameCounter == 100\\.0.*?num98 = 45;', 'RPS bubble frame 100')\nrequire(rps_frame, r'frameCounter != 160\\.0.*?Main\\.netMode == 1.*?num98 = 75;', 'RPS bubble frame 160')\nrequire(rps_frame, r'num108 = Utils\\.SelectRandom<int>\\(Main\\.rand, 38, 37, 36\\).*?EmoteBubble\\.NewBubble\\(num108.*?EmoteBubble\\.NewBubble\\(num109', 'RPS explicit emotes')"
if text.count(old)!=1:
    raise SystemExit('RPS source-contract anchor missing or ambiguous')
p.write_text(text.replace(old,new,1),encoding='utf-8')
print('Town social RPS source checker hardened')
