# CLAUDE.md

Loaded into every session. Keep it short — it is the entry point, not the documentation.

## Where things are

- **`docs/design-principles.md`** — the yardstick: purpose, the nine load-bearing ideas, the
  ten rules, the ten check questions. Read it before changing anything structural.
- **`plan/`** — the German working plan, in eleven parts, **git-ignored**. It is the
  scaffolding, not the building: what carries moves to `docs/`, README and CONTRIBUTING.md as
  it is built, and the plan shrinks to a reference in its place. `plan/STATUS.md` holds the
  current state per milestone and the values that have actually been settled. **It is not
  backed up, deliberately** — a snapshot under `.claude/plans/` used to be step 4 of every
  milestone, was never once made, and would have put the plan into this public repository;
  decided on 03.09.2026 that there is no backup rather than an instruction nobody follows.
- **`plan/checks/M<n>.md`** — per milestone, filled **from part 11**, not from part 10. The
  direction is the point: a checklist copied from the milestone inherits its blind spot.

## Rules that apply to every change

- **Everything developer-facing is English** — identifiers, comments, commits, docs. The
  interface is English and German, English as the neutral resource.
- **Interface text and log messages are precise and short. No filler.** Every sentence tells
  the reader something they cannot already see: what is the case, what follows from it, what
  to do about it. A clause that restates the sentence before it, comments on the situation
  ("deliberate, and worth knowing") or pads it with examples is cut. This applies to labels,
  confirmations, verdicts and `[LoggerMessage]` templates alike — a log line is read when
  something is wrong, and filler is what stands between the reader and the fact.
  *Comments and docs are the opposite case: there, the reasoning is the content.*
- **Dependency direction**: `Core` knows nobody · `Hub`, `Campaign`, `Imaging`, `Transport`
  know only `Core` · `Control` and `Display` sit outside. **`Hub` and `Campaign` do not know
  each other** — the arrangement belongs to the hub, the material to the campaign.
- **The five libraries are `net10.0`, the two applications `net10.0-windows`.** Platform APIs
  belong in the applications. The architecture test additionally forbids P/Invoke,
  `Microsoft.Win32.*`, `ProtectedData`, `DateTime.Now` and `Environment.GetFolderPath` in the
  libraries.
- **Every decision exists exactly once.** If something needs saying in two places, one of them
  becomes a reference.
- **Order of precedence** when something has to give: 1 gestures at the table · 2 new images
  onto displays · 3 feedback that something is happening · 4 currency of thumbnail and touch
  points.
- **Test seams**: `TimeProvider` and every storage path are passed in, never hard wired.

## At the end of every milestone

1. Walk the ten check questions from `docs/design-principles.md`.
2. Tick off `plan/checks/M<n>.md` and note what changed against the plan.
3. Move what now carries into `docs/` — and replace that part of the plan with a reference.

## Working directory

This repository is an **additional** working directory, not the primary one. A shell's directory
can end up back at the primary one between calls — a relative path then fails with "project file
does not exist", which reads like a missing file rather than a wrong directory. Prefer absolute
paths, or set the directory in the same call that uses it.

## Committing

**Write the message to a file and use `git commit -F <file>`.** Not `-m` with a multi-line string.

The reason is a mistake that is easy to make and impossible to see afterwards: `@'…'@` is a
PowerShell here-string, and this repository is worked on through **both** shells. In bash the same
characters are not a here-string at all — they concatenate a literal `@` with a quoted string, and
the commit goes through with `@` as its **subject line** and the real subject one line below. Bash
heredocs (`<<'EOF'`) fare no better: they are refused when a command is chained.

Nothing about the tool result says so. `git commit` reports success, the push succeeds, and on a
protected branch it can no longer be repaired — amending would need a force-push, which is
blocked, rightly. **So verify rather than assume:** `git log -1 --format=%B` after committing.

## Commands

```
dotnet build DnDOverlay.slnx
dotnet test  DnDOverlay.slnx
dotnet format DnDOverlay.slnx --verify-no-changes
dotnet test  DnDOverlay.Libraries.slnf          # what the Linux job checks
dotnet build installer/Control/Control.wixproj  # never through the solution
```

**Build before test, and the order is not a habit.** `dotnet test` builds only the test projects and
what they depend on — and **nothing depends on `Control` or `Display`**, which is the property
`ReachedFromProductionTests` exists to check. So a change to either application is not compiled by
`dotnet test` at all, and that rule fails on the stale assembly until a `dotnet build` has run. It
says so itself; this line is here so it does not have to.
