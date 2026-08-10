# Contributing

## One machine is enough

You do **not** need a special setup. Control and Display may run on the same machine, both
regularly installed and regularly paired — that is how the development machine works, and it
is a sensible way to run it too. With two monitors it is the full arrangement; with one it is
still everything except real multi-touch.

Discovery listens on the loopback interface as well, so the display finds a Control on the
same machine by itself.

## Prerequisites

- The .NET SDK version named in `global.json` (`winget install Microsoft.DotNet.SDK.10`)
- VS Code with the recommended extensions (see `.vscode/extensions.json`); no Visual Studio
  and no Windows SDK are required

## The three commands

```
dotnet build DnDOverlay.slnx
dotnet test  DnDOverlay.slnx
dotnet format DnDOverlay.slnx --verify-no-changes
```

These are exactly what CI runs. A fourth checks that the platform-neutral half really is
platform-neutral:

```
dotnet test DnDOverlay.Libraries.slnf
```

Never through the solution on Linux: `Control` and `Display` target `net10.0-windows` and fail
there with `NETSDK1100`. That is what the filter is for.

If you run this in WSL or a container, install **ICU** first (`sudo apt install -y
libicu-dev`). Without it .NET falls back to invariant globalisation — and the culture-sensitive
comparisons are among the things this run is meant to check, so a green run would then mean
nothing.

The installers are built separately, never through the solution:

```
dotnet build installer/Control/Control.wixproj
dotnet build installer/Display/Display.wixproj
```

## Running from the editor

`.vscode/launch.json` contains a compound configuration that starts Control and Display
together. Both get `--data dev-data`, which moves the entire data root — configuration,
campaigns, image cache, logs — into the project folder, so a test run never touches a
regularly installed copy on the same machine. `dev-data/` is git-ignored; a fresh clone does
not contain it, and the application creates it on first use.

Close a regularly installed Control before pressing F5: the single-instance mutex does not
allow a second one, it brings the running window to the front.

## Test data is generated, never committed

`tests/DnDOverlay.TestData` builds every image, every malformed file and every token container
at the start of each test run. **Nothing is checked in.** In a public repository a committed
test image is a publication, so the question of its provenance never arises — because no such
file exists.

If you add a test case, extend the generator. Whatever ends up under `TestData/` must be
self-generated or demonstrably free.

## Conventions

- **Everything developer-facing is English**: identifiers, comments, commit messages, `docs/`,
  README. The user interface exists in English and German; English is the neutral resource.
- Formatting and naming come from `.editorconfig` and are enforced by the build —
  `TreatWarningsAsErrors` is on, and that includes the analyzer and xUnit rules.
- **C# files are UTF-8 without a byte order mark.** `dotnet new` writes one, so a file created
  from a template fails `dotnet format` until the mark is removed. One line, once, and easy to
  mistake for something deeper.
- Branch from `master`, open a pull request, squash merge. `master` is protected.
- Dependency direction is checked by an architecture test in `DnDOverlay.Core.Tests`. If it
  fails, read the message before changing the test: it is usually right.

## Licence of contributions

By submitting a pull request you agree that your contribution is licensed under
[Apache-2.0](LICENSE), as section 5 of that licence states.
