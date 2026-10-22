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

> **Status: under construction.** Pictures already go from the DM's machine onto the screens
> and the players can move them: displays find the control by themselves and are paired by
> hand, the campaign holds a stock, all four ways in work, the table takes gestures, and the DM
> sees every screen live and handles it with the same grips; campaigns, scenes and layouts can be
> saved, opened and switched, the stock stands in a dockable panel, and undo reaches back through
> the session. Still missing are parts of the device settings and the installers, which are
> empty — so **nothing is installable yet**; it runs from a build.
> The sections below grow with each milestone.

## Documentation

- **[Design principles](docs/design-principles.md)** — what DnDOverlay is for, what it
  deliberately is *not*, and the rules everything else follows from. Start here.
- **[Architecture](docs/architecture.md)** — the projects, what may depend on what, and the
  decisions that were measured rather than chosen.
- **[Protocol](docs/protocol.md)** — messages, scene operations and the log event catalogue.
- **[Data model](docs/data-model.md)** — screens, scenes and pictures: how they are addressed,
  laid out, ordered and locked.

`manual-acceptance.md` appears once the manual checks are written down for good.

## Images

Hand a picture over any of four ways — drop a file, a folder or a link, paste a screenshot,
paste from a browser, or give an address — and it lands in the campaign. Dropped on the campaign
panel it goes into the stock; dropped on a screen's tile it also goes onto that screen, where you
let go. All four go through the same path: two hundred files behave exactly like one, with
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

The players move the pictures themselves, and they cannot break anything doing it: every grip
has a way back, nothing can be made too small to grab, and nothing slides off the screen.
Several people at once, without being told how.

| Grip | Finger | Mouse | On the DM's stage |
|---|---|---|---|
| Move | drag | drag with the left button | the same |
| Scale | pinch | wheel, about the pointer | the same |
| Rotate | turn with two fingers | hold Ctrl and drag, about the centre | the same |
| Bring to the front | touch it | click it | the same — a locked picture too |
| Turn it to face you | double tap | double-click | the same, towards the nearest edge of that screen |
| Park it at the edge | flick towards the park edge | drag it onto the fan and let go | the same |
| Fetch one back | run along the fan, then pull away from the edge | the same, with the button held | the same |

The last column is the point: **one set of grips for two surfaces.** The stage does the same
arithmetic as the table, so nothing has to be learned twice.

The **right mouse button stays unassigned on purpose**: a grip that exists on only one of the
two surfaces is worse than a grip missing from both, and Ctrl+drag sits under the same hand
anyway.

Five things make the gestures survive an evening rather than merely work:

- **Turning has a dead zone and snaps.** Two fingers always turn a picture a little; without a
  threshold everything on the table stands crooked after three hours. Small angles are
  discarded, and an angle close to straight is pulled straight **when you let go** — never
  under your finger, which feels broken.
- **A picture may hang over the edge, but not disappear.** What stays reachable is measured
  against the picture as it is really drawn, corners and all, so a picture turned 37° in a
  corner is as grabbable as one lying straight.
- **Two ways into the fan.** A short quick flick towards the park edge from anywhere on the
  table, or simply pushing the picture over and **letting go with your hand on the fan** — where
  the hand ends is what decides, so nothing is put away that you did not carry there. While your
  hand holds a picture on that strip along the edge, **the strip lights up gold** — also when
  nothing is parked yet and the fan cannot be seen — so letting go there is never a surprise. The
  second way is the only one a mouse has. A picture set gliding stops short of the fan rather than
  sliding underneath it, because what lies under the fan cannot be picked up again.
- **The fan is a fan, and its length is the count.** Parked pictures lie along the park edge at
  the size a picture arrives at, newest at the near end and on top, and the whole fan lies over
  the table so the way back is never covered. Put enough away and the cards close up; every card
  keeps the stretch of the fan it can be *seen* over, so the newest has the most room and the rest
  a sliver each — past about five put away, those slivers get thin enough that picking one takes
  aim. A picture too long for the fan is not shrunk but **cut**, fading out at the cut so the edge
  reads as "there is more of this"; it unfolds whole the moment you touch it, at the size it always
  had. Run a finger along the fan and each card in turn steps out whole where it lies;
  pull away from the edge and that one comes onto the table, in one movement, without changing
  under your hand. Let go without pulling and nothing has happened.
- **A picture you are holding stays under your hand**, whatever else arrives on the screen
  meanwhile.

A picture that is not there yet already has its place: a **dashed frame with its name**, where it
lies, as large and as turned as it will be, and it can be moved like any other. A picture that
could not be loaded at all keeps the frame, with the reason underneath.

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
| `diagnosticEdge` | opposite the park edge | where the diagnostic bar lies — never on the park edge; moving the park edge onto it pushes it across |

