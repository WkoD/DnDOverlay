# Protocol

What travels between the control and a display, and the rules both ends keep to. This document
grows with each milestone; today it covers what **M1a** put on the wire.

For the principles behind these decisions, see [design-principles.md](design-principles.md).

## Transport

Kestrel runs inside the control process and binds **all interfaces** on port **47800**. The
selection is made by the firewall rule anyway, and binding to "the right" address breaks the
moment the machine moves between WLAN and a dock.

Messages are JSON over a WebSocket, in an envelope carrying the type in a field named `t`:

```json
{ "t": "Hello", "deviceId": "…", "screens": [ … ] }
```

**There is no HTTPS.** Self-signed certificates on a home network produce warnings and nothing
else, and running a certificate authority for a games table is not proportionate. The protection
is LAN-bound, token-bound and device-bound instead — the token arrives in M1b.

| Endpoint | Purpose |
|---|---|
| `GET /ws/display` | WebSocket for display PCs |
| `GET /assets/{id}` | the bytes of one image |
| `GET /health` | reachability probe — answers `running` and nothing else |

`/health` gives nothing away on purpose. Versions, device lists and names belong behind the
token, or a diagnostic endpoint becomes a convenient reconnaissance source for anyone on the
network. The name is `/health` rather than `/healthz`: the `z` marks a Kubernetes namespace that
does not exist here, and the address is typed into a browser by a person standing at the table.

## Messages

| Message | Direction | Carries |
|---|---|---|
| `Hello` | display → control | device identifier, name, application version, protocol version, its screens — plus **either** a device token **or** a pairing code, plus the full effective parameter set and the scene it still has on each screen |
| `Welcome` | control → display | the control's identifier, the **path** assets are served from, and a freshly issued token exactly once |
| `PairingPending` | control → display | the pairing code, so the device can put its setup screen down |
| `Rejected` | control → display | why: `Denied` · `InvalidToken` · `LimitExceeded` · `DuplicateDevice` |
| `Ping` / `Pong` | control ↔ display | heartbeat, and the probe that tells a clone from a restart; the ping carries the last measured round trip |
| `SceneSnapshot` | control → display | the complete scene of one screen |
| `ScenePatch` | control → display | one command of the DM, as operations addressed at screens |
| `LogEntry` | display → control | one log message: identifier, name, level, the device's own timestamp, named values, optional raw text, optional screen |
| `ScreensChanged` | display → control | a new screen inventory after a hot-plug or a resolution change — facts only |
| `ConfigUpdate` | control **↔** display | changed display parameters as a **delta**; from the control additionally the screen wish and the transient finding |
| `IdentifyScreens` | control → display | nothing — every overlay of that device shows its own name, large, for a few seconds |
| `ItemTransformed` | display → control | one item's new place, size and angle as an **intention**, with the revision the display had when the hand took hold, and whether this is the first report of the gesture |
| `ItemParked` | display → control | a player swiped an item into the slot bar, or took one back out of it |

`IdentifyScreens` carries no payload on purpose: the device knows its own screens and what each of
them is called, and a list of names from the control would be a second copy that could disagree
with the first. It is **state rather than transient**, unlike the pulse it otherwise resembles —
transient exists to protect rank 1 while the table is busy, and this is pressed while a room is
being set up, when a press that silently does nothing would be worse than a late one. Screens
without an overlay show nothing: an inactive one was given back to Windows, and answering the
question there would break that promise.

`ItemTransformed` is **throttled per item** at about 20 Hz before it is queued, and sent once more
bindingly when the fingers leave. Per item rather than globally, or two pictures moved at once would
halve each other's reporting; before the queue rather than in it, because throttling is a decision
about how much detail a movement needs while dropping is an emergency measure — and in the queue the
binding final report would be the message most likely to be dropped.

`ItemParked` carries **no position**. Where a parked picture lies follows from the list of parked
pictures and the screen's park edge, and both ends work it out with the same function: sending
coordinates would leave a gap in the bar as soon as one picture left it, and a scene loaded onto
another screen would carry the first screen's edge with it.

`Welcome` carries a **path**, never an absolute URL and never host and port. Those come from the
socket the message arrived on. A remembered base URL is a trap: when the machine moves between
WLAN and a dock, the WebSocket finds the new address by itself while the URL still points at the
old one — the display would be *connected* and load nothing.

### Pairing

Four states per device — **unknown · waiting · paired · rejected** — and the way in is decided
entirely by what the `Hello` carries:

| `Hello` carries | Result |
|---|---|
| a **valid token** | in, without a question. The normal case at every power-on |
| **no token**, a pairing code | **waiting** — unless the device was rejected before, new devices are not being accepted, or a limit says no |
| a token **we do not know** | **waiting**, marked as having brought one — not a rejection (below) |
| a valid token whose **device is already connected** | the hub asks the connection it has and waits a second — silence replaces it, an answer makes it a clone |

Four things about this are load-bearing, and each of them is the answer to a failure that would
otherwise be unfixable at the table:

- **An open request has no deadline. It has a connection.** It stands as long as the socket stands
  and vanishes with it, so what is in the list is what is knocking *right now*. A deadline would
  have the opposite fault: the DM steps out, comes back, and the request is gone without anyone
  having decided anything.
