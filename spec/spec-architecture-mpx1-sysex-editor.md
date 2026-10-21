---
title: BB84.MPX1 — Lexicon MPX 1 SysEx Editor Architecture & Protocol Specification
version: 0.1
date_created: 2026-10-19
last_updated: 2026-10-19
owner: BoBoBaSs84
tags: [architecture, protocol, midi, sysex, dotnet, native-aot, avalonia]
---

# Introduction

BB84.MPX1 is a clean-room reimplementation of a MIDI System Exclusive (SysEx) editor and librarian for the Lexicon MPX 1 multi-effects processor (firmware V1.00 and V1.10). It replaces the 1998 16-bit Windows editor `MPX1EDIT.EXE` (WazWare), which cannot run on 64-bit operating systems. The implementation is written in C# on .NET 10, published with Native AOT, and is derived exclusively from Lexicon's published MIDI Implementation document.

## 1. Purpose & Scope

**Purpose**: Define the architecture, protocol behavior, data contracts, constraints and acceptance criteria for BB84.MPX1 so that it can be implemented incrementally (see `docs/roadmap.md`).

**In scope (release 1.0)**:

- Complete coverage of every SysEx message class and every dump documented in `legacy/MPX1_V1_1_MIDI_Impl_Rev1_original.md`, except where section 3 explicitly excludes it.
- Device discovery, parameter-description learning, control-tree learning, parameter editing, program transfer, librarian functions, `.syx` file import/export, virtual front panel.
- Windows x64 as the release platform, with a WinMM MIDI transport.
- An in-process MPX 1 simulator used as the primary test target.

**Out of scope (release 1.0)**:

- macOS and Linux MIDI transports (planned after 1.0; the architecture must allow them).
- Firmware upgrade (`legacy/MPX1_V11_Soft_Upgrade_Rev0_original.md` describes an EPROM swap, not a MIDI procedure).
- Emulating an MPX R1 remote controller as a hardware device.
- Audio processing of any kind.

**Audience**: Developers and AI coding agents implementing BB84.MPX1.

**Assumptions**:

- No physical MPX 1 is guaranteed to be available during development. All behavior must be testable against the simulator.
- The legacy document text extraction (`*.md`) may contain table-flattening errors. The PDF next to it is authoritative.

## 2. Definitions

| Term                  | Definition                                                                                                                                    |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| MPX 1                 | Lexicon MPX 1 Multi Processor FX unit.                                                                                                        |
| SysEx                 | MIDI System Exclusive message: starts with `F0`, ends with `F7`.                                                                              |
| EOX                   | End of Exclusive, byte `F7`.                                                                                                                  |
| Header                | First 5 bytes of every MPX 1 SysEx message: `F0 06 09 <device id> <message class>`.                                                           |
| Lexicon ID            | Manufacturer ID `06`.                                                                                                                         |
| Product ID            | MPX 1 product ID `09`.                                                                                                                        |
| Device ID             | 7-bit unit address `0x00`–`0x7F`, set on the unit. `0x7F` addresses all devices.                                                              |
| Message class         | Byte 5 of the header; identifies the message layout (section 4.3).                                                                            |
| Nibble-ization        | Encoding where each data byte is sent as two bytes, each carrying 4 bits in its low nibble, least-significant nibble first.                   |
| Word                  | 16-bit little-endian value; nibble-ized as 4 bytes, least-significant nibble first.                                                           |
| Control address       | Ordered list of 16-bit control level values (A, B, C, …) that locates a node in the control tree. The null address (zero levels) is the root. |
| Control level         | One position (A, B, C, …) in a control address. MPX 1 uses at most 5.                                                                         |
| Node                  | Any control address that holds a parameter.                                                                                                   |
| Parameter type        | 16-bit ID of a parameter definition (name, size, flags, limits, display units). Many nodes can share one type.                                |
| Parameter description | Message class `04` payload describing one parameter type.                                                                                     |
| Option                | Secondary parameter type attached to a parameter (for example, units). Option data follows parameter data. `0xFFFF` = no option.              |
| Control flags         | Bit field in a parameter description: `0x01` Patchable, `0x02` Automation, `0x04` Control Level (branch), `0x08` Bottom Control Level.        |
| Branch                | Node whose control flags have `0x04` set; it has children.                                                                                    |
| Leaf                  | Editable node; control flags `0x04` clear.                                                                                                    |
| Dump                  | Large parameter (for example, Setup, Display, LEDs) transferred through message class `01`.                                                   |
| Program               | One of 250 stored effect programs: 0–199 presets, 200–249 user registers. `0xFFFF` = active program.                                          |
| Handshake             | Message class `12`; carries flow-control commands (BUSY/READY, etc.).                                                                         |
| Learned database      | Local cache of all parameter descriptions plus the control tree for one firmware version.                                                     |
| Simulator             | In-process software model of an MPX 1 that implements the transport interface.                                                                |
| Native AOT            | .NET ahead-of-time compilation to a self-contained native executable without JIT.                                                             |
| WinMM                 | Windows Multimedia API (`winmm.dll`) used for MIDI I/O.                                                                                       |
| AC                    | Acceptance criterion.                                                                                                                         |

## 3. Requirements, Constraints & Guidelines

### 3.1 Protocol codec (PRO)

