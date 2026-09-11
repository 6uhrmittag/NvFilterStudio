# CLAUDE.md

Guidance for Claude Code working in this repository.

## What this is

A small WPF app that backs up, edits and shares **NVIDIA game filter
(Freestyle) presets**. The NVIDIA App has no export/import and driver updates
can wipe them.

It works by reading and writing one key in the NVIDIA Overlay's Chromium
IndexedDB — a LevelDB store holding a V8-serialised JSON string.

**[`docs/FORMAT.md`](docs/FORMAT.md) is the specification.** It records the
store layout, the binary encoding, and — separately and deliberately — what is
*proven with evidence* versus what is only *assumed*. Read it before touching
anything under `Core/LevelDb` or `Core/Store`. Do not re-derive facts already in
it, and do not re-investigate anything in its *Ruled out* table.

The format was reverse-engineered in a sibling project; the PowerShell reference
implementation lives at
`../PersonalKnowledgeBase/docs/in-progress/frag-punk-nvidia-filter/`.

## Layout

| Project | Role |
|---|---|
| `src/NvFilterStudio.Core` | The whole store format. **No UI dependency.** All tests target this. |
| `src/NvFilterStudio.App` | WPF app, MVVM via CommunityToolkit.Mvvm |
| `tools/NvFilterStudio.Cli` | Console harness — the fastest way to check something against a real store |
| `tests/NvFilterStudio.Core.Tests` | xUnit. 202 tests, no NVIDIA install needed |

## Commands

```powershell
dotnet test
dotnet build -c Release
dotnet format --verify-no-changes          # CI runs this; it will fail the build

dotnet run --project tools/NvFilterStudio.Cli -- status   # paths, lock state, holders
dotnet run --project tools/NvFilterStudio.Cli -- show     # decoded slots and values
dotnet run --project tools/NvFilterStudio.Cli -- verify   # parse -> serialise must be byte-identical
```

Warnings are errors and analyzers run in-build, so a clean build is also a
clean lint.

## The three invariants that fail silently

This format punishes small mistakes by *appearing to work*. Each of these was a
real bug here, each produced a file that grew, read back correctly, and left the
NVIDIA App showing the old values.

1. **Pick records by LevelDB precedence, never by size.** A record in a `.log`
   always beats one in a compacted `.ldb`; within a file the last occurrence
   wins. Choosing "the largest candidate" works until the first compaction, then
   a stale table copy silently wins.
2. **Number writes above `max(highest sequence in log, MANIFEST kLastSequence)`.**
   Numbering from the log alone lets an entry already compacted into a table
   shadow the write.
3. **JSON round trip must be byte-identical.** `System.Text.Json` escapes
   non-ASCII by default, which turns NVIDIA's raw `Schärfen` into `Schärfen`
   and inflates the record. `FilterPresetDocument` uses
   `UnsafeRelaxedJsonEscaping` for exactly this. `cli verify` checks it against
   the real document.

Also: **never hard-code a log filename.** They rotate — a single `000003.log`
became `000004.log` + `000005.ldb` simply because the overlay restarted.

**Any fix for a silent failure gets a regression test.** Both bugs above are
pinned by tests in `tests/.../Store/StoreReaderTests.cs`.

## Model conventions

- The document is a **mutable `JsonNode` tree, not POCOs**. The store holds
  fields this project never characterised, and mapping to typed objects would
  drop them on the way back.
- **`stackIdx` is data.** The stack applies low to high, so order changes the
  rendered image. Renumber contiguously from 0 after any add/remove/reorder.
- **A slider's identity is (shader file name, control id).** Never
  `displayName` — it follows the App's UI language, and NVIDIA has at least one
  mislabelling bug (`Adjustments.fx` control 2 reads `Hoogtepunten`, which is
  Dutch, on a German install).
- **Never fabricate control metadata.** A filter with no known definition is
  reported as unavailable so the UI can explain how to teach it one. Writing a
  guessed range into someone's store is worse than doing nothing.
- One instance of each filter type per stack; slots are numbered, never named.

## WPF gotchas already paid for

