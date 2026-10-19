# Data model

What a screen, a scene and a picture are, how they are addressed, and the rules that hold for
them wherever they are computed. Everything here lives in `DnDOverlay.Core`, so the hub, the
display and the control's thumbnails work from the same types and the same arithmetic.

This document covers what is built. The campaign — scenes and layouts on disk, the inventory as a
user-facing stock, the undo timeline and focus — arrives with M5b and is added here then.

How these types travel and which operations change them: [protocol.md](protocol.md). Why the
model is shaped this way at all: [design-principles.md](design-principles.md), ideas 2, 4 and 8.

## Identifiers

Every identifier is a type of its own, not a bare string or GUID. The compiler then catches the
mix-up nobody else would: a `ScreenId` where a `DeviceId` was meant, an `AssetId` where an
`ItemId` was meant.

| Type | What it is |
|---|---|
| `DeviceId` | A GUID a display creates on its first start and keeps in `display.json` |
| `ScreenId` | A monitor's device instance path — unique **within its device only** |
| `ScreenRef` | `(DeviceId, ScreenId)` — **the** address of a screen |
| `ItemId` | A GUID per item on a scene; a copy gets a new one, a move keeps it |
| `AssetId` | 64 hex digits, the SHA-256 of the source picture |

### `ScreenId` alone is not an address

A device instance path is made of the monitor's EDID, the bus instance and the output — unique on
one machine, and nothing beyond. **Two display PCs cloned from the same image, with the same
monitor model on the same port, can report literally the same path.** Cloning is the ordinary way
to set up a second display PC, so this is not a rare coincidence.

Everything that addresses a screen therefore uses `ScreenRef`, without exception: the hub's scene
store, every `ScreenOp`, `control.json`, the tile order, the view rotation. The collision is ruled
out by construction instead of being unlikely, and an architecture test checks that no signature
takes a bare `ScreenId` where a screen is addressed.

**The price:** a display that loses `display.json` loses its `DeviceId`, and its stored screen
wishes no longer match by themselves. That is healed by assigning the device anew, which exists
for that case anyway.

**The path is derived in `Platform.Windows`, for both applications from the same source** — if
the control and the display each built it their own way, they could form different strings for
the same hardware, and no test would catch it. `Core` treats it as an opaque string it passes on
and compares. It is derived from the device instance path and not from the monitor index, which
would shift whenever a monitor is plugged in or out.

### Geometry belongs to `Core`

`Point`, `Rect` and `PixelSize` are `Core`'s own. `System.Windows.Point` would pull in
PresentationCore and make `Core` depend on a UI framework; `System.Drawing.Point` looks harmless
and is the second trap, because `System.Drawing.Common` has been Windows-only since .NET 7. All
values are normalised (below) except `PixelSize`, which carries physical pixels.

## Screens

### What a display reports, and what it does not

`ScreenInfo` is what a display says about a monitor — `ScreenId`, label, custom name, size in
physical pixels, effective DPI, whether it is the primary — **and nothing else.** In particular it
carries **no state**: the state is the DM's wish, it is born in the control and only ever travels
outwards. A field that holds in one direction and must not be read in the other gets read
eventually; an architecture test keeps `ScreenInfo` free of one.

The control puts the halves together in `ScreenView`: the full `ScreenRef`, the reported
`ScreenInfo`, the wished `ScreenState`, and whatever is in the way right now.

### Four states, all of them wishes

| `ScreenState` | What holds |
|---|---|
| `Enabled` | An overlay window lies on the screen, gestures work |
| `Diagnostic` | As `Enabled`, plus diagnostic data over the pictures |
| `Disabled` | As `Enabled`, but the screen takes no gestures — "frozen" |
| `Inactive` | The screen exists in Windows, and DnDOverlay deliberately puts no window on it |

The four states are stored in `control.json` and nowhere else; `display.json` holds none.