- **PRO-001**: The codec shall encode and decode nibble-ized bytes: byte `b` is sent as `b & 0x0F`, then `b >> 4`.
- **PRO-002**: The codec shall encode and decode 16-bit words as 4 nibble bytes: low nibble of low byte, high nibble of low byte, low nibble of high byte, high nibble of high byte.
- **PRO-003**: The codec shall reject any nibble byte with a value above `0x0F` in a nibble-ized region and report the byte offset.
- **PRO-004**: The framer shall build and validate the header `F0 06 09 <id> <class>` and the trailing `F7`. Header and EOX bytes are never nibble-ized.
- **PRO-005**: The framer shall reject frames with a wrong manufacturer or product ID, a missing `F7`, an odd nibble count, or a length that does not match the class layout.
- **PRO-006**: Control addresses shall be encoded as a word (level count) followed by one word per level.
- **PRO-007**: Strings (names, labels, display text) are ASCII, not null-terminated, often space-padded. Bytes `0x00`–`0x07` are custom LCD characters and shall be preserved, not treated as terminators.
- **PRO-008**: Signed limits: min/max values in a parameter description are signed 16-bit words. For 1-byte parameters they shall be cast to signed or unsigned 8-bit according to the parameter's display units.
- **PRO-009**: The codec shall provide a typed model and a parser/builder for each message class listed below. "Tx" = the MPX 1 transmits it; "Rx" = the MPX 1 receives it.

| ID      | Class | Name                         | Direction | Requirement                                                                                                                                                                   |
| ------- | ----- | ---------------------------- | --------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| PRO-010 | `00`  | System Configuration         | Tx        | Parse major/minor version, 8-char build time, 11-char build date, parameter type count, bottom parameter number, max control levels.                                          |
| PRO-011 | `01`  | Parameter Data               | Tx + Rx   | Parse/build byte count (word), data (parameter followed by option bytes), control address.                                                                                    |
| PRO-012 | `02`  | Parameter Display            | Tx        | Parse character count (word), string, control address.                                                                                                                        |
| PRO-013 | `03`  | Parameter Type               | Tx        | Parse parameter type word.                                                                                                                                                    |
| PRO-014 | `04`  | Parameter Description        | Tx        | Parse type, name length (byte), name, size in bytes (word), control flags (byte), option type (word), unit count (byte), then `n` × {min word, max word, display-units word}. |
| PRO-015 | `05`  | Parameter Label              | Tx        | Parse character count (word), string, control address.                                                                                                                        |
| PRO-016 | `06`  | Request                      | Rx        | Build requests: requested class (nibble-ized byte) followed by class-specific arguments (section 4.4).                                                                        |
| PRO-017 | `12`  | Handshake                    | Tx + Rx   | Parse/build command byte: 0 NOP, 1 ARE_YOU_THERE, 2 IM_ALIVE, 3 BUSY, 4 READY, 5 ERROR.                                                                                       |
| PRO-018 | `16`  | Database Dump                | Tx + Rx   | Parse/build 250 entries × {effect-type word, source-type byte} (sort flags; bit tables in section 4.6).                                                                       |
| PRO-019 | `18`  | Report Effect Parameters     | Tx        | Parse effect/controller type (byte), number (byte), 11-char name, parameter count (byte, 1–30), 30 parameter-type words (unused slots ignored).                               |
| PRO-020 | `19`  | Report All Effect Parameters | Tx        | Parse program number (word), then 6 blocks (Pitch, Chorus, EQ, Mod, Reverb, Delay) × {11-char name, count byte, 30 type words}.                                               |
| PRO-021 | `1A`  | Program Information          | Tx        | Parse program number (word), 12-char name, 6 algorithm-number bytes (Pitch 0–10, Chorus 0–11, EQ 0–18, Mod 0–8, Reverb 0–5, Delay 0–8).                                       |
| PRO-022 | `1B`  | Program Dump                 | Tx + Rx   | Parse/build the full program layout in section 4.5. Program number `0`–`249` or `0xFFFF`.                                                                                     |
| PRO-023 | `1C`  | Compact Program Dump         | —         | Documented as "Lexicon development use only". Recognize the class, keep the raw bytes, and pass them through unchanged. Do not interpret the payload.                         |
| PRO-024 | —     | Universal Device Inquiry     | Rx / Tx   | Build `F0 7E <id> 06 01 F7`; parse Identity Reply `F0 7E <id> 06 02 06 00 00 09 00 <major> <minor> <phase> 00 F7`.                                                            |

- **PRO-025**: Request class `10` ("Memory Tool") appears only in the request quick reference and is not documented. It shall not be sent by the application.
- **PRO-026**: Unknown message classes shall be surfaced as a raw-frame event and never cause an exception that terminates a session.

### 3.2 Transport (TRN)

- **TRN-001**: All MIDI I/O shall go through `IMidiTransport` (section 4.1). No other layer calls OS MIDI APIs.
- **TRN-002**: The WinMM transport shall use `[LibraryImport]` source-generated P/Invoke against `winmm.dll` (`midiInGetNumDevs`, `midiInGetDevCaps`, `midiInOpen`, `midiInPrepareHeader`, `midiInAddBuffer`, `midiInStart`, `midiInStop`, `midiInReset`, `midiInClose`, and the matching `midiOut*` functions including `midiOutLongMsg` and `midiOutShortMsg`).
- **TRN-003**: The WinMM input callback shall be a static `[UnmanagedCallersOnly]` function. It shall copy data out of the driver buffer, re-queue the buffer, and hand the bytes to a managed channel without blocking.
- **TRN-004**: The transport shall reassemble SysEx frames that arrive split across several driver buffers, and support frames of at least 4096 bytes (the Program Dump is 844 bytes; the Database Dump is about 1506 bytes).
- **TRN-005**: The transport shall deliver short (channel) messages separately from SysEx frames.
- **TRN-006**: Port enumeration shall return stable identifiers and display names. Open/close shall be idempotent and safe to call from any thread.
- **TRN-007**: The simulator (SIM) and a record/replay transport shall implement the same interface.

