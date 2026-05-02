# SimHub Trueforce

Drive real Logitech Trueforce haptics on G PRO / RS50 direct-drive wheels for
any SimHub-supported game, via the documented USB protocol — no Logitech SDK
DLLs, no G HUB integration required.

**Status: Phase 1 — HID hello-world.** A standalone WPF GUI that opens the
wheel, runs the Trueforce init sequence, and streams a user-controllable
waveform (sine / square / saw / triangle / noise) with live frequency and
amplitude sliders, plus a logarithmic frequency sweep. Once this verifies
the wheel responds correctly, the same code becomes the backbone of the
SimHub plugin (Phase 2).

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

To run the prebuilt `src/SineTest/sinetest.exe`:
- Windows 10 / 11
- [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) installed
- Logitech G HUB **closed** while running (it claims the HID interface)

To build from source:
- The above, plus the .NET SDK (10.x) and `EnableWindowsTargeting=true` if
  cross-compiling from macOS / Linux.

## Run the prebuilt exe

> **Safety:** a direct-drive wheel can produce significant torque. The wheel
> may rotate during the Trueforce init sequence. **Hold the wheel or clamp it
> down before clicking Start.**

Just double-click `src/SineTest/sinetest.exe`. The window opens, finds the
wheel, and lets you drive it with sliders.

## Build from source

```powershell
cd SimHubTrueforce
dotnet publish src\SineTest\SineTest.csproj -c Release
```

Published exe: `src\SineTest\bin\Release\net10.0-windows\win-x64\publish\sinetest.exe`

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
