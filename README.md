# SimHub Trueforce

Drive real Logitech Trueforce haptics on G PRO / RS50 direct-drive wheels for
any SimHub-supported game, via the documented USB protocol — no Logitech SDK
DLLs, no G HUB integration required.

**Status: Phase 1 — HID hello-world.** A standalone console app that opens the
wheel, runs the Trueforce init sequence, and streams a sine wave. Once this
verifies the wheel actually vibrates on command, the same code becomes the
backbone of the SimHub plugin (Phase 2).

## Hardware support

Derived from [mescon/logitech-rs50-linux-driver](https://github.com/mescon/logitech-rs50-linux-driver)'s
verified protocol coverage:

| Wheel | USB ID |
|---|---|
| Logitech G PRO Racing Wheel (Xbox/PC) | 046D:C272 |
| Logitech G PRO Racing Wheel (PS/PC) | 046D:C268 |
| Logitech RS50 | 046D:C276 |

The G PRO and RS50 use byte-for-byte identical Trueforce packets (verified by
mescon against fresh G HUB captures).

## Requirements

- Windows 10 / 11
- .NET Framework 4.8 SDK + MSBuild (Visual Studio 2022 or `dotnet` SDK)
- Logitech G HUB **closed** while running (it claims the HID interface)

## Build

```powershell
cd SimHubTrueforce
dotnet build src\SineTest\SineTest.csproj -c Release
```

Output: `src\SineTest\bin\x64\Release\net48\sinetest.exe`

## Phase 1 smoke test

> **Safety:** a direct-drive wheel can produce significant torque. The wheel
> may rotate during the Trueforce init sequence. **Hold the wheel or clamp it
> down before running.** The program prints a 5-second countdown.

```powershell
.\sinetest.exe                      # 50 Hz, 2 s, amplitude 0.3
.\sinetest.exe 80 3 0.4             # 80 Hz, 3 s, amplitude 0.4
```

Args: `<freq_hz> <duration_s> <amplitude_0_to_1>`.

If the wheel buzzes during the streaming phase, Phase 1 passes and we move on
to Phase 2 (SimHub plugin scaffold).

## License

GPL-2.0-only. See [LICENSE](LICENSE). The wire protocol used here was
reverse-engineered by the mescon project from USB captures; the canned
68-packet init sequence and the streaming packet layout are derived from
their work, which is GPL-2.0.

## Acknowledgments

- [mescon/logitech-rs50-linux-driver](https://github.com/mescon/logitech-rs50-linux-driver)
  — protocol documentation and reference C implementation.
- [HidSharp](https://github.com/treehopper-electronics/HIDSharp) — cross-platform
  HID library.