### 3.3 Session (SES)

- **SES-001**: A session binds one input port, one output port and one device ID.
- **SES-002**: Requests shall be matched to responses by response class and, where present, by control address, parameter type or program number.
- **SES-003**: Every request shall have a configurable timeout (default 1000 ms) and retry count (default 2). After the final retry fails, the caller receives a typed timeout error that includes the request bytes.
- **SES-004**: Outgoing traffic shall be serialized: at most one outstanding request per response key.
- **SES-005**: After sending a Program Dump (`1B`) or another bulk dump, the session shall pause sending while the device reports BUSY (`12`/3) and resume on READY (`12`/4). If READY does not arrive within a configurable timeout (default 3000 ms), the session reports an error.
- **SES-006**: The session shall support a connectivity check with ARE_YOU_THERE (`12`/1) and expect IM_ALIVE (`12`/2).
- **SES-007**: The session shall detect the firmware version using Device Inquiry first, then System Configuration (`00`).
- **SES-008**: A configurable minimum gap between outgoing SysEx frames (default 0 ms) shall exist to support slow MIDI interfaces.
- **SES-009**: Unsolicited incoming frames (for example, automation, display updates) shall be published as events.

### 3.4 Learning & database (LRN)

- **LRN-001**: Learning shall request System Configuration, then Parameter Description `04` for every type from `0` to `count − 1`.
- **LRN-002**: Learning shall walk the control tree depth-first from the null address using Parameter Type `03` requests. For a branch node with maximum value `max`, child addresses `0`…`max` are valid. Learning stops at leaves (Control Level flag clear).
- **LRN-003**: Learning shall report progress (descriptions done/total, nodes visited) and support cancellation.
- **LRN-004**: The learned database shall be persisted to JSON with a key of firmware version plus build date/time. A matching cache shall be loaded instead of re-learning.
- **LRN-005**: The application shall never hard-code parameter-type numbers that differ between V1.00 and V1.10 (most dump and system types shift by +1 in V1.10). It shall resolve them through the learned database by name and position.
- **LRN-006**: Workaround for known bug 1 (Appendix D): parameter type `0x014A` reports size 0. The database shall override the size to 542 bytes.
- **LRN-007**: Workaround for known bug 2: the branch at `A:1 B:2 C:0` ends with type `0x015B`, which is a control level, not an editable parameter. The tree walker shall treat this node as a terminal non-editable node and not recurse.
- **LRN-008**: Workaround for known bug 3: the editor shall block Parameter Data writes to parameters of an algorithm that is not loaded in the corresponding effect block. Writing them can crash the unit.
- **LRN-009**: Learning shall be able to import a captured `.syx` stream (from the record transport) instead of a live device.

### 3.5 Parameters & values (PAR)

- **PAR-001**: Parameter values shall be read and written through Parameter Data (`01`) using control addresses.
- **PAR-002**: Option data shall be split from parameter data using the parameter's size and the option type's description.
- **PAR-003**: Display text shall come from Parameter Display (`02`) when a device is connected. When offline, the application shall format values from the display-units type (Appendix B). Units whose string tables are not published shall show the raw number.
- **PAR-004**: **Rate** (types `0x0000`–`0x0010`): MSB set means a ratio (high byte numerator, low byte denominator, each 1–24); MSB clear means hundredths of a hertz (max 5000 = 50 Hz).
- **PAR-005**: Option types whose size is 0 (Rate units, Drate units) shall read their value from the MSB of the root parameter.
- **PAR-006**: **MIDI Speed** (type `0x00F2`, address `A:1 B:2 C:10`) shall accept only `0x01`, `0x02`, `0x04`, `0x40`.
- **PAR-007**: **Panel Button** (type `0x011D` in V1.00, `0x011E` in V1.10): send PRESS then RELEASE. HOLD shall be a separate UI action that sends HOLD then RELEASE. Combinations (LEFT+RIGHT, TIP+RING) shall use the dedicated combination codes, never two separate PRESS messages.
- **PAR-008**: Multi-unit parameters (unit count > 1) shall pick the active min/max/display-units triple using the option value.

### 3.6 Programs & librarian (PRG)

- **PRG-001**: Read and write a single program (0–249 and active `0xFFFF`) with Program Dump `1B`.
- **PRG-002**: Model the program structure in section 4.5, including effect data blocks, sort flags, audio routing, algorithm numbers, name, effect status, soft values, tempo fields, meter, master level/mix, patches, knob, LFOs, arpeggiator, ADSRs, random, A/B, sample-and-hold and envelope generators.
- **PRG-003**: Locate a parameter inside a 32-byte effect data block by walking the algorithm's parameter-type list and summing each type's size, including option bytes.
- **PRG-004**: Writes to presets 0–199 shall require explicit user confirmation. Saving to user registers 200–249 shall not.
- **PRG-005**: Batch backup shall read all 250 programs plus the Database Dump (`16`) and selected system dumps into one `.syx` file. Restore shall write them back with handshake flow control.
- **PRG-006**: `.syx` import shall accept files containing any number of MPX 1 frames and ignore frames from other devices with a warning.
- **PRG-007**: Program Information (`1A`) shall be used to list names and algorithms without transferring full dumps.