**`Diagnostic` puts a bar over the pictures** that stays while the table is played, because whoever
wants to know why the table is sluggish needs the numbers while the load is on. It takes no touch:
a tap on it reaches the picture beneath. It lies on an edge of its own, set per screen and **never
the park edge** — unset, it lies opposite the fan, and a park edge moved onto it pushes it across;
it keeps a tenth of the edge from each corner, like the fan, because the corners belong to Windows
and to the rescue mark. What it shows is measured at the device, once a second: the connection and
its round trip, this device's name, address and identifier, the frame times of the last thirty
seconds with the stalls over a tenth of a second that those numbers hide, CPU share, pictures
loading and waiting, the store and the committed memory against its cap, the battery, and the last
three warnings. It is the same reading the control's diagnostic view receives.

**There is no blackout.** A fifth state, a black curtain over everything, was planned and struck
before it was built: `Inactive` takes the overlay away, and that was what was wanted of it. Its
number is retired and never given out again, and a file or a message that still says `Blackout`
is read as `Inactive` (`ScreenStateJson`) — the scaffold used to offer it, so it can stand in a
`control.json` in use, and a file that does not read is set aside as damaged.

**`Disabled` freezes the table** — "hold on, don't touch anything". Everything that starts at the
table is refused; from the control everything works as before, the same split as the lock on a
single picture (below). A touch is not ignored silently: it gets the same short response as a
locked picture, or the players would think the table broken and press harder.

**`Inactive` hands the screen back, it does not switch anything off.** Technically it means one
thing: the overlay window is closed. The display releases the decoded pictures but **keeps the
scene**, or it would drop out of the `Hello` and a control restart would lose it.

**Every screen is fully playable in every state.** Every operation applies unchanged; what rests is
the presentation. That makes `Inactive` a tool for preparing: build the scene, switch over, and it
stands there complete.

### What cannot be shown right now is not a state

A screen can be temporarily unplayable for three reasons: it is **unavailable** (gone from the
Windows inventory, or its device is not connected), **the control's window lies on it** (one
machine running both), or it was **hidden at the device** (the rescue mark, below). All three are
`SuppressReason`s — transient findings, never written into the state.

The reason is the way back. A finding that overwrote the wish would have to restore it afterwards,
so it would have to remember what held before and keep that memory right across crashes,
restarts and simultaneous changes. That is exactly where such models break: a screen is unplugged,
somebody changes its wish meanwhile, and the wrong value wins when it comes back. With the state
untouched there is **nothing to restore**. A screen unplugged as `Inactive` comes back `Inactive`.

The control works out the first two, from the device inventory and its own window position; the
display knows no "unavailable" and only reports which screens it has.

**The third is the display's own, and kept apart from the control's.** Either one hides the
screen, but only the control lifts its own, and the device lifts only what it set — it cannot
judge where the control's window lies. It is set by the **rescue mark** and lifted by any update
that arrives from a control, so the two never stand on one screen at once and there is no state
from which only a dead control could free a screen. It never reaches the wire: it exists only
while no control is there to be told.

### The rescue mark

A display that loses its control keeps showing the table — but an always-on-top overlay can lie
over the taskbar, and with no control nothing can be done about it from afar. So once the control
has been gone for **a minute** (a device parameter, `rescueAfterSeconds`, never under ten seconds),
a thin line appears **top left** on every screen with an overlay — the one corner where Windows has
nothing: *"No control for 3 min - hide overlay"*. A tap, with mouse or finger, asks once more; the
answer hides every overlay of that device, because the taskbar may be on another screen. The
wishes stay as they were: frozen stays frozen, and when a control speaks again the windows return
in exactly those states. It does not appear in windowed mode, where the taskbar is free, nor on a
device that was not connected in this run.

It is the same clock as the takeover window: the mark is up exactly when this device would answer
a call for orphaned displays. When it has answered one, the mark carries a second line — *"Offering
itself to <name> - don't offer"*, with what a change costs: an inactive screen would be played
again, because the screen wishes live in the control. Tapping it takes the answer back, and that
caller is not answered again until this device is accepted somewhere. **The mark is the veto, not
the gate**: nobody has to walk to each display PC, but anybody standing at one can say no.