- **The pairing code belongs to the request, not to the connection attempt.** The device makes it
  once while unpaired and keeps it across drops — otherwise the DM would be comparing a number
  that changed while he walked to the table.
- **A clone is laid in front of the DM, never turned away.** Cloning a disk is the usual way to set
  up a second display PC. He can take it on as its own device, which tells it to make itself a
  fresh identity and pair regularly; the control only says the identity collides, so the rule that
  every device creates its own stays intact.
- **A token we do not know is laid in front of the DM, not turned away** — for the same reason as
  the clone, and it took a hand run to see it. Rejecting it pointed at a way out that needs a hand
  at the device, and the case that produces it is a replaced `control.json`, which produces it on
  **every display at once**: machines that are flat on a table, in a cupboard, on a wall. The gate
  does not move — nobody gets in without the DM either way; what changed is whether he is offered
  the decision at all. A rejected device stays rejected, the "accept new devices" gate still holds,
  and a failed token still counts against the rate limit, so guessing is no cheaper than it was.

  **Two consequences follow, and both are easy to get wrong.** The pairing code now travels *with*
  a token as well — a request the DM cannot compare against the table would leave him allowing a
  device by its name, which is exactly what an impostor would supply. And the `Welcome` carries the
  new token whenever the device did **not** arrive with the valid one, rather than whenever it
  brought none: a device approved while holding a *stale* token would otherwise never learn the new
  one, come back with the stale one, and be laid in front of the DM again for ever.

**A rejection is waited out in minutes, not in seconds.** A refused device keeps knocking — it has
to, or "last seen 3 minutes ago" in the device list would mean nothing and taking a rejection back
would have nobody to reach. But it knocks about every five minutes, and that distance is its own:
it neither grows with repetition nor resets the backoff a real network fault is building up,
because what it waits for is a person changing their mind. Two rejections are told apart from it:
a **limit** that was reached is a state of the hub and passes on its own, so it takes the ordinary
growing wait, and a **collision** with a device that is already connected has the clone make itself
a fresh identity, which makes the next attempt a new question rather than a repetition.

**Tokens** come from `RandomNumberGenerator`, are compared with `CryptographicOperations.FixedTimeEquals`
and are stored encrypted at both ends. The **role** — display or control — sits in our own entry
and is never parsed out of the token: a display token at the control endpoint is refused, and the
other way round.

**The order when a device is allowed** is part of the promise: the control creates the token,
encrypts it, writes `control.json` — and only then calls `ApprovePairingAsync`. The `Welcome` is
sent from inside that call, so it cannot leave before the file exists.

### Finding each other

The control announces itself on **UDP 47800** every two seconds. The beacon carries exactly four
things — control identifier, display name, port, protocol version — and nothing else: it is
unauthenticated and readable by everyone on the network, so device lists, versions of paired
machines and screen names belong behind the pairing.

| | |
|---|---|
| **Sent to every suitable interface**, not to the first | Windows sorts by metric and a Hyper-V adapter likes to be at the top; a beacon that goes only there reaches nobody, and the display PC simply stays quiet |
| **To the subnet broadcast of each address**, never 255.255.255.255 | a limited broadcast leaves through whichever interface the routing table prefers — on a machine with a dock and Hyper-V that is the wrong one |
| **The loopback device is among them** | control and display on one machine are a regular setup, not a development mode |
| **The addresses are looked up every round** | the Surface changes them when it is docked; a socket bound at startup would keep shouting into a network that is no longer there |

**Virtual adapters are not sorted out**, although the plan asked for it. Telling a Hyper-V or VPN
adapter from a real one is guesswork — they present as Ethernet — and the two mistakes cost
differently: a beacon into a virtual subnet reaches nobody and costs one datagram, while wrongly
skipping a real one costs a device that never finds its control.

**The address a display connects to comes from the datagram, never from the beacon's contents.** A
control announcing its own idea of its address would announce the wrong one on every machine with
more than one interface — and that is precisely the machine this has to work on.

**Discovery stays active even when a host is configured.** The stored host is a *preferred*
address, not an exclusive one: it changes when the Surface moves between Wi-Fi and its dock. So a
configured host is tried first, an attempt that fails hands over to discovery, and a connection
that worked hands back.

**A paired display discards foreign beacons.** It belongs to *its* control — the address cannot
tell controls apart, and a second control in the same network is no invention.

**And that rule has a sharp edge, found in a hand run rather than reasoned out.** A control whose
`control.json` had to be replaced comes back with a **new identifier**, so its own displays treat
it as a stranger. They never send a `Hello`: no rejection, no question at the device, no entry in
any list. Both sides go quiet, and each of them is behaving correctly.

**The filter is not loosened, because loosening it *is* the attack it prevents.** The answer is to
stop producing the situation, and it comes in two halves:

- **The control keeps its identity.** A replaced `control.json` is set aside rather than deleted,
  and the `ControlId` is recovered from it — from a document that merely carries a newer
  `schemaVersion`, or out of the bytes of a damaged one. Then the displays find it again by
  themselves and arrive as pairing requests, because their tokens went with the file. Only the
  identity is recovered, never the content.