### 3.7 Dumps & system (DMP)

Each dump is a Parameter Data (`01`) transfer at the control address the learned tree gives for its type. The dump shall be parsed into a typed model when its layout is documented, and kept as raw bytes otherwise.

| ID      | Dump                              | Type V1.00 / V1.10  |
| ------- | --------------------------------- | ------------------- |
| DMP-001 | All LEDs                          | `0x012F` / `0x0130` |
| DMP-002 | Display (32 chars)                | `0x011E` / `0x011F` |
| DMP-003 | Custom Character Bitmap (8 chars) | `0x0130` / `0x0131` |
| DMP-004 | Setup (5 stored + active)         | `0x0128` / `0x0129` |
| DMP-005 | Global Patches (10)               | `0x012D` / `0x012E` |
| DMP-006 | Bypass Controllers (7)            | `0x012C` / `0x012D` |
| DMP-007 | Remap Controllers (163)           | `0x012B` / `0x012C` |
| DMP-008 | Current Choices                   | `0x0125` / `0x0126` |
| DMP-009 | Patches (5 per active program)    | `0x0129` / `0x012A` |
| DMP-010 | Soft Row (10 per active program)  | `0x012A` / `0x012B` |

- **DMP-011**: Controller indices (Appendix A) shall be encoded as static data tables in Core, with source ranges validated per dump (for example, Global Patch sources `0x18`–`0xA2`, Bypass sources `0x19`–`0x96`, `0xFF` = unassigned).
- **DMP-012**: Master Level values: `A0` = Off, `A1`–`FF` = −95…−1 dB, `00` = 0 dB. Master Mix: `0x00`–`0x64` = 0–100 %.

### 3.8 Simulator (SIM)

- **SIM-001**: The simulator shall implement `IMidiTransport` and respond to every request class in PRO-009.
- **SIM-002**: The simulator's model (parameter descriptions, tree, programs) shall be loadable from a JSON fixture. The initial fixture is hand-built from the spec's examples and grows when real captures become available.
- **SIM-003**: The simulator shall emit BUSY then READY after receiving a Program Dump or a dump write, with a configurable delay.
- **SIM-004**: The simulator shall support fault injection: dropped responses, delayed responses, corrupt nibbles, missing READY.
- **SIM-005**: The simulator shall reproduce the three Appendix D bugs so that LRN-006 to LRN-008 can be tested.

### 3.9 User interface (UI)

- **UI-001**: MIDI setup: choose input/output ports and device ID, test the connection (ARE_YOU_THERE), and show the firmware version.
- **UI-002**: Learning: progress, cancel, cache status, forced re-learn.
- **UI-003**: Control-tree browser that shows parameter names and, for leaves, the current value and display text.
- **UI-004**: Parameter editors chosen by display units: numeric slider/spinner, enum list, text (names), toggle, and a Rate editor with Hz/ratio mode.
- **UI-005**: Program librarian: 250-slot list (number, name, algorithms, sort flags), load to active, receive, send, rename, backup/restore, `.syx` import/export.
- **UI-006**: Program editor: six effect blocks with algorithm selection, routing, effect on/off, soft row, tempo/tap, patches, knob, and modulators.
- **UI-007**: Virtual front panel: 2×16 display mirror (Display Dump plus custom characters), LED mirror (All LEDs Dump), and buttons (Panel Button) with PRESS/RELEASE/HOLD.
- **UI-008**: System dumps editors: Setup, Global Patches, Bypass Controllers, Remap Controllers, Database (sort flags).
- **UI-009**: MIDI monitor panel: show sent/received frames as hex with decoded summaries.
- **UI-010**: Offline mode: browse and edit programs and dumps from files without a device.

### 3.10 Security (SEC)

- **SEC-001**: Parsers shall bound every length field by the actual frame size and never allocate buffers based on untrusted length values above 64 KiB.
- **SEC-002**: Imported `.syx` and JSON files shall be treated as untrusted input. Malformed content produces a validation error, not a crash.

### 3.11 Constraints (CON)

- **CON-001**: Target framework `net10.0`. The app is published with `PublishAot=true` for `win-x64`.
- **CON-002**: Builds shall produce zero trimming/AOT warnings (`IL2xxx`, `IL3xxx`). These warnings are treated as errors.
- **CON-003**: No reflection-based serialization. JSON uses `System.Text.Json` source generation.
- **CON-004**: `BB84.MPX1.Core` shall have no I/O, no OS API calls and no UI dependency.
- **CON-005**: Clean room: no code, text, bitmaps or resources from `MPX1EDIT.EXE` or `MPX1HLP.HLP` shall be copied. The old editor may be used only as a black-box behavior reference (for example, run under otvdm and compare MIDI traffic).
- **CON-006**: No Lexicon logo or trademark artwork. Product names may be used descriptively ("for the Lexicon MPX 1").
- **CON-007**: Destructive device operations (writing presets, restoring backups, writing Setup) shall require user confirmation.

### 3.12 Guidelines (GUD)

