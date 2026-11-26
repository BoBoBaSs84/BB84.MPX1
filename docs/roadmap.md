# BB84.MPX1 Roadmap

Delivery plan for the clean-room Lexicon MPX 1 SysEx editor. Requirement IDs refer to [the specification](../spec/spec-architecture-mpx1-sysex-editor.md).

**Release target 1.0**: Windows x64, Native AOT, full SysEx coverage, simulator-verified. Hardware validation is optional.

## Overview

| #    | Milestone                               | Depends on                       | Status       |
| ---- | --------------------------------------- | -------------------------------- | ------------ |
| M0   | Repository scaffold & CI                | —                                | Done         |
| M1   | Core protocol codec                     | M0                               | Done         |
| M2   | Transport & session                     | M1                               | Done         |
| M3   | Simulator & replay                      | M1, M2                           | Done         |
| M4   | Learning & parameter database           | M2, M3                           | Done         |
| M5   | Application shell & parameter editing   | M4                               | Done         |
| M6   | Programs, librarian & files             | M4, M5                           | Done         |
| M7   | Full spec coverage: dumps & front panel | M5, M6                           | Done         |
| M8   | Hardware validation (optional)          | M4+                              | Done (V1.00) |
| M9   | Release 1.0                             | M7 (M8 if hardware is available) | Done         |
| P1   | Linux support (post-1.0)                | M9                               | Planned      |
| P1.1 | Linux groundwork & CI                   | M9                               | In progress  |
| P1.2 | ALSA sequencer transport                | P1.1                             | In progress  |
| P1.3 | Linux publish & AppImage                | P1.1                             | Planned      |
| P1.4 | Flatpak                                 | P1.2, P1.3                       | Planned      |
| P1.5 | Linux validation & release              | P1.2, P1.3, P1.4                 | Planned      |
| P2   | macOS support (post-1.0)                | P1                               | Planned      |
| P2.1 | CoreMIDI transport & macOS CI           | P1                               | Planned      |
| P2.2 | macOS publish & app bundle              | P2.1                             | Planned      |
| P2.3 | Code signing & notarization (last)      | P2.2                             | Planned      |

M8 ran on a V1.00 unit; its recordings are replayed in tests (`tests/data/hardware/v100`). V1.10 hardware has not been tested.

M9 is complete except for its exit criteria that need the maintainer: pushing the `v1.0.1` tag (which runs the release workflow; GitHub refuses the `v1.0.0` tag name) and checking the zip on a clean Windows machine.

---

## M0 — Repository scaffold & CI

**Goal**: An empty, buildable solution that enforces the constraints from day one.

**Scope**

- `BB84.MPX1.slnx` with the projects from spec section 4.1 and one MSTest project per library.
- `Directory.Build.props`: `net10.0`, nullable, implicit usings, `TreatWarningsAsErrors`, `IsAotCompatible`/`IsTrimmable` for libraries, AOT analyzers on.
- `Directory.Packages.props` (central package management).
- `.editorconfig`.
- App project: Avalonia template with compiled bindings and `PublishAot=true`.
- GitHub Actions workflow on `windows-latest`: restore, build, test (exclude `Hardware`), `dotnet publish -r win-x64` for the app, upload artifact.

**Exit criteria**

- CI is green on `main`.
- The AOT publish produces a native exe with zero IL2xxx/IL3xxx warnings (AC-018).
- `CLAUDE.md` lists the real build/test/publish commands.

## M1 — Core protocol codec

**Goal**: Byte-exact encode/decode of every MPX 1 message.

**Scope**

- Nibble and word codec, framing, control addresses, strings (PRO-001 to PRO-008).
- Typed records and parser/builder for classes `00`, `01`, `02`, `03`, `04`, `05`, `06`, `12`, `16`, `18`, `19`, `1A`, `1B`, plus `1C` raw pass-through and Device Inquiry (PRO-009 to PRO-026).
- Request builder for every row of spec table 4.4.
- Handling of document inconsistencies E1–E5.
- Bounds checks (SEC-001).

