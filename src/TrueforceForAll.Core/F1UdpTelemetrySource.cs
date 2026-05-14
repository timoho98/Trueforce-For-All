// F1 25 UDP telemetry source. Listens for the binary packets the EA F1
// games emit when the user enables UDP Telemetry in Settings → Telemetry
// Settings. Targets the F1 25 wire format (PacketFormat=2025); other
// formats are received but skipped with a one-time log line so users
// running F1 24 or earlier see why their packets aren't being parsed.
//
// The F1 protocol splits telemetry across multiple packet types. We only
// consume four of the sixteen:
//   id 0  Motion          - g-forces (heave/sway)
//   id 6  CarTelemetry    - throttle, gear, RPM, DRS, surface type per wheel
//   id 7  CarStatus       - MaxRPM, PitLimiter, ERS deploy mode (KERS), TC level
//   id 13 MotionEx        - per-wheel slip ratio, body angular velocity
//
// One frame is emitted per CarTelemetry packet; values from the other
// three are cached and overlaid. F1 sends every packet type at the
// configured Send Rate, so emission rate matches the user's setting.
//
// All structs are pack=1 little-endian. Header is 29 bytes; player-car
// index is at byte 27 of the header. Per-car arrays are 22 cars deep.
//
// Authoritative offsets cross-checked against the EA Forums F1 25 UDP
// spec and MacManley's F1 25 parser headers (github.com/MacManley/f1-25-udp).

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TrueforceForAll.Core
{
    public sealed class F1UdpTelemetrySource : TelemetrySourceBase
    {
        public override string Name => "F1 (UDP)";
        public override bool   IsEnhanced => true;
        public override bool   IsRunning  => _running != 0;
        // F1 cars are V6 turbo hybrid; cylinder count is fixed at 6 across
        // every car. Reported every frame so the engine pulse effect locks
        // its firing frequency to the right divisor.
        public override bool   ProvidesNumCylinders => true;

        // ---- Header offsets (29 bytes total) ----
        private const int HDR_PACKET_FORMAT      = 0;      // u16
        private const int HDR_PACKET_ID          = 6;      // u8
        private const int HDR_PLAYER_CAR_INDEX   = 27;     // u8
        private const int HEADER_SIZE            = 29;

        // ---- Packet IDs we consume ----
        private const byte PID_MOTION         = 0;
        private const byte PID_CAR_TELEMETRY  = 6;
        private const byte PID_CAR_STATUS     = 7;
        private const byte PID_MOTION_EX      = 13;

        // ---- Per-car offsets (relative to start of the per-car struct) ----
        // CarMotionData is 60 bytes per car.
        private const int MOT_GFORCE_LATERAL      = 36;        // float, lateral g
        private const int MOT_GFORCE_LONGITUDINAL = 40;        // float, longitudinal g (forward = positive)
        private const int MOT_GFORCE_VERTICAL     = 44;        // float, vertical g
        private const int MOT_YAW_ANGLE       = 48;        // float, radians (not rate)
        private const int CAR_MOTION_SIZE     = 60;

        // CarTelemetryData is 60 bytes per car.
        private const int TEL_SPEED           = 0;         // u16, km/h
        private const int TEL_THROTTLE        = 2;         // float, 0..1
        private const int TEL_GEAR            = 15;        // i8
        private const int TEL_RPM             = 16;        // u16
        private const int TEL_DRS             = 18;        // u8
        private const int TEL_SURFACE_TYPE    = 56;        // u8[4]
        private const int CAR_TEL_SIZE        = 60;

        // CarStatusData is 55 bytes per car.
        private const int STA_TC              = 0;         // u8 (0=off, 1=med, 2=full)
        private const int STA_PIT_LIMITER     = 4;         // u8
        private const int STA_MAX_RPM         = 17;        // u16
        private const int STA_ERS_DEPLOY_MODE = 41;        // u8 (0=none, >0 = deploying)
        private const int CAR_STATUS_SIZE     = 55;

        // PacketMotionExData has player-only data, so offsets are absolute
        // after the header.
        private const int MEX_WHEEL_SLIP_RATIO = 64;       // float[4]
        private const int MEX_ANG_VEL_Y        = 148;      // float, rad/s, yaw rate
        // F1's coordinate frame: Y is vertical (per the official UDP spec
        // diagram), so angular velocity around Y axis is the yaw rate.

        // F1 surface-type codes used to set OnRumbleStrip and a coarse
        // SurfaceRumble value. Treating the categorical signal as a 0..1
        // amplitude is approximate, but matches RoadBumpsEffect's contract.
        private const byte SURF_TARMAC       = 0;
        private const byte SURF_RUMBLE_STRIP = 1;
        private const byte SURF_GRAVEL       = 4;
        private const byte SURF_GRASS        = 7;

        // F1 25's wire format. Packets carrying a different value are
        // skipped (and logged once per unique format observed) so users
        // running other game years see a clear hint to update the plugin
        // or the game.
        private const ushort SUPPORTED_PACKET_FORMAT = 2025;

        // 60 Hz target for "best haptic responsiveness." MeasuredHz is
        // sampled by the UI layer; surfacing the threshold here keeps the
        // warning copy and the gating value in the same place.
        public const double RecommendedHz = 60.0;
        public const double LowRateThresholdHz = 50.0;     // warn when <50 Hz observed

        private readonly int _port;
        private readonly IPAddress _bindAddress;
        private readonly IPEndPoint _forwardTo;       // null = no forwarding
        private UdpClient _udp;
        private Socket _forwardSocket;                 // separate so a forward error can't tear down receive
        private Thread _thread;
        private volatile bool _stopping;
        private int _running;

        // Cached state from the most recent packet of each type. Volatile
        // not strictly needed (single producer thread updates them, frame
        // emitted on the same thread), but kept simple and primitive.
        private float _maxRpm;
        private byte  _pitLimiter;
        private byte  _ersDeployMode;
        private byte  _tractionControl;
        private float _gForceLateral;
        private float _gForceLongitudinal;
        private float _gForceVertical;
        private float _yawRateRadPerSec;
        private float _wheelSlipMax;

        // PacketFormat-mismatch log de-dup so a stream of F1 24 packets
        // doesn't flood the log with one entry per packet.
        private ushort _lastWarnedFormat;

        public Action<string> Logger { get; set; }

        /// <summary>Number of valid (PacketFormat=2025) packets received
        /// since Start(). Surfaced in the F1 settings panel as a "received"
        /// counter so the user can confirm wiring.</summary>
        public long PacketsReceived => _packetsReceived;
        private long _packetsReceived;

        /// <summary>Number of packets successfully forwarded to the
        /// secondary destination since Start(). Stays at 0 when no forward
        /// target was configured.</summary>
        public long PacketsForwarded => _packetsForwarded;
        private long _packetsForwarded;

        public F1UdpTelemetrySource(int port, IPAddress bindAddress = null, IPEndPoint forwardTo = null)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            _port        = port;
            _bindAddress = bindAddress ?? IPAddress.Any;
            _forwardTo   = forwardTo;
        }

        public override void Start()
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
            try
            {
                _udp = new UdpClient();
                _udp.ExclusiveAddressUse = false;
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(_bindAddress, _port));
                _udp.Client.ReceiveTimeout = 1000;

                if (_forwardTo != null)
                {
                    _forwardSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                }
            }
            catch
            {
                CleanupSocket();
                Interlocked.Exchange(ref _running, 0);
                throw;
            }

            _stopping = false;
            _thread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name         = "F1UdpTelemetrySource",
            };
            _thread.Start();
            if (_forwardTo != null)
                Log($"F1 UDP source listening on {_bindAddress}:{_port}, forwarding to {_forwardTo}.");
            else
                Log($"F1 UDP source listening on {_bindAddress}:{_port}.");
        }

        public override void Stop()
        {
            _stopping = true;
            try { _udp?.Close(); } catch { }
            try { _thread?.Join(2000); } catch { }
            _thread = null;
            CleanupSocket();
            Interlocked.Exchange(ref _running, 0);
        }

        private void CleanupSocket()
        {
            try { _udp?.Dispose(); } catch { }
            _udp = null;
            try { _forwardSocket?.Close(); } catch { }
            try { _forwardSocket?.Dispose(); } catch { }
            _forwardSocket = null;
        }

        private void ReceiveLoop()
        {
            var remoteEp = new IPEndPoint(IPAddress.Any, 0);
            byte[] scratch = new byte[2048];   // F1 packets max around 1500 bytes; 2048 is generous

            while (!_stopping)
            {
                int len;
                try
                {
                    EndPoint ep = remoteEp;
                    len = _udp.Client.ReceiveFrom(scratch, 0, scratch.Length, SocketFlags.None, ref ep);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
                }
                catch (Exception)
                {
                    if (_stopping) return;
                    Thread.Sleep(50);
                    continue;
                }

                if (len < HEADER_SIZE) continue;

                // Forward FIRST so a parse error in our pipeline can't
                // strand SimHub without telemetry. Forward is fire-and-
                // forget UDP, we don't care if the target listener exists.
                if (_forwardSocket != null && _forwardTo != null)
                {
                    try
                    {
                        _forwardSocket.SendTo(scratch, 0, len, SocketFlags.None, _forwardTo);
                        Interlocked.Increment(ref _packetsForwarded);
                    }
                    catch { /* swallowed; UI shows the counter so the user can see if it's stuck */ }
                }

                try
                {
                    HandlePacket(scratch, len);
                }
                catch (Exception ex)
                {
                    Log($"F1 parse error: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private void HandlePacket(byte[] buf, int len)
        {
            ushort format = ReadUInt16(buf, HDR_PACKET_FORMAT);
            if (format != SUPPORTED_PACKET_FORMAT)
            {
                if (_lastWarnedFormat != format)
                {
                    Log($"F1 packet format {format} unsupported (this build targets {SUPPORTED_PACKET_FORMAT}); skipping.");
                    _lastWarnedFormat = format;
                }
                return;
            }

            byte packetId    = buf[HDR_PACKET_ID];
            byte playerIndex = buf[HDR_PLAYER_CAR_INDEX];

            switch (packetId)
            {
                case PID_MOTION:
                {
                    int playerOffset = HEADER_SIZE + playerIndex * CAR_MOTION_SIZE;
                    if (len < playerOffset + CAR_MOTION_SIZE) return;
                    _gForceLateral      = ReadFloat(buf, playerOffset + MOT_GFORCE_LATERAL);
                    _gForceLongitudinal = ReadFloat(buf, playerOffset + MOT_GFORCE_LONGITUDINAL);
                    _gForceVertical     = ReadFloat(buf, playerOffset + MOT_GFORCE_VERTICAL);
                    // Yaw angle alone isn't useful for traction-loss; the
                    // angular-velocity-Y field in MotionEx is the yaw rate
                    // we actually feed effects. Read here just in case
                    // MotionEx isn't being received but it's not our
                    // primary source.
                    return;
                }

                case PID_CAR_STATUS:
                {
                    int playerOffset = HEADER_SIZE + playerIndex * CAR_STATUS_SIZE;
                    if (len < playerOffset + CAR_STATUS_SIZE) return;
                    _tractionControl = buf[playerOffset + STA_TC];
                    _pitLimiter      = buf[playerOffset + STA_PIT_LIMITER];
                    _ersDeployMode   = buf[playerOffset + STA_ERS_DEPLOY_MODE];
                    _maxRpm          = ReadUInt16(buf, playerOffset + STA_MAX_RPM);
                    return;
                }

                case PID_MOTION_EX:
                {
                    if (len < HEADER_SIZE + MEX_ANG_VEL_Y + 4) return;
                    // wheelSlipRatio[4]: max-abs across all four wheels matches
                    // AC's WheelSlip semantics so TractionLossEffect's existing
                    // direct-slip path applies unchanged.
                    float fl = ReadFloat(buf, HEADER_SIZE + MEX_WHEEL_SLIP_RATIO + 0);
                    float fr = ReadFloat(buf, HEADER_SIZE + MEX_WHEEL_SLIP_RATIO + 4);
                    float rl = ReadFloat(buf, HEADER_SIZE + MEX_WHEEL_SLIP_RATIO + 8);
                    float rr = ReadFloat(buf, HEADER_SIZE + MEX_WHEEL_SLIP_RATIO + 12);
                    _wheelSlipMax = Math.Max(
                        Math.Max(Math.Abs(fl), Math.Abs(fr)),
                        Math.Max(Math.Abs(rl), Math.Abs(rr)));
                    _yawRateRadPerSec = ReadFloat(buf, HEADER_SIZE + MEX_ANG_VEL_Y);
                    return;
                }

                case PID_CAR_TELEMETRY:
                {
                    int playerOffset = HEADER_SIZE + playerIndex * CAR_TEL_SIZE;
                    if (len < playerOffset + CAR_TEL_SIZE) return;

                    int speedKmh   = ReadUInt16(buf, playerOffset + TEL_SPEED);
                    float throttle = ReadFloat(buf,  playerOffset + TEL_THROTTLE);
                    sbyte gear     = (sbyte)buf[playerOffset + TEL_GEAR];
                    int rpm        = ReadUInt16(buf, playerOffset + TEL_RPM);
                    byte drs       = buf[playerOffset + TEL_DRS];

                    byte sFL = buf[playerOffset + TEL_SURFACE_TYPE + 0];
                    byte sFR = buf[playerOffset + TEL_SURFACE_TYPE + 1];
                    byte sRL = buf[playerOffset + TEL_SURFACE_TYPE + 2];
                    byte sRR = buf[playerOffset + TEL_SURFACE_TYPE + 3];

                    bool onStrip = sFL == SURF_RUMBLE_STRIP || sFR == SURF_RUMBLE_STRIP
                                || sRL == SURF_RUMBLE_STRIP || sRR == SURF_RUMBLE_STRIP;

                    // Approximate continuous surface "noise level" from the
                    // discrete category. RoadBumpsEffect treats this as a
                    // 0..1 amplitude; F1 has no continuous channel so we map
                    // categorically: tarmac=0, kerb=1, off-road=0.7, grass=0.5.
                    double surfaceMax = Math.Max(Math.Max(
                        SurfaceLevel(sFL), SurfaceLevel(sFR)),
                        Math.Max(SurfaceLevel(sRL), SurfaceLevel(sRR)));

                    Interlocked.Increment(ref _packetsReceived);

                    const double G        = 9.80665;
                    const double RadToDeg = 180.0 / Math.PI;

                    EmitFrame(new TelemetryFrame
                    {
                        Rpms       = rpm,
                        MaxRpm     = _maxRpm,
                        Throttle01 = throttle < 0 ? 0 : (throttle > 1 ? 1 : throttle),
                        SpeedKmh   = speedKmh,
                        Gear       = GearString(gear),

                        AccelerationHeave = _gForceVertical     * G,
                        AccelerationSway  = _gForceLateral      * G,
                        AccelerationSurge = _gForceLongitudinal * G,
                        YawRateDegPerSec  = _yawRateRadPerSec * RadToDeg,

                        WheelSlip     = _wheelSlipMax,
                        TcActive      = _tractionControl > 0 ? 1 : 0,

                        PitLimiterActive = _pitLimiter > 0 ? 1 : 0,
                        DrsActive        = drs > 0 ? 1 : 0,
                        KersActive       = _ersDeployMode > 0 ? 1 : 0,

                        SurfaceRumble = surfaceMax,
                        OnRumbleStrip = onStrip,
                        // Hardcoded: every F1 car is a V6 turbo hybrid.
                        // Lets EnginePulseEffect lock to the right firing
                        // frequency without the user touching the slider.
                        NumCylinders = 6,
                    });
                    return;
                }

                default:
                    return;   // unhandled packet type, ignore
            }
        }

        private static double SurfaceLevel(byte code)
        {
            switch (code)
            {
                case SURF_TARMAC:        return 0.0;
                case SURF_RUMBLE_STRIP:  return 1.0;
                case SURF_GRASS:         return 0.5;
                case SURF_GRAVEL:        return 0.7;
                default:                 return 0.5;   // mud, sand, cobblestone, etc.
            }
        }

        // F1 gear convention: -1 = R, 0 = N, 1..8 = forward gears. Match
        // the AC / Forza string convention so GearShiftEffect compares
        // unchanged.
        private static string GearString(int gear)
        {
            if (gear < 0) return "R";
            if (gear == 0) return "N";
            return gear.ToString();
        }

        /// <summary>Cheap shape check used by UdpPortScanner: at least the
        /// 29-byte header, with PacketFormat == 2025 and a recognized
        /// packet ID. Lets discovery distinguish F1 25 packets from
        /// random UDP traffic on a candidate port. False positives are
        /// possible but vanishingly unlikely given the format-version
        /// gate.</summary>
        public static bool IsValidPacketCandidate(byte[] buf, int len)
        {
            if (buf == null || len < HEADER_SIZE) return false;
            ushort format = (ushort)(buf[HDR_PACKET_FORMAT] | (buf[HDR_PACKET_FORMAT + 1] << 8));
            if (format != SUPPORTED_PACKET_FORMAT) return false;
            byte packetId = buf[HDR_PACKET_ID];
            return packetId <= 15;   // F1 25 has 16 packet types (0..15)
        }

        /// <summary>Default candidate ports the discovery flow tries when
        /// the user's configured port shows zero packets after startup.
        /// Mostly the F1 default plus a few common alternates the
        /// community uses for parallel listeners.</summary>
        public static readonly int[] DiscoveryCandidatePorts =
            { 20777, 20778, 20779, 20780, 21580 };

        private static ushort ReadUInt16(byte[] b, int off)
            => (ushort)(b[off] | (b[off+1] << 8));

        private static float ReadFloat(byte[] b, int off)
            => BitConverter.ToSingle(b, off);

        private void Log(string msg)
        {
            var l = Logger;
            if (l != null) try { l(msg); } catch { }
        }
    }
}