- **GUD-001**: Use `ReadOnlySpan<byte>`/`Span<byte>` for codec paths; avoid per-byte allocations.
- **GUD-002**: Prefer immutable `record` types for messages and descriptions.
- **GUD-003**: Use `async` APIs with `CancellationToken` on every session and transport call.
- **GUD-004**: Keep Appendix A/B tables as data (static arrays or embedded JSON generated at build time), not as `switch` statements spread across code.
- **GUD-005**: Every requirement ID shall be referenced by at least one test name or test attribute.

### 3.13 Patterns (PAT)

- **PAT-001**: MVVM in the app using CommunityToolkit.Mvvm source generators. Avalonia compiled bindings (`x:CompileBindings="True"`) everywhere.
- **PAT-002**: Ports-and-adapters: Core (domain) ← Midi (session, ports) ← adapters (WinMM, Simulator, Replay) ← App.
- **PAT-003**: Message classes as a closed set of types handled by an exhaustive `switch` expression.

## 4. Interfaces & Data Contracts

### 4.1 Solution layout

| Project                    | Kind                                           | Depends on                        |
| -------------------------- | ---------------------------------------------- | --------------------------------- |
| `src/BB84.MPX1.Core`       | Class library (AOT/trim compatible)            | —                                 |
| `src/BB84.MPX1.Midi`       | Class library: transport abstraction + session | Core                              |
| `src/BB84.MPX1.Midi.WinMM` | Class library: WinMM adapter (Windows only)    | Midi                              |
| `src/BB84.MPX1.Simulator`  | Class library: simulated device + replay       | Core, Midi                        |
| `src/BB84.MPX1.App`        | Avalonia application, `PublishAot=true`        | Core, Midi, Midi.WinMM, Simulator |
| `tests/BB84.MPX1.*.Tests`  | MSTest projects, one per library               | project under test                |

### 4.2 Transport contract

```csharp
public interface IMidiTransport : IAsyncDisposable
{
    IReadOnlyList<MidiPortInfo> GetInputPorts();
    IReadOnlyList<MidiPortInfo> GetOutputPorts();
    ValueTask OpenAsync(MidiPortId input, MidiPortId output, CancellationToken ct);
    ValueTask CloseAsync(CancellationToken ct);
    ValueTask SendSysExAsync(ReadOnlyMemory<byte> frame, CancellationToken ct);   // frame includes F0..F7
    ValueTask SendShortAsync(int message, CancellationToken ct);
    IAsyncEnumerable<MidiInput> ReadAllAsync(CancellationToken ct);              // SysEx frames + short messages
}

public sealed record MidiPortInfo(MidiPortId Id, string Name);
public readonly record struct MidiPortId(string Value);
public abstract record MidiInput(DateTimeOffset Timestamp);
public sealed record SysExInput(DateTimeOffset Timestamp, ReadOnlyMemory<byte> Frame) : MidiInput(Timestamp);
public sealed record ShortInput(DateTimeOffset Timestamp, int Message) : MidiInput(Timestamp);
```

### 4.3 Core message model

Namespace `BB84.MPX1.Core.Protocol` (program model in `BB84.MPX1.Core.Programs`).

```csharp
public readonly struct ControlAddress : IEquatable<ControlAddress>     // structural equality; Root = no levels
{ public ImmutableArray<ushort> Levels { get; } public ControlAddress Child(ushort value); public ControlAddress Parent(); }
public readonly record struct DeviceId(byte Value);                    // 0..127, DeviceId.All = 127

public abstract record Mpx1Message(DeviceId Device) { public byte[] ToBytes(); }
public abstract record LexiconMessage(DeviceId Device) : Mpx1Message(Device) { public abstract MessageClass MessageClass { get; } }

// One sealed record per class (PRO-009):
// SystemConfiguration, ParameterData, ParameterDisplay, ParameterType, ParameterDescription, ParameterLabel,
// Request (static factories per spec 4.4), Handshake, DatabaseDump, EffectParameters, AllEffectParameters,
// ProgramInformation, ProgramDump (wraps Programs.ProgramData), RawLexiconMessage (1C and unknown classes),
// DeviceInquiry, IdentityReply (universal, not nibble-ized).

[Flags] public enum ParameterAttributes : byte { None = 0, Patchable = 0x01, Automation = 0x02, ControlLevel = 0x04, BottomControlLevel = 0x08 }
public enum HandshakeCommand : byte { Nop = 0, AreYouThere = 1, ImAlive = 2, Busy = 3, Ready = 4, Error = 5 }

public static class Mpx1Codec
{
    public static Mpx1Message Decode(ReadOnlySpan<byte> frame);          // throws Mpx1FormatException (Error.Offset)
    public static bool TryDecode(ReadOnlySpan<byte> frame, out Mpx1Message? message, out Mpx1FormatError? error);
    public static byte[] Encode(Mpx1Message message);
}
```

The "control flags" of a parameter description are modelled as `ParameterAttributes`, and the program sort flags as `ProgramCategories` (`EffectCategories` word + `SourceCategories` byte), because analyzer rule CA1711 forbids type names ending in "Flags".

### 4.4 Request arguments (class `06`)

After `F0 06 09 <id> 06`, the requested class is sent as one nibble-ized byte, followed by:

| Requested class                   | Arguments (all nibble-ized)                            |
| --------------------------------- | ------------------------------------------------------ |
| `00` System Configuration         | 3 unused bytes (`00 00 00 00 00 00`)                   |
| `01`, `02`, `03`, `05`            | Control address: level count word + one word per level |
| `04` Parameter Description        | Parameter type word                                    |
| `12` Handshake                    | Command byte                                           |
| `16` Database Dump                | 3 unused bytes                                         |
| `18` Report Effect Parameters     | Effect type byte, algorithm number byte, 1 unused byte |
| `19` Report All Effect Parameters | Program number word, 1 unused byte                     |
| `1A` Program Information          | Program number word, 1 unused byte                     |
| `1B` Program Dump                 | Program number word (`0xFFFF` = active), 1 unused byte |

Effect/controller types for `18`: 0 Pitch, 1 Chorus, 2 EQ, 3 Mod, 4 Reverb, 5 Delay, 6 Knob, 7 LFO 1, 8 LFO 2, 9 MIDI Arpeggiator, 10 ADSR 1, 11 ADSR 2, 12 Random Number Generator, 13 A-B Generator, 14 Sample and Hold, 15 Envelope Generator 1, 16 Envelope Generator 2. For types 6–16 the algorithm number is ignored.

### 4.5 Program Dump (`1B`) layout

Byte offsets are frame offsets (1-based, as in the source document). Sizes are un-nibble-ized bytes.

| Frame bytes | Field                                                                                                                               | Size   |
| ----------- | ----------------------------------------------------------------------------------------------------------------------------------- | ------ |
| 6–9         | Program number                                                                                                                      | 2      |
| 10–393      | Effect data: Pitch, Chorus, EQ, Mod, Reverb, Delay                                                                                  | 6 × 32 |
| 394–399     | Sort flags (effect word, source byte)                                                                                               | 3      |
| 400–479     | Audio routing: 8 blocks × {effect type, upper input, lower input, routing, patch type}                                              | 40     |
| 480–491     | Algorithm numbers (Pitch, Chorus, EQ, Mod, Reverb, Delay)                                                                           | 6      |
| 492–515     | Program name (space padded)                                                                                                         | 12     |
| 516–517     | Effect status (bit 0 Pitch … bit 5 Delay; 1 = active)                                                                               | 1      |
| 518–557     | Soft values: 10 × {effect/controller type, parameter index}                                                                         | 20     |
| 558–561     | Tempo (41–400 BPM)                                                                                                                  | 2      |
| 562–563     | Tempo source (0 internal, 1 MIDI)                                                                                                   | 1      |
| 564–565     | Beat value (0 eighth … 6 whole)                                                                                                     | 1      |
| 566–567     | Tap source (Appendix A)                                                                                                             | 1      |
| 568–569     | Tap average (1–8)                                                                                                                   | 1      |
| 570–571     | Tap source level (0–127)                                                                                                            | 1      |
| 572–573     | Meter (0–24)                                                                                                                        | 1      |
| 574–575     | Master level                                                                                                                        | 1      |
| 576–577     | Master mix                                                                                                                          | 1      |
| 578–697     | Patches: 5 × 12 bytes                                                                                                               | 60     |
| 698–721     | Knob: value, min, max, 9-char name                                                                                                  | 12     |
| 722–843     | LFO 1 (8), LFO 2 (8), Arpeggiator (5), ADSR 1 (9), ADSR 2 (9), Random (4), A/B (5), Sample & Hold (5), Env Gen 1 (4), Env Gen 2 (4) | 61     |
| 844         | `F7`                                                                                                                                | —      |

Total frame length: 844 bytes. Total payload: 419 bytes.

### 4.6 Database Dump (`16`) sort flags

- Source type (1 byte): `01` Live PA, `02` Vocal, `04` Guitar, `08` Keyboard, `10` Acoustic, `20` Drums, `40` Tempo, `80` SoundFX.
- Effect type (word): `0001` Pitch, `0002` Chorus, `0004` EQ, `0008` Mod, `0010` Delay, `0020` Ambient, `0040` Chamber, `0080` Gate, `0100` Hall, `0200` Inverse, `0400` Plate, `0800` Dual.

### 4.7 Persistence formats

| Artifact             | Format                                         | Notes                                                 |
| -------------------- | ---------------------------------------------- | ----------------------------------------------------- |
| Learned database     | JSON (source-generated), `schemaVersion` field | Key: firmware major/minor + build date/time.          |
| Application settings | JSON                                           | Ports, device ID, timeouts, retries, inter-frame gap. |
| Program/backup files | Raw `.syx` (concatenated frames)               | Interoperable with generic SysEx tools.               |
| Simulator fixtures   | JSON                                           | Descriptions, tree, programs, dumps.                  |
| Recorded sessions    | JSON lines: `{t, dir, hex}`                    | Input for replay transport and regression tests.      |

## 5. Acceptance Criteria