- **Where it is gone for good**, a display can still be called for — see *Wenn das Control nicht
  wiederkommt* in the plan: a control asks for orphaned devices, and a display answers only if it
  lost its own ungracefully and has been without one for longer than the rescue mark's deadline.
  Nothing happens without somebody pressing something.

Either way it is readable rather than silent: the display says the first sighting of each strange
control out loud (1048), and the control says at startup whether its identity survived (4010) or
not (4004) — two lines whose difference is a walk through the flat.

**Reconnecting waits one second, doubling to thirty, with spread.** The spread is not cosmetic:
after a control restarts, every display in the house lost its connection in the same moment, and
without it they would all knock again in the same instant. A connection that came up resets the
count, or a display that reconnects once an evening would take half a minute to recover from a
two-second hiccup by the end of the night.

### Addressing

Every operation carries a full `ScreenRef`, which is a device identifier plus a screen
identifier — **never a bare screen identifier**, not even on the wire.

The screen identifier is the Windows device instance path. It is unique per machine and no
further: two display PCs cloned from one disk image, with the same monitor on the same port, can
report literally the same value. Cloning a disk is the usual way a second display PC comes into
being, so this is the normal case rather than a curiosity. The device identifier in front of it
is a GUID each device makes up on its own first start, which rules the collision out by
construction.

The connection to a display does say which device is meant, so the device identifier looks
redundant there. It is not: the control endpoint (M8) carries the messages of *all* devices over
*one* connection, and the same message would need a different shape per endpoint.

### Screens: wish, finding, settings

Three different things are said about one screen, and keeping them apart is what makes the rest
work.

| | What it is | Who owns it | Where it lives |
|---|---|---|---|
| **Facts** — size, DPI, label | what the device reports | the device | `Hello` · `ScreensChanged` |
| **Wish** — one of five states | what the DM asked for | the **control**, always | `control.json` |
| **Finding** — unavailable · control window · hidden at the device | what is in the way right now | derived, never stored | transient |
| **Settings** — the display parameters | either side may set them | both, reconciled | `control.json` **and** `display.json` |

**A finding never overwrites the wish**, and that is not tidiness. A finding that did would have
to restore the wish afterwards — remember what held before, and keep that memory consistent
across crashes, restarts and simultaneous changes. That is exactly where such models come apart:
a screen is unplugged, somebody changes the wish meanwhile, and the wrong value wins when it
comes back. Leaving the wish untouched means there is **nothing to restore**.

A display therefore never reports how it *stands*, only how it is *set*. A `ConfigUpdate`
arriving from a device with a state in it is passed over and logged (3013).

**A display starts silent**: every screen is `Inactive`, no window is put anywhere, and no scene
is in memory. It stays that way until a control says otherwise. The reason is the autostart — a
display PC runs at every logon, not only on game nights, and coming up with the last state set
would make the application a trap: a frozen table nobody can explain, or an overlay on the
monitor the DM expressly gave back, permanently, because on an ordinary Tuesday no control is
running to correct it.

**Settings are a delta in both directions** — `null` means *unchanged*, never *cleared*. They
have to be, because the same value has two writers: every setting must be reachable at the device
as well, and four of them act in the **hub** rather than at the device (the two load values, the
placement mode and the park edge). A park edge changed at the table that the control knew nothing
about would mean the hub computing against the old value while the device renders against the
new — and the re-sorting the change should trigger not happening at all.

The reconciliation has an order, and it is the whole of it:

1. The `Hello` carries the device's **full effective set**. The control takes it over.
2. Only then does the control send what **it** changed while the device was away.

So per key the value set last holds, and nobody overruns something they never touched. Where both
sides changed the *same* key, the control wins — and that is the only thing it wins.

The counter-check that explains the sign of all this: a device that has never seen a control is
fully settable at the table and keeps its settings across restarts. Were it to lose them at the
first `ConfigUpdate`, local operation would be a sham.

**Beside the per-screen parameters there is a device-scope half** — what concerns the process
rather than one of its windows. It carries three things so far: the level the device *writes*,
what of it is worth the wire, and whether it **keeps its screens awake**. The last is held only
while a connection stands: a display PC shows a still picture for an hour at a time and Windows
cannot tell that from an idle machine, so the request is made when a control is there and dropped
the moment it is gone — which is the right answer for a table nobody is playing on any more.

### The scene is transient — and it is handed over

The arrangement is written down nowhere. It survives almost every failure anyway, because the
side that connects hands it to the side that lost it:

| | |
|---|---|
| display restarts | the control still has it and **puts it back** |
| control restarts | it **takes it over** from the displays that are connected |
| both at once | it is gone — that is what saving a scene is for |

The `Hello` therefore carries the scene of each screen. The hub takes it over **only for screens
it has no scene of its own for**; where it has one — because it has been running longer, or has
just loaded a layout — it puts that through with a snapshot instead. That is the one exception to
"the hub is authoritative", and it is kept exactly this narrow: bounded to the start.

The screen **states** are expressly not in the `Hello`. All five are born in the control.

### Send queues

