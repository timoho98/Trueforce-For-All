using System;
using System.Threading;
using TrueforceForAll.Core;
using Xunit;

namespace TrueforceForAll.Core.Tests
{
    // Tests the Forza Data Out packet parser (the part most recently changed:
    // Steer -> SteeringAngle for the stationary spring, plus CarOrdinal, the
    // spawn settle window, and Sled-vs-Dash tolerance). Feeds synthetic
    // packets straight into the internal ParsePacket, no socket involved.
    public class ForzaUdpTelemetrySourceTests
    {
        // Forza "Horizon dash" packet (FH4/FH5/FH6 shape). Offsets mirror the
        // private OFF_* constants in ForzaUdpTelemetrySource.
        private const int HorizonDashLength = 324;
        private const int OFF_IS_RACE_ON   = 0;
        private const int OFF_ENGINE_MAX   = 8;
        private const int OFF_CURRENT_RPM  = 16;
        private const int OFF_ACCEL_Y      = 24;   // heave (m/s^2, up)
        private const int OFF_NORM_SUSP_FL = 68;   // normalized susp travel[4], 0=droop..1=compressed
        private const int OFF_COMBINED_FL  = 180;  // tyre combined slip[4]
        private const int OFF_CAR_ORDINAL  = 212;
        private const int OFF_NUM_CYL      = 228;
        private const int OFF_SPEED        = 256;  // m/s
        private const int OFF_ACCEL_PEDAL  = 315;  // 0..255
        private const int OFF_GEAR         = 319;  // 0=R,1=N,2..=fwd
        private const int OFF_STEER        = 320;  // int8, -127..127

        private static void PutInt32(byte[] b, int off, int v) =>
            BitConverter.GetBytes(v).CopyTo(b, off);
        private static void PutFloat(byte[] b, int off, float v) =>
            BitConverter.GetBytes(v).CopyTo(b, off);

        // Build a Horizon-dash packet with the fields the parser reads. Defaults
        // are a plausible "driving" frame; override per test.
        private static byte[] DashPacket(
            int raceOn = 1, float maxRpm = 8000f, float rpm = 5000f, float heave = 9.0f,
            float combinedSlip = 0.7f, int carOrdinal = 2468, int cylinders = 8,
            float speedMs = 30f, byte accel = 200, byte gear = 4, sbyte steer = 0,
            float suspTravel = 0.5f)
        {
            var b = new byte[HorizonDashLength];
            PutInt32(b, OFF_IS_RACE_ON, raceOn);
            PutFloat(b, OFF_ENGINE_MAX, maxRpm);
            PutFloat(b, OFF_CURRENT_RPM, rpm);
            PutFloat(b, OFF_ACCEL_Y, heave);
            for (int i = 0; i < 4; i++) PutFloat(b, OFF_NORM_SUSP_FL + i * 4, suspTravel);
            for (int i = 0; i < 4; i++) PutFloat(b, OFF_COMBINED_FL + i * 4, combinedSlip);
            PutInt32(b, OFF_CAR_ORDINAL, carOrdinal);
            PutInt32(b, OFF_NUM_CYL, cylinders);
            PutFloat(b, OFF_SPEED, speedMs);
            b[OFF_ACCEL_PEDAL] = accel;
            b[OFF_GEAR] = gear;
            b[OFF_STEER] = unchecked((byte)steer);
            return b;
        }

        private static ForzaUdpTelemetrySource NewSource() => new ForzaUdpTelemetrySource(5300);

        [Fact]
        public void DrivingFrame_ExtractsCoreFields()
        {
            var src = NewSource();
            var f = src.ParsePacket(DashPacket(rpm: 5000f, maxRpm: 8000f, speedMs: 30f,
                                               accel: 255, gear: 4, cylinders: 8, carOrdinal: 2468), HorizonDashLength);

            Assert.Equal(5000.0, f.Rpms, 3);
            Assert.Equal(8000.0, f.MaxRpm, 3);
            Assert.Equal(30.0 * 3.6, f.SpeedKmh, 3);   // m/s -> km/h
            Assert.Equal(1.0, f.Throttle01, 3);         // 255/255
            Assert.Equal("3", f.Gear);                  // gear byte 4 -> "3"
            Assert.Equal(8, f.NumCylinders);
            Assert.Equal(2468, src.CurrentCarOrdinal);
        }

        [Theory]
        [InlineData((sbyte)127, 1.0)]
        [InlineData((sbyte)-127, -1.0)]
        [InlineData((sbyte)0, 0.0)]
        [InlineData((sbyte)64, 64.0 / 127.0)]
        public void Steer_NormalizedToPlusMinusOne(sbyte steerByte, double expected)
        {
            var src = NewSource();
            var f = src.ParsePacket(DashPacket(steer: steerByte), HorizonDashLength);

            Assert.True(f.SteeringAngle.HasValue);
            Assert.Equal(expected, f.SteeringAngle.Value, 4);
        }

