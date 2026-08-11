# Architecture

This document records decisions that outlive the code that implements them. It grows with each
milestone; today it covers what milestone M0 settled about **rendering**.

For the principles these decisions answer to, see
[design-principles.md](design-principles.md).

## The display overlay

Each screen that is played on carries **one full-screen window**: borderless, always on top,
per-pixel transparent, and invisible to hit testing where nothing is drawn. Input that lands on
empty space reaches whatever application runs underneath; input that lands on an image is ours.

Two questions decided this shape, and both were measured before anything was built on top of
them. The measurements live in the milestone checklist; the conclusions live here.

### Pass-through: `Background = null` is enough

Three ways to let input through were prepared and all three were measured against a live
application underneath:

| | Approach |
|---|---|
| **A** | hit-test-free surfaces — `Background = null` rather than `Transparent` |
| B | `WM_NCHITTEST` answered with `HTTRANSPARENT` via an `HwndSource` hook |
| C | `WS_EX_TRANSPARENT` toggled dynamically |

**A carries, for mouse and for touch alike**, and it needs no interop at all. B and C were
confirmed to work for the mouse and are kept only as documented reserve; nothing depends on
them.

Two properties were verified rather than assumed, because both are load-bearing:

- The overlay **stays on top** across foreground changes, without re-asserting topmost.
- **Two fingers on two different images produce two independent manipulations.** Several people
  working at the same table at the same time is the normal case, not an edge case.

### Rendering: the overlay is hardware-composed

The plan this project grew out of assumed that a transparent, always-on-top window falls back
to software rendering, and it was deliberately frugal because of it. **That assumption does not
hold** — at least not on capable hardware.

The evidence is a controlled comparison, not an inference: the same scenarios were run twice,
once with the default render mode and once with `RenderOptions.ProcessRenderMode` forced to
`SoftwareOnly`.

| Load, on a 1080p screen | default | forced software |
|---|--:|--:|
| 10 images, continuous manipulation | 16.7 ms · 5 % CPU | **33.3 ms · 84 %** |
| 20 images plus a running animation | 16.7 ms · 7 % CPU | **74.9 ms · 101 %** |
| 20 images, continuous zoom | 16.7 ms · 7 % CPU | **277 ms** |
| 40 images decoded at full resolution, zooming | 16.7 ms · 6 % CPU | **542 ms** |

`RenderCapability.Tier` does **not** answer this question — it describes the hardware, not
whether a particular layered window benefits from it. Only the forced comparison does.

Two things follow, and the second matters more than the first:

- **Rendering is not the constraint.** Eighty images decoded at full source resolution still
  hold the display's full refresh rate at 8 % of one core.
- **Software rendering is the failure case, and it is now quantified.** A display machine
  without usable graphics, a remote session, or a driver that refuses acceleration lands in the
  right-hand column, where ten images already miss the budget. The frugal limits below are kept
  for exactly that case — and every device reports its own frame time, because that is the only
  way to notice the failure case instead of leaving it to someone at the table to describe it
  as "the screen is stuttering".

**Budget.** Median frame time at or below the display's frame interval plus one millisecond
(≤ 17.7 ms at 60 Hz), 95th percentile ≤ 33 ms, no stall above 100 ms — **and the CPU share
alongside it**, because at a locked refresh rate the frame time alone cannot show how much room
is left. 16.7 ms at 5 % and 16.7 ms at 95 % look identical and are not.

**How this is handled at runtime — one path, no adaptation:**

- **The render mode is never queried and never branched on.** There is no "if accelerated,
  then differently". It cannot be determined reliably, it can change between two starts, and
  two paths would mean two test matrices with one half nobody ever exercises.
- **The limits below apply unchanged on every device.** They are sized for the weakest display
  machine and cost a fast one nothing measurable — for 2K–4K sources stepped decoding is
  exactly as expensive as full, and thirty items is more than ever lies on a table.