In front of **every** socket sit three queues, and exactly one loop writes them. Three rather
than one, because "discard when full" is a property of the channel and not of the message: a
single queue cannot both drop transient traffic and never drop state. Three rather than two,
because the feedback *that* something is being transferred must not share a drawer with the touch
points — under load the first thing to fall away would otherwise be the very display that
explains the load.

| Queue | Carries | Capacity | When full |
|---|---|---|---|
| **state** | everything that has to arrive | 256 messages **and** 8 MB | the connection is treated as dead and closed |
| **progress** | `AssetProgress` (M2) | 1, replacing | overwritten — the newer reading is the right one |
| **transient** | `TouchPoints`, `Diagnostics`, `WindowList`, `SpotlightPulse` (M3, M5) | small | oldest dropped, without a word |

They are served in that order, so the precedence arises on its own: under sustained load the
touch points stop getting a turn while the progress still does, and nothing had to throttle
either of them explicitly.

Two of the three carry no message yet — the classes are declared and the queues are built
regardless, because there must be no moment in which a socket is written *without* these rules.

**A full state queue means neither drop nor wait.** It is a deterministic condition, not a time
window, and it says this connection can no longer be held consistent: it is closed, and the
ordinary reconnect with its `Hello` and `SceneSnapshot` puts the truth back. Waiting would spread
the slowest device's backlog across the whole hub. The ceiling is counted in messages **and** in
bytes, because one `SceneSnapshot` with twenty items weighs as much as a hundred small messages.

**One write may take ten seconds.** A counterpart that holds the connection open and accepts
nothing would otherwise only be noticed once the queue had filled — late, and after the memory
had already been spent. The limit cancels the send rather than merely giving up on the wait,
which aborts the socket; that is the intent.

The queues own the socket, so "exactly one writer" is a property of the construction rather than
a rule somebody has to keep — and it has to hold from the moment the socket is accepted, because
the pairing answers, the refusals and the heartbeat all go out while there is no device yet.

### The stream a surface reads

A surface never polls. `ISessionApi.Subscribe` hands it a stream of `SessionEvent`, and that union
is the definition of what a control can show at all: the control is a client of its own hub and
uses the path a foreign device would, so what is missing from the union is missing from the
screen.

| Event | Carries |
|---|---|
| `Opening` | the device tree, every scene, the waiting requests, the refusals |
| `DevicesChanged` | the device tree, whole |
| `PairingChanged` | who is knocking and who was turned away |
| `ScenePatched` | the same patch the displays got |
| `SceneReplaced` | a whole arrangement — the take-over out of a `Hello`, later a loaded scene |
| `Logged` | one line, ours or forwarded |

The undo labels arrive with the timeline, `AssetProgress` with M2, `TouchPoints` with M3, and
`Diagnostics`, `WindowList` and `WindowResult` with M5. Each is an added case, never a changed one.

**Every call gets a stream of its own.** With two control devices a shared one would have the
second taking the first one's events away.

**The first element is always a complete opening picture**, and that is not a convenience for the
caller. The hub is a hosted service and listens **before any surface stands**: a display PC on
autostart can connect, hand over its state and lodge a pairing request entirely before the first
subscription exists. Without the opening picture the surface would see none of it and would wait
for events that are long past. It is the same property `SceneSnapshot` has for a connecting
display, only for the event stream.

**Registering and photographing happen under two locks — the scene gate and the fan-out**, in the
order every command takes them. Taking the picture first would lose what happened in between;
registering first would deliver events the picture already contains. That is harmless for a whole
list and a real fault for a patch, because applying an `AddItem` twice makes two items — and the
fan-out's lock alone would not close it: a command writes its scene and *then* publishes its
patch, and a picture taken between the two contains the item and receives it again.

**The classes of the send queues apply here as well.** What was transient on the way from a device
stays transient on the way to a second control. A state event is never dropped: where a subscriber
cannot keep up, its stream is **ended** rather than served something stale, and subscribing again
yields a fresh opening picture — the same rule and the same way back as for a socket.

**The list-bearing events are whole lists, and they are relied on to be idempotent.** A device
leaving moves two sources — the connection list and the presence in the screen catalogue — so it
announces twice, and only the second carries the whole truth. A reader takes the latest and is
right. A delta per screen arrives when there is a surface that gains from it.

### Heartbeat

The control sends `Ping` every **5 seconds** and treats a connection as dead after **12 seconds**
of silence. The ping carries back the last round trip the control measured, so both sides show
the same latency instead of each working one out its own way.

**A deadline on silence rather than a count of unanswered pings**, and the difference is not
cosmetic: a device that is busy sending is alive whether or not a `Pong` happened to cross the
wire. Anything arriving counts.

**It runs while a pairing request is waiting, too.** An open request stands as long as its
connection stands — that is what keeps the device list showing only what is knocking right now.
Without a beat there, a display PC switched off mid-request would sit in that list for hours,
because TCP alone would not notice.

The same measurement answers the clone probe: a second connection with a valid token gets the
existing one asked, and it is its **answer** that decides. The two questions differ only in what
counts as an answer — the heartbeat takes anything, the probe wants a `Pong` to *its* `Ping`,
because it has to tell one connection from another.

