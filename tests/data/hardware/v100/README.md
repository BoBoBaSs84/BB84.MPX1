# MPX 1 V1.00 hardware recordings

MIDI traffic recorded from a real Lexicon MPX 1 by the `Hardware` tests (roadmap M8). `HardwareReplayTests` in
`BB84.MPX1.Simulator.Tests` replays them deterministically.

- Unit: firmware V1.00, built Nov 21 1996 18:56:45, device ID 3.
- Interface: USB Uno MIDI Interface (WinMM), recorded 2026-11-15.
- Format: one JSON object per frame, `{"t": ms, "dir": "out"|"in", "hex": "F0…F7"}` (`RecordedFrame`).

| File | Hardware test | Contents |
| --- | --- | --- |
| `detect-version.jsonl` | `DetectVersion_ReportsFirmware` | Device Inquiry and System Configuration |
| `learn.jsonl` | `Learn_WalksDescriptionsAndTree` | Full learn: 448 descriptions, 2,653 nodes, algorithm lists |
| `backup.jsonl` | `FullBackup_WritesSyxFile` | Sort flags, 209 programs (empty user programs skipped), all setups, Global Patches, Bypass, Remap |
| `write-slot-215.jsonl` | `WriteSlot_RoundTripsAndRestores` | Program 214 written, read back, restored, with BUSY/READY |
| `system-dumps-write-back.jsonl` | `SystemDumps_WriteBackUnchanged` | System dumps read, written back unchanged, read again |
| `read-empty-slot-250.jsonl` | `ReadEmptySlot_ThrowsEmptyProgram` | Program Information of an empty user program |
| `full-restore.jsonl` | `FullRestore_SecondBackupMatches` | Backup, full restore (209 programs, system dumps, sort flags unchanged so not written), second backup; recorded 2026-11-18 |
| `sort-flags-preset-1.jsonl` | `SortFlags_PresetChange_RoundTrips` | Sort flags of program 1 changed with a compensated Database Dump (E18) and restored; recorded 2026-11-18 |

`learn.jsonl` also feeds the V1.00 simulator fixture: `FixtureBuilder` turns the database learned from it into a full-tree fixture, and the Librarian, front-panel and simulator tests run against it as well as against the V1.10 fixture.

The recordings contain the owner's user programs and setups as stored on the unit that day.