- **AC-001** (PRO-001/002): Given byte `0xA5`, When nibble-encoded, Then output is `05 0A`. Given word `0x0125`, When encoded, Then output is `05 02 01 00`.
- **AC-002** (PRO-016): Given device 0 and a Parameter Type request for address `A:0 B:2 C:1 D:2`, When encoded, Then output equals `F0 06 09 00 06 03 00 04 00 00 00 00 00 00 00 02 00 00 00 01 00 00 00 02 00 00 00 F7`.
- **AC-003** (PRO-016): Given a Parameter Description request for type `0x0125`, When encoded, Then output equals `F0 06 09 00 06 04 00 05 02 01 00 F7`.
- **AC-004** (PRO-016): Given a System Configuration request for device 0, When encoded, Then output equals `F0 06 09 00 06 00 00 00 00 00 00 00 00 F7`.
- **AC-005** (PRO-016): Given a Program Information request for program 8, When encoded, Then output equals `F0 06 09 00 06 0A 01 08 00 00 00 00 00 F7`.
- **AC-005a** (PRO-016): Given a Program Dump request for program 8, When encoded, Then output equals `F0 06 09 00 06 0B 01 08 00 00 00 00 00 F7`.
- **AC-006** (PRO-016): Given a Report Effect Parameters request for Chorus algorithm 4, When encoded, Then output equals `F0 06 09 00 06 08 01 01 00 04 00 00 00 F7`.
- **AC-007** (PRO-017): Given ARE_YOU_THERE for device 0, When encoded, Then output equals `F0 06 09 00 12 01 00 F7` (the spec example omits the high nibble; see section 9).
- **AC-008** (PRO-011): Given the documented response `F0 06 09 00 01 01 00 00 00 00 00 04 00 00 00 00 00 00 00 02 00 00 00 01 00 00 00 02 00 00 00 F7`, When decoded, Then the result is Parameter Data with 1 data byte `0x00` at address `A:0 B:2 C:1 D:2`.
- **AC-009** (PRO-005, SEC-001): Given a frame with a byte count larger than the frame, When decoded, Then `TryDecode` returns false with the offending offset and no exception escapes.
- **AC-010** (PRO-022): Given any valid Program Dump, When decoded and re-encoded, Then the bytes are identical.
- **AC-011** (SES-005): Given the simulator emits BUSY after a Program Dump write, When a second dump is queued, Then it is not sent until READY arrives.
- **AC-012** (SES-003): Given the simulator drops the first two responses, When a request is sent with 2 retries, Then the third attempt succeeds and exactly 3 requests are on the wire.
- **AC-013** (LRN-002/004): Given the simulator fixture, When learning completes, Then the tree matches the fixture exactly; When the app restarts against the same firmware, Then no `04` requests are sent.
- **AC-014** (LRN-006/007): Given the simulator reproduces the Appendix D bugs, When learning completes, Then type `0x014A` has size 542 and the walker does not recurse below `A:1 B:2 C:0`.
- **AC-015** (LRN-008): Given Detune (M) is loaded, When the user edits a Shift (M) parameter, Then no Parameter Data frame is sent and the UI explains why.
- **AC-016** (PAR-004): Given Rate value `0x8304`, Then it is displayed as ratio 3:4. Given `0x0064`, Then it is displayed as 1.00 Hz.
- **AC-017** (PRG-005): Given a simulator with 250 programs, When backup and then restore run, Then the simulator state is byte-identical before and after.
- **AC-018** (CON-001/002): When `dotnet publish src/BB84.MPX1.App -r win-x64 -c Release` runs, Then it produces a native executable with zero IL2xxx/IL3xxx warnings.

## 6. Test Automation Strategy

- **Test levels**:
  - Unit: Core codec and models, using byte vectors from the legacy document.
  - Integration: session + learning + librarian against the simulator and the replay transport.
  - UI: view-model tests (no UI automation for 1.0). Avalonia headless tests for critical views are optional.
  - Hardware: tests marked `[TestCategory("Hardware")]`, excluded by default, run manually against a real MPX 1.
- **Frameworks**: MSTest 4 on Microsoft.Testing.Platform. No mocking framework is required; the simulator and hand-written fakes replace mocks.
- **Test data**: spec byte vectors live inline in tests next to their requirement ID. Simulator fixtures and recorded sessions live under `tests/data/`.
- **CI/CD**: GitHub Actions on `windows-latest` runs restore, build (warnings as errors), test (excluding Hardware) and an AOT publish of the app. Upload the published exe as a build artifact.
- **Coverage**: at least 90 % line coverage for `BB84.MPX1.Core`, at least 75 % for `BB84.MPX1.Midi`. No threshold for App.
- **Performance**: learning against the simulator with zero latency shall finish in under 5 s. Codec decode of a Program Dump shall allocate no more than the resulting model.

## 7. Rationale & Context

- **Clean room over porting**: `MPX1EDIT.EXE` is 16-bit segmented Borland Pascal. Decompilation gives low-quality output, and its copyright ("WazWare (c) 1989-98 All rights reserved") does not permit porting. Lexicon's document fully specifies the protocol, and the device describes itself, so a spec-driven implementation is both legal and practical.
- **Learning instead of hard-coding**: parameter-type numbers shift between V1.00 and V1.10. Learning from the device (and caching the result) supports both versions without version tables.
- **Simulator first**: no hardware is guaranteed. A simulator that implements the same transport contract makes every protocol path testable and lets hardware validation become an optional, late milestone.
- **Native AOT + Avalonia**: a single self-contained executable with fast startup. WinForms and WPF do not support AOT; Avalonia does with compiled bindings, and keeps the macOS/Linux path open.
- **WinMM first**: it is the API the original editor used, is available on every Windows version, and is easy to call from AOT code. Other OS transports only need a new adapter.

## 8. Dependencies & External Integrations

### External Systems

- **EXT-001**: Lexicon MPX 1 (V1.00 or V1.10) — MIDI SysEx over 5-pin DIN via a MIDI interface. Optional during development.

### Third-Party Services

- None. The application runs fully offline.

### Infrastructure Dependencies

