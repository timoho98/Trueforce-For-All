// Per-car preset file storage, multi-preset model.
//
// Each car can have multiple named presets stored as separate files at
// <SimHub>/PluginsData/Common/TrueforceCars/<carId>~<presetName>.tfcar.json.
// Files contain a CarPresetFile (CarId, PresetName, IsBuiltin, GameName,
// Override). The on-disk schema is the same one used for shareable
// exports, so a file in this folder is directly importable / exportable
// without translation.
//
// Two preset kinds:
//   - Built-in: shipped with the plugin via BuiltinCarPresets and written
//     by InstallOrUpdateBuiltinCarPresets on every Init. Idempotent if the
//     content matches; updates the file when the bundled JSON differs from
//     what's on disk. The runtime refuses to delete or overwrite-in-place
//     these (the Save flow forks to a new user preset instead).
//   - User: created when the user explicitly saves a tuning. Free to
//     edit, rename, delete.
//
// Active-preset selection lives in TrueforceSettings.CarDefaults
// (carId -> presetName), mirroring GameDefaults. This class only handles
// storage; the plugin layer owns activation logic.
//
// File-naming uses '~' as the carId/presetName separator because '~' is
// always valid in NTFS filenames and avoids the '__' collision risk if
// either carId or presetName contains underscores. Sanitization replaces
// any actual '~' in carId or presetName with '-' to keep the separator
// unambiguous.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TrueforceForAll.Core;

namespace TrueforceForAll.Plugin
{
    /// <summary>One car preset's worth of data, surfaced to public callers
    /// (UI dropdown, plugin queries). Public because TrueforcePlugin exposes
    /// it from public APIs; the underlying CarPresetStore stays internal.</summary>
    public sealed class CarPresetEntry
    {
        public string CarId       { get; set; }
        public string PresetName  { get; set; }
        public string GameName    { get; set; }
        public bool   IsBuiltin   { get; set; }
        public CarOverride Override { get; set; }
    }

    internal sealed class CarPresetStore
    {
        private const string FolderName   = "TrueforceCars";
        private const string FileExtension = ".tfcar.json";
        private const char   Separator    = '~';

        private readonly string _folderPath;
        private readonly Action<string> _log;

        public CarPresetStore(Action<string> log = null)
        {
            // SimHub's plugin host sets BaseDirectory to its install dir; the
            // common-settings folder is the convention for plugin data.
            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            _folderPath = Path.Combine(baseDir, "PluginsData", "Common", FolderName);
            _log = log;
        }

        public string FolderPath => _folderPath;