Until the display's tray arrives (M6), the way back from a mistaken tap is a control that speaks
again, or a restart of the display.

### A display starts quietly

**At start every screen is `Inactive`: no overlay window, no scene in memory.** Only once connected
does the display get its states from the hub; a device or screen the hub has never seen becomes
`Enabled`. A display PC starts DnDOverlay at every sign-in, not only on game nights — coming up in
its last state would leave a frozen table nobody can explain, or an overlay on a monitor the DM
had handed back, on an ordinary Tuesday with no control running to correct it.

**It never returns there by itself.** A lost connection, a network change, a crashed control: the
scene stays and no screen falls back to `Inactive`, or a crashed control would have nothing to
take back over.

### `ScreenContext`: everything a computation about a screen needs

The reducer and `ItemToRect` take a `ScreenContext`: size and DPI as hardware facts, and the
display parameters that enter the arithmetic — `MinVisiblePixels`, `MinScale`, `MaxScale`,
`ScaleOnLoad`, `MaxWidthOnLoad`, placement mode, default rotation, park edge, caption size and the
gesture values. Half hardware, half setting; split, they would be two things that never occur
apart. As constants in the reducer the parameters would do nothing; read from configuration, the
reducer would no longer be pure.

**It is stored for every known screen in `control.json`**, and refreshed with every `Hello`,
`ScreensChanged` and `ConfigUpdate`. That is what lets the hub place, cap and park for a screen
whose device is switched off — preparing on a powered-down table is a supported case, not a
degraded one.

**Only what differs from the default is written** (`ScreenSettings.Opinion`). Before that, every
known screen had its full set written out, and a corrected default reached no machine that had
ever run. **The price:** a value the DM deliberately set *to* the default cannot be told from one
never touched, so changing that default later moves their screen too. For parameters that are
starting points, that is the wanted behaviour.

## Scenes

**The hub holds one scene per screen, not per device**, keyed by `ScreenRef`. A scene is:

| Field | What it holds |
|---|---|
| `Background` | the background layer, or none |
| `Items` | the pictures on the table and in the fan |
| `ItemsVisible`, `BackgroundVisible` | the two layer switches, independent in all four combinations |
| `FocusItems` | reserved; focus arrives with M5b |

The screen state is not part of the scene: it belongs to the screen, not to what lies on it.

### Items

```csharp
abstract record SceneItem(ItemId ItemId, double CenterX, double CenterY, double Scale,
                          double AspectRatio, double RotationDeg, int ZOrder,
                          bool Locked, bool Parked, long Revision);

record ImageItem(..., AssetId AssetId, AssetMeta Meta, string Name, bool ShowName,
                 bool AnimationPaused) : SceneItem;
```

- **`AssetMeta` travels with the item**, not only in the stock: the thumbnail and the edge clamp
  compute before the picture has loaded, and `IsAnimated` decides without decoding whether
  "pause animation" is offered.
- **`AspectRatio` sits on `SceneItem`, not on `AssetMeta`.** Layout, edge clamp, flow placement and
  thumbnail need the shape of every item, including a future text card without an asset.
- **`Name` belongs to the asset, `ShowName` to the instance** (below).
- **`Revision` is handed out by the hub alone** (rule 3). It orders transformations — what a display
  weighs its own running gesture against — and nothing else.
- **The base type is discriminated over a fixed list** (`kind`), never over a transmitted type
  name. Text cards will be the second kind.

### The background is placed like a picture

`BackgroundItem` carries `CenterX`, `CenterY`, `Scale` and `RotationDeg` like an item, so
`ItemToRect` and the background share **one** formula (rule 9). It deliberately has none of the
bookkeeping fields — no `ItemId`, no `ZOrder`, no lock, no parking: a screen carries exactly one
background, and a grip on it travels as the whole layer.

`Cover` and `Contain` are buttons, not a mode: they compute centre and scale once — for a quarter
turn against the turned aspect ratio — and the layer then lies like any picture. **`Fit` only
remembers which button held last**, so the menu can tick it, and becomes `null` the moment the DM
moves the layer by hand. A filled layer turned by a quarter is filled again at the new angle.

