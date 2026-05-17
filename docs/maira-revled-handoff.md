# G PRO rev-LED handoff for MAIRA

**Audience:** the MAIRA developer. **Goal:** drive the Logitech G PRO
(and likely RS50) rim rev LEDs from MAIRA so iRacing users who run with
in-game Trueforce off (and TF4ALL tunnelling FFB into the Trueforce
endpoint) get their RPM lights back.

Everything here was reverse-engineered from USB captures and verified on
a G PRO (pid `0xC272`). Logitech publishes none of it. Re-verify against
your own captures before depending on byte layouts.

## TL;DR

- The G PRO rev LEDs are **level-based** over HID++ page `0x807A`: you
  send a single 0..10 "how many LEDs" value. The wheel's onboard profile
  owns colour / direction / scaling. There is no per-LED RGB.
- **Only the owner of the wheel's HID++ FFB write loop can drive the
  LEDs without wrecking FFB.** That is MAIRA, not TF4ALL. Measured: an
  LED command issued by a *second, independent* process stalls the
  wheel's servicing of PID FFB (`0x8123`) for a median of **1.5 s** per
  write (84% FFB dead-time in testing). A *single* owner that emits the
  LED command from inside its FFB loop has zero contention , this is
  exactly what G HUB does natively and it works perfectly on this wheel.
- So: emit the LED pair **from MAIRA's existing FFB output loop**
  (sequentially, between FFB writes). Do **not** drive it from a
  separate LED thread, that recreates the two-writer stall inside your
  own process.

## Why MAIRA and not TF4ALL

The wheel has one HID++ command processor. PID FFB (`0x8123`) and rev
LEDs (`0x807A`) both go through it. TF4ALL is a separate process from
MAIRA's FFB writer; when it issues an LED command, the OS HID stack and
the wheel serialize it against MAIRA's ~320 Hz `0x8123` stream
pathologically (~1.5 s exclusion per LED write , not tunable; cadence,
batching, rate limiting were all tried and all failed in the same way).

A single writer avoids this by never letting an LED command overlap an
in-flight FFB command. Capture proof: with G HUB as sole owner,
`0x807A` LED + `0x8123` FFB coexist on the same pipe at full FFB rate,
no stalls. MAIRA owns the `0x8123` writer, so MAIRA can interleave the
LED pair into that loop the same way. TF4ALL cannot, by construction.

(The ep3 Trueforce endpoint is a physically separate resource and is
*not* involved in this contention. FFB-via-ep3 tunnelling is unaffected
either way; the LED problem is entirely on the HID++ control pipe.)

## Transport (Windows)

The wheel's HID++ interface is exposed by Windows as **three HID
collections by report size**:

| maxOutputReportLength | HID++ report | report ID |
|---|---|---|
| 7  | SHORT     | `0x10` |
| 20 | LONG      | `0x11` |
| 64 | VERY_LONG | `0x12` |

A report ID is only valid on its own collection, and a request's reply
comes back on a different collection than you wrote to. Open all three
(skip the `MI_02` Trueforce-audio interface and the input-only
gamepad). Writes to the G PRO go out as ep0 control SET_REPORT
(`bmRequestType 0x21`, `bRequest 0x09`); there is no interrupt OUT
endpoint.

## Feature index resolution (do this once)

Resolve page `0x807A` to a runtime feature index via HID++ root
(`0x0000`) getFeature. It is **not** fixed , this G PRO resolved to
`0x09`; other wheels/firmware differ.

Request (SHORT, on the 7-byte collection):

```
10 FF 00 0B 80 7A 00
   |  |  |  |  \__\__ page 0x807A
   |  |  |  fn0 | sw-id 0x0B
   |  |  root feature index (0x00)
   |  device index 0xFF (wired)
   report ID 0x10
```

Reply arrives on the LONG or VERY_LONG collection; the feature index is
at byte offset 4 of the HID++ message (`12 FF 00 .. <IDX> ..`). Call the
resolved value `IDX` below.

## The rev-LED protocol (page 0x807A, sw-id nibble 0x0D)

Function byte = `(fn << 4) | 0x0D`. So fn0=`0x0D`, fn1=`0x1D`,
fn2=`0x2D`, fn3=`0x3D`, fn6=`0x6D`.

**Arm once** (SHORT reports; G HUB spaces these a few ms apart):

```
10 FF IDX 0D 00 00 00     fn0
10 FF IDX 1D 00 00 00     fn1
10 FF IDX 2D 00 00 00     fn2
10 FF IDX 3D 02 00 00     fn3, param 0x02
10 FF IDX 0D 00 00 00     fn0
```

**Per update** , the level pair (G HUB sends this ~every 156 ms, even
when the level is unchanged; the wheel reverts if never refreshed):

```
10 FF IDX 2D 00 00 00                                  SHORT fn2
11 FF IDX 6D 00 01 00 0A 00 LL 00 00 00 00 00 00 00 00 00 00   LONG fn6
```

`LL` = byte index 9 of the LONG report = **rev level 0..10** = number of
rim LEDs lit. `0` = all off. Byte 7 = `0x0A` appears to be the LED-count
scale max.

**Emit this pair from inside the FFB loop**, e.g. once per ~150 ms slot:
finish the current `0x8123` FFB write, then send the SHORT, then the
LONG, then continue the FFB loop. Sequential, single-threaded, no
overlap. That is the whole trick.

To turn the LEDs off: send the pair with `LL = 0` and stop refreshing.

## RPM -> level mapping (so the lights match iRacing)

This is what MAIRA already has good data for, but our findings from the
iRacing telemetry, in case useful:

- iRacing's `CarSettings_RPMShiftLight1/2` come through SimHub as
  *boolean* stage flags that flicker 0/1 every sample near threshold ,
  do **not** drive the level off them directly or it oscillates.
- `CurrentDisplayedRPMPercent` is just `rpm/maxRpm` , lights far too
  early (≈ idle).
- What worked: a smooth band from `RedLineRPM`. First LED at
  ≈ `0.87 * RedLineRPM`, all 10 at `RedLineRPM`, linear, monotonic:
  `level = round( clamp01( (rpm - 0.87*red) / (0.13*red) ) * 10 )`.
  On the test car (red 7125) first light landed ~6150 rpm, matching
  where iRacing's own first shift light engaged. `0.87` is an
  approximation; MAIRA likely has the true per-car shift RPMs from the
  iRacing SDK (`DriverCarSLFirstRPM` / `SLShiftRPM` / `SLBlinkRPM`),
  which would be exact , prefer those if available.
- Redline/blink: drive `LL = 10` at/after the shift RPM.

## Reference

- Decoded LED protocol + transport: `WheelLedChannel.cs` (TF4ALL repo,
  `rpm-leds-iracing` branch) , a working single-owner C# implementation
  you can read for exact framing.
- Signal-path model (three streams, two shared resources):
  `docs/wheel-signal-paths.md`.
- Captures + decoder: `gpro_leds.pcap` (G HUB native, working) and
  `gpro_ffb_withleds.pcap` (two-writer contention) with `gpro_leds.py`.
