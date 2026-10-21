# Changelog

All notable changes to BB84.MPX1 are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

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
