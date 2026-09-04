# Design principles

> This document is the yardstick. Everything else is execution and may change; if a later
> decision collides with this document, either the decision is wrong — or we change the
> principle here, deliberately and in writing. The two must never drift apart silently.

## What it is for

A game master shows their group images — NPCs, handouts, scenery — on screens everybody can
see: a touch screen in the middle of the table, plus a projector or TV depending on the
setup. They drive all of it from their own device, without standing up, and without the
players seeing what is being prepared.

Five sentences everything is measured against:

1. **The DM shows an image, and it is there.** Seconds, no file management, from any source —
   file, clipboard, browser, URL.
2. **The players may touch — and cannot break anything.** Move, rotate, scale, park at the
   edge to make room, and fetch back later; several at once, without explanation. No image
   can disappear, become too small to find again, or slide off the screen. That is not a side
   condition but the precondition for handing the table over at all.
3. **Whatever runs underneath stays usable.** We are a layer *above* it, not a replacement for
   it. Wherever we show nothing, the application below must react to mouse and finger as if we
   were absent — whichever one the group happens to use.
4. **Nobody at the table *has* to operate the application.** The display machine starts by
   itself, connects by itself and is configured from the DM's side; in normal operation one
   only ever sees images on it. It is nevertheless fully operable locally, through tray and
   options window — a machine that can only be rescued remotely is a trap.
5. **Preparing is possible without anything flashing up.** What the DM assembles is seen only
   by them, until they show it. And if they do show it too early, one grip takes it back.

If any of these five gets worse because of an implementation decision, the decision is wrong,
however elegant it may otherwise be.

## The defining constraint: something else lies underneath

On the table's touch screen a map application runs and does that well — which one is the
group's business, and we deliberately assume nothing about it. DnDOverlay is the layer *above*
it for everything such an application does not do: NPC images, handouts, scenery, hints. This
is not a side condition; it determines the entire technology:

- The overlay must be **per-pixel transparent and always-on-top**, and mouse **and touch** on
  free areas must pass through to whatever lies below. That is the hardest technical
  requirement of the project and the only point at which it can fail — which is why it is
  spike zero. Everything else, up to and including the UI framework, is subordinate to it.
- **Fog of war and grid/range measurement are out, with no replacement.**
- The background layer inevitably covers the application below completely. Accepted: it exists
  for scenes without a map and is one toggle away.

**The requirement is deliberately phrased about the layer, not about a program.** A promise
that one named application keeps working would be both narrower and weaker than the one we
actually make: on free areas we are not there at all. That is testable against anything.

## Decided ground rules

| Topic | Decision |
|---|---|
| Stack | C# / .NET (LTS) + **WPF** |
| Player view | Transparent always-on-top overlay per monitor, **input on free areas passes through** |
| State | Shared scene state, synchronised both ways, authoritative in the hub |
| Topology | **The DM machine is the hub** — display machines connect to it |
| Reach | Several display machines, 1..n screens each, not all with touch, all Windows |
| Discovery | UDP auto-discovery plus manual host entry |
| DM device | **A Windows tablet — the interface is fully operable by touch** |
| Delivery | **Per-user MSI, no admin rights**, autostart option; Control self-contained, Display framework dependent |
| Updates | Self-check against GitHub releases — **reported automatically, installed by hand** |
| Branch | `master` |
| Licence | **Apache-2.0** |
| Language | Code, comments, `docs/`, README **English**; interface **English and German**, English as fallback |
| Repository | **public**, `master` protected, contributions through fork and pull request |

**Terms.** **Control** = the DM machine (listens, holds the state). **Display** = a screen
machine (connects outwards, renders). **Screen** = one monitor of a display; each is an
independent target. **Hub** = the HTTP/WebSocket service *inside* the Control process — not a
separate application, not a service. **Campaign** = the stored holdings of one group across
many evenings. **Inventory** = the visible index of those holdings. **Session** = one game
evening, that is, the runtime — everything that ends with the process.

The last two must not be confused: the campaign is what remains, the session is what is
transient.

## The load-bearing ideas

Nine decisions the rest follows from. They are not implementation details but answers to
questions one answers only once.

**1. A transparent overlay, not a display of our own** — with pass-through as the topmost
technical requirement. Rendering approach, gesture layer and, if it comes to it, the UI
framework are subordinate to it, not the other way round.

**2. The state is authoritative in exactly one place: the hub, inside the Control process.**
Displays send intentions and render; they hold no truth.

> **The hub owns the arrangement and nothing else.** No material, no files, no view — the
> holdings belong to the campaign, the interface to the Control.
>
> **It starts nothing of its own accord.** Every domain action comes from outside.

