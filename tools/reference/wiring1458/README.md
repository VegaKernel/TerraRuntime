# Terraria 1.4.5.8 wiring reference capture

This developer-only probe invokes methods in the unmodified official dedicated-server binary through reflection. It references no TerraRuntime production assembly and contains no decompiled game method bodies. Obtain the original server distribution separately; game binaries, assets and text are not included here.

The recipe runs the **Linux edition under Windows CoreCLR** with the repository's .NET SDK. It does not execute on Linux and does not establish Linux or NativeAOT compatibility. The probe deliberately rejects other execution hosts and binary hashes so its metadata cannot silently describe a different capture.

Supply the original `TerrariaServer.exe` from the Linux distribution, with its original neighboring assemblies such as `FNA.dll`. The accepted executable SHA256 is:

```text
4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034
```

From the repository root, using PowerShell:

```powershell
$originalServerExe = 'D:\references\terraria-1458\Linux\TerrariaServer.exe'
dotnet run --project tools/reference/wiring1458/Probe.csproj -c Release -- $originalServerExe .cache/wiring-reference/facts.json
dotnet run --project tools/reference/wiring1458/Probe.csproj -c Release --no-build -- $originalServerExe .cache/wiring-reference/facts-repeat.json
Get-FileHash .cache/wiring-reference/facts.json, .cache/wiring-reference/facts-repeat.json
Get-Content .cache/wiring-reference/timer-save-load.json
```

The probe writes JSON and a `timer-save-load.json` sidecar in the selected output directory. Neither output belongs in the original server directory. Scene inputs are explicit in `Program.cs`: five timer styles and their first two pulse deadlines, off/on before the next mechanism update, every nonempty four-color subset for switch/repeat/lever, three multi-cell lights, four actuator progression cases, and eight ordinary plate styles across five source actor categories. The original 98 rows remain unchanged in the `rows` array.

Additional arrays contain eight `doorScenes`, three `timerEdges` traces and one `momentary` trace, giving 110 scenes in total: 109 complete supported comparisons and one eligibility-only case. Door scenes use source metadata initialized by `TileObjectData.Initialize`, supported ordinary doors/tall gates/trapdoors, different wired footprints, repeated pulses and a blocked gate followed by actor departure. Trapdoor fixtures have solid anchors on both sides and pass the original `WorldGen.CheckTrapDoor` before triggering. Snapshots retain active state, type, both frames and all wires across the complete object rectangle, including cells cleared by closing. Captured RNG offers check door frame transforms and callback count/order; they do not establish whole-world framing RNG parity.

The delegated skips come from the original `WorldGen.OpenDoor`, `CloseDoor`, `ShiftTallGate` and `ShiftTrapdoor` methods while `Wiring.running` is set. Skips can precede actor occupancy rejection and do not always cover the same cells as the resulting object; trapdoor closing uses the source style-dependent coordinates. The edge traces exercise `Wiring.UpdateMech` checking `WorldGen.InWorld(x,y,1)`: an edge registration decrements the table count without shifting entries, potentially discarding a different final registration. The momentary trace covers a second click before expiration and the source reset choosing its direction from the current frame.

The probe calls the original `Wiring.HitSwitch`, `Wiring.UpdateMech`, `Collision.SwitchTiles`, delegated `WorldGen` device methods, `WorldFile.SaveWorldTiles` and `WorldFile.LoadWorldTiles`. The expected scene JSON SHA256 is `56488C253AF73041F08B8A9F5800AA081581ACC7B342BDEB03F14D603687F7ED`. Both captures must match. The decompressed contents of `tests/TerraRuntime.Tests/Fixtures/WiringOriginalLinux1458.json.gz` match that JSON; the committed gzip SHA256 is `60070AE80EC94CC8D0FE57F5AE6A0E4F429BB2F61C5C0F18BB909BC1A30E60EB`.

The timer sidecar records `base64: "Aw8mkDYAEgA="` and `loadedFrameY: 0`. Its input is a single active timer `144`, frames `(54,18)`, all four wires, an actuator and the inactive bit. This independently demonstrates that saving retains frame `18` while canonical loading resets it to `0`. Its expected JSON SHA256 is `6106F5A0F79A11E782C1DF8FEDCFB762A6BD70394B8C67B2B7418726F85495FC`.

