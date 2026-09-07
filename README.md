# ✿ NvFilterStudio

[![CI](https://github.com/6uhrmittag/NvFilterStudio/actions/workflows/ci.yml/badge.svg)](https://github.com/6uhrmittag/NvFilterStudio/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-FFB7C5.svg)](LICENSE)
[![Latest release](https://img.shields.io/github/v/release/6uhrmittag/NvFilterStudio?include_prereleases&color=C8B6E2)](https://github.com/6uhrmittag/NvFilterStudio/releases)

Back up, edit and share your **NVIDIA game filters**.

The NVIDIA App has no export or import for game filter (Freestyle) presets, and
a driver update can wipe them. Tune a look you love, lose it, and there is no
way to get it back — or to send it to a friend. This fixes that.

> ⚠️ **Not affiliated with NVIDIA.** Independent tool, MIT licensed.

**Will this get me banned?** No. It edits **NVIDIA's own settings file** — the
same one the NVIDIA App writes when you drag a slider in the overlay — while
your game is closed. It never injects into a game, hooks a process, or draws an
overlay, and it does not run while you play. It is not ReShade and it is not a
mod: everything it writes, NVIDIA's own UI could have written. See
[Is this safe?](#is-this-safe).

| Light | Dark |
|---|---|
| ![NvFilterStudio, light theme](docs/images/screenshot.png) | ![NvFilterStudio, dark theme](docs/images/screenshot-dark.png) |

## What it does

- 🔍 **Inspect** every filter slot for every game, with real values
- 🎚️ **Edit** sliders, reorder the stack, add and remove filters
- 💾 **Export** a profile to a JSON file
- 💌 **Share** a profile as a short code you can paste into Discord
- ↩️ **Undo** anything, and back up automatically before every write

## Getting started

Download `NvFilterStudio.exe` from the
[latest release](https://github.com/6uhrmittag/NvFilterStudio/releases)
and run it. No installer, nothing to configure, and .NET is bundled — which is
why it is around 60 MB.

> **Windows will warn you the first time.** The download is not code-signed, so
> SmartScreen shows "Windows protected your PC". Choose **More info → Run
> anyway**. Signing needs a paid certificate; until then you can build it
> yourself with `dotnet build` and see exactly what you are running.

It follows your Windows light/dark setting, and the ☾ button in the corner
switches manually.

Releases also include **`nvfs.exe`**, a small console tool. `nvfs status`
reports where your store is and whether anything is holding it open, which is
the quickest way to answer "why is Apply greyed out" — and the most useful
thing to paste into a bug report.

### Before you can apply changes

Reading works any time — while gaming, with everything running. **Writing needs
the overlay switched off**, because the NVIDIA Overlay holds the settings
database open and anything written underneath it is discarded.

1. **NVIDIA App → Settings → Features → In-Game Overlay → off**
2. Close the NVIDIA App
3. Apply your changes in NvFilterStudio
4. Turn the overlay back on

The app watches for this and enables **Apply** on its own once the way is clear.

> Closing the NVIDIA App alone is not enough — the `NvContainerLocalSystem`
> service restarts the overlay within seconds. The setting toggle is what
> actually releases it.

## Keyboard

| | |
|---|---|
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo |
| `Ctrl+S` | Apply to NVIDIA |
| `Ctrl+R` | Re-read from NVIDIA |
| `Ctrl+E` | Export to a file |
| `Ctrl+Shift+C` / `Ctrl+Shift+V` | Copy / paste a share code |

Start with `--theme light` or `--theme dark` to override the Windows setting.

## Sharing

Two formats, for two different jobs:

| | Use it for | Size |
|---|---|---|
| **Share code** | Pasting into chat | ~200 characters |
| **JSON file** | Backups, moving machines | ~35 KB |

A share code carries only the shape of the look — which filters, in what order,
at what values — and is rebuilt against your own machine's filter definitions, so
one from a stranger is safe to paste. A JSON file carries everything, so it
restores even onto a machine that has never used those filters; it also records
each game's install path, because importing matches on it.

**Made something good?**
[Post it in Show and tell](https://github.com/6uhrmittag/NvFilterStudio/discussions/41)
— that thread is where share codes get traded.

## Is this safe?

It writes to one key in NVIDIA's own settings database, and:

- **never touches a game process.** No injection, no hooking, no DLL proxying,
  no overlay. It cannot run while a game does, because writing needs the NVIDIA
  Overlay switched off. Anti-cheat has nothing to look at.
- takes a timestamped backup first, and **checks the backup decodes** rather
  than assuming the copy worked
- refuses to write while NVIDIA is running, rather than writing into the void
- keeps your NVIDIA account id out of every export
- works whatever language your NVIDIA App is in

Backups live in `%LOCALAPPDATA%\NvFilterStudio\backups`, and the app shows how
much space they use.

**It makes no network connections at all** — no telemetry, no update check, no
crash reporting. It has two dependencies, `CommunityToolkit.Mvvm` and `Snappier`
(a decompressor), neither of which opens a socket. The only thing it ever
launches is Explorer, when you click the backups link.

Every release ships `SHA256SUMS.txt`, so you can check the download arrived
intact:

````powershell
Get-FileHash .\NvFilterStudio.exe -Algorithm SHA256
````

And every release asset is **provenance-attested**, which is the stronger check —
a checksum only tells you the file did not change on the way to you, whereas this
tells you where it came from:

````powershell
gh attestation verify .\NvFilterStudio.exe --repo 6uhrmittag/NvFilterStudio
````

That confirms the exact bytes were built by this repository's CI from a specific
commit. It needs the [GitHub CLI](https://cli.github.com/); the checksum needs
nothing. Neither replaces code signing (see
[Releases are unsigned](SECURITY.md#releases-are-unsigned)).

## How it works

Presets live in the NVIDIA Overlay's Chromium IndexedDB, as a JSON string inside
a LevelDB store. Reading it means reassembling LevelDB's block framing and
unwrapping a V8-serialised string; writing means appending a correctly framed,
correctly sequenced write batch.

The full format — store layout, binary encoding, and what is proven versus
assumed — is documented in **[docs/FORMAT.md](docs/FORMAT.md)**.

## Building

```powershell
dotnet test                 # 187 tests, no NVIDIA install needed
dotnet run --project tools/NvFilterStudio.Cli -- show
dotnet build -c Release
```

| Project | Role |
|---|---|
| `src/NvFilterStudio.Core` | Store format. No UI dependency, fully tested. |
| `src/NvFilterStudio.App` | WPF app |
| `tools/NvFilterStudio.Cli` | Console harness for poking at a store |
| `tests/NvFilterStudio.Core.Tests` | Fixtures are synthetic — no real store bytes, ever |

## Contributing

**You do not need to write code to help.** All 18 filters and their 73 sliders
are named now — but those names are **translations of German labels**, because
the machine they were harvested from runs the NVIDIA App in German. If yours is
in English, checking a few against what your overlay actually says is the single
most useful thing you can do, and it needs no code. There is
[an issue form for exactly that](https://github.com/6uhrmittag/NvFilterStudio/issues/new?template=filter_mapping.yml).

Just as useful: **a filter your overlay offers that is not in
[the table](docs/FORMAT.md)**. Those eighteen were observed on one RTX 4090 with
one driver, and whether the set varies by GPU or driver version is unknown.

See [CONTRIBUTING.md](CONTRIBUTING.md) to build it, and
[docs/FORMAT.md](docs/FORMAT.md) before touching anything that reads or writes
the store. Also: [Code of Conduct](CODE_OF_CONDUCT.md) ·
[Security policy](SECURITY.md) · [Changelog](CHANGELOG.md).

## License

MIT. Not affiliated with, endorsed by, or connected to NVIDIA Corporation.
"NVIDIA" and "Freestyle" are their trademarks and are used here only to say what
this tool works with.
