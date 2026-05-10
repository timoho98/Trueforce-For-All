// Reads Project Cars 2's "$pcars$" shared memory MMF directly. PC2 (and
// PC1 before it) write a SharedMemory struct as defined by SMS's public
// C header (mirrored at github.com/viper4gh/CREST2 SharedMemory.h). The
// struct is updated once per graphics frame, so polling rate is bounded
// by the user's framerate (60-144 Hz typical) — faster than the SimHub
// IDataPlugin tick (60 Hz). Reading directly bypasses SimHub's tick
// rate cap and gives us per-tire data SimHub's NormalizedData doesn't
// expose: mTerrain[4] (surface IDs), mTyreSlipSpeed[4] (true slip
// velocity), mSuspensionVelocity[4] (high-quality road texture).
//
// Torn-read protection: PC2 uses a volatile mSequenceNumber that is
// "Odd when Shared Memory is being filled, even when the memory is not
// being touched" (per the SMS header). We sample seq twice per poll and
// discard frames where seq is odd or where the value changed mid-read.
//
// Field offsets are computed sequentially from the C struct field order.
// MSVC default Pack=8, but with all members ≤ 4-byte aligned the layout
// matches Pack=4. Each OFF_* const is defined as PREV + sizeof(prev), so
// a top-to-bottom read of the offsets block verifies the layout. The
// SHARED_MEMORY_VERSION mismatch check provides a cheap runtime sanity
// check too: if the struct layout is wrong, mVersion at offset 0 won't
// equal 9 and we bail.
//
// Game ID: SimHub uses "PCars2" for Project Cars 2. PC1 ("PCars") uses
// the same MMF name but an older SHARED_MEMORY_VERSION; if the version
// validation finds something other than 9, we bail and fall back to
// the SimHub source.