`Name` and `ShowName` are on the type and not offered: a caption under a layer that reaches past
the screen was unreadable.

## Normalised coordinates

**`CenterX` and `CenterY` run from 0 to 1 across the screen; `Scale` is a fraction of the screen
height.** Four things follow without special cases: moving between screens is re-addressing, the
thumbnail is the same mapping with other metrics (rule 9), the view rotation is one more matrix on
top, and a saved scene fits any screen (idea 8).

`Scale` means height on purpose: between 16:9, 21:9 and 4:3 the height is the steadier reference.
The cost is that every rule involving width has to travel through **both** aspect ratios, the
picture's and the screen's.

### How large a picture arrives

1. **`ScaleOnLoad`, capped by width.** A 5000×500 panorama at 0.5 would be three table-widths wide,
   and in flow mode it would wreck the placement of everything after it. Because `Scale` means
   height while `MaxWidthOnLoad` means width, the screen's aspect ratio enters:

   ```
   Scale = min(ScaleOnLoad, MaxWidthOnLoad × screenAspect / pictureAspect)
   ```

   The same width cap applies when a picture is moved or copied to a screen of another shape —
   arriving is arriving, wherever from.
2. **In `Flow`, held down to fit its place in the grid.** A wide picture therefore arrives smaller
   than `ScaleOnLoad` says — a 16:9 one at three quarters of it. That is the price of the one
   sentence the mode exists for: side by side, without overlapping.
3. **Then lifted to where it can be taken hold of** — 80 DIP on the shorter edge — **but never past
   what fits the screen.** Expressed against the shorter edge, that floor does not merely bind on
   a sliver, it explodes: a 39×6500 picture would have to be twelve screens tall. A picture that
   does not fit is unusable for everyone; one under 80 DIP is merely hard to grab. Arrival and
   gesture read the same floor, so touching a picture never changes its size.

**Lengths are in DIP** where they are about fingers and reading — `MinVisiblePixels`, the caption
size. In physical pixels, 60 on a 4K screen would be a few millimetres.

## Three layers, and `ZOrder` orders within one

A screen has **background**, **table** and **fan**, in that order. The fan always lies above the
whole table: a picture put away has to stay reachable, or it is a drawer with a cupboard in front
of it. **Within** table and fan, `ZOrder` orders, and `Parked` says which layer is meant. The
drawing depth is layer first, `ZOrder` second (`Parking.Depth`).

**There is one ordering number, not two.** The fan once had a second field, and every command over
a selection then had to decide which of the two it meant — decided wrongly three times in a row.
With one field per layer the question cannot be asked.

### What is touched comes to the top

One principle decides it: **whoever takes hold of a picture wants to see it.** `ZOrder` rises to
the maximum of the picture's layer plus one when

- a player touches it at the table,
- the DM takes hold of it in the thumbnail — **locked or not**: at the table a locked picture
  cannot be taken hold of at all, in the thumbnail the DM can, and then he has to see it,
- it is added,
- it arrives on another screen,
- it is parked, or pulled back out of the fan.

It does **not** rise for the visibility switches, nor while a gesture runs (the first report
carries the grip, the rest would only run the numbers up). The maximum is counted **per layer**, so
a parked card does not inflate the table's numbers, and the old number is spent: what comes out of
the fan was touched and goes to the front. The number space is **per screen** — that is why a move
gets a fresh number instead of keeping its own; a picture with `ZOrder` 3 arriving on a screen
whose maximum is 47 would land invisibly at the bottom.

Only the hub hands it out. A display raises a touched picture locally at once, with the hub's
formula, and takes the binding value when it comes back.

## The lock

**The lock guards against gestures at the table, not against commands from the control.** It is the
DM's protection *from* the table; he would otherwise have to unlock before every correction.
Enforced twice: the display does not let the gesture start, and the hub refuses an
`ItemTransformed` for a locked item (3021). The check sits in `ISessionApi`, where the origin of a
command is known — the reducer sees only the operation.

