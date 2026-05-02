// Phase 1 hello-world: open the wheel, run the Trueforce init sequence,
// stream a sine wave, verify vibration.
//
// Mirrors mescon/logitech-rs50-linux-driver:
//   userspace/libtrueforce/tests/sine.c
//
// SAFETY: a direct-drive wheel can produce significant torque and may rotate
// during the init sequence. HOLD THE WHEEL or clamp it down before running.

using System;
using System.Threading;

namespace SimHubTrueforce.SineTest
{
    internal static class Program
    {
        private const double SampleRateHz = 1000.0;

        private static int Main(string[] args)
        {
            double freqHz   = ParseArg(args, 0, 50.0);
            double duration = ParseArg(args, 1, 2.0);
            double amp      = ParseArg(args, 2, 0.3);

            Console.WriteLine($"sine: freq={freqHz} Hz, duration={duration} s, amp={amp:0.00}");

            var matches = WheelDiscovery.FindAll();
            if (matches.Count == 0)
            {
                Console.Error.WriteLine("No supported Logitech direct-drive wheel found.");
                Console.Error.WriteLine("Looked for VID 0x046D, PIDs: 0xC272, 0xC268, 0xC276 (interface 2).");
                Console.Error.WriteLine("If your wheel is plugged in, ensure G HUB is closed and try again.");
                return 1;
            }

            var match = matches[0];
            Console.WriteLine($"Found: {match.Model} (VID 0x{match.Vid:X4}, PID 0x{match.Pid:X4})");
            Console.WriteLine($"Path:  {match.Device.DevicePath}");

            // Countdown so the user can brace.
            for (int s = 5; s >= 1; s--)
            {
                Console.WriteLine($"Hold the wheel — starting in {s}...");
                Thread.Sleep(1000);
            }

            using (var dev = new TrueforceDevice(match.Device))
            {
                Console.Write("Opening device... ");
                try
                {
                    dev.Open();
                    Console.WriteLine("ok");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAILED");
                    Console.Error.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
                    Console.Error.WriteLine("  Likely causes: G HUB has the device open, or insufficient HID access.");
                    return 2;
                }

                Console.Write("Sending init sequence (68 packets x 2 passes)... ");
                try
                {
                    dev.RunInitSequence();
                    Console.WriteLine("ok");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAILED");
                    Console.Error.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
                    return 3;
                }

                Console.Write("Starting stream thread... ");
                dev.StartStream();
                Console.WriteLine("ok");

                Console.WriteLine($"Streaming {freqHz} Hz sine for {duration:0.0} s...");
                int total = (int)(duration * SampleRateHz);
                const int batch = 64;  // 64 ms of samples at a time
                float[] buf = new float[batch];
                double phase = 0.0;
                double step = 2.0 * Math.PI * freqHz / SampleRateHz;

                for (int i = 0; i < total; i += batch)
                {
                    int n = Math.Min(batch, total - i);
                    for (int j = 0; j < n; j++)
                    {
                        buf[j] = (float)(amp * Math.Sin(phase));
                        phase += step;
                    }
                    dev.PushFloats(buf, n);
                }

                // Let the streaming thread drain the ring before tearing down.
                int drainMs = (int)(total * 1000.0 / SampleRateHz) + 200;
                Thread.Sleep(drainMs);

                Console.Write("Stopping stream... ");
                dev.StopStream();
                Console.WriteLine("ok");
            }

            Console.WriteLine("Done.");
            return 0;
        }

        private static double ParseArg(string[] args, int index, double fallback)
        {
            if (args == null || index >= args.Length) return fallback;
            return double.TryParse(args[index], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }
    }
}
