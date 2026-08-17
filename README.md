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

> **Status: under construction.** Pictures already go from the DM's machine onto the screens:
> displays find the control by themselves and are paired by hand, the campaign holds a stock,
> and all four ways in work. Still missing are the gestures at the table, the DM's own surface,
> and the installers, which are empty — so **nothing is installable yet**; it runs from a build.
> The sections below grow with each milestone.

## Documentation

- **[Design principles](docs/design-principles.md)** — what DnDOverlay is for, what it
  deliberately is *not*, and the rules everything else follows from. Start here.
- **[Architecture](docs/architecture.md)** — the projects, what may depend on what, and the
  decisions that were measured rather than chosen.
- **[Protocol](docs/protocol.md)** — messages, scene operations and the log event catalogue.

Further documents (`data-model.md`, `manual-acceptance.md`) appear as the features they
describe are built.

## Images

Hand a picture over any of four ways — drop a file or a folder on the window, paste a
screenshot, paste from a browser, or give an address — and it lands in the campaign and can go
on a screen. All four go through the same path: two hundred files behave exactly like one, with
one collected message at the end rather than two hundred dialogs.

**A picture is stored once.** The name is the content: two copies of the same image are one
entry, whatever the files were called. Re-importing something already there does not duplicate
it and does not rename it.

### What comes in

| | |
|---|---|
| **Promised** | PNG, JPEG, GIF, BMP, WebP, AVIF — animated GIF and WebP included |
| **Tolerated** | whatever else the image library on the machine happens to read (TIFF, PSD, JPEG XL …). It works, it is not assured, and the collected message says so |
| **Also** | MapTool tokens (`.rptok`) — the portrait is taken out of the container and the token's own name comes with it |

**Refused, and told to your face:** HEIC/HEIF — for the HEVC patent situation, not a technical
reason, so saving as JPEG or PNG gets you straight in, and AVIF is unaffected. Files that are
not images but scripts, whatever the extension says. And anything past the limits below. Every
refusal names the file and the reason; nothing is dropped quietly.

### Limits

| | | why |
|---|---|---|
| **100 MiB** | per file | above this the wait stops being a wait |
| **20 000 px** | per side | a header may claim more; nothing is unfolded to find out |
| **120 M px** | in total | 20 000 × 20 000 is not the same as 20 000 × 6 000 |
| **500** | frames | an animation past this is a video, and this is not a video player |
| **64 MiB** | kept free | a campaign that fills the drive is refused with the drive named, before decoding |

### What is thrown away

Metadata does not travel to the table. **JPEG** loses `APP1` (EXIF and XMP, where the GPS trail
of a holiday photo lives), `APP13` and the comment segment; the colour transform stays, or CMYK
pictures come out inverted. **PNG** keeps what carries meaning — transparency, colour profile,
animation — and drops the rest, including `eXIf`, the text chunks and the timestamp.

The pixels are **not** touched in either case: same bytes in, same bytes out, minus the
metadata. A 562-byte PNG with a GPS tag leaves as 131 bytes with none.

## On the table

Each screen carries its own parameters, and what you set at the table is never rearranged
behind your back.

| Parameter | Default | What it does |
|---|---|---|
| `scaleOnLoad` | `0.4` | how tall a new picture arrives, as a fraction of the screen |
| `maxWidthOnLoad` | `0.9` | the width it is capped to, so a panorama cannot arrive wider than the table |
| `minScale` | ≈80 dip tall | how small a picture can be made — a floor in real size on that screen, not a factor |
| `maxScale` | `10` | how large |
| `minVisiblePixels` | `96` | how much of a picture must stay on the screen; it cannot be pushed away entirely |
| `placement` | `Flow` | where a new picture goes: `Flow` fills free places side by side and wraps, `Cascade` stacks with a growing offset from the centre |
| `defaultRotationDeg` | `0` | the angle a picture arrives at — for a table people sit around |
| `parkEdge` | `Right` | which edge a parked picture waits at |

Every display keeps its own copy of the pictures it has shown, up to **4 GiB** — so moving a
picture from one screen to another, hiding it and bringing it back, or putting the same map up
twice in an evening costs no transfer at all. Only a picture that was evicted to make room is
fetched again. Pictures arrive **three at a time**: twenty at once are twenty pictures that are
all slow, and after ten seconds none of them is there.

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