It is not a mere relay, though, and that is the most common misreading. It decides everything
that follows *necessarily* from an instruction: what may only be issued once (`Revision`,
`ItemId`, `ZOrder`), what requires reading and writing in one breath (placement of a new
image, edge limiting, width capping), and what a display is not allowed to do itself (enforce
the lock, guard limits, refuse a connection).

**An instruction need not be an intention — a statement of fact is one too.** A screen
inventory change asks for nothing, it merely reports how the monitors now stand — and the hub
recomputes edge limiting and capping from it. Whoever reports a fact also reports its
consequences; they simply do not compute them themselves. **Two sides drawing the same
conclusion separately will eventually draw it differently.**

**3. Arrangements are saved by the DM — the holdings write themselves.** The dividing line
runs between what somebody *arranged* and what is simply *there*. Scenes and layouts are saved
explicitly. Configuration and the campaign's holdings are written automatically. **The
inventory is therefore not a document but an index**: a list that does not match the folder is
not an "unsaved state", it is wrong.

**4. The state survives in whatever is still running.** If a display restarts, the Control
restores its table state; if the Control restarts, it adopts the state from the connected
displays. If both die at once it is gone — and that, and only that, is what saving is for.

**5. The players move, the DM manages.** At the table there are gestures and nothing else.
For that to hold, **every gesture must be reversible by the players themselves**: parking is a
feature, there is a minimum size, and nothing slides out of view.

**6. Every action of the DM can be taken back — and redone.** Control-wide, across the whole
session, one timeline across all screens. What is recorded is what brings an image onto a
screen, takes it off, or moves it between screens — **not** the nudging into place. An entry
is the counter-move, not the previous state.

**7. Images travel separately from the state.** Assets are content-addressed (SHA-256) and
fetched over HTTP; messages carry only the identifier. **An image that is needed goes over the
wire once per device.** Nothing is left behind on a display machine beyond the session.

**8. Coordinates are normalised, not in pixels.** Almost everything pleasant follows: moving
between screens is a re-hang of the identifier, the thumbnail in the Control is the same
mapping with different metrics, view rotation is a matrix on top, and a saved scene fits any
screen.

**9. The protection level is LAN-bound, token-based and device-coupled — deliberately no
HTTPS.** These are images for a game table, not secrets. **Numbers go over the wire, not
screen contents.**

## What DnDOverlay is not

A yardstick needs the opposite direction, otherwise slow drift goes unnoticed. None of the
following is excluded because it would be bad — they are excluded because they would make a
**different product**:

- **No replacement for a virtual tabletop.** No maps, no fog of war, no grid, no range
  measurement, no rule-bearing tokens.
- **No image editor.** Cropping, filters and retouching belong to the tool the image came
  from. We accept, normalise and show.
- **No service, no account, no cloud.** Nothing leaves the LAN, with a single exception: the
  update check against GitHub.
- **No user management.** There is exactly one operator. Devices are paired, not people.
- **No remote administration.** The DM may *place* foreign windows — move, resize, minimise,
  restore — because they set programs up for the group. **Closing, starting, clicking and
  typing are expressly not included.**
- **No catalogue beyond the campaign.** No tags, no nested collections, no ratings, no search
  across campaigns.
- **No video, no sound.** Moving images yes, playback with a timeline no.
- **No handouts to individual players**, and **no HEIC/HEIF** (patent situation).

## Order of precedence

When compute time, bandwidth or render time run short, the higher rank suffers first. Decided
up front, because otherwise the accident of the implementation decides, differently each time:

| Rank | What |
|---|---|
| **1** | **Interaction at the table** — the players' gestures and their immediate local effect |
| **2** | **Getting new images onto the displays** |
| **3** | **Feedback that something is happening** — transfer running, image has arrived |
| **4** | **Currency of the thumbnail and of the touch points** |

Two things follow that the order does not show. **Rank 1 must be defended against local
disturbances, not against network load** — gestures run locally anyway; what is dangerous is
GC pauses, CPU contention with the render thread, and work that inevitably lands on the UI
thread. And **rank 3 sits above rank 4**, which is why progress display is decoupled from
thumbnail rendering.

## Consumption and performance must be findable

Whoever decides in advance what suffers under load must also show *that* something is
suffering — otherwise a deliberately throttled thumbnail looks like a fault. The principle is
**findable, not permanently visible**: every quantity has one known place and announces itself
only when it crosses a threshold.

For every new feature that costs space or compute time the question applies: **where does the
DM see this?** An answer of "nowhere" is a reason to stop.

## The ten rules

**1. The Control is a client of its own hub.** The hub offers one command interface plus an
event stream; the own interface calls it in-process. **There is exactly one way into the
state**, and the mobile one (later) uses the same one that is under the DM's fingers daily.
"Into the state" is literal — the holdings are not included; they live on disk, are never
negotiated, and belong to the Control alone.