**Exit criteria**

- AC-001 to AC-010 pass.
- Every class has a decode → encode round-trip test.
- Core line coverage ≥ 90 %.

## M2 — Transport & session

**Goal**: Reliable request/response over real MIDI ports on Windows.

**Scope**

- `IMidiTransport` contract (spec 4.2).
- WinMM adapter: `LibraryImport`, `UnmanagedCallersOnly` callback, buffer re-queueing, split-frame reassembly (TRN-001 to TRN-006).
- Session: response matching, timeouts, retries, serialization, BUSY/READY flow control, ARE_YOU_THERE, version detection, inter-frame gap, unsolicited events (SES-001 to SES-009).
- Loopback transport for tests.

**Exit criteria**

- Session tests pass over the loopback transport, including timeout/retry paths.
- A manual console check enumerates real MIDI ports on Windows and sends/receives a SysEx frame through a loopback cable or virtual MIDI port.

## M3 — Simulator & replay

**Goal**: A fake MPX 1 that makes every protocol path testable without hardware.

**Scope**

- Simulator implementing `IMidiTransport` that answers every request class (SIM-001).
- JSON fixture with a partial V1.10 tree built from the spec examples: root, Program, Pitch, Detune (M), EQ 1 Band (M), System, Display (SIM-002).
- BUSY/READY behavior, configurable latency, fault injection, Appendix D bugs (SIM-003 to SIM-005).
- Record transport (wraps a real transport, writes JSON-lines) and replay transport (TRN-007).

**Exit criteria**

- AC-011 and AC-012 pass against the simulator.
- A recorded session replays deterministically in tests.

## M4 — Learning & parameter database

**Goal**: Learn a device once, cache it, and resolve every parameter by name and address.

**Scope**

- Description learning, depth-first tree walk, progress, cancellation (LRN-001 to LRN-003).
- JSON cache keyed by firmware build, with a source-generated serializer (LRN-004, CON-003).
- Version-independent resolution of dump and system types (LRN-005).
- Appendix D workarounds (LRN-006 to LRN-008).
- Learning from a recorded `.syx` stream (LRN-009).
- Value model: option splitting, multi-unit limits, Rate, MIDI Speed, offline display-unit formatting from Appendix B (PAR-001 to PAR-008).

**Exit criteria**

- AC-013 to AC-016 pass.
- Learning the full simulator fixture finishes in under 5 s.

## M5 — Application shell & parameter editing

**Goal**: A usable AOT desktop app that edits live parameters.

**Scope**

- Avalonia shell with MVVM (PAT-001).
- MIDI setup view (UI-001).
- Learning view (UI-002).
- Control-tree browser (UI-003).
- Parameter editors driven by display units (UI-004).
- MIDI monitor (UI-009).
- Settings persistence.
- A "Simulator" pseudo-port in the port list for demos and manual testing.

**Exit criteria**

- In the published AOT exe, a user can connect to the simulator, learn, browse the tree, change a parameter, and see the change in the simulator and the monitor.
- No AOT warnings.

## M6 — Programs, librarian & files

**Goal**: Move programs in and out of the device and to disk.

**Scope**

- Program Dump model, including effect-block parameter offsets (PRG-001 to PRG-003).
- Program Information listing (PRG-007).
- 250-slot librarian with preset-overwrite confirmation (PRG-004, CON-007).
- Batch backup/restore with handshake flow control (PRG-005).
- `.syx` import/export (PRG-006).
- Database Dump sort flags editing.
- Program editor view (UI-005, UI-006).
- Offline editing of files (UI-010).

**Exit criteria**

- AC-017 passes.
- A program exported to `.syx` re-imports byte-identically.

## M7 — Full spec coverage: dumps & front panel

