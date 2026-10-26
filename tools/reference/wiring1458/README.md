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
