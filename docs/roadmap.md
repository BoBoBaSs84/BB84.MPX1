# BB84.MPX1 Roadmap

Delivery plan for the clean-room Lexicon MPX 1 SysEx editor. Requirement IDs refer to [the specification](../spec/spec-architecture-mpx1-sysex-editor.md).

**Release target 1.0**: Windows x64, Native AOT, full SysEx coverage, simulator-verified. Hardware validation is optional.

## Overview

| #   | Milestone                               | Depends on                       | Status       |
| --- | --------------------------------------- | -------------------------------- | ------------ |
| M0  | Repository scaffold & CI                | —                                | Done         |
| M1  | Core protocol codec                     | M0                               | Done         |
| M2  | Transport & session                     | M1                               | Done         |
| M3  | Simulator & replay                      | M1, M2                           | Done         |
| M4  | Learning & parameter database           | M2, M3                           | Done         |
| M5  | Application shell & parameter editing   | M4                               | Done         |
| M6  | Programs, librarian & files             | M4, M5                           | Done         |
| M7  | Full spec coverage: dumps & front panel | M5, M6                           | Done         |
| M8  | Hardware validation (optional)          | M4+                              | Planned      |
| M9  | Release 1.0                             | M7 (M8 if hardware is available) | Ready to tag |
| P1  | Cross-platform transports (post-1.0)    | M9                               | Planned      |

M8 can start as soon as hardware is available. Its captures feed back into M3 fixtures.

M9 is complete except for its exit criteria that need the maintainer: pushing the `v1.0.0` tag (which runs the release workflow) and checking the zip on a clean Windows machine.

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

- Tagged `v1.0.0` release with an attached `win-x64` zip.
- A clean Windows machine runs the exe without installing .NET.

## P1 — Cross-platform transports (post-1.0)

**Goal**: Run on macOS and Linux.

**Scope**

- CoreMIDI adapter (macOS).
- ALSA sequencer adapter (Linux).
- `osx-arm64`, `osx-x64` and `linux-x64` AOT publishes.
- CI matrix.

**Exit criteria**

- Session and integration tests pass on each OS.
- Port enumeration and SysEx round-trip are verified manually on each OS.

---

## Open research items

| #   | Item                                                                                                                         | Needed by | Approach                                                                                  |
| --- | ---------------------------------------------------------------------------------------------------------------------------- | --------- | ----------------------------------------------------------------------------------------- |
| R1  | Verify flattened tables in the `*.md` extraction against the PDFs (Program Dump, Setup, Current Choices, LEDs, Appendix A/B) | M1, M7    | Manual check against the PDF. Record fixes in spec section 9.                             |
| R2  | Display-unit string tables (for example, `eq_mode_strings`) are not published                                                | M4        | Use Parameter Display (`02`) online. Capture strings from hardware in M8 for offline use. |
| R3  | Full control tree and descriptions for V1.00 and V1.10                                                                       | M3, M8    | Start from spec examples. Replace with hardware captures.                                 |
| R4  | `paramall.bin` layout (MPX1EDIT cache)                                                                                       | Optional  | Analyze the file format only (no code reuse) to offer an import path, if worthwhile.      |
| R5  | Real timing (response latency, BUSY duration, safe inter-frame gap)                                                          | M8        | Measure on hardware and with MPX1EDIT under otvdm.                                        |
| R6  | Compact Program Dump (`1C`) contents                                                                                         | —         | Out of scope (development-only). Pass through raw.                                        |