**2. A single reducer, in `Core`.** `Apply(SceneState, PatchOp, ScreenContext)` — a pure
function, used by hub **and** display. The reducer sees exactly one scene; everything created
arrives ready-made from the hub in the patch; the screen's computation context is passed in.

**3. Only the hub issues `Revision`.** The display sends intentions, not states.

**4. One owner for the state, one serialised entrance.** The scene store is mutated only from
a loop over a channel. No locks, immutable records, UI marshals via the dispatcher. **Nothing
long-running inside that loop** — ingest, decoding, file IO and downloads run outside it.

**5. Both applications run on `Microsoft.Extensions.Hosting`.** DI, configuration, logging and
lifetime from one hand. Everything comes out of the container — which is what keeps the five
libraries instantiable, and therefore testable, without WPF.

**6. Everything persisted carries a `schemaVersion`** with a read path for older states.
Exactly two numbers, not one per file: one for the configuration, one for the campaign.

**7. The protocol is additive — and identifiers are never reused.** New optional fields and
message types do not change the version; unknown fields are skipped, unknown messages ignored
and logged. **This rule has no safety net beneath it, and that is deliberate**: a differing
protocol version never refuses a connection, because that would cut the very wire over which
a device is updated.

**8. Foreign dependencies sit behind interfaces of our own.** **Foreign does not only mean
"somebody else's" but also "the operating system's".** A platform API that ends up in one of
the platform-neutral libraries is the same case with the same answer — and it needs the rule
*more* than a library does, because it compiles without complaint and only fails at run time.

**9. Shared geometry.** `Core` provides `ItemToRect(item, screenContext)`; display and Control
thumbnail render **with the same function**. The promise "thumbnail and table show the same
thing" therefore holds structurally rather than by test.

**10. Test seams from the start.** `TimeProvider` and all storage paths are passed in, never
hard wired — so that the checklist stays automatable.

## The interface must work on any surface

The Control does not run on a known screen but on very different ones, and it **moves between
them while running**. Seven rules that therefore apply everywhere:

1. **Layout follows available area, not device.** Wrapping, in device-independent units,
   re-laid out **live** while the window is dragged. No query for a particular device, a fixed
   resolution or a named setup, anywhere.
2. **No function may be reachable only above a certain size.** What is a button on a wide
   surface becomes a menu entry on a narrow one — but it does not vanish.
3. **What the user set themselves is never rearranged by the application.** The available area
   determines only the initial value at the very first start.
4. **Two kinds of view state, two storage places.** What the user **deliberately arranged** is
   remembered globally; what merely describes **how they are sitting right now** is remembered
   per monitor arrangement.
5. **Per-monitor V2 DPI, in both applications.**
6. **Finger and mouse at the same time, not either/or.** Hit areas ≥ 40 DIP, no function only
   via hover or right-click, and a counterpart for every gesture.
7. **Dark appearance by default.** Games are played in dimmed light; a white glowing screen
   dazzles the DM and everyone next to them. An operating condition, not a matter of taste.

**Colour and icon deliberately do not appear here.** Both are right or wrong depending on
where they are used, and a rule about them would force the wrong choice in at least one place.
That is decided **at each display individually, with its own reasoning**.

## Robustness in operation

An application that runs during a game session must not stand there with an error dialog in
front of the players.

- **Global exception handling**: log, keep running. A fault on one image must never take the
  overlay with it.
- **Losing the connection does not change what is displayed.** The display keeps what it
  shows and reconnects in the background.
- **Atomic writes** for all files: side file, then replace. If that fails — full disk, locked
  file — it is reported **visibly**, not merely logged; from then on every further change
  would be lost.
- **Virtualise lists.**

## Check questions

To be applied at every milestone and at every larger decision. A "no" is not a prohibition,
but a reason to stop and either change the decision or change this document:

1. Is the application underneath still operable on free areas with **mouse and finger**?
2. Does a gesture at the table stay fluid while loading and transferring happen in the
   background?
3. Does an image arrive **within seconds** from trigger to visible, from each of the four
   sources?
4. Does the display machine still run **without being operated** — and is it nevertheless
   fully operable locally if it cannot get through at all?
5. Can the DM prepare **without anything flashing up at the table**?
6. Is the **arrangement** still transient — is nobody writing it down in secret? (The image
   material expressly is not, idea 3.)
7. Can every grip of the DM be **taken back** — an image shown too early as much as a cleared
   table, without a confirmation dialog stopping the flow of play? And does that leave what
   the players arranged at the table untouched?
8. Can the players undo everything they can cause **with the same gestures**, without calling
   the DM? And is every restriction that takes this from them **deliberately set and
   recognisable at the table**?
9. Do the rules still exist **exactly once** — one reducer, one geometry, one command
   interface? And does every thing still sit with its owner: **the arrangement in the hub, the
   material in the campaign, the view in the Control**?
10. Would a stranger holding only the README get from the MSI and one approval to a running
    display machine?
