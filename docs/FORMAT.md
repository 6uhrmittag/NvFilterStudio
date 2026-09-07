# NVIDIA App — Game Filter Store

Reference for where the NVIDIA App keeps game filter (Freestyle) presets, how
that data is encoded, and two working PowerShell tools that read and write it.

?> **Status: working, verified end to end** on 2026-09-06. A record written by
`Import-NvFilters.ps1`, with NVIDIA's software not running, was afterwards
displayed correctly in the App's own overlay UI and survived a full App restart.

This page is intended as the specification for a small Windows GUI app that
inspects, edits, imports and exports these filters. Everything below is measured
on a real machine; anything unproven is listed under *Not proven*.

## Store location

````text
%LOCALAPPDATA%\NVIDIA Corporation\NVIDIA Overlay\CefCache\Default\IndexedDB\https_nvfile_0.indexeddb.leveldb\
````

A Chromium IndexedDB database. The payload is a **JSON string** under the
IndexedDB key `FilterPresets_v1`, living in the LevelDB write-ahead log.

!> The `Default\` segment matters. A second `IndexedDB` folder exists one level
up (`CefCache\IndexedDB\`) and is stale — last written 2025-01-08.

!> **Never hard-code the log filename.** LevelDB compacts and rotates. Observed
directly: a restart of the overlay turned a single `000003.log` (3.4 MB) into
`000004.log` + `000005.ldb` (a compacted table). Always take the
highest-numbered `*.log`, and expect `.ldb` tables to be present — that is the
normal steady state, not an edge case.

**There is exactly one store.** The NVIDIA App has its own separate IndexedDB
(with compacted `.ldb` tables); it contains no `filterPresets` and no `.fx`
references. Nothing needs to be kept in sync.

## Data model

Top level, keyed by **full executable path**:

````json
{
  "filterPresets": {
    "C:\\Program Files (x86)\\Steam\\steamapps\\common\\FragPunk\\FragPunk\\Binaries\\Win64\\FragPunk.exe": {
      "anselSlotsInfo": { "lastSlotIdx": 1, "slots": [] },
      "modsSlotsInfo":  { "lastSlotIdx": 3, "slots": [] }
    }
  }
}
````

- `modsSlotsInfo` — **the game filter slots**. This is the one that matters.
- `anselSlotsInfo` — photo-mode (Ansel) slots.
- Slot `id` maps to the numbered slots in the overlay UI; `id: 0` is *None*.

Each slot holds `filterStack.filters[]`:

````json
{
  "id": "C:\\Windows\\system32\\DriverStore\\FileRepository\\nvmdi.inf_amd64_<hash>\\NvCamera\\Details.fx",
  "name": "Einzelheiten",
  "stackIdx": 1,
  "isSelected": true,
  "isExpanded": true,
  "isPPEFilter": false,
  "isVisible": false,
  "errorCodes": [],
  "controls": [
    { "controlType": "slider", "displayName": "Schärfen", "id": 0,
      "dataType": "float", "currentValue": 0.1, "currentValueArray": [0.1],
      "minValue": 0, "maxValue": 1, "stepSize": 0.01,
      "uiMinValue": 0, "uiMaxValue": 100, "uiStepSize": 1,
      "currentUIValue": 10, "defaultValue": 50 }
  ]
}
````

### Rules a GUI must respect

- **`stackIdx` is the filter order**, applied low to high. The same values in a
  different order give a different image, so order is data, not presentation.
- **`currentUIValue` is the number shown in the App**; `currentValue` is the
  normalised float the shader receives. Measured relation:
  `currentValue = (maxValue - minValue) * (uiValue - uiMinValue) / (uiMaxValue - uiMinValue) + minValue`.
  Read the bounds from the control rather than assuming ±100.
- **Not every control is a slider.** `controlType` is `slider` or `boolean`. A
  boolean stores `currentValue` as a JSON `true`/`false`, and carries *none* of
  the numeric fields — no `minValue`, `maxValue`, `stepSize`, `uiMinValue`,
  `uiMaxValue`, `uiStepSize`, `currentUIValue` or `defaultValue`. Reading one as
  a number throws; writing one through the numeric path destroys it.
- **`dataType` is `float`, `int` or `bool`.** `int` controls (`Letterbox.fx`,
  `Painterly.fx`) still follow the generic bounds mapping — their raw and UI
  scales are simply identical, so the conversion is an identity.
- **A boolean control has no recorded default**, so there is nothing to reset it
  to.
- **The control metadata is authoritative — the overlay does not re-derive it.**
  A filter written into a slot with deliberately wrong bounds (`minValue 0`,
  `maxValue 5`, `uiMinValue -50`, `uiMaxValue 250`, `uiStepSize 7`) rendered
  using exactly those numbers, and the invented `displayName` was kept too.
  Adding a filter therefore requires real per-control metadata; a name and a
  control count are not enough.
- **Sliders snap to `uiMinValue + k × uiStepSize`.** The grid is anchored at
  `uiMinValue`, and the reachable maximum is the last grid point at or below
  `uiMaxValue` — a control declaring `-50..250` step 7 topped out at 244. A
  stored value off that grid displays correctly but jumps to the nearest grid
  point as soon as the user touches the slider, and cannot then be restored
  from NVIDIA's UI.
- **A slider's stable identity is (shader basename, control `id`).**
  `displayName` is localised to the App's UI language. On the test machine it is
  German, and NVIDIA has a localisation bug where `Adjustments.fx` control 2 is
  labelled `Hoogtepunten` (Dutch) rather than *Highlights*. Never key on it.
- **Only one instance of each filter type** is allowed in a stack — no two
  `Color.fx` entries.
- Filters only reach this store when they land in a **numbered slot**.

### Control map

| Shader | UI name | Control ids |
|---|---|---|
| `Adjustments.fx` | Brightness/Contrast | 0 Exposure, 1 Contrast, 2 Highlights, 3 Shadows, 4 Gamma |
| `BeautifyDOF.fx` | Auto Depth of Field | 0 Speed, 1 Intensity, 2 InvertZAxis*, 3 InvertYAxis* |
| `BlacknWhite.fx` | Black and White | 0 Intensity, 1 EnableDepth*, 2 EdgeDistance, 3 InvertZAxis*, 4 InvertYAxis* |
| `Color.fx` | Color | 0 Tint Color, 1 Tint Intensity, 2 Temperature, 3 Vibrance |
| `Colorblind.fx` | Color Blind Mode | 0 Protanopia, 1 Deuteranopia, 2 Tritanopia |
| `DOF.fx` | Depth of Field | 0 FocusDepth, 1 FarBlurCurve, 2 NearBlurCurve, 3 BlurRadius, 4 InvertZAxis*, 5 InvertYAxis* |
| `Details.fx` | Details | 0 Sharpen, 1 Clarity, 2 HDR Toning, 3 Bloom |
| `Letterbox.fx` | Letterbox | 0 HorizontalScale†, 1 VerticalScale† |
| `NightMode.fx` | Night Mode | 0 Intensity |
| `NvNewSharpen.fx` | Sharpen+ | 0 Intensity, 1 TextureDetail |
| `NvTiltShift.fx` | Tilt-Shift | 0 Axis, 1 BlurSize, 2 BlurCurve |
| `NvVignette.fx` | Vignette | 0 Intensity |
| `OldFilm.fx` | Old Film | 0 Gamma, 1 Exposure, 2 Contrast, 3 VignetteStrength, 4 FilterStrength, 5 GrimeStrength |
| `Painterly.fx` | Painterly | 0 Iterations†, 1 SampleDirections†, 2 Radius†, 3 EdgeSharpness |
| `Sharpen.fx` | Sharpen | 0 Intensity, 1 IgnoreFilmGrain |
| `SpecialFX.fx` | Special FX | 0 Retro, 1 Sketch, 2 Halftone, 3 Sepia |
| `Splitscreen.fx` | Splitscreen | 0 SplitAndCompare*, 1 Position, 2 Rotation, 3 DividerWidth, 4 DividerColor, 5 GradientFade*, 6 Zoom |
| `Watercolor.fx` | Watercolor | 0 Gamma, 1 Exposure, 2 Contrast, 3 Saturation, 4 TintIntensity, 5 PencilIntensity, 6 PencilBlur, 7 PencilSoftness, 8 ColorDetail, 9 ColorBlur |

`*` boolean control &nbsp; `†` `dataType: int`

Eighteen filters, 73 controls, harvested by adding every filter the
overlay offers into one slot and reading the store back. Ids and counts
are observed; the English names are translations of the German labels on
the test machine, not text from an English NVIDIA App.

Other shaders exist (`Letterbox.fx`, `NightMode.fx`, `SpecialFX.fx`,
`Watercolor.fx`, `Painterly.fx`, `Splitscreen.fx`) — their control ids have not
been mapped.

### Shader paths are identifiers, not files

`…\nvmdi.inf_amd64_<hash>\NvCamera\Color.fx` **does not exist on disk.** That
directory holds the NvCamera runtime (`NvCamera64.dll`, `ReShadeFXC`, stickers)
and **no `.fx` files at all** — the effects are embedded. The path is a logical
filter ID that happens to carry the versioned DriverStore hash.

!> Consequence for import: **keep the hash the target store already uses**, since
that is by definition what the local NvCamera reports. Only rewrite it when that
directory no longer exists. Blindly rewriting to "newest DriverStore folder" is
wrong — two `nvmdi.inf_amd64_*` directories with an `NvCamera` subfolder can
coexist.

## Binary encoding

The IndexedDB value wrapping the JSON, determined by diffing two store versions
that differed by one slider (JSON 9088 vs 9090 bytes):

````text
value = varint(version) FF 15 FE 00*12 FF 0F 22 varint(jsonLen) <json>
        28 -> 2A                                 80 47 -> 82 47
        IDB value version                        V8 one-byte string length
````

Only those two fields change; everything between is constant. `FF 15` is the
structured-clone v21 header, `FF 0F 22` a nested v15 **one-byte (Latin-1)**
string tag.

- Non-ASCII is stored **raw and unescaped** — `Schärfen` carries byte `0xE4`.
- A document containing any character above U+00FF cannot use the one-byte tag.
  V8 then uses **`0x63`**, where the payload is UTF-16LE. This is what an NVIDIA
  App in Russian, Japanese, Polish and similar produces.
- **The varint is a length in bytes for both forms**, so a two-byte string
  reports twice its character count. Reading it as characters truncates the
  document.

The tag must be read rather than assumed. A two-byte document scanned as Latin-1
does not merely decode oddly — the marker is not found at all, so the store
looks empty rather than differently encoded.

**IndexedDB key** `FilterPresets_v1`: `00 03 0D 01` (KeyPrefix — db 3, object
store 13, index 1) + `01` (string type) + `10` (16 chars) + the name in
**UTF-16BE**.

**LevelDB log framing**: 32 KiB blocks; each record `crc32c(4) length(2)
type(1)` then payload, with types 1=FULL 2=FIRST 3=MIDDLE 4=LAST. CRC is
CRC32**C** (Castagnoli, reflected poly `0x82F63B78`) over `type||payload`, masked
as `((c >> 15) | (c << 17)) + 0xa282ead8`.

**Write batch**: `sequence(8) count(4)`, then per entry
`type(1) varint(keyLen) key varint(valLen) value`, `type` 1 = put.

**MANIFEST**: itself a LevelDB log whose records are VersionEdits — a stream of
`(tag, payload)` pairs. Tags: 1 comparator, 2 log number, 3 prev log number,
**4 last sequence**, 5 compact pointer, 6 deleted file, 7 new file, 9 next file
number. Every tag must be walked correctly or the varints desynchronise and
tag 4 is misread. `CURRENT` names the active MANIFEST.

## Reading

Works with everything running — the App, the overlay and the game.

1. Open with `FileShare.ReadWrite | Delete`. A plain read returns nothing while
   the overlay is running.
2. Strip the LevelDB block framing. Without this, records are chopped at every
   32 KiB boundary and longer presets will not parse.
3. Pick the newest record **by LevelDB precedence, never by size**:
   - a record in a `.log` is always newer than one in a `.ldb`, because
     compaction moves older data into tables and later writes go to a fresh log;
   - within one file the **last** occurrence wins.

!> Step 3 is easy to get wrong. Selecting "the longest candidate" works until
the first compaction, after which a large stale record in a `.ldb` can outrank
the live one. The `.ldb` here held 66 superseded copies against 2 in the log.

````powershell
.\Export-NvFilters.ps1                                        # everything
.\Export-NvFilters.ps1 -Game FragPunk -OutFile out.json       # one game
.\Export-NvFilters.ps1 -Game FragPunk -StoreDir <dir>         # a backup copy
.\Export-NvFilters.ps1 -Game FragPunk -Raw                    # + untouched record
````

Read-only; it never writes to the NVIDIA store. Each exported filter carries a
`native` block — the verbatim NVIDIA object — alongside the human-editable
`settings`. Import overlays `settings` onto `native`, which is what lets a
profile be rebuilt on a machine whose store has never held that filter.

## Writing

Appends a write batch to the highest-numbered log. LevelDB replays in order and,
for one user key, **the entry with the highest sequence number wins** — so the
appended batch must outrank both the log and anything already compacted into a
`.ldb` table. The importer takes `max(highest sequence seen in the log,
kLastSequence from the MANIFEST) + 1`.

!> Numbering from the log alone is not enough once a `.ldb` exists: a table
entry with a higher sequence would shadow the write, which fails silently —
the file grows, the read-back looks right, and the App still shows the old
values.

!> **The In-Game Overlay must be switched off. Closing the NVIDIA App is not
enough.** The store is held by the overlay's `storage.mojom.StorageService`
process, and `NVIDIA Overlay.exe` is tied to neither the App nor a running game
— the `NvContainerLocalSystem` service respawns it within seconds with fresh
PIDs, however often it is killed. Stopping that service requires elevation.

Procedure, no elevation needed:

1. **NVIDIA App → Settings → Features → In-Game Overlay → off**
2. Close the NVIDIA App
3. Confirm no `NVIDIA Overlay.exe` remains and the log opens with
   `FileShare.None` — that open is the only test that actually answers it
4. Import
5. Turn the overlay back on

`nvcontainer.exe` is **not** a blocker: it is a service host and does not hold
the store.

````powershell
.\Import-NvFilters.ps1 -InFile out.json -WhatIf            # dry run
.\Import-NvFilters.ps1 -InFile out.json                    # same machine
.\Import-NvFilters.ps1 -InFile out.json -MatchBy Executable
````

`-MatchBy Executable` pairs on filename rather than full path, for a machine
where the game lives on a different drive. `-StoreDir` targets a copy, which is
how the sandbox tests run.

### Why writing cannot be done live

Chromium holds the database open with its own in-memory memtable. An append to
the log file is invisible to it, so its next flush writes from its own state and
the appended batch is lost. This is a property of writing around a live
database, not a gap in the tooling.

The overlay changes filters live because it writes *through* its own IndexedDB
API. Reaching that would mean the `NvCameraService` IPC (see below); CEF remote
debugging is not an option — no `--remote-debugging-port`, no
`DevToolsActivePort` file, no listening socket, and `NVIDIA Overlay.exe` takes
no arguments of its own because nvcontainer spawns it with IPC handles.

### There is no explicit save

The overlay has no *Save* button. **Moving a slider and closing the overlay is
the commit**, straight into whichever slot is active — no confirmation, no undo.
The write lands when the overlay closes, not on slider release.

## Proven

| Claim | Evidence |
|---|---|
| This file is the store | Moving Sharpen 10→13 in the UI changed `000003.log` by +114 961 bytes; the decoded export differed in **exactly one** value, the one moved |
| `uiValue == raw × 100` | That change read back as `currentValue` 0.13 / `currentUIValue` 13 |
| CRC32C implementation is correct | Matches the `123456789` → `0xE3069283` vector, and all **2 700** real log records verify |
| Import round-trips | Edit one value → import → read back with the *exporter*: exactly one value changed, 2 701 records CRC-valid |
| Import reconstructs from nothing | A 4-filter stack restored into an **empty** slot: identical order, shaders, every value, shader path |
| Cross-machine matching | `-MatchBy Executable` with the exe on a different drive paired to the existing entry |
| Guards hold | `-WhatIf` leaves the file byte-identical; import refuses while the overlay runs; importing twice is idempotent |
| **The App accepts what this writes** | Slot 3 Sharpen imported 13→10 with the overlay off; the App's own UI then showed 10, other three filters intact and in order |
| The write is durable | Sharpen 10 still read back after a full App + overlay restart and a game launch |
| LevelDB is crash-safe here | Six hard kills of the overlay left all 2 740 records CRC-valid and slot 3 intact |
| Compacted stores work | Against the real post-compaction store (`000004.log` + 975 KB `000005.ldb`): export picked the live value from the log rather than a stale table copy, import chose sequence 11 732 from `max(log 11 731, manifest 11 406)`, and the read-back changed exactly one value |
| `.ldb` tables are parsed, not scanned | The 975 KB `000005.ldb` decodes to 11 400 entries, highest sequence 11 399, of which 89 are `filterPresets` records; the newest (seq 11 384) yields the same 9 092-character document the log holds. Blocks are a mix of uncompressed and **Snappy** — the uncompressed ones are why a plain byte scan saw JSON at all |
| The GUI's Apply writes a real store | Driven through its own UI: slot 3 `Details.fx` Sharpen 10 → 37 → 10 over two applies, value version 48 → 49 → 50, one value changed each time and the pre-write backup decoded to the original |
| A fabricated filter node is accepted | `NightMode.fx`, never previously in any slot, was written into empty slot 2 with one control and deliberately wrong bounds. The overlay rendered it, honoured every wrong number, kept the invented `displayName`, and wrote the node back with only `currentValue`, `currentUIValue` and `isExpanded` changed |

## Not proven

- **Never been through a real driver update** — the scenario the tool exists
  for. The "rewrite the shader path when the DriverStore hash changed" branch
  has therefore never executed; only the "keep the existing hash" branch has.
- The importer writes only the `FilterPresets_v1` key. NVIDIA's own writes also
  touch LevelDB *scopes* bookkeeping (an undo journal) in the same batch. That
  is crash-recovery state for in-flight transactions, so a bare put should be
  harmless — an assumption, not a verified fact.
- A compacted-store import has been verified by read-back, but **not yet
  confirmed in the App's UI**. The UI confirmation was done pre-compaction. The
  sequence-number reasoning says it will hold; that is not the same as having
  seen it.
- Table *writing* is not implemented. Compaction is left entirely to LevelDB;
  this project only ever appends to the log.
- Writing only appends to the log; it never writes tables.
- The two-byte string path is covered by tests but has **not** been exercised
  against a real non-Latin-1 NVIDIA App.
- The English control names are translations of German labels, not text seen in
  an English NVIDIA App. Ids and counts are observed; the wording is not.
- Whether the overlay would supply its own localised `displayName` if a written
  filter node omitted one. The `.acef` effects carry the label in some fifteen
  languages, so it plausibly can — but #12 proved it honours a `displayName`
  that *is* present, and omitting one has never been tried. This decides whether
  a shipped filter catalogue can be language-neutral.
- One machine, one driver, one App version (see *Verified on*).

## Ruled out

Recorded so nobody re-investigates these.

| Candidate | Verdict |
|---|---|
| `%LOCALAPPDATA%\Temp\NvCamera` | Compiled shader cache, not settings. `_binaries\<F>.fx\<F>.acef` are ReShade FX 4.8.2 compiled effects, `_pscache\*.cso` shader bytecode. Holds localised control *names* and *defaults*, no user values — searched for the actual slider floats, absent. Looks like "exactly what I activated" only because a filter is compiled here the first time it is used, so the listing reveals *which* filters are in play, never their values. |
| `…\NVIDIA Overlay\CefCache\Default\Local Storage\leveldb` | Empty — `000003.log` is 0 bytes. One directory from the real store: `IndexedDB`, not `Local Storage`. |
| `…\NVIDIA Overlay\CefCache\IndexedDB\` (no `Default\`) | Stale since 2025-01-08. |
| `%ProgramData%\NVIDIA Corporation\Drs\nvdrsdb0.bin` | Driver profile DB. Contains `FragPunk`, but so does the *shipped* `nvdrsdb.bin` in the OTA artifacts — NVIDIA's stock game profile, not user filter data. |
| Preset names as strings | Slots are numbered, not named. No user-chosen name is stored anywhere, in ASCII or UTF-16LE. |
| `UXD` folders, `NvConfig` | Logs and localisation only. |
| GeForce Experience era: `NvCameraConfiguration.exe`, `Ansel\Custom\*.ini` | Not wired into the NVIDIA App. Ignore guides referencing them. |

## Recovery: the overlay console log

`%LOCALAPPDATA%\NVIDIA Corporation\NVIDIA Overlay\console.log` (and `.bak`) logs
every filter operation as JSON — a fallback if a store is ever lost:

````text
NvCameraService  command:  SetFilterAndAttributes
NvCameraService  params:  {
    "filterId": "…\\NvCamera\\Adjustments.fx",
    "stackIdx": 1,
    "controls": [ { "id": 0, "type": 1, "values": [0.12], "dataType": "float" } ]
}
````

Commands observed: `SetFilterAttribute`, `SetFilterAndAttributes`, `SetFilter`,
`RemoveFilter`, `ResetFilterStack`, `SetAnselEnable`, `GetFeatureSet`,
`GetProcessInfo`. This is also the live API a future GUI would need if it ever
wanted to write while the App is running.

## Verified on

| | |
|---|---|
| NVIDIA App | 11.0.9.251 |
| Display driver | 32.0.16.1664 |
| GPU | GeForce RTX 4090 (AMD iGPU also present) |
| OS | Windows 11 Pro 26200 |
| PowerShell | 7 (uses `0x…u` unsigned literals; **not** 5.1-compatible) |
| Date | 2026-09-06 |

## Backups

Verbatim copies of the store plus the decoded record go to
`.local/nv-filter-backups/` at the **repo root** — outside `docs/` and
gitignored, because the raw log contains a 43-character NVIDIA account `userId`
and session GUIDs, and `docs/` is published to slashlog.org. Exported JSON is
clean of both; only the raw store is sensitive.

To roll back: copy a backup directory over the store with the overlay off.
