# Contributing

Thanks for looking ♡

## The most useful thing you can do: correct a filter name

All **18** filters and their **73** sliders are mapped. The catch is how:

> The ids and control counts are observed fact, read back out of a real store.
> The English names are **translations of the German labels** on the machine
> they were harvested from — not text anyone has seen in an English NVIDIA App.

So some of them are probably subtly wrong. `GrimeStrength`, `EdgeDistance` and
`SplitAndCompare` are guesses at *Stärke Schmutzfilmeffekt*, *Randabstand* and
*Teilen & Vergleichen*.

**If your NVIDIA App is in English**, comparing what your overlay actually says
against the table in [docs/FORMAT.md](docs/FORMAT.md) is the single most valuable
contribution here, and it needs no code. Even one filter helps.

### Also valuable: a filter we have never seen

Those eighteen were observed on one RTX 4090 with one driver. Nobody knows
whether the set varies by GPU or driver version. If your overlay offers a filter
that is not in the table, that is worth an issue on its own.

Note that `%LOCALAPPDATA%\Temp\NvCamera\_binaries` is *not* the list — it only
holds filters that have actually been used, which is why this project spent a
while believing there were ten.

### Why it has to be done by hand

A slider's stable identity is **(shader file name, control id)**. The name NVIDIA
stores alongside it follows the App's UI language, so it cannot be used: on a
German install `Adjustments.fx` control 2 is labelled `Hoogtepunten`, which is
*Dutch*. The mapping therefore has to be observed, not derived.

### How to map or correct one

1. In the NVIDIA overlay, add the filter to a spare slot and set every slider to
   a different, memorable value.
2. Close the overlay — that is what commits it. There is no Save button.
3. Run `dotnet run --project tools/NvFilterStudio.Cli -- show` and note which
   `id=` holds which value.
4. Edit `FilterNames.ControlsByShader` in
   `src/NvFilterStudio.Core/Share/ExportDocument.cs`.
5. Open a PR saying which NVIDIA App version and UI language you used.

Names should match the English NVIDIA App labels, without spaces —
`HDRToning`, `TintIntensity`.

If you change a name, regenerate the shipped catalogue too: its `displayName`
values are what NVIDIA's own overlay renders, since the overlay never supplies a
name of its own.

## Building

```powershell
dotnet test          # 202 tests, no NVIDIA install required
dotnet build
dotnet run --project tools/NvFilterStudio.Cli -- status
```

Tests build their fixtures in code and never touch a real store, so they run
anywhere.

## CI on your pull request

Every pull request runs the same workflow as `master`: build,
`dotnet format --verify-no-changes`, the test suite, a full publish of both
executables, and a CodeQL scan. Runs from forks start automatically for
established GitHub accounts; a brand-new account needs a maintainer to approve
its first run.

The published binaries are attached to the run as artifacts, so you can download
and try the exact `.exe` built from your branch: open the run from the PR's
Checks tab and scroll to **Artifacts** — `NvFilterStudio-win-x64` is the app,
`nvfs-cli-win-x64` the CLI. You need to be signed in to download them.

A draft PR runs CI too, so open one early if you want the runner's opinion
while you work.

To run CI on your fork before opening a PR: enable Actions on the fork (the
Actions tab asks once), then **Actions → CI → Run workflow** and pick your
branch. Pushing a branch alone does not trigger it — the `push` trigger only
fires on `master`.

Fork runs get a read-only token and no secrets, which is why they can be
enabled without review. The release job only runs on `v*` tags in this
repository.

## How this project is written and reviewed

The README says it up front, and it applies here too: the owner does not know
C#, and nearly all of the code was written by AI coding agents (Claude Code)
steered by the owner, who decides what to build, tests it on their own machine
and decides what ships. The co-author trailers in the history are the record.

For a pull request that means:

- **CI is the gatekeeper, not a maintainer's eye for C#.** Warnings are errors,
  `dotnet format` must pass and the tests must be green, because that is what
  the review can lean on. A test that pins your change is worth more than a
  paragraph explaining it.
- **Review comments are drafted with AI assistance and decided by the owner.**
  If a comment is wrong, say so plainly. It will be checked, not defended.
- **Say what you observed, not only what you changed.** Which NVIDIA App
  version, which UI language, what `nvfs show` printed. Observed facts are what
  keep `docs/FORMAT.md` honest, and they are the one thing no agent can supply.

## Ground rules

- **Never commit real store data.** A live LevelDB log contains the machine
  owner's NVIDIA account id and session GUIDs. Fixtures are synthetic; keep them
  that way.
- **Add a regression test for anything that failed silently.** This format has a
  habit of it — a wrongly-chosen record or a too-low sequence number produces a
  file that grows, reads back correctly, and still shows the old values. Both are
  pinned by tests; keep that up.
- Warnings are errors and `dotnet format --verify-no-changes` runs in CI.
- Read [`docs/FORMAT.md`](docs/FORMAT.md) before touching anything under
  `Core/LevelDb` or `Core/Store`. It records what is proven, with its evidence,
  and separately what is only assumed.

## Things known to be missing

- Never tested across a real driver update, which is the case the tool exists
  for. The shader-path rewrite branch has therefore never run
  ([#11](https://github.com/6uhrmittag/NvFilterStudio/issues/11)).
- Writing a store from a non-Latin-1 UI language (Cyrillic, CJK) uses V8's
  two-byte string tag. That path is unit-tested, but it has never met a real
  NVIDIA App in one of those languages.
