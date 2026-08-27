# Reference policy

`TerraRuntime` is a clean-room C# server runtime project.

The official Terraria dedicated server may be downloaded and decompiled locally for interoperability, protocol research, behavioral comparison, and regression investigation. The resulting binaries and decompiled source are local reference material only and must not be committed to this repository.

Use `tools/decompile-reference.ps1` on Windows or `tools/decompile-reference.sh` on Linux/macOS. Both scripts download the official dedicated server archive from `terraria.org`, locate `TerrariaServer.exe`, install `ilspycmd` into the repository-local `.tools/` directory, and generate a project-style decompilation under `decompiled/<version>/`.

The implementation under `src/` should be written independently. Prefer behavioral tests and named protocol/domain contracts over copying method bodies from the reference tree.
