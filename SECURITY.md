# Security

## Reporting

Report anything sensitive privately through
[GitHub's advisory form](https://github.com/6uhrmittag/NvFilterStudio/security/advisories/new)
rather than a public issue. Anything else is fine as a normal issue.

## What this app does and does not touch

Worth being explicit, because it edits settings belonging to another vendor's
software and sits next to games with kernel anti-cheat.

**It does:**

- Read and write one key, `FilterPresets_v1`, in the NVIDIA Overlay's local
  IndexedDB store
- Take a timestamped backup before every write, and verify that backup *decodes*
  rather than assuming the copy worked
- Refuse to write while NVIDIA holds the store open, because such a write would
  be silently discarded

**It does not:**

- Touch any game process — no injection, no hooking, no overlay of its own.
  It has no reason to go near one, and does not.
- Send anything anywhere. There is no network code, no telemetry, no update
  check.
- Require administrator rights.

## Your data

The raw NVIDIA store contains a **43-character NVIDIA account identifier and
session GUIDs**. Two consequences:

- Exports produced by this app contain neither. They are safe to share, and a
  test asserts it.
- **Never attach raw `.log` or `.ldb` files to an issue.** If a maintainer asks
  for store internals, they will ask for specific fields.

Backups written to `%LOCALAPPDATA%\NvFilterStudio\backups\` are raw copies and
therefore *do* contain those identifiers. They stay on your machine; do not
upload them.

## Releases are unsigned

Release binaries are not code-signed, so Windows SmartScreen warns on first run.
Code signing needs a paid certificate.

Until that changes, the meaningful defence is that the build is public and
checkable: every release is produced by
[the CI workflow](.github/workflows/ci.yml) from a tagged commit, on a GitHub
runner, and you can build the same thing yourself with `dotnet publish`.

You do not have to take that on trust. Every release asset carries a
[build provenance attestation](https://docs.github.com/actions/security-guides/using-artifact-attestations-to-establish-provenance-for-builds),
signed through Sigstore, which you can verify yourself:

````powershell
gh attestation verify NvFilterStudio.exe --repo 6uhrmittag/NvFilterStudio
````

That confirms the exact bytes you downloaded came out of this repository's
workflow at a known commit — not merely that the download arrived intact.

The two checks answer different questions and neither replaces the other:

| | Proves |
|---|---|
| `SHA256SUMS.txt` | the file was not altered in transit or storage |
| Attestation | the file was built by this repository's CI, from a known commit |

The checksum needs no tools; the attestation needs the
[GitHub CLI](https://cli.github.com/).

Neither is a substitute for code signing, and neither stops the SmartScreen
warning.

## Supported versions

Pre-1.0, only the latest release is supported.