        [Fact]
        public void SpawnFrame_IsSettling_SuppressesImpactButFlowsMotion()
        {
            // First raceOn=1 frame on a fresh source is the IsRaceOn 0->1 edge,
            // so the settle window is open: impact/grip channels must be zeroed
            // (no spawn jolt) while engine/speed/steer flow normally.
            var src = NewSource();
            var f = src.ParsePacket(DashPacket(heave: 50f, combinedSlip: 0.9f, steer: 64), HorizonDashLength);

            Assert.Equal(0.0, f.AccelerationHeave.GetValueOrDefault(), 6); // suppressed
            Assert.Equal(0.0, f.WheelSlip.GetValueOrDefault(), 6);         // suppressed
            Assert.True(f.Rpms > 0);                                       // engine flows
            Assert.True(f.SpeedKmh > 0);                                   // speed flows
            Assert.True(f.SteeringAngle.HasValue);                         // steering flows
        }

        [Fact]
        public void AfterSettleWindow_ImpactChannelsResume()
        {
            // The settle window is 400 ms wall-clock. Open it, wait it out, then
            // a subsequent raceOn=1 frame must let impact/grip through again.
            var src = NewSource();
            src.ParsePacket(DashPacket(heave: 50f, combinedSlip: 0.9f), HorizonDashLength);
            Thread.Sleep(450);
            var f = src.ParsePacket(DashPacket(heave: 50f, combinedSlip: 0.9f), HorizonDashLength);

            Assert.True(Math.Abs(f.AccelerationHeave.GetValueOrDefault()) > 0.0);
            Assert.True(f.WheelSlip.GetValueOrDefault() > 0.0);
        }

        [Fact]
        public void Airborne_FlagSetWhenAllWheelsDrooped()
        {
            // All four wheels drooped (suspension travel ~0) = car in the air.
            // Past the settle window, the Airborne flag is set so AirborneEffect
            // can duck the configured voices; raw channels still flow (the
            // effect, not the source, decides what to silence).
            var src = NewSource();
            src.ParsePacket(DashPacket(), HorizonDashLength);   // open + spend settle window
            Thread.Sleep(450);
            var f = src.ParsePacket(DashPacket(combinedSlip: 0.9f, suspTravel: 0.0f), HorizonDashLength);

            Assert.True(f.Airborne.GetValueOrDefault());
            Assert.True(f.Rpms > 0);        // engine flows
            Assert.True(f.SpeedKmh > 0);    // speed flows
        }

        [Fact]
        public void Grounded_LightlyLoadedSuspension_NotAirborne()
        {
            // A loaded wheel sits well off full droop even when light; the flag
            // stays clear (we only call it airborne when all four are drooped).
            var src = NewSource();
            src.ParsePacket(DashPacket(), HorizonDashLength);
            Thread.Sleep(450);
            var f = src.ParsePacket(DashPacket(combinedSlip: 0.9f, suspTravel: 0.1f), HorizonDashLength);

            Assert.False(f.Airborne.GetValueOrDefault());
            Assert.True(f.WheelSlip.GetValueOrDefault() > 0.0);
        }

        [Fact]
        public void PausedFrame_ZeroesVolatileChannels()
        {
            var src = NewSource();
            var f = src.ParsePacket(DashPacket(raceOn: 0, rpm: 5000f, speedMs: 30f), HorizonDashLength);

            Assert.Equal(0.0, f.Rpms, 6);
            Assert.Equal(0.0, f.SpeedKmh, 6);
            Assert.Equal("N", f.Gear);
        }

        [Fact]
        public void CarOrdinalZero_IsNull()
        {
            var src = NewSource();
            src.ParsePacket(DashPacket(carOrdinal: 0), HorizonDashLength);
            Assert.Null(src.CurrentCarOrdinal);
        }

        [Fact]
        public void SledOnlyPacket_HasNoDashFields()
        {
            // 232-byte Sled-only packet (FM7 shape): no speed/steer/gear in the
            // dash region, so SteeringAngle stays null and speed stays 0.
            var src = NewSource();
            var sled = new byte[232];
            PutInt32(sled, OFF_IS_RACE_ON, 1);
            PutFloat(sled, OFF_CURRENT_RPM, 5000f);

            var f = src.ParsePacket(sled, sled.Length);

            Assert.Null(f.SteeringAngle);
            Assert.Equal(0.0, f.SpeedKmh, 6);
        }
    }
}
