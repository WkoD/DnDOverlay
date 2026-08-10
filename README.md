# DnDOverlay

Show images to your players on the screens they are already looking at — a touch table, a
projector, a TV — and drive all of it from your own tablet without standing up.

DnDOverlay is a layer *above* whatever you already run, not a replacement for it: wherever it
shows nothing, mouse and finger reach straight through to the application underneath. Which
one that is stays your choice; DnDOverlay assumes nothing about it.

```
        ┌──────────── DM tablet (Control) ──────────────┐
        │  Touch-first UI                               │
        │  Stage: live thumbnails · inventory · scenes  │
        │  Hub: Kestrel (HTTP + WebSocket)              │
        │  Authoritative scene state · campaigns        │
        └───────┬───────────────┬──────────────┬────────┘
        WS+HTTP │       WS+HTTP │         HTTP │ (later)
        ┌───────┴──────┐ ┌──────┴───────┐ ┌────┴───────┐
        │  Display 1   │ │  Display 2   │ │  Phone     │
        │  Touch table │ │ Projector/TV │ │ (browser)  │
        │  ▲ your app  │ │              │ └────────────┘
        └──────────────┘ └──────────────┘
```

> **Status: under construction.** The repository currently contains the scaffolding — project
> structure, architecture tests, CI, empty installers. Nothing is installable yet. The
> sections below grow with each milestone.

## Documentation

- **[Design principles](docs/design-principles.md)** — what DnDOverlay is for, what it
  deliberately is *not*, and the rules everything else follows from. Start here.

Further documents (`architecture.md`, `data-model.md`, `protocol.md`,
`manual-acceptance.md`) appear as the features they describe are built.

## Building

You need the .NET SDK named in `global.json` and nothing else — no Visual Studio, no Windows
SDK, no installer workload.

```
dotnet build DnDOverlay.slnx
dotnet test  DnDOverlay.slnx
dotnet format DnDOverlay.slnx --verify-no-changes
```

The five libraries are platform-neutral and also build on Linux:

```
dotnet test DnDOverlay.Libraries.slnf
```

The two installers are **not part of the solution** and are always built through their own
project file:

```
dotnet build installer/Display/Display.wixproj
```

See [CONTRIBUTING.md](CONTRIBUTING.md) — one machine is enough for everything.

## Licence

[Apache-2.0](LICENSE). Third-party components are listed in [NOTICE](NOTICE).
