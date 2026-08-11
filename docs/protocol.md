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
| `Hello` | display → control | device identifier, name, application version, protocol version, its screens |
| `Welcome` | control → display | the control's identifier and the **path** assets are served from |
| `SceneSnapshot` | control → display | the complete scene of one screen |
| `ScenePatch` | control → display | one command of the DM, as operations addressed at screens |

`Welcome` carries a **path**, never an absolute URL and never host and port. Those come from the
socket the message arrived on. A remembered base URL is a trap: when the machine moves between
WLAN and a dock, the WebSocket finds the new address by itself while the URL still points at the
old one — the display would be *connected* and load nothing.

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

| Range | Subject |
|---|---|
| 1000–1999 | connection |
| 2000–2999 | assets |
| 3000–3999 | display |
| 4000–4999 | operations |

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

**Next free: 1015.** (1007–1009 are reserved for the hub so its block stays contiguous.)

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

**Next free: 4003.**

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
