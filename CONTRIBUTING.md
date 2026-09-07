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
dotnet test          # 184 tests, no NVIDIA install required
dotnet build
dotnet run --project tools/NvFilterStudio.Cli -- status
```

Tests build their fixtures in code and never touch a real store, so they run
anywhere.

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
  for. The shader-path rewrite branch has therefore never run.
- Writing supports Latin-1 UI languages only; a Cyrillic or CJK install needs
  V8's two-byte string tag, which is not implemented (the app refuses rather
  than corrupting names).
- `.ldb` tables are scanned as bytes, not parsed, so a compressed table could
  hide a record.