**Goal**: Every remaining documented feature.

**Scope**

- All dumps DMP-001 to DMP-010, with typed models where the layout is documented.
- Appendix A controller tables and validation (DMP-011, DMP-012).
- Virtual front panel: display mirror with custom characters, LED mirror, Panel Button with PRESS/RELEASE/HOLD and combinations (PAR-007, UI-007).
- System dump editors: Setup, Global Patches, Bypass, Remap (UI-008).
- Report Effect Parameters (`18`) and Report All Effect Parameters (`19`) used for algorithm parameter lists.

**Exit criteria**

- Every spec requirement ID is referenced by at least one passing test (GUD-005).
- Spec section 10 validation criteria pass.

## M8 — Hardware validation (optional)

**Goal**: Confirm behavior against a real MPX 1 and close the gaps the document leaves open.

**Scope**

- Run the `[TestCategory("Hardware")]` smoke suite: inquiry, config, learn, read/write a user program, handshake.
- Record full learn and backup sessions on V1.00 and/or V1.10; add them as replay fixtures and extend the simulator fixture to the full tree.
- Black-box comparison with `legacy/MPX1_V1/MPX1EDIT.EXE` running under otvdm/winevdm: capture its MIDI traffic for timing, ordering and retry behavior (CON-005: observation only).
- Measure real response latency and BUSY durations; tune default timeouts.

**Exit criteria**

- The hardware smoke suite passes.
- The recorded fixtures are committed under `tests/data/`.
- Any spec corrections are logged in spec section 9.

**Result (V1.00, built Nov 21 1996, via a USB Uno interface)**