| Case | Behaviour |
|---|---|
| removing, moving or parking a locked picture from the control | goes through |
| an operation for an `ItemId` the scene does not carry | **no effect, no error** — with two ways of steering and hands at the table, gone-in-the-meantime is a normal course of events |

**Parking takes the lock off.** The lock holds a picture *where it lies*; in the fan there is no
such place — the fan owns every card's size, angle and position — and a padlock there would only
refuse the one gesture that matters, pulling the card back out. It is a rule of the scene, applied
in the reducer, so the table and the hub apply exactly the same one.

**The lock is visible, not only felt**: a padlock on the picture, at the table and in the
thumbnail. That is why "unlock all" needs no way back — whoever sees which five were locked taps
them again in seconds.

## Placement

An aimed drop always wins. Otherwise the screen's **placement mode** decides:

- **`Flow`** (default): a fixed grid of equal cells, filled in reading order and then from the
  start again — six places on a 1080p table at arrival size, so the seventh picture goes to the
  first place. **Nothing is searched for and nothing is skipped:** a mode that looked for a free
  place would answer the same action differently each time, depending on where everything else
  happens to lie, and the DM could not learn it. The price is that a picture can land on one that
  was dragged onto its place. Each picture is centred in its cell, so rows line up whatever shape
  arrives, and the caption is drawn inside the picture, so it never grows a row.
- **`Cascade`**: stacked from the centre with a growing offset.

**A flat table has no right way up** for anybody sitting opposite. Each screen therefore has a
`DefaultRotation` (0/90/180/270) for new pictures, set to the usual direction of view; beamers and
TVs keep 0.

**Placement is computed in the hub**, from `ISessionApi`; the arithmetic is a pure function in
`Core`. Choosing the next place means reading the scene and writing it in the same breath —
computed in the control, the paste hotkey and a double-tap in the stock could pick the same place
and lay two pictures exactly on top of each other.

## One picture, one name

**Within its campaign every picture has exactly one name, and every name belongs to exactly one
picture.** The name is the only handle on a picture — filter search, the paste notification and
the placeholder of a missing asset all show nothing else — and two entries called `Warrior` would
make each of them a riddle.

**The starting value** is found in five stages, the first that yields wins:

1. the token name from a `.rptok` ("Testfigur" rather than an MD5 file name);
2. the file name, from a file dialog or an Explorer drop;
3. the last path segment of the source URL, for a URL import;
4. from the clipboard itself: the `alt` or `title` of the copied `<img>`, else the last segment of
   the `SourceURL:` header of `CF_HTML`, else a text lying beside it that is a valid URL;
5. otherwise a counted name, `Clipboard {n}` by default (`countedNamePattern` in `control.json`).

Names carry **no extension** — `png` would otherwise find everything, and `IsAnimated` says what an
extension would. Nothing is invented: anything made up would mislead more than a number. Stages 2
to 5 are plain string work in `Core` (`AssetNaming`); stage 1 needs the container opened, so the
stock overrides the proposal when it finds a better name. **The result is a proposal**: the stock
holds it against what is already there and numbers a collision (`Orc (2)`), because an import of
two hundred files cannot stop to ask.

**The name is kept on the item too** (`ImageItem.Name`), so a display can draw the caption without
knowing the stock. Renaming changes every item of a scene showing that asset in one operation
(`setName`); carrying a rename across every screen of a campaign is M5b.

**Whether the name is shown belongs to the instance.** `ShowName` sits on the item: the councillor
everybody knows wears his name badge, the stranger does not. Each screen has a default for new
pictures, off by default, because a freshly taken-in picture still carries its file name.

## Parked pictures

`Parked` puts an item into the **fan** along the screen's park edge. A parked item carries no
position of its own: where it lies, how large it is and at what angle all follow from the list of
parked items and the park edge, and both ends compute them with the same function
(`Parking.Arrange`). Sending coordinates would leave a gap as soon as one card left, and a scene
moved to another screen would bring the first screen's edge along. The order within the fan is
the `ZOrder` of the fan layer.
