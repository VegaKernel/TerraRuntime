# Reference policy

`TerraRuntime` is a clean-room C# server runtime project.

The official Terraria dedicated server may be downloaded and decompiled locally for interoperability, protocol research, behavioral comparison, and regression investigation. The resulting binaries and decompiled source are local reference material only and must not be committed to this repository.

Use `tools/decompile-reference.ps1` on Windows or `tools/decompile-reference.sh` on Linux/macOS. Both scripts download the official dedicated server archive from `terraria.org`, locate `TerrariaServer.exe`, install `ilspycmd` into the repository-local `.tools/` directory, and generate a project-style decompilation under `decompiled/<version>/`.

The implementation under `src/` should be written independently. Prefer behavioral tests and named protocol/domain contracts over copying method bodies from the reference tree.

## Current official reference

- Terraria dedicated server: **1.4.5.8** (`1458`)
- Windows `TerrariaServer.exe` SHA-256: `d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e`
- Official download endpoint: `https://terraria.org/api/download/pc-dedicated-server/terraria-server-1458.zip`

The hash identifies the behavioral reference binary; the binary and its decompilation remain ignored
local material.
