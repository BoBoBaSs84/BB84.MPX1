# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

BB84.MPX1 is a clean-room C# reimplementation of a SysEx editor/librarian for the **Lexicon MPX 1** multi-effects processor (firmware V1.00 and V1.10). Target: .NET 10, Native AOT, Avalonia UI, Windows first (WinMM), macOS/Linux after 1.0.

- **Specification**: `spec/spec-architecture-mpx1-sysex-editor.md`. It holds requirement IDs (PRO-, TRN-, SES-, LRN-, PAR-, PRG-, DMP-, SIM-, UI-, CON-), acceptance criteria with exact byte vectors, and known inconsistencies in the source document (section 9). Reference requirement IDs in tests.
- **Roadmap**: `docs/roadmap.md`. Milestones M0–M9; work in milestone order.

## Commands

```powershell
dotnet build BB84.MPX1.slnx -c Release                       # warnings are errors
dotnet test --solution BB84.MPX1.slnx -c Release -- --ignore-exit-code 8 --filter "TestCategory!=Hardware"
dotnet test --project tests/BB84.MPX1.Core.Tests -- --filter "FullyQualifiedName~NibbleCodecTests"   # single class/test
dotnet publish src/BB84.MPX1.App -c Release -r win-x64 -o artifacts/publish                         # Native AOT
./build/package.ps1 -Version 1.0.0                                                                    # release zip in artifacts/release
```

- Tests use MSTest 4 on Microsoft.Testing.Platform (`MSTest.Sdk`, pinned in `global.json`); test options go after `--`. Exit code 8 means "no tests found".
- AOT publish needs the MSVC linker. If it fails with `'vswhere.exe' is not recognized`, add `C:\Program Files (x86)\Microsoft Visual Studio\Installer` to `PATH`.
- Package versions live in `Directory.Packages.props` (central package management).
- `Directory.Build.targets` marks every library `IsAotCompatible`/`IsTrimmable`, so AOT analyzer warnings show up at build time.
- Releases: pushing a `v*` tag runs `.github/workflows/release.yml`, which calls `build/package.ps1` and creates the GitHub Release from the matching `CHANGELOG.md` section. Never push or tag without being asked.
- `ArchitectureTests.EveryRequirement_HasATest` (GUD-005) fails when a spec requirement ID has no test; tag new tests with the IDs they cover in `[Description]`.

## Architecture (spec section 4.1)

- `BB84.MPX1.Core`: codec (`Protocol`), learned parameter database and system-dump lookup (`Database`), values and Appendix B formatting (`Values`), program model, block layouts, routing rules and Appendix A tables (`Programs`), dump layouts and front-panel models (`Dumps`), `.syx` files (`Files`). No I/O.
- `BB84.MPX1.Midi`: `IMidiTransport`, the session (request/response matching, retries, BUSY/READY), learning, parameter editing, the program librarian, the dump service and the front-panel controller.
- `BB84.MPX1.Midi.WinMM`: the Windows transport adapter.
- `BB84.MPX1.Simulator`: fake MPX 1 (`DefaultFixture` is a partial V1.10 model) plus record/replay. It is the primary test target, because no hardware is guaranteed.
- `BB84.MPX1.App`: Avalonia + CommunityToolkit.Mvvm workbench: toolbar, control tree, center tabs (Parameters, Librarian, Program, System, Front panel) and the MIDI monitor. View models are tested in `tests/BB84.MPX1.App.Tests` against the simulator; services that need Avalonia sit behind `IUiDispatcher` and `IUserDialogs`.

## Hard constraints

- Zero IL2xxx/IL3xxx (trim/AOT) warnings.
- No reflection-based serialization; use `System.Text.Json` source generation.
- P/Invoke via `[LibraryImport]` only.
- Clean room: never copy code, text or resources from `legacy/MPX1_V1/MPX1EDIT.EXE` or `MPX1HLP.HLP`. Never disassemble them for implementation. Black-box behavior observation only.
- Never hard-code parameter-type numbers that differ between V1.00 and V1.10. Most dump and system types are +1 in V1.10. Resolve them through the learned database.

## `legacy/` reference material

Each original Lexicon PDF has a sibling `*_original.md` text extraction. Search/read the `.md` files. The PDFs are authoritative when the extraction is garbled: tables and multi-column layouts were flattened, for example the QRG, the MIDI doc's table of contents, and the dump tables.

- `MPX1_V1_1_MIDI_Impl_Rev1_original.md`: SysEx MIDI implementation (V1.00 and V1.10). The primary protocol source.
- `MPX1_User_Guide_Rev1_original.md`: full user guide (~300 KB; grep rather than read whole).
- `MPX1_V1_1_Adden_Rev1_original.md`: V1.10 additions (MPX R1 remote controller support).
- `MPX1_Presets_Rev1_original.md`, `MPX1_V1_1_Presets_Rev1_original.md`: factory preset lists for V1.00 and V1.10.
- `MPX1_QRG_Rev1_original.md`: quick reference / menu map.
- `MPX1_V11_Soft_Upgrade_Rev0_original.md`: firmware upgrade service instructions (EPROM swap; out of scope).
- `MPX1_V1.exe`: DOS LHA self-extractor (LHA's SFX 2.12S); do not execute. Its extracted contents are in `MPX1_V1/`:
  - `MPX1EDIT.EXE`: WazWare MPX 1 SysEx editor V1.00 (1998), a 16-bit Win16 NE executable built with Borland Pascal. It runs only under otvdm/winevdm or a Win3.x/32-bit Windows VM.
  - `MPX1HLP.HLP`: WinHelp file.
  - `README.TXT`.

## Key SysEx protocol facts

- Header: `F0 06 09 <device id 0-127> <message class>`; the message ends with `F7`.
- Everything except the 5-byte header and the trailing `F7` is **nibble-ized**: each data byte is sent as two bytes carrying 4 bits each, low nibble first. 16-bit words are sent as low-nibble/low-byte, high-nibble/low-byte, low-nibble/high-byte, high-nibble/high-byte. In requests (class `06`), the requested class byte is also nibble-ized (for example, `0x1B` → `0B 01`).
- The device is self-describing. A controller requests System Configuration (`00`) for the type count, then a Parameter Description (`04`) for every type. It then walks the **control tree** with Parameter Type (`03`) requests at successive control addresses (levels A, B, C, …). A branch's max value gives the legal child indices. The Control Level flag (`0x04`) marks branches.
- Bulk writes (Program Dump `1B`, dumps) are flow-controlled by Handshake (`12`) BUSY/READY messages.
