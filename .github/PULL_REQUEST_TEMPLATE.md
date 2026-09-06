## What this changes

<!-- One or two sentences. Link the issue if there is one: Fixes #12 -->

## How you know it works

<!--
Not a formality. This project's failures are usually silent — a wrongly chosen
record or a too-low sequence number produces a file that grows, reads back
correctly, and still shows the old values.
-->

- [ ] `dotnet test` passes
- [ ] If it could fail silently, there is a regression test for it
- [ ] If it touches the store format, I read `docs/FORMAT.md` first
- [ ] No real store bytes committed (they carry an NVIDIA account id)

## Anything you are unsure about

<!-- Genuinely fine to leave open questions here. -->