- The 11 `Hardware` tests pass: inquiry, firmware, ARE_YOU_THERE, learn (448 descriptions, 2,653 nodes, 95 s), program read, empty program, full backup, combined setup dump, program write/read-back/restore, and system dump write-back.
- Recordings of learn, backup, program write, system dump write-back, firmware detection and an empty program are in `tests/data/hardware/v100` and replayed by `HardwareReplayTests`.
- Full-tree V1.00 simulator fixture (done after M8, issue #24): the simulator's special cases are declared by the fixture, and `FixtureBuilder` builds the V1.00 fixture from the learn recording. The simulator test suites run against V1.10 and V1.00.
- Spec corrections E10–E13, E18–E19 and new edge cases are in section 9: the Appendix D bug 2 address, Global Patch/Bypass "None" values, 43-byte V1.00 setups, the setup dump order the broken single-setup reads, the Database Dump receive offset and how Program Dumps carry sort flags.
- Fixes found on hardware: dead-end detection by echo, skipping empty user programs, BUSY timing over USB interfaces, V1.00 dump names and sizes, all setups (active included) in backups and the System tab.
- Full restore (issue #25): a backup of 200 presets, 9 user programs, the sort flags and 4 system dumps restores in 70 s and reads back identically, apart from unused effect-data bytes of presets (E19). BUSY follows a Program Dump after 145–206 ms, READY 8–48 ms later; no timeouts or retries. The first run found the V1.00 Database Dump receive offset (E18), which scrambled the sort flags of programs 1–33; the Librarian now compensates for it. Recordings: `full-restore.jsonl`, `sort-flags-preset-1.jsonl`.
- Timing (R5): replies take 17–46 ms, up to 405 ms for a Program Dump and 630 ms for the Database Dump, mostly MIDI wire time. The default request timeout is now 2000 ms.

**Not done (moved out of M8)**

- Black-box comparison with `MPX1EDIT.EXE` under otvdm/winevdm (optional; needs the maintainer).

## M9 — Release 1.0

**Goal**: A downloadable, documented release.

**Scope**

- `README.md` (features, screenshots, MIDI setup, simulator mode).
- License.
- Changelog.
- Version stamping.
- Release workflow: tag → AOT publish `win-x64` → zip → GitHub Release.
- Make sure Lexicon documents and legacy binaries are not part of the release artifact (COM-001).

**Exit criteria**

- Tagged `v1.0.1` release with an attached `win-x64` zip.
- A clean Windows machine runs the exe without installing .NET.

## P1 — Linux support (post-1.0)

**Goal**: Run on Linux x64 with real MIDI ports, shipped as an AppImage and a Flatpak.

Linux comes before macOS because the maintainer can test it in a Linux VM. P1.2 and P1.3 depend only on P1.1 and can run in parallel.

### P1.1 — Linux groundwork & CI

Issue [#40](https://github.com/BoBoBaSs84/BB84.MPX1/issues/40).

**Goal**: The solution builds and its tests pass on Linux, using the simulator as the only transport.

**Scope**

- CI matrix: build and test on `ubuntu-latest` next to `windows-latest`. WinMM tests stay Windows-only.
- Transport selection by OS in `MidiPortCatalog.CreateDefault`, ready for further adapters. Without a hardware transport the app keeps offering only the simulator.
- Settings and learned-database paths follow the XDG directories on Linux (`$XDG_CONFIG_HOME`, `$XDG_DATA_HOME`).
- `build/package.ps1` takes the runtime identifier as a parameter instead of hard-coding `win-x64`.
- Spec CON-001, AC-018 and the CI/CD section list the supported runtime identifiers.

**Exit criteria**

- CI is green on Windows and Linux.
- The app starts on Linux (a `dotnet run` build is enough) and works against the simulator.

### P1.2 — ALSA sequencer transport

Issue [#41](https://github.com/BoBoBaSs84/BB84.MPX1/issues/41).

**Goal**: Real MIDI ports on Linux.

**Scope**

- New project `BB84.MPX1.Midi.Alsa` implementing `IMidiTransport` on the ALSA sequencer API (`libasound.so.2`), P/Invoke via `[LibraryImport]` only (TRN-001, TRN-002).
- Port enumeration with stable IDs built from client and port names, made unique like the WinMM adapter (TRN-006).
- One application client with one input and one output port; subscriptions to the chosen ports.
- SysEx in: ALSA delivers long SysEx in chunks; reassemble them with `SysExAssembler`. Drop clock and active sensing like the WinMM adapter.
- SysEx out: send frames in chunks small enough for the sequencer's buffer, and wait until they are drained.
- Reader thread on `poll()` with a clean shutdown on close and dispose.
- A clear error when `libasound` or `/dev/snd/seq` is missing; the app then offers only the simulator.

**Exit criteria**

- Unit tests cover chunk reassembly and port naming.
- An integration test opens two sequencer ports wired to each other and round-trips SysEx frames up to the largest dump size. It runs in CI if the runner provides the `snd-seq` module, otherwise in the Linux VM under its own test category.
- Zero IL2xxx/IL3xxx warnings.

### P1.3 — Linux publish & AppImage

Issue [#42](https://github.com/BoBoBaSs84/BB84.MPX1/issues/42).

**Goal**: A downloadable Linux build.

**Scope**

- Native AOT publish for `linux-x64` on a Linux runner, including Avalonia's native libraries (SkiaSharp, HarfBuzzSharp).
- `.desktop` file, application icon and AppStream metainfo, shared with P1.4.
- AppImage built from the publish output.
- `release.yml` builds Windows and Linux in a matrix and attaches the AppImage and its SHA-256 file to the GitHub Release.
- The COM-001 check (no Lexicon documents or legacy binaries) runs on every artifact.
- README: Linux download, MIDI setup and permissions (the user needs access to `/dev/snd/seq`).

**Exit criteria**

- A tag build attaches the AppImage.
- The AppImage starts on a clean Linux VM without installing .NET.

### P1.4 — Flatpak

Issue [#43](https://github.com/BoBoBaSs84/BB84.MPX1/issues/43).

**Goal**: A sandboxed Linux package.

**Scope**

- Flatpak manifest (application ID, freedesktop runtime, the AOT publish output from P1.3, metadata from P1.3).
- Sandbox permissions: display (Wayland/X11) and access to the ALSA sequencer device. File access for `.syx` import/export through the portal.
- Settings and the learned database inside the Flatpak's own XDG directories.
- `release.yml` builds a `.flatpak` bundle and attaches it to the GitHub Release.

Flathub submission is out of scope; it can follow once the bundle works.

**Exit criteria**

- The bundle installs with `flatpak install` on a clean Linux VM, sees the MIDI ports and talks to the simulator and to a real port.

### P1.5 — Linux validation & release

Issue [#44](https://github.com/BoBoBaSs84/BB84.MPX1/issues/44).

**Goal**: Linux verified end to end and released.

**Scope**

- Manual check in the Linux VM: port enumeration and SysEx round-trip through a virtual MIDI port, for the AppImage and the Flatpak.
- If a USB MIDI interface can be passed through to the VM: run the `Hardware` smoke suite against the MPX 1.
- Update the README, the roadmap and spec section 9 with anything found.
- Release with the Linux artifacts (a minor version, for example `v1.1.0`).

**Exit criteria**

- Session and integration tests pass on Linux.
- Port enumeration and SysEx round-trip are verified manually on Linux.
- The GitHub Release carries the Windows zip, the AppImage and the Flatpak bundle.

## P2 — macOS support (post-1.0)

**Goal**: Run on macOS. Starts after P1.

### P2.1 — CoreMIDI transport & macOS CI

**Scope**

- CoreMIDI adapter (`BB84.MPX1.Midi.CoreMidi`) via `[LibraryImport]`: endpoint unique IDs as port IDs, an `UnmanagedCallersOnly` read callback, SysEx sends with completion.
- CI matrix: build and test on `macos-latest`; a loopback test through CoreMIDI virtual endpoints.

### P2.2 — macOS publish & app bundle

**Scope**

- Native AOT publishes for `osx-arm64` (and `osx-x64` only if it can be tested).
- `.app` bundle, ad-hoc signed, attached to the GitHub Release. Users have to allow it in Gatekeeper; the README explains how.

### P2.3 — Code signing & notarization (last)

**Scope**

- Developer ID signing and notarization. Needs an Apple Developer account, which the project does not have; this comes last.

**Exit criteria (P2)**

- Session and integration tests pass on macOS.
- Port enumeration and SysEx round-trip are verified manually on macOS.

---

## Open research items

| #   | Item                                                                                                                                                                       | Needed by | Approach                                                                                  |
| --- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------- | ----------------------------------------------------------------------------------------- |
| R1  | Verify flattened tables in the `*.md` extraction against the original Lexicon PDFs, kept outside the repository (Program Dump, Setup, Current Choices, LEDs, Appendix A/B) | M1, M7    | Manual check against the original PDF. Record fixes in spec section 9.                    |
| R2  | Display-unit string tables (for example, `eq_mode_strings`) are not published                                                                                              | M4        | Use Parameter Display (`02`) online. Capture strings from hardware in M8 for offline use. |
| R3  | Full control tree and descriptions for V1.00 and V1.10                                                                                                                     | M3, M8    | V1.00 captured in M8 (`tests/data/hardware/v100/learn.jsonl`). V1.10 still open.          |
| R4  | `paramall.bin` layout (MPX1EDIT cache)                                                                                                                                     | Optional  | Analyze the file format only (no code reuse) to offer an import path, if worthwhile.      |
| R5  | Real timing (response latency, BUSY duration, safe inter-frame gap)                                                                                                        | M8        | Measured on V1.00 in M8 (see M8 result). No inter-frame gap needed over USB Uno.          |
| R6  | Compact Program Dump (`1C`) contents                                                                                                                                       | —         | Out of scope (development-only). Pass through raw.                                        |
