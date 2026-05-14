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
            };

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