Every display keeps its own copy of the pictures it has shown, up to **4 GiB** — so moving a
picture from one screen to another, hiding it and bringing it back, or putting the same map up
twice in an evening costs no transfer at all. Only a picture that was evicted to make room is
fetched again. **The copy ends with the display**: closing it empties the store, and so does
switching the PC off or logging off, so the next evening transfers again and no picture stays on a
display PC that is not in use. Settings changed just before a shutdown are written first. Only after a
crash is the store found again — then the first scenes keep what they need and the rest goes, and
if no control comes within five minutes, all of it goes. Pictures arrive **three at a time**: twenty at once are twenty pictures that are
all slow, and after ten seconds none of them is there.

## On the DM's stage

The control shows every screen as a **live tile** — the pictures where they lie, the fan, the
background, and the fingers of the players as circles with a fading trail. The tiles stand in the
order you drag them into by their head, and each one opens on its own as a large single view.
Whatever you do on a tile happens at the table at once; a picture still on its way there shows
it by **filling with colour from the bottom up** as it arrives.

Under each tile, the **state selector** says how you want that screen: *Active*, *Frozen* (the
table takes no gestures), *Diagnostic* (a bar of numbers over the pictures at the table) or
*Inactive* (no overlay; the head turns grey). It always shows your wish. What keeps a screen dark
although you did not ask for it — the device not paired or not connected, the screen unplugged,
the control's own window lying on it, or hidden at the device — shows as **⚠ 2 reasons** at the
right of the head; tap the head to read them. Beside it stands the **battery** of a display PC that
has one, red from 10 %, with a lightning sign on the cable.

**Windows** under a tile shows the other programs' windows on that screen behind the pictures —
half-transparent, named by their program, never by their title, where two overlap the darker area
lies on top. Take one by its strip along the top to move it, by a corner to size it; its menu
minimises, maximises, restores or fills the screen with it. Inside a window the selection frame
still works. A window of a program that runs elevated, or on an inactive screen, says so instead of
moving. A device can be set to only report its windows, or to neither (*Windows of other programs*
in its band).

**The campaign panel** holds what you bring: the tabs *Pictures* and *Scenes*, a filter that
finds a name by any part of it — case and accents do not matter, several words must all match —,
an order (newest, oldest, by name) and a zoom (− and +, or Ctrl+wheel). A picture's tile carries
its name, its resolution and marks: *GIF* for an animation, *on 2* when it lies on two screens,
*in 3* when three saved scenes or layouts use it, *missing* when its file is gone. A double tap sends it to the active screen; its menu sends it as
a picture or as the background to any screen or to all, says where it lies, and shows its file in
the folder. Ctrl+click and a frame drawn with the mouse pick several, which then go together. The
panel docks at any edge — *Dock panel* in the menu ☰ —, and the line between it and the stage can
be dragged; both are kept. The same menu holds *Clear all*, *Send to all*, the devices, the log and
the settings.

