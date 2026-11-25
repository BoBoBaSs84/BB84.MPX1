# Changelog

All notable changes to BB84.MPX1 are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## Unreleased

### Added

- Reference material: Markdown extractions of the Rev0 (V1.00) Lexicon documents (MIDI implementation, user guide, presets, quick reference) in `legacy/`. They stay out of release builds. The specification now logs the V1.00/V1.10 differences found in them (spec section 9, E14–E17).
- Simulator: a V1.00 device model, built at test time from the V1.00 learn recording (`FixtureBuilder`). It has the full control tree of the real unit and answers like it: no single active-setup address, and every stored setup reads as setup 1 (E13). The Librarian, front-panel and simulator tests now run against both V1.10 and V1.00, including fault injection and BUSY/READY, which the replay tests cannot cover.

- Hardware tests for a full restore (back up, restore all programs, sort flags and system dumps, back up again, and compare byte by byte) and for a sort-flag change of a preset. Their recordings from a V1.00 unit are in `tests/data/hardware/v100` and replayed in the default test run. Programs are compared without the effect-data bytes no parameter uses, because a V1.00 unit reports varying values there for presets (spec section 9, E19; `ParameterDatabase.WithoutUnusedEffectBytes`). Diagnostic probes write programs or sort flags from a backup file; they run only when `MPX1_PROBE_BACKUP` or `MPX1_PROBE_USER_FLAGS` is set.

### Fixed