### Log forwarding

Errors travel to where the DM sits; as little as possible stays on the display PC, which stands
unattended at the table where nobody looks into files. A display sends its entries over the
connection that is already there — no second channel and no second port.

**An entry is a stable identifier plus named values, never a finished sentence.** A device that
sent finished text would make what the control shows depend on the language setting of a foreign
machine. The control renders it in its own language, from the same catalogue, with the three
fallback stages behind it.

**Two timestamps, and the stream is sorted by the second one.** The entry carries the device's own
clock; the control notes when it arrived. An unattended machine without internet and with a flat
coin cell can be hours out, and sorting by its clock would scatter its lines through the nowhere
of the list instead of putting them next to ours.

**This is the only absolute foreign clock in the protocol, and it is measured the moment it
appears** — on the first entry a connection forwards, and only reported when it is more than a
minute out (`1046`). Everywhere else time is relative for exactly that reason: touch points carry
an age, the round trip is measured by the control that sent the ping, the heartbeat watches
silence. A wrong clock should produce a wrong answer about *itself*, never a plausible one about
the world.

**Which is why the notice is a convenience and not a promise, and it is worth saying why.** A
device that forwards nothing — the ordinary evening at the default level — is never measured, and
that is fine: a foreign clock that never crosses the wire cannot mislead anybody here. Where it
*does* cross, it is already visible without any notice at all, because every forwarded line in the
control's file carries the device stamp beside the arrival one. And nothing on a display runs on
the wall clock: the heartbeat, the backoff, the rate window and the log rotation are all monotonic
or size-based, so a machine that is two hours out behaves exactly like one that is not. What is
wrong is the reading of its own diagnostic file — and wrong by a **constant** amount within one
run, so durations and order inside that file still hold.

**What comes up while nothing can be sent goes out when the connection comes back.** The device
keeps a bounded ring buffer and a mark of what it has already forwarded; the mark outlives the
connection, the buffer says how much fell out of it. A pushed batch lands at the end of the
stream, in its own order — which is what "sorted by arrival" means and is worth knowing before
somebody reads it as a fault.

**The rate follows the level**: 20 entries a second at Information, 500 at Debug. The documented
way to look for a fault is to raise a display to `Debug` on purpose, so a fixed rate would bite
exactly when the DM asked for the flood. Over the limit, entries are dropped and it is said once
per second (`1047`) — refused and reported, never swallowed.

**The forwarding never logs itself**, or a line about forwarding would produce a line to forward.

**Raw text is defused on the way in, a second time.** An entry may carry an exception message, and
it was already cleaned where it was written — but that was on another machine. A device is trusted,
not infallible: line breaks and control characters are replaced before writing, or a crafted name
or exception message could write lines of its own into the DM's file.

### Compatibility

A differing protocol version **rejects nothing, in either direction**. An old display connects
to a new control and the other way round; the version is reported, not enforced.

The reason is concrete rather than principled: the control is the path along which a display
gets updated. Rejecting it would cut the one wire at the exact moment it is needed and leave the
DM walking to every display PC. It also solves nothing — a device that cannot keep up is mute
with rejection and mute *plus updatable* without it.

What has to carry that is additivity:

- unknown fields are passed over,
- unknown messages are **ignored and logged**, never fatal,
- identifiers are never reused for something else,
- new fields always come with a default.

### Closed deserialisation

Both discriminated bases — the message envelope and the scene item — resolve over a **fixed list
of permitted types** declared in attributes, never over a transmitted type name. The
source-generated serialisation context is not only an optimisation: there is no type resolution
at run time at all.

## Operations

A patch is what **one command of the DM** produces: one patch, with as many operations as that
one command needs. Independent commands are never merged, not even in quick succession — five
separately inserted images are five patches with five revisions and five steps in the undo
timeline.

| Operation | Since | Effect |
|---|---|---|
| `addItem` | M1a | puts a finished item on a screen |
| `transformItem` | M3a | where an item lies now — pushed, zoomed, turned |
| `setLocked` | M3a | locks one item against gestures at the table, or releases it |
| `parkItem` | M3a | lays an item into the slot bar along the park edge, or takes it out |

The remaining operations arrive with the milestones that implement them. An operation that
serialises but does nothing in the reducer would look implemented while being a trap.

**"Unlock all" is not an operation**: it is one `setLocked` per locked item, in one patch. Nor is
there one for parking positions — where a parked picture lies follows from the list of parked
pictures and the screen's park edge, and both ends work it out with the same function. Sending
coordinates would leave a gap in the bar as soon as one picture left it.

**The lock guards against the table, not against the DM.** A `transformItem` that came from a
display is refused for a locked item and logged (3021); the same operation from the control goes
through, or the DM would have to unlock before every correction.

**Revisions are handed out by the hub alone.** That is what makes the order globally
unambiguous, and it is why a display sends an *intention* rather than a fact.