**Devices** (☰ → *Devices …*, or *Set up screen …* in a tile's menu) lists every device with its
screens on the left; on the right stand two bands for what is chosen. The **device band** says it
applies to all of the device's screens and holds what concerns the device as a whole — its name
(the screens' default labels follow it; refused if another device or screen has it), keeping the
screens awake, how soon the rescue mark appears, its language, its log levels — plus *Show the screen names*,
*Clear the picture store …* (fetched again for what lies on the screens), *Restart …* (it connects
again by itself) and *Unpair …*. The **screen band** holds the screen's name (refused if another
screen or device already has it), its state and every display parameter: whether it is shown as an
**overlay** or as an ordinary **window** — for a television on the wall or a player's monitor, where
an overlay would take the whole screen; the window remembers where it was put, and the rescue mark
does not appear on it —, arrival size and width, placement, the direction new
pictures face, where parked pictures line up, the smallest and largest picture, names, turning,
snapping, the arrival highlight, gliding, scrolling, the diagnostic bar. A value at its default
stands back in grey with *default* after it; a value that was set stands in full with *Reset* beside
it, and *Reset all to defaults* clears the screen. Every change takes effect at once — and works with
the device switched off too: it goes out when the device connects. *Unpair …* names the screens and
pictures that would be lost and offers to save every occupied screen as a scene first.

A display waiting to be paired shows its **setup screen** — its name, its address and the pairing
code, large enough to read from across the room — for exactly as long as the request lies before
you; at the control the same three stand in the waiting line. A display that lost its identity —
reinstalled Windows, a swapped disk — knocks as a new device: **Assign to a known device …** lets it
in as the old one, which is not connected, taking over its name, settings, screens and what lies on
them. On the same hardware the screens simply carry on. After new hardware the old screens stand
beside the new ones as missing, and **Take over a missing screen** in a new screen's band moves the
old one's state, settings, name, arrangement, tile place and view onto it.

**Settings** (☰ → *Settings …*) holds what is set once: this control's language, how a double tap or a drag loads a scene
(replacing, by default) and a layout (adding), the name pasted pictures get, whether the tables
show touch points, this control's log level, its port (taken at the next start) and the network
check with its firewall buttons. **Where the campaigns lie** can be changed there too: the question
names both places, how many campaigns and how big, and offers to take them along or only to change
the place. On the same drive the move is a rename, over in an instant; to another drive the
campaigns are copied with progress and a way to break off, checked, and only then removed from the
old place. If anything fails, everything stays where it was. The open campaign is closed first.

**Language** is *Automatic* (as Windows), *English* or *Deutsch* — for the control in *Settings*, for
each display in its device band, independently of one another. Log lines follow a change at once, in
the panel and in the files under `logs\`, and the file notes where it switched; windows and screens
take it at the next start. Everything the two applications show is translated - windows, menus,
the device bands, the reasons on a tile, why a picture was refused; names, labels and values the DM
chose stay as they are.

**The log** (☰ → *Log*) is a panel beside the stage, at another edge than the campaign panel — the
opposite one unless *Dock log* says otherwise; edge, size and whether it is shown are kept. It shows
this control's lines and those the devices forward in one stream: time, level as a word, the
source's name, event number and name, the sentence. The values behind a line are in the files under
`logs\`. A coloured bar at each line's edge matches its source — the control grey, every device a
colour, its screens shades of it —, and the name stands there as text too, so a copied line loses
nothing. The bubbles at the head filter: a tap on a device hides it with its screens, a tap on a
screen hides only that one and leaves the device half filled; right-click or a long press offers
*Only these* and *Show all*. Devices that are not connected stand dimmed. The filter only changes
the view — nothing stops being forwarded or written — and is gone at the next start.

**Undo and redo** sit in the panel head, each labelled with what the next press does — *↶ Add
"Grimmbart"*, *↷ Clear TISCH-PC//1* —, or with Ctrl+Z and Ctrl+Y. What lays, takes or moves a
picture is a step: adding, removing, moving, copying, a background set or removed, a screen
cleared, a scene or a layout loaded, *Clear all*, *Send to all* — one step each, however many
screens it reaches. Pushing pictures around, locking, parking, names and screen states are not.
Undo takes back only what the step did: pictures the players have moved since stay where they
are. The timeline lasts as long as the control runs; switching the campaign ends it, and deleting a
picture from the stock cuts it where it would bring the picture back — the question says how many
steps that costs.

**Renaming** a picture — from its menu in the stock or from its menu on a tile — changes the name
everywhere: in the stock, on every screen, in every scene and layout. A name another picture
already carries is refused. **Deleting** takes the picture out of the campaign for good, after one
question that names what goes: the screens it is taken off, the scenes and layouts it is removed
from. Scenes and layouts left empty by it are offered for deletion too. *Used in* in the menu lists
them beforehand. Several chosen pictures are deleted with one question, and Del does the same while
the stock has the focus — on the stage, Del only takes pictures off the screen.

Pictures and scenes are **dragged** out of the panel onto a tile: a picture lands where it is let
go, several side by side, a scene replaces what lies on that screen. While something is carried,
every tile shows a strip along its lower edge — let go there and the picture becomes the
background. The other way round, a picture dragged off a tile into the panel leaves the screen and
stays in the stock; deleting it is a separate step. With a finger the direction decides: along the
panel the list scrolls, toward the stage the entry comes out. A long press opens an entry's menu,
a double tap sends it. *Show in stock* in a picture's menu on a tile narrows the panel to that
picture.

Unticking *Dock panel ▸ Pinned* **retracts** the panel to a narrow tab at its edge, giving the
stage the whole width. A tap on the tab pushes the panel out over the stage; a tap beside it, a
swipe toward the edge or the start of a drag sends it back. A picture dragged from a tile onto the
tab leaves its screen without the panel opening.

**Campaigns** keep the stock of one group apart from another's. The control starts with **none
open**, so it never puts the wrong group's pictures in front of you; the head above the stage says
so. Adding the first picture begins a new unnamed campaign — its folder exists from that file on,
so nothing is lost before it has a name — and saving its first scene asks for one. The button in
the head lists every campaign and switches: switching clears the table on every screen, because
those pictures belong to the campaign being closed, and it asks first. *Set up campaign ...*
renames the open campaign together with its folder, opens the folder, and deletes it — only after
you type its name. Each campaign is a folder under `%LOCALAPPDATA%\DnDOverlay\campaigns\`, and
**that folder is the backup and the exchange format**: copy it. On opening, the folder is checked
against its inventory: pictures laid into it by hand are taken in, a byte-identical copy is
removed, a picture deleted by hand is marked missing (taking it in again heals it), other files
are left alone, and an inventory that cannot be read is rebuilt from the folder. One note beside
the stage says what was found. The control remembers the campaign open last without opening it:
the switcher marks it, and when the control restarts while the displays still show their pictures,
the note beside the stage names that campaign and offers to open it.

**Tidy up campaign ...** in the campaign menu lists the open campaign's pictures, largest first,
with their size and where they are used; a double click shows one in the stock, where pictures are
deleted. *Check folder again* repeats the check of the opening; *Verify stock* reads every picture
and names those whose file no longer matches what was taken in. When a campaign opens, the control
also speaks up if the drive the campaigns lie on has less than 5 GB free or the campaign holds more
than 2000 pictures — once per start, and after *OK* only when it has become a quarter worse. *Don't
report again* switches that one notice off; both thresholds and switches are in *Settings*.

**Scenes** keep an arrangement for next time. *Save screen as scene ...* in a screen's menu asks
for a name — suggested from the screen and the time — and asks before it overwrites one that
exists. The saved scenes stand in the panel's tab *Scenes* with a preview: a double tap lays one
onto the active screen, replacing what lies there, and its menu loads it onto any screen, replacing or
adding, renames, overwrites it with a screen, or deletes it. A scene brings the pictures' current
names, a background that filled the screen fills the new one, parked pictures go into that
screen's fan, and a picture the stock no longer has stands as a placeholder — one message says how
many. Scenes are files in the campaign folder (`scenes\<name>.json`), named as they are called.

A **layout** holds several screens at one grip — every screen with something on it, inactive ones
included. *Save layout …* at the end of the search bar in the tab *Layouts* keeps the table as it
stands; a tile shows the screens side by side and says how many it takes. A double tap, or dragging
it anywhere onto the stage, lays it with *Add*; its menu also offers *Replace used screens* (clear
only the screens it names) and *Replace all screens* (clear every screen first), saves its scenes as
single scenes, renames, overwrites it with the table, or deletes it. Screens are found by their
address and, failing that, by their name, so a reinstalled display still gets its scene. When a
screen is not there, a table asks per scene: onto another screen, saved as a scene of its own, or
not loaded — the layout file stays whole. Layouts are files too (`layouts\<name>.json`).

While a display is connected, the control keeps its own machine from going to sleep — the table
hangs on it. Its own battery is the one that gets a warning: at 10 % and off the cable, a note
beside the stage says so, because when it runs out every display loses its control.

| Grip | Finger | Mouse |
|---|---|---|
| Select one | tap it | click it |
| Select several | drag a frame from free area | the same, or Ctrl+click one by one |
| Move a picture to another screen | drag it onto that tile | the same |
| … or copy it there | — | hold Ctrl while dragging it off the tile |
| Point at something for the players | tap with two fingers | middle button, or hold Space and click |
| Open a menu | press and hold | right-click |
| Remove what is selected | — | Del |

**A picture you drag off its tile is still in your hand.** Come back without letting go and you
go on moving it, under the same spot of your hand; let go on another tile and it goes there. A
whole selection goes to another screen through the menu, stacked as it was.

**The two menus carry the rarer things.** A picture's menu — or the selection's, if the picture is
part of one — turns it to face you or to a fixed **0°, 90°, 180° or 270°**, parks, locks, pauses
an animation, shows the name, selects everything on the screen or in the fan, copies and moves
to another screen, and, set apart at the bottom, removes. A screen's menu, on free area or on the
tile's head, opens the single view, sets up the screen, turns the view of it by a quarter for a
DM who sits at the side, shows the **diagnostic view** — the device's frame times, CPU, loads,
store and memory, the same numbers as the bar at the table, in place of the thumbnail — and
handles the background: **Customize** lets you move, zoom and turn
it with the same grips as a picture, while the pictures above it turn see-through; **Fill
screen** and **Fit whole on screen** put it back into the two obvious positions, keeping a
quarter turn; and it turns by the quarter like a picture — filled or fitted, it stays so.

**Unlock all** is a button on each tile, because a padlock on a picture is set one at a time and
should not have to be taken off that way.

**Focus** — the button beside it — puts the selected pictures large in front of the players: one
in the middle, two side by side, up to six in a grid, each keeping its angle, in the order they were
chosen, with the rest of the screen dimmed. Nothing moves in the scene, so pressing it without a
selection brings back exactly what was there. The button shows how many pictures are in focus;
more than six are refused, and the tile says so. While a focus lies the table takes no gestures. A
picture's menu sets that one picture as the focus, and **Reset item** puts it back to the size,
angle and place a newly arriving picture would get. **Remove all pictures** in the screen menu
clears the screen and leaves the background — one step to undo.

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
