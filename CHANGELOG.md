# Changelog

Notable changes, newest first. Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and [semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Window remembers its size, position and maximised state, and refuses to
  restore onto a display that is no longer connected (#15)
- Accessible names on every icon-only button and slider, so a screen reader
  announces "move filter earlier in the stack" rather than "up arrow" (#16)
- Keyboard shortcuts: `Ctrl+S` apply, `Ctrl+R` reload, `Ctrl+E` export,
  `Ctrl+Shift+C` / `Ctrl+Shift+V` for share codes
- Community health files: issue forms, pull request template, code of conduct,
  security policy

## [0.1.0] — 2026-09-06

First pre-release. Pre-release because the GUI's **Apply** has never written to
a real store; the code beneath it is tested and the same operation is proven via
the PowerShell reference tools, but the button itself is unexercised.

### Added

- Read every game filter slot for every game, live, while gaming
- Edit slider values, reorder the stack, add and remove filters
- Export to JSON, import from JSON, and short `NVF1:` share codes for chat
- Automatic timestamped backup before every write, verified to decode
- Apply gate that watches for the overlay being switched off and enables itself
- Light and dark themes, following the Windows setting, with a manual toggle
  and a `--theme light|dark` startup flag
- App icon and a version stamped into the binary and shown in the header
- Crash handler that writes a report to `%LOCALAPPDATA%\NvFilterStudio\logs\`
  and keeps the app running rather than discarding in-memory edits

### Fixed

Defects found and fixed during development, recorded because each failed
*silently* — the class of bug this format invites:

- Record selection picked the largest candidate rather than following LevelDB
  precedence, so after the store compacted a stale copy in a `.ldb` table could
  outrank the live one
- Write batches were numbered from the log alone, so an entry already compacted
  into a table could shadow the write: the file grew, the read-back looked
  correct, and the app still showed old values
- `System.Text.Json` escaped non-ASCII, turning NVIDIA's raw `Schärfen` into
  `Schärfen` and inflating the record; the round trip is now byte-identical
  against a real 9,092-character document
- `AddFilter` appended a skeleton still carrying `stackIdx 0`, so a stable sort
  placed it mid-stack — and since the stack applies low to high, that silently
  changed the resulting image
- Unapplied edits were discarded without warning on reload or window close (#3)
- No unhandled-exception handler, so any unexpected error showed the stock
  Windows crash dialog (#4)
- `InvariantGlobalization` broke WPF at startup: the published `.exe` would not
  launch while the build stayed green
- `Assembly.Location` returns an empty string in a single-file app, so version
  lookup failed only in the published build

[Unreleased]: https://github.com/6uhrmittag/NvFilterStudio/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/6uhrmittag/NvFilterStudio/releases/tag/v0.1.0