- **The guard against writing parameters of an unloaded effect algorithm now notices program changes** (#47). Writing such a parameter can crash the MPX 1. The guard used to keep the algorithms it read when a Parameters page opened. Now it reads them again after the active program may have changed: after Send to active, audition or Load to active, after a Program Change from the unit, and after front-panel presses. An open algorithm page is rebuilt when you return to the Parameters tab (or right away when it is visible), so it marks the right parameters as read-only.
- **Restoring a backup or writing sort flags no longer scrambles the sort flags of programs 1–33 on firmware V1.00.** A V1.00 unit stores a received Database Dump shifted by 2 bytes in its first 100 bytes (spec section 9, E18). The Librarian now writes sort flags only when they differ from the unit's. On V1.00 it writes changed user programs as Program Dumps and shifts the Database Dump to make up for the offset. It reads the flags back after every write. The flags of programs 33 and 34 cannot always be written on V1.00; the status line names any program whose flags the unit did not take.
- Restore now writes the sort flags last, after the programs and system dumps.
- Simulator: user Program Dumps set the program's sort flags and Program Dump reads report the Database Dump's flags, as on the unit (E19). The V1.00 model stores a received Database Dump with the V1.00 offset.
- Unexpected errors in background reads (front-panel polling, System tab sections, Librarian and System tab first loads, device-side parameter refreshes) now show on the status line instead of being lost silently.
- A failure while starting up (loading settings, reconnecting to the last ports) now shows on the status line instead of crashing the app.
- Closing the window no longer crashes the app when closing the MIDI connection fails; the error goes to the trace log.
- Sending SysEx over WinMM now waits for the driver's completion callback instead of polling every millisecond. On Windows each poll waited about 15 ms, so every frame was delayed.
- Listing MIDI ports no longer blocks the UI thread on an asynchronous transport close.
- The session stops its timeout timers once a response, BUSY or READY arrives, instead of leaving one running per request until the timeout runs out.

### Changed

- Roadmap: post-1.0 platform work is split into Linux (P1.1–P1.5: groundwork and CI, ALSA sequencer transport, AppImage, Flatpak, validation and release) and macOS (P2.1–P2.3), with Linux first.
- Simulator fixtures (schema version 2) declare their special cases: Panel Button, Display, LEDs, Patches, Soft Row, the all-setups dump and the single-setup addresses. The simulator no longer assumes V1.10 type numbers. Schema 1 fixtures load with the V1.10 special cases.
- **Program numbers match the front panel**: the app now numbers programs 1–250 like the MPX 1 display and the user guide (presets 1–200, user programs 201–250), instead of the protocol's 0–249. This applies to the Librarian list, status messages, confirmations, export file names, error messages and the Program tab's store box. The MIDI monitor still shows the protocol numbers it decodes from the wire.
- MVVM source generation moved from CommunityToolkit.Mvvm to BB84.SourceGenerators. The app version, enum names and dispose code are now also generated at compile time. `ControlAddress` keeps its hand-written equality, because the generated version boxes the level array and enumerates it through LINQ on every dictionary lookup.
- The settings store and the learned-database cache now access the disk through injectable `IFileProvider`/`IDirectoryProvider` wrappers, generated by BB84.SourceGenerators. Tests can now cover atomic saves and unreadable files without using the real disk.

## 1.0.1 - 2026-11-15

First stable release. It is validated on a real MPX 1 V1.00 unit (roadmap M8) and fixes what that validation found: learning, program writes over USB MIDI interfaces, backups with empty user programs, and V1.00 system dumps. Version 1.0.0 was never published, because GitHub refused its tag name, so this is the first release after 1.0.0-Alpha. Release builds no longer carry the "Alpha" suffix in their version.

### Added

- Tests over the real WinMM path using loopMIDI virtual ports: transport round trips, large frames, open/close cycles, and the session, learner and librarian against the simulator. Without the ports, these tests report as skipped. The README explains the setup.
- Hardware tests (`Hardware` category) against a real MPX 1: Device Inquiry, firmware detection, ARE_YOU_THERE, a full learn, reading a program, reading an empty program, a full backup to `.syx` with its dumps checked against their layouts, the combined setup dump, writing, reading back and restoring a user program, and writing the system dumps back unchanged. Set `MPX1_MIDI_IN`, `MPX1_MIDI_OUT` and optionally `MPX1_DEVICE_ID` to the MIDI ports and device ID. Without them, the tests report as skipped. Each run records its MIDI traffic to `artifacts/hardware/<test>.jsonl` for replay fixtures and its messages to `<test>.log`.
- Recordings of a real MPX 1 V1.00 (learn, backup, program write, system dump write-back) in `tests/data/hardware/v100`, replayed by new tests in the default run. The README explains how to run the hardware tests.
- GitHub issue forms (bug report, feature request, hardware test report), a pull request template and a Contributor Covenant Code of Conduct.

### Changed

- The default request timeout is now 2000 ms instead of 1000 ms. On a V1.00 unit the Database Dump takes 630 ms to arrive, so the old default left little room. Saved settings keep their value.
- Internal refactoring with no change in behavior. Large view models and the simulator are split into partial files by concern. The tabs share an `IDeviceTab` attach/detach interface. Error text and progress reporting are shared. Name normalization and sort-flag formatting moved to `ProgramFields` in Core.
- Internal refactoring: the App view models now have one type per file, and the tabs share one transfer runner (`OperationStatus.RunAsync`) for status text and error handling.
- Internal: the version test reads the expected version from `Directory.Build.props` instead of a hard-coded `1.0.0`.

### Fixed

- **Learning on a V1.00 unit**: learning took over 11 minutes and filled the control tree with about 26,700 bogus entries below System > MIDI > Cont Remap. The unit has the documented dead-end branch (Appendix D bug 2) at a different address and answers every address below it. The learner now recognizes such a branch wherever it is and skips it, so learning a V1.00 unit takes about 95 seconds. Databases cached before this fix are cleaned up when loaded.
- **Writing programs over USB MIDI interfaces**: after a program write, the next message could reach the unit before it reported BUSY, because USB interfaces report a send as finished before the data has crossed the MIDI cable. The wait for BUSY now starts after the frame's time on the cable (about 270 ms for a program), so restores no longer send while the unit is busy.
- **Backup with empty user programs**: a backup stopped with a timeout at the first empty ("available") user program, because the unit sends no Program Dump for it. Empty user programs are now left out of the backup.
- **Opening empty user programs**: exporting, renaming, loading or opening an empty ("available") user program in the editor waited several seconds and then reported that the device did not answer. It now fails at once with "Program NNN is empty".
- **System dumps on a V1.00 unit**: backups and the System tab missed the Setups and Global Patches. V1.00 setups are 43 bytes, because "Global Bypass Level" exists only on V1.10, and V1.00 abbreviates dump names (for example `GPatcDump`, `PnlDisply`, `CurChoics`, `SftRowDmp`). Both are now recognized, and V1.00 setups are edited with their own layout.
- **Setups in backups**: a backup now holds all six setups in one dump, the active setup included, and restoring it brings the active setup back. The backup option is now called "All setups (active and stored)". Single setups are no longer backed up when the unit offers the combined dump, and a restore skips single setups found in older files, because a V1.00 unit reads every single setup as setup 1 and restoring them could overwrite setups 2–5.
- **System tab setups on a V1.00 unit**: every setup showed stored setup 1, because the unit answers each single-setup address with it. The Setups section now reads and writes through the combined setup dump, lists the active setup first and then setups 1–5, and changes only the setup you edit. Export and import use the combined dump.
- **Global Patches and Bypass Controllers validation**: dumps read from the unit were rejected as holding illegal values and skipped on restore. Unused sources are stored as the value just below the first real source (`0x17` for Global Patches, `0x18` for Bypass), which the unit shows as "None". These values are now accepted and shown as "None".
- **Closing ports**: the MIDI ports are now always closed on disconnect, reconnect, a failed connect and app exit, even when stopping the session fails. Before, ports could stay open and block the next connection.
- **App exit**: the window now closes even when cleanup fails.
- **Session**: a faulty event subscriber or an undecodable frame no longer stops the receive loop. Before, every later request timed out. Disconnecting now cancels waiting requests at once instead of waiting for their timeout. Calls made after disconnecting fail with `ObjectDisposedException`.
- **WinMM transport**: a cancelled or timed-out send now gets its buffer back from the driver before the buffer is freed. If the input port fails to close, it is reset and closed again. If that also fails, the buffers are deliberately leaked instead of freed while the driver still uses them.
- **Front panel tab**: polling no longer restarts after the tab is disposed, and the tab copes with the connection closing during a request.
- **Control tree**: loading a page copes with the connection closing during the load.
- **MIDI monitor**: a burst of frames no longer adds entries that would be removed at once.
- **Librarian tab**: after a restore, the same slot is selected again and the pending sort-flag edits are dropped, as Refresh already did.
- **Disconnecting during a transfer**: disconnecting while the System, Librarian, Program or Front panel tab is waiting for the unit no longer raises an error. The tab keeps its "Connect and learn a device" status instead of showing "Cancelled.".
- **Status line errors**: every tab now reports a timeout, a protected preset, a file error or denied file access on its status line. Before, some tabs let these errors through.
- **Parameters tab**: disconnecting while a parameter is being re-read no longer leaves a background error behind.
- **Restore**: system dumps in a backup file that hold illegal values are skipped with a warning instead of written to the unit (DMP-011).
- **Main window**: disconnecting during a connection test or a learn no longer raises an error or leaves tabs attached to the closed connection. If the database cache can't be read or written, learning still works and the status line shows a warning.

## 1.0.0-Alpha - 2026-10-21

First release: a SysEx editor and librarian for the Lexicon MPX 1 (firmware V1.00 and V1.10). It is a single Windows x64 executable built with Native AOT and needs no .NET installation.

### Added

- **Connection**: WinMM MIDI ports, a built-in "MPX 1 Simulator" port, a device ID, timeouts and retries, a frame gap for slow interfaces, and a connection test. The app reconnects to the last ports on start.
- **Learning**: the app reads the unit's parameter descriptions, control tree and algorithm parameter lists, then caches them per firmware build. V1.00 and V1.10 type numbers are resolved through this learned database, never hard-coded.
- **Parameters tab**: a control-tree browser with editors chosen by display units (slider, choice, toggle, text, Rate in Hz or ratio). Edits are sent live and throttled, and the unit's display text is read back after each change. Parameters of algorithms that are not loaded are read-only (Appendix D bug 3).
- **Librarian tab**: a 250-slot list with names, algorithms and sort flags. You can load a slot into the active program, rename programs, edit sort flags, and import or export single programs as `.syx`. Backups and restores include the chosen system dumps, and overwriting factory presets always asks first.
- **Program tab**: a full program editor for programs from a slot, the active program or a file:
  - six effect blocks with algorithm choice and parameters
  - a routing map and rule-aware routing table
  - soft row and patches with a block-then-parameter picker
  - tempo, tap, meter, master level and mix, and the knob
  - all ten modulator blocks

  Changes can be auditioned on the active program, and the editor works offline on files with the last learned database.

- **System tab**: typed editors for Setups (five stored plus the active one), Global Patches, Bypass and Remap Controllers, plus a read-only view of Current Choices. Controller sources are limited to each dump's range, and writing a setup asks first.
- **Front panel tab**: mirrors the display (with custom characters), the LEDs, the headroom bars and the 7-segment digits. Buttons support click (press + release), hold (right-click or long press), the Left + Right combination, encoder steps, and the V1.10 Tap and A/B toggle. The mirror is refreshed only while the tab is visible.
- **MIDI monitor**: shows every frame as hex with a decoded summary, with pause and clear.
- **Simulator**: a fake MPX 1 used for testing and demos, plus record/replay transports.

### Known limitations

- The release was validated against the built-in simulator only; no hardware validation has been done yet (roadmap M8).
- Names of value lists that the MIDI documentation does not publish are shown as numbers, with the unit's own display text next to them.
- System dumps are found by name in the learned tree. Their exact names on real hardware are still to be confirmed.
