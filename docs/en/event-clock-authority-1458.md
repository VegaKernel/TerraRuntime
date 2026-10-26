# Event clock authority in Terraria 1.4.5.8

`RuntimeWorldClock` owns the live Blood Moon, Solar Eclipse and Pumpkin/Frost Moon flags. An accepted Moon start clears Blood Moon and initializes the shared wave counter to one, with zero wave kills and total points. The player invocation owner must accept payment before calling the scalar start methods. Repeated active-event requests and requests at the wrong time are rejected.

The source time boundaries are strict: day ends after `54000`, and night ends after `32400`. Dusk clears Solar Eclipse. Every night tick also clears a stale Eclipse flag, and every daytime tick stops Moon events, including when time is frozen. Dawn clears Blood Moon and Moon wave counters. Moon wave progress is transient and does not resume after a world reload.

Blood Moon and Solar Eclipse are saved world flags. The detached checkpoint captures their accepted values along with the clock; the header patcher updates the two verified bytes immediately after the saved moon phase. A queued checkpoint retains its captured values even if a later event changes the live clock. Packet `7`, late-join snapshots, town move-in, town scheduling and shop conditions consume the live Eclipse value instead of retaining the flag loaded at startup.

Prepared NPC deaths adopt the shared Moon wave counters and progress revision before loot or entity observers run. The existing NPC generation and death-plan ownership remains responsible for accepting each death once. The Moon point table, difficulty scalar and discarded surplus on a wave transition remain in the existing clock implementation.

A Moon start publishes packet `7` before the one-time packet `78` indicator with progress `0`, maximum `1` and wave `1`. Late join keeps the actual first-wave requirement `25`; the initial indicator does not replace that immutable baseline. Stopping the event cancels a pending initial indicator.

Raw client packet `28` cannot damage progression bosses or their linked encounter parts. This includes Eater/Destroyer segments, Wall of Flesh eyes, Prime arms and Moon Lord parts, which cannot be protected by a root-only boss flag. Refusal occurs before interaction credit, HP changes and RNG. Available melee damage calculations do not establish a server-owned swing collision. The existing owned projectile, server melee and environment paths retain death, reward and progression ownership; older reward tests now enter those paths directly.

Evidence: local `Main.startPumpkinMoon`, `Main.startSnowMoon`, `Main.UpdateTime`, `Main.UpdateTime_StartDay`, `Main.UpdateTime_StartNight`, `MessageBuffer` case `61`, `NPC.CheckProgressFrostMoon`, `NPC.CheckProgressPumpkinMoon` and `WorldFile.SaveWorldHeader` in `decompiled/1458`. Regression coverage includes strict time and repeat boundaries, frozen cleanup, both directions of the live packet flag, exact two-byte changes in an existing independent official header fixture, detached checkpoint serialization and cold restart.

The [Moon score and temporary-season owner](moon-season-progression-1458.md) now handles dawn rewards and expiration. Remaining work includes natural Blood Moon/Eclipse scheduling in the shared source RNG order, dial cooldown side effects, Moon wave announcements and Moon boss completion milestones. These changes do not establish complete event encounter or whole vanilla scheduler parity.