- **INF-001**: GitHub Actions Windows runners — build, test and AOT publish.

### Data Dependencies

- **DAT-001**: `legacy/MPX1_V1_1_MIDI_Impl_Rev1_original.md` / `.pdf` — authoritative protocol source.
- **DAT-002**: Appendix A (controller indices) and Appendix B (display units) — encoded as static data in Core.
- **DAT-003**: Recorded SysEx sessions from real hardware, when available — regression fixtures.

### Technology Platform Dependencies

- **PLT-001**: .NET 10 SDK — Native AOT, `LibraryImport`, `System.Text.Json` source generation.
- **PLT-002**: Avalonia UI with compiled bindings — cross-platform AOT-compatible UI.
- **PLT-003**: CommunityToolkit.Mvvm — source-generated MVVM, AOT-compatible.
- **PLT-004**: MSTest on Microsoft.Testing.Platform.
- **PLT-005**: Windows `winmm.dll` — MIDI I/O for the 1.0 transport.

### Compliance Dependencies

- **COM-001**: Copyright — no material from `MPX1EDIT.EXE`/`MPX1HLP.HLP`; Lexicon documents stay in `legacy/` as reference only and are not redistributed in release artifacts.
- **COM-002**: Trademarks — "Lexicon" and "MPX 1" are used only descriptively.

## 9. Examples & Edge Cases

```text
// Nibble-ized word 0x01C0 (parameter type count in System Configuration)
00 0C 01 00

// Parameter Data request: Program > EQ > 1 Band (M) > Gain  (A:0 B:2 C:1 D:2), device 0
F0 06 09 00 06 01 00 04 00 00 00 00 00 00 00 02 00 00 00 01 00 00 00 02 00 00 00 F7

// Request Report All Effect Parameters for the active program (0xFFFF)
F0 06 09 00 06 09 01 0F 0F 0F 0F 00 00 F7
```

Known inconsistencies in the source document that the implementation must handle:

| #   | Location                         | Issue                                                      | Resolution                                                                 |
| --- | -------------------------------- | ---------------------------------------------------------- | -------------------------------------------------------------------------- |
| E1  | Device Inquiry reply             | Header printed as `0xF0, 0xFE`                             | Use `F0 7E` (MIDI 1.0 Universal Non-Real-Time). Accept both when parsing.  |
| E2  | Handshake example                | `F0 06 09 00 12 01 F7` sends one nibble for a 1-byte field | Send `01 00`. When parsing, accept a single nibble before `F7`.            |
| E3  | Classes `19`, `1A`               | Marked "Receive only" though the unit transmits them       | Treat as Tx (device to editor).                                            |
| E4  | Device ID range                  | "0–127 transmit, 0–126 receive" vs. "all devices (127)"    | Allow 0–127. Treat 127 as broadcast; responses carry the real device ID.   |
| E5  | Database Dump byte offsets       | Offsets in the table are inconsistent                      | Derive layout from field sizes: 250 × 3 bytes payload.                     |
| E6  | Request class `10` "Memory Tool" | Undocumented                                               | Never send (PRO-025).                                                      |
| E7  | Text extraction                  | Tables flattened in `*.md`                                 | Verify layouts against the PDF before implementing.                        |
| E8  | Tap Source range                 | Starts at "Footswitch Pulse 1 (tip)", not in Appendix A    | Start at `Sw1` (`0x1C`, momentary foot switch 1). Confirm on hardware.     |
| E9  | Panel Button combinations        | Text names TIP/RING as a combination; table has no code    | Offer only Left + Right (`15`/`2B`/`41`); Tip and Ring are single buttons. |

Edge cases:

- Requests to an address outside the tree: the device may not answer; the session times out (SES-003) and the walker records the gap.
- A parameter with an option of size 0 (Rate units): the data length equals the parameter size only (PAR-005).
- Names containing bytes `0x00`–`0x07`: rendered with the custom character glyphs, never truncated (PRO-007).
- Program number `0xFFFF` in a Program Dump write: replaces the active program without storing it.

## 10. Validation Criteria

- Every message class in the source document's "Message Classes" list maps to a PRO requirement, and every dump in its "Dumps" chapter maps to a DMP requirement.
- Every requirement ID is covered by at least one passing test (GUD-005), except Hardware-only tests.
- AC-001 to AC-018 pass in CI.
- `dotnet publish` with Native AOT produces zero IL2xxx/IL3xxx warnings.
- A full learn → edit → backup → restore cycle runs against the simulator with no unhandled exceptions.

## 11. Related Specifications / Further Reading

- [Roadmap](../docs/roadmap.md)
- `legacy/MPX1_V1_1_MIDI_Impl_Rev1_original.pdf` — MPX 1 V1.10 MIDI Implementation Details (Lexicon 070-11835 Rev 1)
- `legacy/MPX1_User_Guide_Rev1_original.pdf` — MPX 1 User Guide
- `legacy/MPX1_V1_1_Adden_Rev1_original.pdf` — V1.10 addendum (MPX R1 support)
- `legacy/MPX1_Presets_Rev1_original.pdf`, `legacy/MPX1_V1_1_Presets_Rev1_original.pdf` — factory preset lists
- MIDI 1.0 Detailed Specification 4.2 — Universal Non-Real-Time Device Inquiry
- Microsoft Learn: Native AOT deployment; `LibraryImport` source-generated P/Invoke
- Avalonia documentation: Native AOT and compiled bindings
