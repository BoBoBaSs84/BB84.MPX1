# BB84.MPX1

A SysEx editor and librarian for the **Lexicon MPX 1** multi-effects processor (firmware V1.00 and V1.10). It replaces the 1998 16-bit editor, which no longer runs on 64-bit Windows.

BB84.MPX1 is a clean-room implementation, written from Lexicon's published MIDI Implementation document only. It is a single native Windows x64 executable (.NET 10 Native AOT, Avalonia UI), and you don't need to install .NET.

![Parameter editing](docs/images/parameters.png)

## Features

- **Learns your unit.** The editor reads the parameter descriptions, control tree and algorithm parameter lists from the MPX 1 and caches them per firmware build. Both V1.00 and V1.10 work without built-in version tables.
- **Live parameter editing.** Browse the control tree and edit values with editors that match each parameter (slider, list, switch, text, Rate in Hz or tempo ratio). Changes reach the unit as you drag. The unit's own display text is read back after each change.
- **Librarian.** View all 250 programs with names, algorithms and sort flags. Load, rename, edit sort flags, import and export `.syx` files, and make full backups and restores, including Setups, Global Patches, Bypass and Remap Controllers.
- **Program editor.** Edit every part of a program:
  - the six effect blocks and their algorithms
  - audio routing on a signal-flow map, offering only the routings the unit allows
  - soft row and patches
  - tempo, tap, meter, master level and mix, and the knob
  - all modulators (LFOs, ADSRs, arpeggiator, random, A/B, sample and hold, envelopes)

  You hear each change on the active program as you edit; it is stored only when you choose Store. Program files can also be edited offline.

- **System dumps.** Typed editors for the five stored setups and the active one, Global Patches, Bypass and Remap Controllers, plus the read-only Current Choices.
- **Virtual front panel.** The 2×16 display (with custom characters), LEDs, headroom bars and digits are mirrored live. You can press, hold and combine the panel buttons, and turn the encoder.
- **MIDI monitor.** Every frame is shown as hex with a decoded summary.
- **Built-in simulator.** Try everything without hardware: choose "MPX 1 Simulator" as the MIDI port.
- **Safe by default.** Overwriting factory presets, restoring backups and writing setups always ask first. Parameters of algorithms that are not loaded are never written, because doing so can crash the unit (Appendix D of the MIDI document).

| Librarian                               | Program editor                             |
| --------------------------------------- | ------------------------------------------ |
| ![Librarian](docs/images/librarian.png) | ![Program editor](docs/images/program.png) |

| System dumps                            | Front panel                                 |
| --------------------------------------- | ------------------------------------------- |
| ![System dumps](docs/images/system.png) | ![Front panel](docs/images/front-panel.png) |

## Getting started

1. Download `BB84.MPX1-<version>-win-x64.zip` from the releases page and extract it anywhere.
2. Start `BB84.MPX1.exe`.
3. Open **MIDI setup**, choose the MIDI input and output connected to the MPX 1, and set the **Device ID** to the unit's Receive Device ID (System > MIDI > SysEx, then Options). Choose **Connect**.
4. The first time you connect a given firmware build, choose **Learn**. Learning takes a short while; the result is cached, so later connections are immediate.

To try the editor without a unit, select **MPX 1 Simulator** as the input and output.

### MIDI setup tips

- Connect the MPX 1 MIDI OUT to your interface's IN, and the interface's OUT to the MPX 1 MIDI IN. SysEx reception must be on (System > MIDI > SysEx = On on the unit).
- The MPX 1 MIDI IN is the 7-pin DIN jack labelled **Remote/In**; it takes a normal 5-pin MIDI plug. Do not use **Thru**: the unit then hears nothing. On cable interfaces such as the USB Uno, the plug marked "To MIDI In" goes into the unit's IN.
- If the unit answers nothing at all, the device ID may be wrong: the hardware test `DeviceInquiry_Broadcast_IsAnswered` (below) reports the ID the unit answers with.
- If you see timeouts, check the device ID, then raise **Timeout** or set a **Frame gap** of a few milliseconds for slow interfaces.
- **Test connection** sends ARE_YOU_THERE and reports whether the unit answered.

