// Built-in car presets shipped with the plugin. Each entry is a full
// CarPresetFile JSON snapshot (carId, presetName, isBuiltin=true, override).
// Installed / refreshed by CarPresetStore.InstallOrUpdateBuiltinCarPresets
// on every Init: factory files always rewrite, user-saved files (different
// preset names) are untouched.
//
// Naming convention: PresetName = "<carId> (default)" mirrors the
// "(default)" suffix used for built-in game presets. The IsBuiltin flag
// inside the JSON is the runtime authority though, the suffix is just
// human-readable.
//
// Adding / updating a default: paste the user's tuned .tfcar.json content
// into the corresponding string constant, set PresetName to
// "<carId> (default)" and IsBuiltin to true. The next plugin install
// rewrites the factory file on every machine.

using System.Collections.Generic;

namespace TrueforceForAll.Plugin
{
    internal static class BuiltinCarPresets
    {
        /// <summary>Map keyed by a stable carId for log readability. The
        /// authoritative carId / presetName comes from the JSON itself, so
        /// the dictionary key is informational only.</summary>
        public static readonly IReadOnlyDictionary<string, string> PresetJsons =
            new Dictionary<string, string>
            {
                ["bdc_streetspec_ae86_v4"]              = BdcStreetspecAe86V4,
                ["car11:default"]                       = Car11Default,
                ["gravygarage_street_e36_touring"]      = GravyGarageE36Touring,
                ["ks_nissan_skyline_r34"]               = KsNissanSkylineR34,
                ["ks_toyota_ae86_tuned"]                = KsToyotaAe86Tuned,
                ["nohesi_realistic_nissan_gtr_r35_vlct"] = NohesiGtrR35Vlct,
                // Forza Horizon rev-limiter engage-point tunes (owner-tuned per
                // car). Each ships only a RevLimiter override (engage Threshold
                // + the shared buzz shape); all other sections fall through to
                // the game/global defaults. CarId is Forza's car ordinal, which
                // is stable across installs, so these match the same car for
                // every user.
                ["fh6:Car_2267"]   = Fh6_Car_2267,
                ["fh6:Car_378"]    = Fh6_Car_378,
                ["fh6:Car_4222"]   = Fh6_Car_4222,
                ["fh6:Forza_4268"] = Fh6_Forza_4268,
                ["fh6:Forza_455"]  = Fh6_Forza_455,
                ["fh6:Forza_513"]  = Fh6_Forza_513,
            };