**`ZOrder` is per screen** and rises to the maximum in use plus one whenever something is
touched — including when an item is newly added, and when one comes back out of the park bar. It
does **not** rise for a locked item, which cannot be taken hold of, nor while a gesture runs: the
first report of a gesture carries `grabbed`, and raising it twenty times a second afterwards would
run the number space up without anybody seeing a difference. That is also why moving an item between screens
gets it a fresh one: an item with `ZOrder` 3 arriving on a screen whose maximum is 47 would land
invisibly at the bottom.

## Asset identifiers

An `AssetId` is the SHA-256 of the **source** bytes, rendered as 64 lower-case hex digits.

It is validated before it touches anything resembling a path. Without that check,
`GET /assets/..%5C..%5Cwindows%5C…` is the classic way to read arbitrary files off the DM's
machine — with a valid token, so from any paired device.

Identity and integrity are two questions, so there are two hashes: the `AssetId` hashes what came
in, and `AssetMeta.ContentHash` hashes what goes out. Hashing the output for identity would break
deduplication twice over — image libraries write timestamps by default, and an encoder update
changes the bytes.

## Event identifier catalogue

A log message is born as a **stable numeric identifier plus named values**, never as a finished
sentence. It is translated when it is written or shown, in the language of whichever application
does it.

The number is the contract; the name is for reading. Numbers are grouped into ranges and are
strictly ascending within a range.

**A retired number is never reused.** Were 1002 to take on a new meaning, an older counterpart
would render a *plausible but wrong* line from its old catalogue entry — worse than an unknown
identifier, which at least looks unknown.

| Range | Subject | The question it answers |
|---|---|---|
| 1000–1999 | connection | *who is talking to whom, and does it still work?* |
| 2000–2999 | assets | *what happened to this image on its way in or out?* |
| 3000–3999 | display | *what is on a screen, and why is it or is it not?* |
| 4000–4999 | operations | *what is the process doing to itself?* |

The third column is there so the range of a new message is read off rather than argued about.
Where two of them seem to fit, the **subject of the sentence** decides: a failed asset download
is an asset (2000) even though it travelled over the connection; a display that drops a patch
for a screen it does not have is display (3000) even though the patch arrived over the wire;
a data root, a taken port and a shutdown are operations (4000) even though they are written by
the same application that draws.

### 1000–1999 · Connection

| Id | Name | Level | Where |
|---|---|---|---|
| 1001 | `DisplayConnected` | Information | hub |
| 1002 | `DisplayDisconnected` | Information | hub |
| 1003 | `DisplayWithoutHello` | Warning | hub |
| 1004 | `ProtocolVersionDiffers` | Information | hub |
| 1005 | `UnhandledMessageIgnored` | Debug | hub |
| 1006 | `SendFailed` | Information | hub |
| 1010 | `Connecting` | Information | transport |
| 1011 | `Connected` | Information | transport |
| 1012 | `Disconnected` | Information | transport |
| 1013 | `ConnectFailed` | Warning | transport |
| 1014 | `UnknownMessageIgnored` | Debug | transport |
| 1020 | `PairingRequested` | Information | hub |
| 1021 | `PairingApproved` | Information | hub |
| 1022 | `PairingDenied` | Information | hub |
| 1023 | `PairingWithdrawn` | Debug | hub |
| 1024 | `TokenRefused` | Warning | hub |
| 1025 | `NewDevicesBlocked` | Information | hub |
| 1026 | `LimitReached` | Warning | hub |
| 1027 | `CloneDetected` | Warning | hub |
| 1028 | `ConnectionReplaced` | Information | hub |
| 1029 | `FreshIdentityRequested` | Information | hub |
| 1030 | `Unpaired` | Information | hub |
| 1031 | `RejectionCleared` | Information | hub |
| 1032 | `MessageIgnored` | Debug | hub |
| 1033 | `PairingPending` | Information | display |
| 1034 | `Paired` | Information | display |
| 1035 | `TokenUnknown` | Warning | display |
| 1036 | `FreshIdentityTaken` | Warning | display |
| 1037 | `PairingRefused` | Warning | display |
| 1015 | `ListeningForControls` | Information | transport |
| 1016 | `ControlHeard` | Information | transport |
| 1017 | `ForeignControlIgnored` | Debug | transport |
| 1018 | `ListeningFailed` | Warning | transport |
| 1038 | `BeaconStarted` | Information | hub |
| 1039 | `BeaconStopped` | Information | hub |
| 1040 | `BeaconInterfaceFailed` | Debug | hub |
| 1041 | `BeaconReachedNobody` | Warning | hub |
| 1042 | `RetryingIn` | Information | display |
| 1043 | `StateQueueFull` | Warning | hub |
| 1044 | `WriteTimedOut` | Warning | hub |
| 1045 | `HeartbeatLost` | Information | hub |
| 1046 | `DeviceClockDiffers` | Warning | hub |
| 1047 | `LogRateExceeded` | Warning | hub |
| 1048 | `UnknownControlHeard` | Information | transport |
| 1049 | `ConnectionLoopFailed` | Error | display |
| 1050 | `BeaconTargetsChanged` | Information | hub |
| 1051 | `SendQueueFull` | Warning | transport |
| 1052 | `SendTimedOut` | Warning | transport |
| 1053 | `TouchRateExceeded` | Warning | hub |

