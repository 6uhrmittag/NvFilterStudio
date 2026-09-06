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
| `Color.fx` | Color | 0 Tint Color, 1 Tint Intensity, 2 Temperature, 3 Vibrance |
| `Details.fx` | Details | 0 Sharpen, 1 Clarity, 2 HDR Toning, 3 Bloom |
| `Colorblind.fx` | Color Blind Mode | 0 Protanopia, 1 Deuteranopia, 2 Tritanopia |

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
- Because the tag is one-byte, any character above U+00FF would require V8's
  two-byte tag `0x63`. Not implemented; the importer refuses rather than mangle.

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
- `.ldb` tables are only ever *read as bytes*, scanned for the JSON marker. The
  sorted-table format (index, restart points, optional Snappy compression) is
  not parsed. A compressed or unusually laid-out table could hide a record.
- Writing is Latin-1 only, and only appends to the log — it never writes tables.
- Only `Adjustments`, `Color`, `Details` and `Colorblind` control ids are mapped.
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