        // ---- Forza Horizon rev-limiter engage tunes ----
        // Shared buzz shape; only Threshold (engage %) differs per car. Full
        // const JSON (not a builder) so they're available when PresetJsons'
        // static initializer runs, regardless of field order.
        private const string Fh6_Car_2267 = @"{
  ""Type"": ""trueforce-car-preset"", ""Version"": 2, ""GameName"": ""FH6"",
  ""CarId"": ""Car_2267"", ""PresetName"": ""Car_2267 (default)"", ""IsBuiltin"": true,
  ""Override"": { ""RevLimiter"": { ""Enabled"": true, ""Gain"": 0.11, ""Freq"": 53.79011, ""PulseFreq"": 18.1786652, ""DutyCycle"": 0.5, ""ActiveAmp"": 0.35, ""Threshold"": 0.930675447, ""Waveform"": ""Square"" } }
}";
        private const string Fh6_Car_378 = @"{
  ""Type"": ""trueforce-car-preset"", ""Version"": 2, ""GameName"": ""FH6"",
  ""CarId"": ""Car_378"", ""PresetName"": ""Car_378 (default)"", ""IsBuiltin"": true,
  ""Override"": { ""RevLimiter"": { ""Enabled"": true, ""Gain"": 0.11, ""Freq"": 53.79011, ""PulseFreq"": 18.1786652, ""DutyCycle"": 0.5, ""ActiveAmp"": 0.35, ""Threshold"": 0.8634156, ""Waveform"": ""Square"" } }
}";
        private const string Fh6_Car_4222 = @"{
  ""Type"": ""trueforce-car-preset"", ""Version"": 2, ""GameName"": ""FH6"",
  ""CarId"": ""Car_4222"", ""PresetName"": ""Car_4222 (default)"", ""IsBuiltin"": true,
  ""Override"": { ""RevLimiter"": { ""Enabled"": true, ""Gain"": 0.11, ""Freq"": 53.79011, ""PulseFreq"": 18.1786652, ""DutyCycle"": 0.5, ""ActiveAmp"": 0.35, ""Threshold"": 0.868041933, ""Waveform"": ""Square"" } }
}";
        private const string Fh6_Forza_4268 = @"{
  ""Type"": ""trueforce-car-preset"", ""Version"": 2, ""GameName"": ""FH6"",
  ""CarId"": ""Forza_4268"", ""PresetName"": ""Forza_4268 (default)"", ""IsBuiltin"": true,
  ""Override"": { ""RevLimiter"": { ""Enabled"": true, ""Gain"": 0.11, ""Freq"": 53.79011, ""PulseFreq"": 18.1786652, ""DutyCycle"": 0.5, ""ActiveAmp"": 0.35, ""Threshold"": 0.670533061, ""Waveform"": ""Square"" } }
}";
        private const string Fh6_Forza_455 = @"{
  ""Type"": ""trueforce-car-preset"", ""Version"": 2, ""GameName"": ""FH6"",
  ""CarId"": ""Forza_455"", ""PresetName"": ""Forza_455 (default)"", ""IsBuiltin"": true,
  ""Override"": { ""RevLimiter"": { ""Enabled"": true, ""Gain"": 0.11, ""Freq"": 53.79011, ""PulseFreq"": 18.1786652, ""DutyCycle"": 0.5, ""ActiveAmp"": 0.35, ""Threshold"": 0.927935839, ""Waveform"": ""Square"" } }
}";
        private const string Fh6_Forza_513 = @"{
  ""Type"": ""trueforce-car-preset"", ""Version"": 2, ""GameName"": ""FH6"",
  ""CarId"": ""Forza_513"", ""PresetName"": ""Forza_513 (default)"", ""IsBuiltin"": true,
  ""Override"": { ""RevLimiter"": { ""Enabled"": true, ""Gain"": 0.11, ""Freq"": 53.79011, ""PulseFreq"": 18.1786652, ""DutyCycle"": 0.5, ""ActiveAmp"": 0.35, ""Threshold"": 0.7462359, ""Waveform"": ""Square"" } }
}";

        // ---- Snapshots ----

        private const string BdcStreetspecAe86V4 = @"{
  ""Type"": ""trueforce-car-preset"",
  ""Version"": 2,
  ""GameName"": ""AssettoCorsa"",
  ""CarId"": ""bdc_streetspec_ae86_v4"",
  ""PresetName"": ""bdc_streetspec_ae86_v4 (default)"",
  ""IsBuiltin"": true,
  ""Override"": {
    ""EnginePulse"": {
      ""Enabled"": true,
      ""Gain"": 0.0755927339,
      ""Cylinders"": 4,
      ""Pitch"": 1.00160933,
      ""LowpassHz"": 513.32973107776661,
      ""Waveform"": ""Sine""
    },
    ""RoadBumps"": null,
    ""TractionLoss"": null,
    ""GearShift"": null,
    ""AbsClick"": null,
    ""AudioCapture"": null
  }
}";

        private const string Car11Default = @"{
  ""Type"": ""trueforce-car-preset"",
  ""Version"": 2,
  ""GameName"": ""Wreckfest2"",
  ""CarId"": ""car11:default"",
  ""PresetName"": ""car11:default (default)"",
  ""IsBuiltin"": true,
  ""Override"": {
    ""EnginePulse"": {
      ""Enabled"": true,
      ""Gain"": 0.06518083,
      ""Cylinders"": 8,
      ""Pitch"": 1.00160933,
      ""LowpassHz"": 401.7727,
      ""Waveform"": ""Sine""
    },
    ""RoadBumps"": null,
    ""TractionLoss"": null,
    ""GearShift"": null,
    ""AbsClick"": null,
    ""AudioCapture"": null
  }
}";

        private const string GravyGarageE36Touring = @"{
  ""Type"": ""trueforce-car-preset"",
  ""Version"": 2,
  ""GameName"": ""AssettoCorsa"",
  ""CarId"": ""gravygarage_street_e36_touring"",
  ""PresetName"": ""gravygarage_street_e36_touring (default)"",
  ""IsBuiltin"": true,
  ""Override"": {
    ""EnginePulse"": {
      ""Enabled"": true,
      ""Gain"": 0.06518083,
      ""Cylinders"": 8,
      ""Pitch"": 1.00160933,
      ""LowpassHz"": 775.68685315266612,
      ""Waveform"": ""Triangle""
    },
    ""RoadBumps"": null,
    ""TractionLoss"": {
      ""Enabled"": true,
      ""Gain"": 0.06434366,
      ""Sensitivity"": 0.1,
      ""Freq"": 133.901657,
      ""NoiseLowpassHz"": 250.0,
      ""NoiseHighpassHz"": 40.932499272621456,
      ""Waveform"": ""Noise""
    },
    ""GearShift"": null,
    ""AbsClick"": null,
    ""AudioCapture"": {
      ""Enabled"": true,
      ""Gain"": 0.0517140329,
      ""LowpassCutoffHz"": 567.09339540296833,
      ""HighpassCutoffHz"": 35.205952034581649
    }
  }
}";

        private const string KsNissanSkylineR34 = @"{
  ""Type"": ""trueforce-car-preset"",
  ""Version"": 2,
  ""GameName"": ""AssettoCorsa"",
  ""CarId"": ""ks_nissan_skyline_r34"",
  ""PresetName"": ""ks_nissan_skyline_r34 (default)"",
  ""IsBuiltin"": true,
  ""Override"": {
    ""EnginePulse"": {
      ""Enabled"": true,
      ""Gain"": 0.07882283,
      ""Cylinders"": 6,
      ""Pitch"": 1.00160933,
      ""LowpassHz"": 338.38688224780793,
      ""Waveform"": ""Triangle""
    },
    ""RoadBumps"": null,
    ""TractionLoss"": null,
    ""GearShift"": null,
    ""AbsClick"": null,
    ""AudioCapture"": null
  }
}";

        private const string KsToyotaAe86Tuned = @"{
  ""Type"": ""trueforce-car-preset"",
  ""Version"": 2,
  ""GameName"": ""AssettoCorsa"",
  ""CarId"": ""ks_toyota_ae86_tuned"",
  ""PresetName"": ""ks_toyota_ae86_tuned (default)"",
  ""IsBuiltin"": true,
  ""Override"": {
    ""EnginePulse"": {
      ""Enabled"": true,
      ""Gain"": 0.06518083,
      ""Cylinders"": 4,
      ""Pitch"": 1.00160933,
      ""LowpassHz"": 333.18093021322596,
      ""Waveform"": ""Triangle""
    },
    ""RoadBumps"": null,
    ""TractionLoss"": null,
    ""GearShift"": null,
    ""AbsClick"": null,
    ""AudioCapture"": null
  }
}";

        private const string NohesiGtrR35Vlct = @"{
  ""Type"": ""trueforce-car-preset"",
  ""Version"": 2,
  ""GameName"": ""AssettoCorsa"",
  ""CarId"": ""nohesi_realistic_nissan_gtr_r35_vlct"",
  ""PresetName"": ""nohesi_realistic_nissan_gtr_r35_vlct (default)"",
  ""IsBuiltin"": true,
  ""Override"": {
    ""EnginePulse"": {
      ""Enabled"": true,
      ""Gain"": 0.06518083,
      ""Cylinders"": 6,
      ""Pitch"": 1.00160933,
      ""LowpassHz"": 510.18329938900223,
      ""Waveform"": ""Triangle""
    },
    ""RoadBumps"": null,
    ""TractionLoss"": {
      ""Enabled"": true,
      ""Gain"": 0.121609129,
      ""Sensitivity"": 0.178701326,
      ""Freq"": 133.901657,
      ""NoiseLowpassHz"": 250.0,
      ""NoiseHighpassHz"": 30.0,
      ""Waveform"": ""Noise""
    },
    ""GearShift"": null,
    ""AbsClick"": {
      ""Enabled"": true,
      ""Gain"": 0.244887292,
      ""Freq"": 150.0,
      ""PulseFreq"": 9.821309,
      ""DutyCycle"": 0.331281453,
      ""TickDurationMs"": 35.0,
      ""Mode"": ""Pulse"",
      ""Waveform"": ""Square""
    },
    ""AudioCapture"": null
  }
}";
    }
}
