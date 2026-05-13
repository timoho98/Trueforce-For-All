// Reads AC's outgoing HID++ FFB target from the USB bus by spawning USBPcapCMD
// as a child process, parsing its pcap stdout, and latching the most-recent
// FFB target value for the Trueforce stream to inject into ep3 bytes 6-9.
//
// AC sends DirectInput-equivalent FFB to the wheel as HID Set_Output_Reports
// on ep0 (control endpoint). The actual force command is HID++ feature 0x0e
// function 2 long-form messages, signed 16-bit big-endian at offset 10-11
// of the HID++ payload. When we stream Trueforce on ep3, the wheel uses
// bytes 6-9 of our packet as motor torque, ignoring AC's ep0 commands. By
// mirroring AC's commands into bytes 6-9, FFB and Trueforce coexist.
//
// USBPcap installs as a kernel-mode USB filter driver. USBPcapCMD.exe streams
// pcap to stdout when invoked with -o -. We don't require admin in our
// process — USBPcap's own access checks happen in its CMD process.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TrueforceForAll.Core
{
    public sealed class UsbPcapFfbTap : IDisposable
    {
        // Default install path. We probe Program Files and Program Files (x86)
        // and a couple of common alternates.
        private static readonly string[] CandidatePaths = new[]
        {
            @"C:\Program Files\USBPcap\USBPcapCMD.exe",
            @"C:\Program Files (x86)\USBPcap\USBPcapCMD.exe",
        };

        private const int DLT_USBPCAP = 249;

        // _packed layout: low 16 bits = ffbTarget bit-pattern, high 48 bits =
        // Stopwatch.ElapsedTicks masked to 48 bits. Masking on store + logical
        // (unsigned) right shift on read + modular subtraction on age makes
        // the freshness check wrap-safe — without the mask, a left-shift by
        // 16 lands in the sign bit at ~162 days of QPC uptime and silently
        // breaks FFB pass-through until reboot.
        private const long TimestampMask = 0x0000_FFFF_FFFF_FFFFL;

        // How long the reader loop sleeps between auto-discovery retries when
        // the wheel can't be found. Long enough to not spam logs, short enough
        // that a replug or re-elevation feels responsive.
        private const int RediscoveryRetryMs = 15000;

        // Resolved interface + device address. Either supplied via the manual
        // override constructor args, or filled in by discovery inside the
        // reader loop. Reset to null/0 by ClearDiscovered() when the user
        // explicitly clears the manual override and wants auto-discovery again.
        private string _usbPcapInterface;
        private int _deviceAddress;

        // Set when the caller explicitly passed an interface+address (manual
        // picker or env-var override). When true, the reader loop skips
        // discovery and never re-runs it. When false, the loop will retry
        // discovery on failure.
        private readonly bool _manualOverride;

        // Optional VID/PID of the wheel that HID enumeration already found.
        // Plumbed to WheelUsbDiscovery so it can log the smoking-gun
        // "HID saw it, USBPcap didn't" line when discovery fails.
        private ushort? _hidFoundVid;
        private ushort? _hidFoundPid;

        private readonly string _usbPcapCmdPath;

        private Process _proc;
        private Thread _readerThread;
        private volatile bool _stopping;

        // Most-recent FFB target (signed int16) and the Stopwatch timestamp at
        // which it was captured. Read from any thread; written only by the
        // reader thread. We use a single int64 field with packed value+timestamp
        // so reads are torn-tear-safe under Volatile semantics.
        // Layout: low 16 bits = signed int16 (cast to ushort for storage),
        //         high 48 bits = stopwatch ticks (truncated, monotonic).
        private long _packed;

        // Stopwatch tick (masked to 48 bits) of the last successfully parsed
        // FFB sample. Used by HasRecentPackets / GetLastSampleAgeMs so the UI
        // can distinguish "process is alive" from "process is alive AND
        // actually receiving FFB data". Read from any thread; written only
        // by the reader thread.
        private long _lastSampleTicks;

        private static readonly Stopwatch _sw = Stopwatch.StartNew();

        // Status surfaced to the UI / logs. Populated by the reader thread.
        public string Status { get; private set; } = "Stopped";
        public bool IsRunning => _proc != null && !_proc.HasExited;
        public long PacketsParsed { get; private set; }
        public long FfbSamplesCaptured { get; private set; }

        // True when this tap was constructed with an explicit (interface,
        // address) override. The UI uses this to decide whether to show the
        // "clear manual override" affordance.
        public bool IsManualOverride => _manualOverride;
        public string CurrentInterface => _usbPcapInterface;
        public int CurrentDeviceAddress => _deviceAddress;

        // Milliseconds since the last FFB sample was latched, or long.MaxValue
        // if we've never latched one. Used by the UI to detect the "process
        // running but no data flowing" state.
        public long MsSinceLastSample
        {
            get
            {
                long last = Interlocked.Read(ref _lastSampleTicks);
                if (last == 0) return long.MaxValue;
                long now = _sw.ElapsedTicks & TimestampMask;
                long ageTicks = (now - last) & TimestampMask;
                return ageTicks * 1000L / Stopwatch.Frequency;
            }
        }

        // Optional logger (e.g., SimHub.Logging.Current.Info). Avoids a hard
        // dependency on log4net from this library.
        public Action<string> Logger { get; set; }

        // Pass null/0 (the defaults) to auto-discover via WheelUsbDiscovery on
        // Start(). Pass explicit values only when overriding (env vars,
        // manual picker, tests).
        // usbPcapCmdPathOverride: absolute path to USBPcapCMD.exe; checked
        // first before the env var / default-path probe. Used by the
        // settings-panel Browse action when USBPcap is installed somewhere
        // off the beaten path.
        public UsbPcapFfbTap(string usbPcapInterface = null, int deviceAddress = 0, string usbPcapCmdPathOverride = null)
        {
            _usbPcapInterface = usbPcapInterface;
            _deviceAddress = deviceAddress;
            _manualOverride = !string.IsNullOrEmpty(usbPcapInterface) && deviceAddress > 0;
            _usbPcapCmdPath = LocateUsbPcapCmd(usbPcapCmdPathOverride);
        }

        // Tell discovery the VID/PID the HID stack already enumerated. Surfaces
        // the "HID found it, USBPcap didn't" log line on auto-discovery failure
        // so a bug-report log makes the bisection obvious.
        public void SetHidDiscoveredWheel(ushort vid, ushort pid)
        {
            _hidFoundVid = vid;
            _hidFoundPid = pid;
        }

        // Public so the settings UI can validate a user-picked path with the
        // same probe order the constructor uses.
        public static string LocateUsbPcapCmd(string pathOverride = null)
        {
            if (!string.IsNullOrEmpty(pathOverride) && File.Exists(pathOverride)) return pathOverride;
            string fromEnv = Environment.GetEnvironmentVariable("USBPCAPCMD");
            if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv)) return fromEnv;
            foreach (var p in CandidatePaths)
                if (File.Exists(p)) return p;
            return null;
        }

        public bool Start()
        {
            if (_usbPcapCmdPath == null)
            {
                Status = "USBPcap not installed (FFB pass-through disabled)";
                Log(Status);
                return false;
            }
            if (_readerThread != null) return true;

            _stopping = false;
            _readerThread = new Thread(ReaderLoop)
            {
                IsBackground = true,
                Name = "UsbPcapFfbTap",
                Priority = ThreadPriority.AboveNormal,
            };
            _readerThread.Start();
            return true;
        }

        public void Stop()
        {
            _stopping = true;
            try { _proc?.Kill(); } catch { }
            try { _readerThread?.Join(2000); } catch { }
            _readerThread = null;
            _proc = null;
            Status = "Stopped";
        }

        public void Dispose() => Stop();

        // Returns the latest FFB target if it's no older than maxAgeMs, else null.
        public short? TryGetFreshFfbTarget(int maxAgeMs)
        {
            long packed = System.Threading.Interlocked.Read(ref _packed);
            if (packed == 0) return null;

            short value     = (short)(packed & 0xffff);
            long  timestamp = (long)((ulong)packed >> 16);

            long now = _sw.ElapsedTicks & TimestampMask;
            long ageTicks = (now - timestamp) & TimestampMask;
            long maxAgeTicks = (Stopwatch.Frequency / 1000L) * maxAgeMs;
            if (ageTicks > maxAgeTicks) return null;

            return value;
        }

        // ---------- reader thread ----------

        private void ReaderLoop()
        {
            // Outer loop owns BOTH discovery (when there's no manual override)
            // and capture. Splitting them here means a stale-cache failure on
            // first try can be recovered by replugging the wheel mid-session
            // without restarting SimHub.
            while (!_stopping)
            {
                try
                {
                    if (!_manualOverride && (string.IsNullOrEmpty(_usbPcapInterface) || _deviceAddress <= 0))
                    {
                        Status = "Discovering wheel on USB bus...";
                        Log(Status);
                        var hit = WheelUsbDiscovery.Find(_usbPcapCmdPath, Logger, hidFoundVid: _hidFoundVid, hidFoundPid: _hidFoundPid);
                        if (hit == null)
                        {
                            Status = "No supported wheel found on any USBPcap interface (FFB pass-through disabled). Retrying in 15s...";
                            Log(Status);
                            if (SleepInterruptible(RediscoveryRetryMs)) break;
                            continue;
                        }
                        _usbPcapInterface = hit.Interface;
                        _deviceAddress = hit.DeviceAddress;
                        Log($"Auto-discovered: {hit}");
                    }

                    StartUsbPcapCmd();
                    ParseStream();
                }
                catch (Exception ex)
                {
                    Status = $"Error: {ex.Message}";
                    Log($"UsbPcapFfbTap: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    try { _proc?.Kill(); } catch { }
                    _proc = null;
                }

                if (_stopping) break;
                // Brief cooldown so we don't spin if the install is broken.
                // After a process exit we also clear the discovered interface
                // so the next iteration re-runs discovery (catches replugs).
                if (!_manualOverride)
                {
                    _usbPcapInterface = null;
                    _deviceAddress = 0;
                }
                Thread.Sleep(2000);
            }
        }

        // Sleep that returns true if interrupted by Stop() request.
        private bool SleepInterruptible(int ms)
        {
            int slept = 0;
            while (slept < ms)
            {
                if (_stopping) return true;
                int chunk = Math.Min(200, ms - slept);
                Thread.Sleep(chunk);
                slept += chunk;
            }
            return false;
        }

        private void StartUsbPcapCmd()
        {
            var psi = new ProcessStartInfo
            {
                FileName = _usbPcapCmdPath,
                Arguments = $"-d {_usbPcapInterface} -o - --devices {_deviceAddress}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            _proc = Process.Start(psi);
            if (_proc == null) throw new InvalidOperationException("Process.Start returned null");
            Status = $"Tapping {_usbPcapInterface} dev {_deviceAddress}";
            Log($"UsbPcapFfbTap started: {_usbPcapInterface} dev {_deviceAddress}");

            // Drain stderr so it doesn't fill its pipe buffer and stall the child.
            new Thread(() =>
            {
                try
                {
                    string line;
                    while ((line = _proc.StandardError.ReadLine()) != null)
                        Log($"[USBPcapCMD] {line}");
                }
                catch { }
            }) { IsBackground = true, Name = "UsbPcapFfbTap-stderr" }.Start();
        }

        private void ParseStream()
        {
            var s = _proc.StandardOutput.BaseStream;

            // ---- pcap global header (24 bytes, LE) ----
            byte[] gh = ReadExact(s, 24);
            uint magic    = BitConverter.ToUInt32(gh, 0);
            int  linkType = BitConverter.ToInt32(gh, 20);
            if (magic != 0xa1b2c3d4 || linkType != DLT_USBPCAP)
                throw new InvalidDataException($"Not a USBPcap stream (magic=0x{magic:x8}, linktype={linkType})");

            byte[] payload = new byte[1024];

            while (!_stopping)
            {
                byte[] rh = ReadExact(s, 16);
                int caplen = BitConverter.ToInt32(rh, 8);
                if (caplen <= 0 || caplen > 65535)
                    throw new InvalidDataException($"caplen={caplen}");
                if (payload.Length < caplen) payload = new byte[caplen];
                ReadExactInto(s, payload, 0, caplen);
                PacketsParsed++;

                // ---- USBPcap pseudo-header ----
                if (caplen < 27) continue;
                int headerLen = BitConverter.ToUInt16(payload, 0);
                if (headerLen < 27 || headerLen > caplen) continue;

                int dev      = BitConverter.ToUInt16(payload, 19);
                byte ep      = payload[21];
                byte xfer    = payload[22];
                if (dev != _deviceAddress) continue;
                if (xfer != 0x02) continue;             // control transfer
                if ((ep & 0x7f) != 0x00) continue;       // ep0
                if (headerLen < 28) continue;
                byte stage = payload[27];
                if (stage != 0) continue;                // setup stage only

                int setupOffset = headerLen;
                if (setupOffset + 8 > caplen) continue;
                byte bmRequestType = payload[setupOffset + 0];
                byte bRequest      = payload[setupOffset + 1];
                if (bmRequestType != 0x21 || bRequest != 0x09) continue; // HID Set_Report

                int dataOffset = setupOffset + 8;
                int dataLen = caplen - dataOffset;
                if (dataLen < 12) continue;

                // HID++ payload: [reportID][devIdx][featIdx][funcByte][params...]
                byte reportId = payload[dataOffset + 0];
                byte featIdx  = payload[dataOffset + 2];
                byte funcByte = payload[dataOffset + 3];

                // AC's FFB: feature 0x0e long form, function 2 (high nibble of funcByte).
                // FFB target = signed int16, big-endian, at offset 10-11 of the HID++ payload.
                if (reportId == 0x11 && featIdx == 0x0e && (funcByte & 0xf0) == 0x20)
                {
                    short ffbTarget = (short)((payload[dataOffset + 10] << 8) | payload[dataOffset + 11]);
                    long timestamp = _sw.ElapsedTicks & TimestampMask;
                    long packed = (timestamp << 16) | (uint)(ushort)ffbTarget;
                    System.Threading.Interlocked.Exchange(ref _packed, packed);
                    System.Threading.Interlocked.Exchange(ref _lastSampleTicks, timestamp);
                    FfbSamplesCaptured++;
                }
            }
        }

        private static byte[] ReadExact(Stream s, int n)
        {
            byte[] buf = new byte[n];
            ReadExactInto(s, buf, 0, n);
            return buf;
        }

        private static void ReadExactInto(Stream s, byte[] buf, int offset, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = s.Read(buf, offset + got, n - got);
                if (r <= 0) throw new EndOfStreamException();
                got += r;
            }
        }

        private void Log(string msg)
        {
            var l = Logger;
            if (l != null) try { l(msg); } catch { }
        }
    }
}