The numeric replay tests compare 97 complete supported scenes from the original array and the additional device/mechanism traces. The remaining style-`7` player contact captures the original plate destruction and adjacent torch reframing; current production support checks its actor eligibility and required destruction flag only. It does not claim that unsupported final effect. See `docs/en/wiring-mechanisms.md` and its Russian counterpart for the full admitted scope.

## Conventional installation tools and conditional cutter evidence

`ToolsProbe.csproj` is a separate bounded capture, using the same pinned original binary. Its intermediate files and executable go into `.cache/wiring-tools-build`; it does not modify the original 110-scene corpus.

```powershell
dotnet run --project tools/reference/wiring1458/ToolsProbe.csproj -c Release -- $originalServerExe .cache/wiring-reference/tools-first.json
dotnet run --project tools/reference/wiring1458/ToolsProbe.csproj -c Release --no-build -- $originalServerExe .cache/wiring-reference/tools-repeat.json
Get-FileHash .cache/wiring-reference/tools-first.json, .cache/wiring-reference/tools-repeat.json
```

Both JSON files must have SHA256 `DBC48228CCFE84FBD5AD2B42A7C620C27ED24B595984C0A4AD2F725717814A97`. The matching committed `WiringToolsOriginalLinux1458.json.gz` fixture has SHA256 `5DE8B2DBD551EF1FA58E48F13C99671A44A229EAFF8E0D2425FE6F5D562FDF71`.

The capture contains 34 direct calls to unmodified `Player.ItemCheck`: 26 profiles for the four conventional wrenches and selected actuator item, plus eight cutter profiles retained as explicit unsupported/conditional evidence. Profiles exercise stock slots 57/58, missing resources and existing bits, inclusive reach boundaries, temple walls, input/time gates, last-actuator consumption and removal priority. An original socket-interface recorder captures original packet bytes and state at each publication. Client profiles use `netMode=1`, `myPlayer=0`, a configured visible viewport and a real dust pool. `dedServ=true` suppresses rendering/audio asset requests, but it does **not** suppress `Dust.NewDust`; these are configured `ItemCheck` projections, not a GUI-client or complete `Main.Update` capture.

The identical cutter operation with free dust slots consumes 30 extra internal RNG samples compared with a full 6000-slot dust pool, while its tile result, clocks and packet bytes agree. Therefore this fixture does not qualify client cutter use from a headless visual-state assumption. The installation tools do not have that removal dust effect.

Six dedicated-server (`netMode=2`) primitive cycles separately verify four wire colors and actuator placement/removal on air and inactive stone. The server caller scopes reproduce `MessageBuffer`17's `Item.DefaultAssignNewItemsToPlayer(0)`. Four framing profiles use the distinct packet59 current-user scope, without the packet17 item-owner override: a supported attached switch drops, while a torch remains attached to an actuated support. Seeds 1458 and 7 are captured; the seed-7 scenes match the production-command regression fixture. Drops retain physical state, ownership/grab fields, RNG state/next value, original network bytes and state at publication. The replay tests compare source framing against the existing production world/item/RNG owners; the primitive tile owner itself has no drop responsibility.

That production comparison uses the existing tile ABI projection: support type/actuation/wires, exact attachment frames, drops and public RNG state. The original `TileFrameCosmetic` also changes the isolated stone support's atlas coordinates to `(162,54)`. Those original values remain in the numeric fixture and are separately pinned, but existing `ToolSourcePass` omits non-frame-important cosmetic merge frames, as do canonical tile and wire codecs. This test does not claim complete rendered-cell framing parity.

The owner comparison distinguishes original server fields from advertised client state. `WorldItem.ReserveFor(int player, int timeToKeepReservation=15)` emits packet22 with hold15 but does not assign its same-named server field, which remains0 for a fresh item. `MessageBuffer`22 writes the advertised value into the receiving client's field. The original wiring fixture retains raw server0 and advertised15 independently. The reservation follow-up now compares the raw field to `WorldItemSnapshot` and carries the live advertised hold separately in publication metadata; the fixture itself is unchanged. This closes the earlier snapshot representation boundary without claiming the complete item lifecycle.

## Independent reservation lifecycle probe

`LeaseProbe.csproj` is a separate developer-only executable with explicit compilation of `LeaseProgram.cs`. Its build intermediates/output stay under `.cache/wiring-lease-build`; it does not reference production projects or change the 44/110-scene wiring fixtures. It accepts the same pinned original binary and two explicit paths:

```powershell
dotnet run --project tools/reference/wiring1458/LeaseProbe.csproj -c Release -- $originalServerExe .cache/wiring-reference/lease.json
dotnet run --project tools/reference/wiring1458/LeaseProbe.csproj -c Release --no-build -- $originalServerExe .cache/wiring-reference/lease-repeat.json
Get-FileHash .cache/wiring-reference/lease.json, .cache/wiring-reference/lease-repeat.json
```

The original executable is the Linux edition executed under Windows CoreCLR, not a Linux-host or NativeAOT capture. All gameplay methods run in the original assembly. The reflection/socket harness only configures scenes, invokes methods and records numeric state and bytes; it contains no copied method bodies. There are 33 profiles with raw owner/hold/reservation-age, active/instanced/reuse state, grab/enemy countdowns, physical fields, source RNG state/next value, packet recipients and state at each original send:

- Fresh `ReserveFor` default15, `ApplySpawnOwnership` local100, positive raw37 preservation, and ordinary/forced `FindOwner` with active/disconnected owners.
- Actual `Item.NewItem` live21/22 versus default-argument join21/22, including raw37 versus join hold0.
- Actual `WorldItem.UpdateItem` positive countdowns and inactive-item behavior. Reservation age is a different counter: captured `Main.UpdateServer` traces show unowned selection at age6, assigned handoff at300, pending age-1 without resend, and immediate disconnected-owner reassignment before the previous100 offer could expire.
- Actual server/client `MessageBuffer.GetData`22/39. Acknowledgment with raw37 preserves37 unless forced; default unowned22 advertises0 and excludes the releasing connection. A successful reassignment15 broadcasts to the sender too. Server22 is ignored; client39 forces its local release even when the received flag is false.
- Actual `MakeInstanced` with lifetime3/default54000 and inactive `UpdateItem` ticks, expiry151 and new item slot reuse. The slot reuse lease is separate from the ordinary raw reservation clock. Raw original39 has no generation token and can affect a reused same-owner slot; TerraRuntime's exact-generation command rejection is an explicit trust-boundary policy, not claimed identical behavior.
- Two configured pending-transfer preimages place an already unowned item with raw37 on either endpoint. Actual original `RequestOwnershipReleaseForPendingTransfers` clears it silently, preserving reservation age17; the peer owned by active player7 emits only targeted39. Direct/deferred replay uses the existing runtime capacity-pressure fixture, explicitly mapping source slots0/1 to runtime50/51. Only that slot mapping is normalized; raw fields, recipient and packet shape remain pinned. Configuring the original private list does not claim independent verification of emergency pair selection.
- A releasable pending pair runs actual `Main.UpdateServer` six times: the owner scan sees the pending guard before processing transfers, so step1 emits destination21/source151 without22. Steps2..5 emit nothing; step6 assigns the merged destination and advertises15. Both items start as type2/stack10 at640,640, owner255/raw0/age0 with nearby active player0.

`WorldItemReservationOriginal1458Tests` reads the separate `WorldItemReservationOriginalLinux1458.json.gz` corpus. It compares source allocation through direct/deferred publication, raw fields and separate live22 offers, positive countdowns, production command39 bytes/recipient exclusion, inactive instanced lease expiry/reuse, positive-raw-hold join defaults and both pending-endpoint clock clears. The source fixture also retains client/control/scheduler observations beyond those projected comparisons. Countdown tests compare the three `UpdateItem` countdown fields; the runtime tick additionally owns the separate server reservation-age phase. This is bounded lifecycle evidence, not GUI/visual, every item type, or whole original-server parity.

Both independent runs produced raw SHA256 `b3f7494abb3ae0e22b822c104d5c7cd4bfceff9bcf4eee179a21082cb03734a8`. The committed gzip SHA256 is `1c34cde43c3ece02311f670f46d40cf37cbf81d1639c968d583dc77a1508d980`; decompressing it yields those exact raw bytes. The 79 recorded steps invoke original `WorldItem.ReserveFor`, `ApplySpawnOwnership`, `FindOwner`, `UpdateItem`, `MakeInstanced`, `Item.NewItem`, `MessageBuffer.GetData`22/39, `NetMessage.SendData`21/22, `EmergencyStacking.RequestOwnershipReleaseForPendingTransfers` and `Main.UpdateServer`. Relevant local 1458 source boundaries are `WorldItem.cs:188/278/292/325/342`, `MessageBuffer.cs:1552/2232`, `NetMessage.cs:678`, `EmergencyStacking.cs:396` and `Main.cs:65577` in the separately obtained decompile. The earlier30 profiles remain logically exact in the extended corpus.
