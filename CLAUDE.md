# CLAUDE.md

Loaded into every session. Keep it short — it is the entry point, not the documentation.

## Where things are

- **`docs/design-principles.md`** — the yardstick: purpose, the nine load-bearing ideas, the
  ten rules, the ten check questions. Read it before changing anything structural.
- **`plan/`** — the German working plan, in eleven parts, **git-ignored**. It is the
  scaffolding, not the building: what carries moves to `docs/`, README and CONTRIBUTING.md as
  it is built, and the plan shrinks to a reference in its place. `plan/STATUS.md` holds the
  current state per milestone and the values that have actually been settled.
- **`plan/checks/M<n>.md`** — per milestone, filled **from part 11**, not from part 10. The
  direction is the point: a checklist copied from the milestone inherits its blind spot.

## Rules that apply to every change

- **Everything developer-facing is English** — identifiers, comments, commits, docs. The
  interface is English and German, English as the neutral resource.
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
4. Overwrite the snapshot under `.claude/plans/` from `plan/`; it is the only backup, because
   `plan/` is git-ignored.

## Commands

```
dotnet build DnDOverlay.slnx
dotnet test  DnDOverlay.slnx
dotnet format DnDOverlay.slnx --verify-no-changes
dotnet test  DnDOverlay.Libraries.slnf          # what the Linux job checks
dotnet build installer/Control/Control.wixproj  # never through the solution
```