        /// <summary>Walks the folder and returns every car preset file
        /// indexed by carId then presetName. Skips malformed files with a
        /// log line. Legacy single-preset-per-car files (no '~' in the name,
        /// or no PresetName field) are surfaced as preset entries with
        /// PresetName == CarId so the migration step can rewrite them.</summary>
        public Dictionary<string, Dictionary<string, CarPresetEntry>> LoadAll()
        {
            var result = new Dictionary<string, Dictionary<string, CarPresetEntry>>();
            try
            {
                if (!Directory.Exists(_folderPath)) return result;
                foreach (var path in Directory.GetFiles(_folderPath, "*" + FileExtension))
                {
                    try
                    {
                        var json = File.ReadAllText(path);
                        var f = JsonConvert.DeserializeObject<CarPresetFile>(json);
                        if (f == null || string.IsNullOrEmpty(f.CarId) || f.Override == null) continue;

                        // Legacy files (v1, written before the multi-preset
                        // schema) lack PresetName. Surface them with
                        // PresetName=CarId so migration in TrueforcePlugin
                        // can rewrite under the new naming convention.
                        string presetName = string.IsNullOrEmpty(f.PresetName) ? f.CarId : f.PresetName;
                        var entry = new CarPresetEntry
                        {
                            CarId      = f.CarId,
                            PresetName = presetName,
                            GameName   = f.GameName ?? "",
                            IsBuiltin  = f.IsBuiltin,
                            Override   = f.Override,
                        };
                        if (!result.TryGetValue(f.CarId, out var perCar))
                        {
                            perCar = new Dictionary<string, CarPresetEntry>(StringComparer.Ordinal);
                            result[f.CarId] = perCar;
                        }
                        perCar[presetName] = entry;
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"[Trueforce] Skipping malformed car preset '{Path.GetFileName(path)}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Trueforce] LoadAll car presets failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>Writes a car preset file for (carId, presetName). Empty
        /// overrides are deleted instead of written. Throws nothing; logs on
        /// I/O failure.</summary>
        public void Save(string carId, string presetName, string gameName, CarOverride ovr, bool isBuiltin = false)
        {
            if (string.IsNullOrEmpty(carId) || string.IsNullOrEmpty(presetName)) return;
            if (ovr == null || ovr.IsEmpty)
            {
                Delete(carId, presetName);
                return;
            }
            try
            {
                Directory.CreateDirectory(_folderPath);
                var path = PathFor(carId, presetName);
                var f = new CarPresetFile
                {
                    GameName   = gameName ?? "",
                    CarId      = carId,
                    PresetName = presetName,
                    IsBuiltin  = isBuiltin,
                    Override   = ovr,
                };
                AtomicWriteAllText(path, JsonConvert.SerializeObject(f, Formatting.Indented));
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Trueforce] Save car preset '{carId}/{presetName}' failed: {ex.Message}");
            }
        }

        /// <summary>Deletes a car preset file. No-op if it doesn't exist.</summary>
        public void Delete(string carId, string presetName)
        {
            if (string.IsNullOrEmpty(carId) || string.IsNullOrEmpty(presetName)) return;
            try
            {
                var path = PathFor(carId, presetName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Trueforce] Delete car preset '{carId}/{presetName}' failed: {ex.Message}");
            }
        }

        /// <summary>True iff a file already exists for (carId, presetName).</summary>
        public bool Exists(string carId, string presetName)
            => !string.IsNullOrEmpty(carId)
            && !string.IsNullOrEmpty(presetName)
            && File.Exists(PathFor(carId, presetName));

        /// <summary>Writes (or overwrites) every built-in car preset.
        /// Always called on Init. User-saved files (different filenames) are
        /// untouched. If a built-in preset with the same name already exists
        /// on disk, the file content is rewritten from the bundled JSON, so
        /// shipping an updated default in a new plugin release just works.
        ///
        /// Each value in <paramref name="presetJsons"/> is the full
        /// CarPresetFile JSON (carId, presetName, isBuiltin=true, override).
        /// The carId and presetName are read out of the JSON to derive the
        /// filename; the key in the dictionary is informational.</summary>
        public int InstallOrUpdateBuiltinCarPresets(IReadOnlyDictionary<string, string> presetJsons)
        {
            if (presetJsons == null) return 0;
            int written = 0;
            try
            {
                Directory.CreateDirectory(_folderPath);
                foreach (var kv in presetJsons)
                {
                    try
                    {
                        var f = JsonConvert.DeserializeObject<CarPresetFile>(kv.Value);
                        if (f == null || string.IsNullOrEmpty(f.CarId) || string.IsNullOrEmpty(f.PresetName)
                            || f.Override == null)
                        {
                            _log?.Invoke($"[Trueforce] Skipping malformed builtin car preset '{kv.Key}'.");
                            continue;
                        }
                        // Force IsBuiltin=true on disk regardless of what
                        // the bundled JSON says, so a future bundled JSON
                        // missing the flag still lands as a built-in.
                        f.IsBuiltin = true;
                        var path = PathFor(f.CarId, f.PresetName);
                        var json = JsonConvert.SerializeObject(f, Formatting.Indented);

                        // Skip rewrite when on-disk content already matches:
                        // saves disk churn on every Init when nothing changed.
                        if (File.Exists(path))
                        {
                            try
                            {
                                if (File.ReadAllText(path) == json) continue;
                            }
                            catch { /* fall through to write */ }
                        }

                        AtomicWriteAllText(path, json);
                        written++;
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"[Trueforce] Builtin car preset '{kv.Key}' write failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Trueforce] InstallOrUpdateBuiltinCarPresets failed: {ex.Message}");
            }
            return written;
        }

        /// <summary>Walks the folder for legacy v1 files (no '~' in the
        /// filename) and rewrites each as a v2 file under the new
        /// <c>&lt;carId&gt;~&lt;carId&gt;.tfcar.json</c> name with
        /// PresetName=CarId and IsBuiltin=false. Returns the list of carIds
        /// that were migrated so the plugin can set CarDefaults entries for
        /// them (mapping the active per-car override to the migrated user
        /// preset).</summary>
        public List<string> MigrateLegacyFiles()
        {
            var migrated = new List<string>();
            try
            {
                if (!Directory.Exists(_folderPath)) return migrated;
                foreach (var path in Directory.GetFiles(_folderPath, "*" + FileExtension))
                {
                    string fileName = Path.GetFileNameWithoutExtension(path);
                    // GetFileNameWithoutExtension strips only the last extension,
                    // so for "x.tfcar.json" it returns "x.tfcar". Strip ".tfcar".
                    if (fileName.EndsWith(".tfcar", StringComparison.Ordinal))
                        fileName = fileName.Substring(0, fileName.Length - ".tfcar".Length);
                    if (fileName.IndexOf(Separator) >= 0) continue; // already v2
                    try
                    {
                        var json = File.ReadAllText(path);
                        var f = JsonConvert.DeserializeObject<CarPresetFile>(json);
                        if (f == null || string.IsNullOrEmpty(f.CarId) || f.Override == null) continue;
                        // Old files always get treated as user presets named
                        // after the carId. The user can rename via UI later.
                        f.PresetName = string.IsNullOrEmpty(f.PresetName) ? f.CarId : f.PresetName;
                        f.IsBuiltin  = false;
                        f.Version    = 2;

                        var newPath = PathFor(f.CarId, f.PresetName);
                        var newJson = JsonConvert.SerializeObject(f, Formatting.Indented);
                        AtomicWriteAllText(newPath, newJson);
                        if (!string.Equals(path, newPath, StringComparison.OrdinalIgnoreCase))
                            File.Delete(path);
                        migrated.Add(f.CarId);
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"[Trueforce] Skipping legacy car preset '{Path.GetFileName(path)}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Trueforce] MigrateLegacyFiles failed: {ex.Message}");
            }
            return migrated;
        }

        // Sanitize for filesystem: replace invalid filename chars with '_',
        // and replace any '~' with '-' so it doesn't collide with the
        // carId/presetName separator. Preset names with backslashes, slashes,
        // colons (etc.) are safe to use in JSON; they just can't appear in
        // the filename.
        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var arr = s.ToCharArray();
            var invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == Separator) arr[i] = '-';
                else if (Array.IndexOf(invalid, arr[i]) >= 0) arr[i] = '_';
            }
            return new string(arr);
        }

        private string PathFor(string carId, string presetName)
            => Path.Combine(_folderPath,
                Sanitize(carId) + Separator + Sanitize(presetName) + FileExtension);

        // Atomic write: stage to <path>.tmp then swap into place. A crash
        // mid-write leaves either the old file (if the swap hadn't started)
        // or a stray .tmp (cleaned up on next save), never a truncated
        // .tfcar.json that the loader would then have to skip.
        private static void AtomicWriteAllText(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            if (File.Exists(path))
                File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tmp, path);
        }
    }
}