**1049 is the line that exists because its absence was the fault.** The loop that looks for a
control runs fire-and-forget; a fault in it takes the reconnect with it and nothing else notices —
the windows stay, the scene stays, and the device never connects again, without one line to say
why. On a machine nobody is sitting at, a silence is the worst shape a fault can take.

**1050 is the answer to the one discovery promise that could not be read.** "Every suitable
interface, not the first" was, until a hand run asked for it, an assertion with nothing behind it:
the start line says the beacon runs, the failure line says one interface would not carry it, and
silence says at least one did — none of them says *where it went*. It is written only when the set
of targets changes, so it stands once at startup and then exactly at the interesting moments: a
dock, Wi-Fi going on or off, a VPN coming up.

**It is Information, and that took a second look.** Debug was the obvious answer — the beacon
repeats every two seconds, and a line per round would bury the file it is meant to explain. But
the change filter has already taken the repetition away, which is the same ground on which 1038
and 1039 are Information. And it has to be: **the control gates its own file at Information and
has no setting to lower it**, so a Debug line in the hub is one that can never be read. Measured
the hard way — the first cut was Debug, and the line simply was not there.

**1051 and 1052 are 1043 and 1044 at the other end of the wire**, and they exist because until
M3c there was nothing at that end to say them: the display wrote into one unbounded channel, which
cannot fill and therefore cannot report. Since the three send queues moved into Core and serve both
ends, a control that stops taking messages is answered the same way a device is — the connection
ends and the ordinary reconnect puts the truth back. **The third case, a socket that refuses a
write outright, gets no line here**: the receive side already reports the end, and two lines for
one event would be noise exactly when the log is being read.

**Next free: 1054.** 1007–1009 stay unassigned so the first block could still grow, 1019 is left
free at the end of transport's, pairing has 1020–1037, discovery 1038–1041, the display's backoff
1042, the send side 1043–1045 with its counterparts at 1051–1052, and log forwarding 1046–1047 with
the touch rate beside it at 1053.
**1050 belongs to discovery without adjoining it** — a number is never moved to keep a range tidy,
because the number is the contract and the tidiness is not.

**1017 and 1048 are the same finding at two levels, and the pair exists because of a dead end.**
A display discards the beacons of any control it is not bound to — that is the rule that keeps a
forged beacon from unbinding it. But a control whose `control.json` was replaced comes back with a
**new `ControlId`**, so its own displays discard it too: no `Hello` is ever sent, the remedy below
(*a token that is valid nowhere*) is never reached, and the control lists no device at all. 1048
says the first sighting of each strange control out loud so that this is readable rather than
silent; the repeats fall back to 1017 at Debug, which is what keeps a household with two controls
from writing a line every two seconds.

**1006 names an address rather than a device**, and that is not carelessness: the send loop exists
from the moment the socket is accepted, so at that point there may be no device yet. The same
holds for 1043–1045.

**Two levels here are deliberate and they are opposites.** `BeaconInterfaceFailed` is Debug: on a
machine with VPN or Hyper-V adapters one interface refusing is the ordinary state of affairs, and a
warning every two seconds would cry wolf. `BeaconReachedNobody` is a Warning, because it means no
display will ever find this control by itself.

**1033–1037 are written by the display application, and they are still connection.** The range
follows the **subject of the sentence**, never the assembly it is written in — the same rule that
decides where a new message goes (above). Pairing seen from the device answers *who is talking to
whom*, so it belongs beside the hub's lines and not in the display range.

**1020 is written once per pairing code, never once per connection.** An unpaired device on weak
Wi-Fi loses its connection and comes back every few seconds; because the code survives that, a
second `Hello` refreshes the standing request instead of opening another. What the DM looks for
later is not a notification but the trail: *did this device ever knock, and what came of it?*

### 2000–2999 · Assets

| Id | Name | Level |
|---|---|---|
| 2001 | `AssetTakenIn` | Information |
| 2002 | `AssetRefused` | Information |
| 2003 | `IntakeFinished` | Information |

**Next free: 2004.**

**2001 carries a duration, and that is the point of it.** The range stood empty through M2a and
M2b, and a hand run showed what that cost: twelve pictures were taken into a campaign and the
control's log held not one word about any of them, so *"it takes a few seconds and I do not know
why"* could not be answered from the trail — only by measuring afterwards. Measured with the real
files: a 24 MB PNG at 4616×6000 costs **11.6 s** to normalise and 1.1 s for its thumbnail, a 2 MB
JPEG costs 1 ms. The JPEG path hands the bytes through, the PNG path decodes and re-encodes, and
from the outside the two are indistinguishable without the number.

**2002 is Information and not Warning.** A refused picture is the hardening working — the DM is
told at the panel and the trail says the same. A warning would put a correct refusal on the same
footing as a fault, and the file is read for faults.

**2003 is the line that answers a question 2001 cannot.** Two hundred pictures dropped in one go
write two hundred `2001` lines, and reading them tells you about each picture and nothing about the
run: how long the whole thing took, how many were already there, whether it was broken off. It is
written once per run — including for a run of one, because a single paste is the same path and a
line that only appears above some threshold is a line nobody can rely on.