Each of these built green and failed only at runtime, or only in the published
binary.

- **`InvariantGlobalization` breaks WPF.** `XmlLanguage` cannot resolve a
  specific culture for `en-us`, and the window dies on its first layout pass.
  It is explicitly set to `false` in `Directory.Build.props` — leave it.
- **Brush resources from BAML are frozen.** Mutating `SolidColorBrush.Color`
  silently does nothing. `ThemeManager` replaces the resources instead, and the
  XAML references brushes with `DynamicResource` for that reason.
- **`Assembly.Location` is empty in a single-file app.** Anything reading it
  works in `dotnet run` and fails only in the release `.exe`. See `AppInfo`.
- **A custom `ComboBox` template breaks `DisplayMemberPath`.** Use an explicit
  `ItemTemplate`.
- **A `CornerRadius` larger than half an element's height draws a tapered lens,
  not a pill.** Hence `TrackRadius` = 4 for the 8px slider track.
- Explicit `Foreground` on a `TextBlock` beats an inherited
  `TextElement.Foreground` from a parent — which is why the slot chip's labels
  set no foreground of their own.

Screenshot both themes when changing anything visual:
`NvFilterStudio.exe --theme dark` / `--theme light`.

## Tests and privacy

- **Fixtures are built in code** (`SyntheticStore`). Real store bytes are
  **never** committed: a live log carries the machine owner's NVIDIA account id
  and session GUIDs, and this repository is public.
- Exports contain no account identifiers, and a test asserts it.
- The same rule applies to issues and PRs — ask for an exported `.json`, never a
  raw `.log` or `.ldb`.

## Guardrails when working against a real store

- **Never write while the NVIDIA Overlay is running.** Chromium holds the
  database open with its own memtable, so the write is discarded. `StoreWriter`
  refuses; do not `-Force` past it casually.
- Releasing the store needs **NVIDIA App → Settings → Features → In-Game
  Overlay → off**. Closing the App is not enough — `NvContainerLocalSystem`
  restarts the overlay within seconds. Do not kill NVIDIA processes to get
  around this, especially unattended.
- **Back up before every write, and verify the backup decodes** rather than
  assuming the copy worked. `StoreWriter` does this; keep it.
- **Never touch a game process.** Games here run kernel anti-cheat. This project
  concerns NVIDIA's own configuration data and has no reason to go near a game —
  no injection, no hooking, and no synthetic input into the overlay while a game
  is running.

## CI and releases

`.github/workflows/ci.yml` — build, `dotnet format`, test, then a compressed
self-contained single-file publish. No trimming: WPF does not trim cleanly.

A `v*` tag stamps that version into the build and attaches the `.exe` to a
GitHub release. CodeQL runs weekly and on PRs.

Verify a release by **downloading the asset and running it**, not by trusting a
green check.

## Honest state

Every release so far is a **pre-release**, and the README opens with a
"Read this first" callout saying why: a hobby project with one user, written
almost entirely by AI coding agents steered by an owner who does not know C#.
Keep that callout true when the facts change.

What is proven, and where:

- The GUI **Apply** button has written to a real store, twice, driven through
  its own UI, and the pre-write backup decoded to the exact prior document
  (#18, closed). Still only ever on one machine.
- All 18 filters and 73 controls are mapped (#6, closed), but the English
  names are translations of German labels, not text seen in an English NVIDIA
  App. `CONTRIBUTING.md` asks for corrections.
- `.ldb` tables are parsed, Snappy-compressed blocks included, with the byte
  scan kept as a fallback (#9, closed).
- Two-byte (UTF-16LE) string writing exists and is unit-tested (#10, closed).

What is deliberately not claimed:

- Never tested across a real driver update (#11), which is the scenario the
  tool exists for. The shader-path rewrite branch has never executed.
- The two-byte write path has never met a real non-Latin-1 NVIDIA App.

Keep that separation honest: `docs/FORMAT.md` and the issue list are where
uncertainty is recorded rather than smoothed over. When one of those issues
closes, update this section, `CONTRIBUTING.md` "Things known to be missing"
and the README callout in the same PR.
