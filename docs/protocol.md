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
| `Hello` | display → control | device identifier, name, application version, protocol version, its screens — plus **either** a device token **or** a pairing code |
| `Welcome` | control → display | the control's identifier, the **path** assets are served from, and a freshly issued token exactly once |
| `PairingPending` | control → display | the pairing code, so the device can put its setup screen down |
| `Rejected` | control → display | why: `Denied` · `InvalidToken` · `LimitExceeded` · `DuplicateDevice` |
| `Ping` / `Pong` | control ↔ display | heartbeat, and the probe that tells a clone from a restart; the ping carries the last measured round trip |
| `SceneSnapshot` | control → display | the complete scene of one screen |
| `ScenePatch` | control → display | one command of the DM, as operations addressed at screens |

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
| a token **we do not know** | `Rejected(InvalidToken)`, and the device stays visible with that reason |
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
- **`InvalidToken` does not unbind anything by itself.** The beacon is unauthenticated, so a forged
  control answering every `Hello` this way would unbind every display in the house and could then
  adopt them. It takes a tap at the device — and that tap is the hurdle an attacker on the network
  cannot take.

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

The remaining thirteen operations arrive with the milestones that implement them. An operation
that serialises but does nothing in the reducer would look implemented while being a trap.

**Revisions are handed out by the hub alone.** That is what makes the order globally
unambiguous, and it is why a display sends an *intention* rather than a fact.

**`ZOrder` is per screen** and rises to the maximum in use plus one whenever something is
touched — including when an item is newly added. That is also why moving an item between screens
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

**Next free: 1046.** 1007–1009 stay unassigned so the first block could still grow, 1019 is left
free at the end of transport's, pairing has 1020–1037, discovery 1038–1041, the display's backoff
1042, and the send side 1043 onwards.

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

*(none yet — the ingest arrives in M2)*

**Next free: 2001.**

### 3000–3999 · Display

| Id | Name | Level |
|---|---|---|
| 3001 | `ScreenFound` | Information |
| 3002 | `NoScreens` | Warning |
| 3003 | `OverlayOpened` | Information |
| 3004 | `UnknownScreenDiscarded` | Warning |
| 3005 | `AssetFailed` | Warning |
| 3006 | `AssetDecoded` | Information |

**Next free: 3007.**

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

**Next free: 4008.**

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
