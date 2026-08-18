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

**Check the feature band, not just the major version.** `global.json` rolls forward over patches
only, so an SDK from a *later* feature band does not satisfy it — `winget` installs the newest,
and on a machine set up months after this was written that can be the wrong one. The build then
fails with "compatible SDK not found", which reads like a broken project rather than a missing
install. Run `dotnet --list-sdks`, compare the middle number with `global.json`, and install the
exact version if it differs (`winget install Microsoft.DotNet.SDK.10 --version <version>`).

## The three commands

```
dotnet build DnDOverlay.slnx
dotnet test  DnDOverlay.slnx
dotnet format DnDOverlay.slnx --verify-no-changes
```

These are exactly what CI runs, **and the first two are a sequence rather than a choice.**
`dotnet test` compiles the test projects and whatever they depend on — and nothing depends on
`Control` or `Display`. That is deliberate: an application is what everything else is reached
*from*, and `ReachedFromProductionTests` checks exactly that by reading their assemblies. The
consequence is easy to trip over: **a change inside either application is not compiled by
`dotnet test`**, so that one rule keeps answering out of the previous build until you run
`dotnet build`. It fails loudly rather than answering wrongly, and its message says what to do.

A fourth command checks that the platform-neutral half really is platform-neutral:

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

## Running the display on a second machine

The two-machine steps of the acceptance need a display PC, and a display PC does not need a
development environment: **no .NET, no editor, no installer, no administrator.** A self-contained
publish is a folder you copy over and delete afterwards.

On the development machine:

```
dotnet publish src/DnDOverlay.Display/DnDOverlay.Display.csproj ^
  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o out/display
```

That is eleven files and about 136 MB — the runtime travels inside the executable, and the four
`.pdb` files travel beside it on purpose: without them an unhandled fault logs a stack trace
without line numbers, and that is the one line a hand-run cannot go back and fetch.

Copy the folder over, then start it from a command prompt:

```
DnDOverlay.Display.exe --host 192.168.1.23 --name TISCH --data D:\dnd-data
```

- **`--host`** skips discovery. Discovery listens on UDP 47800, which is an INCOMING port and
  therefore a firewall question on a machine nobody has administrator rights on; the connection
  itself is outgoing and needs no rule. Leave the switch off once, deliberately, to check that
  discovery works — but not on the run where you are trying to measure something else.
- **`--data`** puts configuration, image cache and logs in one folder of your choosing instead of
  under `%LOCALAPPDATA%`. Removing the machine from the experiment is then deleting two folders.
- **`--name`** is what the device list shows before anybody has renamed it.

**Nothing will appear, and that is the silent start.** Every screen begins `Inactive` and stays
that way until a control says otherwise, so the machine shows its own desktop and the taskbar
stays free. In the control: allow the pairing request — comparing the code on the device — and then
set the screens to `Enabled` in the *Devices* window. Only then does an overlay appear.

**There is no way to quit it from the device yet.** The tray icon is M6 and the rescue marker is
M5a, so until then it is Task Manager, or:

```
taskkill /IM DnDOverlay.Display.exe /F
```

The overlay never takes the foreground and passes input through wherever nothing is drawn, so the
desktop underneath stays usable while it runs — including Task Manager.

**A table driven as a second monitor needs its touch assigned to it.** Windows maps a digitizer to
a display, and it guesses wrong as soon as there is more than one: *Tablet PC Settings* → *Setup*,
then follow the prompt on the screen you are pointing at. Without it the fingers land on the other
screen, which looks exactly like broken gesture handling.

The log is `<data>\logs\display-0001.log`. Its header names version, protocol version and both
languages; the frame-time lines (`3023`, and `3024` when a budget is missed) are what the
acceptance of M3, M4 and M5 is read from until the diagnostic bar exists.

The MSI under `installer/Display` is the other way in and the one the acceptance of M7 is about.
It needs the .NET Desktop Runtime on the target and installs per user; for a measuring run the
copied folder is the smaller intervention.

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