Settings are stored in `%APPDATA%\BB84.MPX1\settings.json`. Learned databases are cached in `%LOCALAPPDATA%\BB84.MPX1\databases`.

## Building from source

Requires the .NET 10 SDK. Native AOT publishing also needs the Visual Studio C++ build tools.

```powershell
dotnet build BB84.MPX1.slnx -c Release
dotnet test --solution BB84.MPX1.slnx -c Release -- --ignore-exit-code 8 --filter "TestCategory!=Hardware"
dotnet publish src/BB84.MPX1.App -c Release -r win-x64 -o artifacts/publish
```

### Unit tests and loopMIDI

Most tests run against the built-in simulator and need no MIDI hardware. The WinMM transport tests (`tests/BB84.MPX1.Midi.WinMM.Tests`, category `LoopMidi`) also exercise the real Windows MIDI path. They need a virtual loopback port:

1. Install [loopMIDI](https://www.tobias-erichsen.de/software/loopmidi.html).
2. Create three ports with exactly these names, and keep loopMIDI running while you test:
   - `loopMIDI Port` (the default name): whatever is sent to it comes back on its input. The transport tests use it as a plain echo.
   - `loopMIDI Port Editor` and `loopMIDI Port Simulator`: the editor reads `Editor` and writes `Simulator`, and the built-in simulator does the reverse. These tests run the session, the learner and the librarian against the simulator with every frame crossing the real WinMM transport.

Without the ports, these tests report as inconclusive (skipped) instead of failing, so machines without loopMIDI, and CI, still pass. The same happens when a freshly opened port pair delivers nothing in one direction, which loopMIDI does now and then. The test rig reopens the pair up to 16 times before it gives up. To run only these tests:

```powershell
dotnet test --project tests/BB84.MPX1.Midi.WinMM.Tests -c Release -- --filter "TestCategory=LoopMidi"
```

### Hardware tests

Tests in the `Hardware` category (`tests/BB84.MPX1.Midi.WinMM.Tests/Hardware`) run against a real MPX 1 and are excluded from the default run. Without configuration they report as skipped. Set the ports and device ID, then run them:

```powershell
$env:MPX1_MIDI_IN = 'USB Uno MIDI Interface'; $env:MPX1_MIDI_OUT = 'USB Uno MIDI Interface'; $env:MPX1_DEVICE_ID = '3'
dotnet test --project tests/BB84.MPX1.Midi.WinMM.Tests -c Release -- --filter "TestCategory=Hardware"
```

They check inquiry, firmware, ARE_YOU_THERE, a full learn, program reads, a full backup and an empty program. Two tests write to the unit. Each saves the affected data to `artifacts/hardware/backups` first and puts it back afterwards:

- `WriteSlot_RoundTripsAndRestores` writes, reads back and restores one user program: `MPX1_WRITE_SLOT`, protocol number, default 214 (front panel 215). Pick a program you have backed up; empty ("available") programs cannot be used.
- `SystemDumps_WriteBackUnchanged` writes the setups, Global Patches, Bypass and Remap dumps back unchanged. The unit applies the active setup at once, with the same values.

Optional: `MPX1_READ_SLOT`/`MPX1_READ_NAME` (a program and its expected name), `MPX1_EMPTY_SLOT` (an empty user program). Every run records its MIDI traffic to `artifacts/hardware/<test>.jsonl` and its messages to `<test>.log`. Recordings of a V1.00 unit are kept in `tests/data/hardware/v100` and replayed by `HardwareReplayTests` in the default run.

The architecture, protocol details and requirements are in [the specification](spec/spec-architecture-mpx1-sysex-editor.md). Delivery milestones are in [the roadmap](docs/roadmap.md).

## Status

Validated on a real MPX 1 with firmware V1.00 (roadmap M8): learn, program read and write, full backup, and system dump write-back. V1.10 has been tested against the simulator only. If you own a V1.10 unit and can test, reports are welcome.

## Legal

BB84.MPX1 is licensed under the [MIT License](LICENSE).

"Lexicon" and "MPX 1" are trademarks of their respective owner and are used here only to describe the device this software works with. This project is not affiliated with or endorsed by Lexicon or Harman. No code, text or artwork from the original WazWare editor is used.