using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace TrueforceForAll.Core
{
    public sealed class Pcars2SharedMemoryTelemetrySource : TelemetrySourceBase
    {
        public override string Name => "Project Cars 2";
        public override bool   IsEnhanced => true;
        public override bool   IsRunning  => _running != 0;

        private const string MmfName = "$pcars$";
        private const uint   ExpectedVersion = 9;

        // ---- Field offsets (each line = previous offset + sizeof(prev field)) ----
        // Header (28 bytes)
        private const int OFF_VERSION                 = 0;
        // 4 mBuildVersionNumber (uint)
        private const int OFF_GAME_STATE              = 8;
        // 12 mSessionState, 16 mRaceState, 20 mViewedParticipantIndex,
        // 24 mNumParticipants — not read.
        // ParticipantInfo struct = 100 bytes (bool+pad → 4, char[64], float[3]=12,
        // float, uint, uint, uint, int = 4+64+12+4+4+4+4+4 = 100). 64 entries.
        private const int OFF_PARTICIPANT_INFO_BLOCK  = 28;
        private const int SIZE_PARTICIPANT_INFO       = 100;
        private const int OFF_AFTER_PARTICIPANTS      = OFF_PARTICIPANT_INFO_BLOCK + 64 * SIZE_PARTICIPANT_INFO; // 6428

        // Skip mUnfilteredThrottle/Brake/Steering/Clutch (4 floats) + mCarName[64] +
        // mCarClassName[64] + mLapsInEvent (uint) + mTrackLocation[64] +
        // mTrackVariation[64] + mTrackLength (float) + mNumSectors (int) = 284 bytes.
        // Then mLapInvalidated (bool 1 + pad 3 = 4 bytes).
        private const int OFF_AFTER_LAP_INVALIDATED   = OFF_AFTER_PARTICIPANTS + 284 + 4;       // 6716

        // 21 floats of lap/sector times = 84 bytes.
        // 5 uints of flags / pit / car flags = 20 bytes.
        // 7 floats of oil/water/fuel = 28 bytes.
        private const int OFF_SPEED                   = OFF_AFTER_LAP_INVALIDATED + 84 + 20 + 28; // 6848
        private const int OFF_RPM                     = OFF_SPEED + 4;                            // 6852
        private const int OFF_MAX_RPM                 = OFF_RPM + 4;                              // 6856
        private const int OFF_BRAKE                   = OFF_MAX_RPM + 4;                          // 6860
        private const int OFF_THROTTLE                = OFF_BRAKE + 4;                            // 6864
        // mClutch (4) + mSteering (4) skipped.
        private const int OFF_GEAR                    = OFF_THROTTLE + 12;                       // 6876
        // mNumGears (4) + mOdometerKM (4) +
        // mAntiLockActive bool+pad (4) +
        // mLastOpponentCollisionIndex (4) + mLastOpponentCollisionMagnitude (4) +
        // mBoostActive bool+pad (4) + mBoostAmount (4) = 28 bytes.
        // Then 7 vec3 floats = 7 * 12 = 84 bytes.
        private const int OFF_TYRE_FLAGS_BLOCK        = OFF_GEAR + 4 + 28 + 84;                  // 6992
        private const int OFF_TERRAIN_BLOCK           = OFF_TYRE_FLAGS_BLOCK + 16;               // 7008
        // Skip mTyreY[4] = 16.
        private const int OFF_TYRE_RPS_BLOCK          = OFF_TERRAIN_BLOCK + 16 + 16;             // 7040
        private const int OFF_TYRE_SLIP_SPEED_BLOCK   = OFF_TYRE_RPS_BLOCK + 16;                 // 7056
        // Skip mTyreTemp[4]+mTyreGrip[4] = 32.
        private const int OFF_TYRE_HEIGHT_BLOCK       = OFF_TYRE_SLIP_SPEED_BLOCK + 16 + 32;     // 7104

        // From mTyreHeightAboveGround[4] (ends at 7120) skip:
        // mTyreLateralStiffness, mTyreWear, mBrakeDamage, mSuspensionDamage,
        // mBrakeTempCelsius, mTyreTreadTemp, mTyreLayerTemp, mTyreCarcassTemp,
        // mTyreRimTemp, mTyreInternalAirTemp = 10 * 16 = 160 bytes.
        // Then mCrashState (uint, 4) + mAeroDamage..mCloudBrightness = 9 floats (36 bytes).
        private const int OFF_SEQUENCE_NUMBER         = OFF_TYRE_HEIGHT_BLOCK + 16 + 160 + 4 + 36; // 7320

        // Skip mWheelLocalPositionY[4] = 16.
        private const int OFF_SUSP_TRAVEL_BLOCK       = OFF_SEQUENCE_NUMBER + 4 + 16;            // 7340
        private const int OFF_SUSP_VELOCITY_BLOCK     = OFF_SUSP_TRAVEL_BLOCK + 16;              // 7356
        // Skip mAirPressure[4] = 16.
        private const int OFF_ENGINE_SPEED            = OFF_SUSP_VELOCITY_BLOCK + 16 + 16;       // 7388
        private const int OFF_ENGINE_TORQUE           = OFF_ENGINE_SPEED + 4;                    // 7392

        // ---- Game state enum values (subset; see SMS header) ----
        private const uint GAME_INGAME_PLAYING        = 2;
        private const uint GAME_INGAME_RESTARTING     = 5;
        private const uint GAME_INGAME_REPLAY         = 6;

        // ---- Polling cadence ----
        // 1 kHz poll, mirroring AcSharedMemoryTelemetrySource. PC2 writes
        // its struct once per graphics frame (60-240 Hz depending on user
        // framerate), so polling at 1 kHz catches every new frame within
        // ≤1 ms regardless of the user's monitor rate. mSequenceNumber
        // dedupe ensures we emit at most one frame per game write; the
        // emission rate caps at the user's framerate, not 1 kHz. Aligning
        // with the Trueforce 1 kHz packet cadence means events don't get
        // aliased against packet boundaries. Requires timeBeginPeriod(1)
        // for Thread.Sleep(1) to honor 1 ms instead of the default ~15 ms.
        private const int TickPeriodMs = 1;
        private const int RetryPeriodMs = 200;
        private const int ReopenAfterConsecutiveErrors = 5;

        // ---- State ----
        private MemoryMappedFile         _mmf;
        private MemoryMappedViewAccessor _view;

        private Thread _thread;
        private volatile bool _stopping;
        private int _running;

        // mSequenceNumber-based dedupe: emit only when the value changes.
        // 0 sentinel is fine because PC2 uses odd-when-writing semantics
        // and even values >= 2 for stable.
        private uint _lastSeq;

        public Action<string> Logger { get; set; }

        public override void Start()
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;

            try
            {
                _mmf  = MemoryMappedFile.OpenExisting(MmfName, MemoryMappedFileRights.Read);
                _view = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            }
            catch
            {
                Interlocked.Exchange(ref _running, 0);
                CleanupMmf();
                throw;
            }

            // Validate version up-front. If the user is running PC1 (older
            // version) or some other game that happens to use $pcars$,
            // we bail so the plugin can fall back to SimHub.
            try
            {
                uint ver = (uint)_view.ReadInt32(OFF_VERSION);
                if (ver != ExpectedVersion)
                {
                    Log($"PC2 shared memory version mismatch: got {ver}, expected {ExpectedVersion}. Falling back.");
                    CleanupMmf();
                    Interlocked.Exchange(ref _running, 0);
                    throw new InvalidOperationException(
                        $"PC2 shared memory version {ver} unsupported (expected {ExpectedVersion}).");
                }
            }
            catch (InvalidOperationException) { throw; }
            catch
            {
                CleanupMmf();
                Interlocked.Exchange(ref _running, 0);
                throw;
            }

            _stopping = false;
            _thread = new Thread(PollLoop)
            {
                IsBackground = true,
                Name = "Pcars2SharedMemoryTelemetrySource",
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();
            Log("PC2 shared memory source started.");
        }

        public override void Stop()
        {
            _stopping = true;
            try { _thread?.Join(2000); } catch { }
            _thread = null;
            CleanupMmf();
            _lastSeq = 0;
            Interlocked.Exchange(ref _running, 0);
        }

        private void CleanupMmf()
        {
            try { _view?.Dispose(); } catch { }
            try { _mmf?.Dispose();  } catch { }
            _view = null;
            _mmf  = null;
        }

        private bool TryReopenMmf()
        {
            try
            {
                _mmf  = MemoryMappedFile.OpenExisting(MmfName, MemoryMappedFileRights.Read);
                _view = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                uint ver = (uint)_view.ReadInt32(OFF_VERSION);
                if (ver != ExpectedVersion)
                {
                    CleanupMmf();
                    return false;
                }
                return true;
            }
            catch
            {
                CleanupMmf();
                return false;
            }
        }

        private void PollLoop()
        {
            TimeBeginPeriod(1);
            try
            {
                var sw = Stopwatch.StartNew();
                long nextTickMs = 0;

                int consecutiveErrors = 0;
                bool reopenPending = false;

                while (!_stopping)
                {
                    int periodMs = TickPeriodMs;
                    if (reopenPending)
                    {
                        if (TryReopenMmf())
                        {
                            consecutiveErrors = 0;
                            reopenPending     = false;
                            _lastSeq          = 0;
                            Log("PC2 shared memory reopened.");
                        }
                        else
                        {
                            periodMs = RetryPeriodMs;
                        }
                    }
                    else
                    {
                        try
                        {
                            // Torn-read guard: read seq twice and skip if odd
                            // or mismatched.
                            uint seq1 = (uint)_view.ReadInt32(OFF_SEQUENCE_NUMBER);
                            if ((seq1 & 1) == 0 && seq1 != _lastSeq)
                            {
                                var frame = ReadFrame();
                                uint seq2 = (uint)_view.ReadInt32(OFF_SEQUENCE_NUMBER);
                                if (seq2 == seq1)
                                {
                                    _lastSeq = seq1;
                                    EmitFrame(frame);
                                }
                            }
                            consecutiveErrors = 0;
                        }
                        catch (Exception ex)
                        {
                            consecutiveErrors++;
                            if (consecutiveErrors == 1)
                                Log($"PC2 poll error: {ex.GetType().Name}: {ex.Message}");
                            if (consecutiveErrors >= ReopenAfterConsecutiveErrors)
                            {
                                Log("PC2 shared memory unresponsive; will attempt reopen.");
                                CleanupMmf();
                                reopenPending = true;
                            }
                        }
                    }

                    nextTickMs += periodMs;
                    long elapsed = sw.ElapsedMilliseconds;
                    int sleepMs = (int)(nextTickMs - elapsed);
                    if (sleepMs <= 0)
                    {
                        nextTickMs = elapsed + periodMs;
                        sleepMs = periodMs;
                    }
                    Thread.Sleep(sleepMs);
                }
            }
            finally
            {
                TimeEndPeriod(1);
            }
        }

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint uPeriod);

        private TelemetryFrame ReadFrame()
        {
            uint  gameState = (uint)_view.ReadInt32(OFF_GAME_STATE);

            float speedMps  = _view.ReadSingle(OFF_SPEED);     // m/s
            float rpm       = _view.ReadSingle(OFF_RPM);
            float maxRpm    = _view.ReadSingle(OFF_MAX_RPM);
            float brake     = _view.ReadSingle(OFF_BRAKE);     // 0..1
            float throttle  = _view.ReadSingle(OFF_THROTTLE);  // 0..1
            int   gear      = _view.ReadInt32 (OFF_GEAR);

            // Per-tire slip speed: max-abs across 4 tires.
            float slip0 = _view.ReadSingle(OFF_TYRE_SLIP_SPEED_BLOCK);
            float slip1 = _view.ReadSingle(OFF_TYRE_SLIP_SPEED_BLOCK + 4);
            float slip2 = _view.ReadSingle(OFF_TYRE_SLIP_SPEED_BLOCK + 8);
            float slip3 = _view.ReadSingle(OFF_TYRE_SLIP_SPEED_BLOCK + 12);
            float maxSlipSpeed = Math.Max(
                Math.Max(Math.Abs(slip0), Math.Abs(slip1)),
                Math.Max(Math.Abs(slip2), Math.Abs(slip3)));

            // Per-tire suspension velocity: max-abs as a road-texture proxy.
            // Normalize against a typical hard-bump magnitude (~5 m/s).
            float sv0 = _view.ReadSingle(OFF_SUSP_VELOCITY_BLOCK);
            float sv1 = _view.ReadSingle(OFF_SUSP_VELOCITY_BLOCK + 4);
            float sv2 = _view.ReadSingle(OFF_SUSP_VELOCITY_BLOCK + 8);
            float sv3 = _view.ReadSingle(OFF_SUSP_VELOCITY_BLOCK + 12);
            float maxSuspVel = Math.Max(
                Math.Max(Math.Abs(sv0), Math.Abs(sv1)),
                Math.Max(Math.Abs(sv2), Math.Abs(sv3)));
            const float SuspVelScale = 5.0f;
            double surfaceRumble = Math.Min(1.0, maxSuspVel / SuspVelScale);

            // Speed conversion: PC2's mSpeed is m/s.
            const float MpsToKmh = 3.6f;

            return new TelemetryFrame
            {
                Rpms       = rpm,
                MaxRpm     = maxRpm,
                Throttle01 = Clamp01(throttle),

                SpeedKmh           = speedMps * MpsToKmh,
                // PC2 doesn't expose vertical/lateral acceleration in shared
                // memory in a way we can use without reading the full
                // mLocalAcceleration vec3 (skipped above). DispatchFrame's
                // SimHub overlay will fill these.
                AccelerationHeave  = null,
                AccelerationSway   = null,
                YawRateDegPerSec   = null,

                Gear      = GearString(gear),

                // Direct slip in m/s. RoadBumps and TractionLossEffect treat
                // WheelSlip as a slip magnitude (~0 grip, >0.5 noticeable).
                // mTyreSlipSpeed is the relative velocity at the contact
                // patch — an excellent direct slip signal, no heuristic.
                WheelSlip = maxSlipSpeed,

                // mSuspensionVelocity scaled into 0..1 as a road-texture
                // signal. RoadBumpsEffect folds this in like Forza's
                // SurfaceRumble channel.
                SurfaceRumble = surfaceRumble,

                // OnRumbleStrip would need terrain-ID classification; defer.
                // NumCylinders, MaxRpm overlays — leave to SimHub fallback /
                // user override. AbsActive, PitLimiterActive, DrsActive,
                // KersActive — left null, DispatchFrame's overlay fills them.

                // Diagnostics: gameState used to gate paused/menu sessions.
                // Effects already tolerate "no-input" frames (zero gas/RPM)
                // so we still emit frames during pause; the slowed values
                // make the engine pulse fade naturally.
            };
        }

        // PC2 convention: 0=N, -1=R, 1+ = forward gears. Map to SimHub's
        // string convention ("R", "N", "1", "2", ...).
        private static string GearString(int gear)
        {
            if (gear < 0) return "R";
            if (gear == 0) return "N";
            return gear.ToString();
        }

        private static double Clamp01(double v)
        {
            if (v < 0) return 0;
            if (v > 1) return 1;
            return v;
        }

        private void Log(string msg)
        {
            var l = Logger;
            if (l != null) try { l(msg); } catch { }
        }
    }
}
