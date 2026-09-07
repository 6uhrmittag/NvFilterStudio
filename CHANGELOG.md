# Changelog

Notable changes, newest first. Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and [semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `.ldb` sorted tables are parsed properly rather than scanned for the JSON
  marker (#9). Blocks are Snappy-decompressed and key prefix compression is
  undone, which recovers the **key bytes** a scan cannot — so a store whose log
  holds no preset record is now editable, not merely readable
- Window remembers its size, position and maximised state, and refuses to
  restore onto a display that is no longer connected (#15)
- Accessible names on every icon-only button and slider, so a screen reader
  announces "move filter earlier in the stack" rather than "up arrow" (#16)
- Keyboard shortcuts: `Ctrl+S` apply, `Ctrl+R` reload, `Ctrl+E` export,
  `Ctrl+Shift+C` / `Ctrl+Shift+V` for share codes
- Community health files: issue forms, pull request template, code of conduct,
  security policy
- CodeQL analysis, weekly and on every pull request
- Photo-mode (Ansel) slots reachable behind a quiet toggle (#8)
- Imports and pasted share codes show what they would change before doing
  it, with removals called out (#20)
- Undo and redo, `Ctrl+Z` / `Ctrl+Y` (#25). A run of slider edits collapses
  into one step; structural changes are each their own
- Backups are pruned, and the footer shows how much disk they use with a
  link to open the folder (#21). The oldest backup is kept forever, and
  anything renamed by hand is never touched
- `nvfs.exe` console tool shipped in releases (#24). `nvfs status` reports
  the store paths and what is holding it open
- **Support for non-Latin-1 NVIDIA App languages** (#10). Russian, Japanese,
  Polish and similar store the document as a V8 two-byte (UTF-16LE) string;
  writing was previously refused outright, locking those users out entirely

### Fixed

- **Boolean controls no longer crash the reader or get corrupted on write.**
  Filters such as `BeautifyDOF.fx` carry on/off controls that store a JSON
  `true` and none of the numeric fields; reading one as a double threw and took
  the whole document with it, and writing one through the slider path would have
  replaced the boolean with a float
- Filter-preset keys are matched by index id as well as name, so Chromium's
  `ExistsEntry` index — same name, sometimes a *higher* sequence, but a version
  counter rather than a document — can no longer be mistaken for the record
- UI values are snapped to NVIDIA's step grid (`uiMinValue + k × uiStepSize`)
  on write (#30). A value off that grid displayed correctly but jumped as soon
  as the slider was touched, and could not then be restored from NVIDIA's own
  UI — a quiet, one-way way to lose a value on import or paste
- Applying no longer throws away where you were: the selected game and slot
  survive the re-read, and the confirmation is no longer overwritten by the
  reload's own status a moment later (#27)
- Screen readers no longer announce raw view-model type names before every
  filter and slider (#28). Item containers now name themselves
  `Details (Details.fx), position 1` and `Sharpen, Schärfen`
- `ToRaw` guarded a division with `uiSpan == 0`. Double equality misses the case
  that matters: a span of 1e-300 is not zero but still yields infinity
- Log reassembly leaked its partial-fragment buffer when a log ended
  mid-fragment, which is the normal state of a live log being appended to
- The PowerShell reference tool identified a preset record partly by size
  (`> 1000` bytes), which would skip a legitimately small document — a fresh
  install with one filter in one slot

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