### 3000–3999 · Display

| Id | Name | Level |
|---|---|---|
| 3001 | `ScreenFound` | Information |
| 3002 | `NoScreens` | Warning |
| 3003 | `OverlayOpened` | Information |
| 3004 | `UnknownScreenDiscarded` | Warning |
| 3005 | `AssetFailed` | Warning |
| 3006 | `AssetDecoded` | Information |
| 3007 | `ScreenAdded` | Information |
| 3008 | `ScreenMissing` | Information |
| 3009 | `ScreenMetricsChanged` | Warning |
| 3010 | `ScreenStateChanged` | Information |
| 3011 | `ScreenSuppressed` | Information |
| 3012 | `ScreenAvailable` | Information |
| 3013 | `ScreenCommandIgnored` | Warning |
| 3014 | `SceneTakenOver` | Information |
| 3015 | `OverlayClosed` | Information |
| 3016 | `ScreensReported` | Information |
| 3017 | `SettingsApplied` | Information |
| 3018 | `ScreensIdentified` | Information |
| 3019 | `WakeLockChanged` | Information |
| 3020 | `AssetsLoaded` | Information |
| 3021 | `LockedItemNotMoved` | Information |
| 3022 | `ForeignScreenRefused` | Warning |
| 3023 | `FrameTimes` | Information |
| 3024 | `FrameBudgetMissed` | Warning |
| 3025 | `PictureSharpened` | Debug |
| 3026 | `RenderPath` | Information |
| 3027 | `SurfaceMeasured` | Information |
| 3028 | `GestureConfirmed` | Information |
| 3029 | `SessionPulse` | Information |
| 3030 | `GestureCorrected` | Warning |
| 3031 | `TouchReporting` | Information |

**Next free: 3032.**

**3031 exists because nothing else about the switch can be seen.** At the table it changes
nothing at all — that is the promise (Part 4) — so without a line "the DM sees no fingers" would
have two causes that look identical: switched off, or not arriving.

3019 sits in the display range although what it changes is a process-wide flag — the subject of
the sentence is whether a screen stays lit, and that is what decides the range. Both directions
are worth the same line, and the second one more than the first: from the room, a device that was
*told* to let go looks exactly like one that failed to hold on.

Of these, 3007–3014 and 3021–3022 are written by the **hub** and 3015–3019 and 3023–3031 by the
**display** — the range
follows the subject of the sentence, never the assembly it is written in. Only one of the three
inventory findings is a warning, and that is the point of telling them apart: a missing screen
loses nothing, a new one is a fact, and a screen whose **metrics changed** has had its images
recomputed — with undo not reaching them, because transformations are not in the timeline.

### 4000–4999 · Operations

What a process does to itself: where it stores things, which port it took, that it is going
away. The fourth range exists because a data root is none of the three above — and putting it
into one of them would have made the range names stop meaning anything.

| Id | Name | Level | Where |
|---|---|---|---|
| 4001 | `DataRootChosen` | Information | control |
| 4002 | `DataRootChosen` | Information | display |
| 4003 | `ConfigurationCreated` | Information | control |
| 4004 | `ConfigurationReplaced` | Warning | control |
| 4005 | `ConfigurationCreated` | Information | display |
| 4006 | `ConfigurationReplaced` | Warning | display |
| 4007 | `KnownDevicesRestored` | Information | control |
| 4008 | `LogFileFailed` | Error | both |
| 4009 | `PortTaken` | Error | control |
| 4010 | `IdentityRecovered` | Warning | control |
| 4011 | `UnhandledFault` | Critical | control |
| 4012 | `UnhandledFault` | Critical | display |

**Next free: 4013.**

**4011 and 4012 catch nothing** — the fault still ends the run. What they prevent is a run that ends
*mutely*: measured, a control went away with exit code -1 while its log stopped mid-sentence, and a
hand run that had just found a real fault could say nothing about it beyond "it was gone". Three
doors are watched, because a fault leaves through whichever one it started behind: the UI thread,
any other thread, and a task nobody awaited. The last is the quietest — since .NET 4.5 it does not
end the process at all, so without a line it is invisible for ever.

**4004 and 4010 are the two halves of the same event**, and the difference between them is a walk
through the flat: with the identity recovered the displays find this control again by themselves,
without it they treat it as a stranger and never knock. Both are warnings — the file is gone
either way — but only one of them ends in something to do at every device.

**4008 is the one message built by hand**, without a `[LoggerMessage]`, and it has to be: it says
that the log file gave up, so routing it through `ILogger` would return straight into the sink
that is failing. It goes into the ring buffer alone, and from there in front of the DM — a failed
write must be visible, because from then on every further line is lost.

Both applications say the same thing and still carry their own number: the identifier is the
contract, and a shared one would make a line in the file ambiguous about who wrote it.

### Falling back, never falling over

Showing a message steps down three times and never fails:

1. translated text in the language of whoever is showing it,
2. the neutral English text from the catalogue,
3. **the event name and its values in plain text.**

Step 3 is the point. An unknown identifier must *never* produce "unknown event" or an empty
line — mixed versions are exactly when the message is needed most, and a name plus arguments is
enough to work with.