- **Nothing adapts while running.** Tuning limits to a measured frame time would make the same
  table behave differently on two evenings, with nobody able to explain why. The only
  device-dependent number is the memory ceiling, and it derives from a fixed property (a
  quarter of physical RAM), not from a fluctuating measurement.
- **The question asked is never "is this accelerated?" but "does it hold the frame budget?"** —
  an accelerated but weak device fails just as a software-rendered one does, and a
  software-rendered device with five images may well be fine.
- **`RenderCapability.Tier` is used only as a negative test.** Tier 0 means software for
  certain and is worth a line at startup. Any higher value proves nothing — Tier 2 was measured
  while rendering was forced to software.
- **A device that misses the budget says so.** Sustained median frame time above the budget
  produces one warning per session and screen, carrying both numbers, forwarded like any other
  log entry; it repeats only if things get measurably worse. That is the only way the failure
  case surfaces as data rather than as somebody at the table saying the screen stutters.

If a driver ever misbehaves with layered windows, Windows already provides the escape hatch
(`HKCU\Software\Microsoft\Avalon.Graphics\DisableHWAcceleration`). It belongs in
troubleshooting documentation, not in a setting of ours.

### One window per screen, not one per image

A layered window per image was measured as a fallback in case the full-screen window proved too
slow. It is not needed, and it is worse in every respect: moving *n* layered windows costs a
full frame of UI-thread time **independent of n**, while CPU grows with it. Two failure modes
sit in the obvious implementation — resizing a layered window every frame exhausts the
process's quota and terminates it, and a per-frame handler that consumes the frame budget
starves every dispatcher continuation below render priority.

The second lesson generalises beyond this experiment: **per-frame work must not fill the frame
budget**, or unrelated parts of the application stop making progress without anything appearing
to fail.

### Memory is the real constraint, so images are decoded in steps

Since rendering is free and memory is not, the decode strategy is decided by memory alone.
Twenty 6000×4000 images, three strategies, measured as working set:

| Strategy | Memory |
|---|--:|
| full source resolution | 1912 MB |
| fixed cap at twice the screen edge | 832 MB |
| **stepped, starting at the longer screen edge** | **269 MB** |

**Stepped wins, but only for large sources.** For 2K–4K sources — the common case — full and
stepped decoding cost the same, so the strategy earns its keep on scanned maps and panoramas
and nowhere else.

An image whose rendered size outgrows its step is re-decoded one step up, capped at the source
resolution; it never steps back down except under memory pressure. **The transition is not
perceptible**: 21 ms from crossing a step to the sharp image, 36 ms at worst.

### Limits

| | Value | Derived from |
|---|---|---|
| Process memory ceiling | 1.5 GB committed, additionally capped at ¼ of physical RAM | 20 images occupy 866 MB committed; a 1 GB ceiling would trigger in normal use |
| Threshold for downscaling parked images | 75 % of the ceiling | a share, not a fixed number — so the promise "never at 1080p, reliably at 4K" survives a different ceiling |
| Items per screen (display side) | 30 at 1080p, scaling with resolution | 34 MB per image against the budget |
| Simultaneous animations | 8 | eight running animations cost 3 % CPU and no frame time |
| Parallel decodes and downloads | 3 | frame time is unaffected even at six, but six draw 449 % CPU — on a two-core machine that is the whole machine |

Note on the first row: the ceiling measures **committed** memory, which runs at roughly twice
the working set. A ceiling that watches the working set reacts too late.

One assumption behind these numbers was checked and turned out to be wrong: garbage collection
was expected to stall rendering, because a collection suspends managed threads. It does not —
**WPF's render thread is native and is not suspended.** Six parallel full-resolution decodes
produced 522 gen-2 collections in twelve seconds without a single frame above 18.6 ms. The
limit on parallelism stands, but it stands on CPU cost rather than on collection pauses.

### What these numbers do not cover

They were taken on one machine. The display machines this project is frugal for — a small
box driving a projector, a tablet at the table — are unmeasured, and for them the software
column above remains a possibility rather than a curiosity. Every device therefore measures and
reports its own frame time.
