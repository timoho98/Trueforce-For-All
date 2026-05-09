// Resolves a (gameName, carId) pair to a cylinder count + EV flag for
// EnginePulseEffect. Three-stage cascade:
//
//   1. Baked lookup (BuiltinCarCylinders) — Kunos lineup + heuristic-derived
//      pre-bake covering ~91% of the typical AC library at ship time.
//   2. Persistent cache (Settings.CarCylinderCache) — heuristic results
//      from prior sessions for cars not in the bake. Cached forever per
//      install; cleared en masse when CarCylinderCacheVersion bumps.
//   3. Heuristic detector — for carIds that are in neither, we read the
//      game's per-car metadata (AC's content/cars/<carId>/ui/ui_car.json)
//      and pattern-match tags + description + name + carId.
//
// In-process cache: the resolver also keeps a process-lifetime cache so
// repeated car switches to the same carId don't re-hit disk during a
// single session. This is separate from (and faster than) the persistent
// cache, which only matters across SimHub restarts.
//
// AC install root discovery: parses Steam's libraryfolders.vdf to find
// app 244210 (Assetto Corsa). Cached after first successful lookup.
// Returns null if Steam isn't installed or AC isn't in any known library.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TrueforceForAll.Plugin
{
    internal static class CarCylinderResolver
    {
        // Result codes mirror BuiltinCarSpec but distinguish a miss (null)
        // from a hit that says "EV". Caller treats the IsElectric flag as
        // a directive to halve EnginePulse amplitude rather than override
        // the cylinder count.
        public sealed class Result
        {
            public int  Cylinders  { get; set; }
            public bool IsElectric { get; set; }
            public string Source   { get; set; }   // "baked", "tag", "desc", "codename", "chassis", "cylword"
        }

        // Process-lifetime cache. Two states:
        //   key present, value non-null  → hit, reuse
        //   key present, value null      → miss, don't re-probe disk
        //   key absent                   → not yet probed
        private static readonly Dictionary<string, Result> Cache =
            new Dictionary<string, Result>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new object();

        // Bump to invalidate persistent caches across all installs after a
        // heuristic improvement. Compared against Settings.CarCylinderCacheVersion
        // at AttachPersistentCache time; mismatch clears the cache and updates
        // the version stamp before any new entries are written.
        // v2: cascade reorder (codename before desc-layout), per-letter
        //     plausibility filter on V<n>/I<n>/etc, rotor-phrase detection,
        //     engine-context proximity check, chassis lookup before desc-layout.
        public const int CurrentCacheVersion = 2;

        // Persistent cache (settings-backed). Plugin calls AttachPersistentCache
        // on startup with the loaded Settings.CarCylinderCache reference; the
        // resolver reads through to it on miss and writes back on heuristic hit.
        // Cylinder encoding: positive = cyl count (1..16); -1 = EV sentinel
        // (caller treats this as "leave Cylinders alone, set AutoGainScale=0.5").
        private static Dictionary<string, Dictionary<string, int>> _persistentCache;
        private static readonly object PersistentLock = new object();
        private const int EvSentinel = -1;

        /// <summary>Attach the settings-backed persistent cache. Pass the live
        /// reference from TrueforceSettings; the resolver writes new entries
        /// through it, and the plugin's existing settings save flow flushes
        /// them to disk. Call once at plugin init. If currentVersion does not
        /// match CurrentCacheVersion, the cache is cleared and the version
        /// stamp updated.</summary>
        public static void AttachPersistentCache(
            Dictionary<string, Dictionary<string, int>> cache,
            ref int currentVersion)
        {
            lock (PersistentLock)
            {
                if (cache == null) return;
                if (currentVersion != CurrentCacheVersion)
                {
                    cache.Clear();
                    currentVersion = CurrentCacheVersion;
                }
                _persistentCache = cache;
            }
        }

        /// <summary>Try to resolve a cylinder count for a (game, car) pair.
        /// Returns false on miss; the caller should leave AutoCylinders
        /// alone (so the user's configured Cylinders applies) when this
        /// returns false.</summary>
        public static bool TryResolve(string gameName, string carId, out Result result)
        {
            result = null;
            if (string.IsNullOrEmpty(gameName) || string.IsNullOrEmpty(carId)) return false;

            string key = gameName + "|" + carId;
            lock (CacheLock)
            {
                if (Cache.TryGetValue(key, out var cached))
                {
                    result = cached;
                    return cached != null;
                }
            }

            Result resolved = ResolveInternal(gameName, carId);
            lock (CacheLock)
            {
                Cache[key] = resolved;   // store nulls too so misses don't re-probe
            }
            result = resolved;
            return resolved != null;
        }

        private static Result ResolveInternal(string gameName, string carId)
        {
            if (BuiltinCarCylinders.TryGet(gameName, carId, out var spec))
            {
                return new Result
                {
                    Cylinders  = spec.Cylinders,
                    IsElectric = spec.IsElectric,
                    Source     = "baked",
                };
            }

            // Persistent cache: prior heuristic hits saved across sessions.
            if (TryReadPersistent(gameName, carId, out var cached)) return cached;

            // Heuristic only implemented for AC today.
            if (string.Equals(gameName, "AssettoCorsa", StringComparison.OrdinalIgnoreCase))
            {
                var hit = TryAcHeuristic(carId);
                if (hit != null) WritePersistent(gameName, carId, hit);
                return hit;
            }

            return null;
        }

        private static bool TryReadPersistent(string gameName, string carId, out Result result)
        {
            result = null;
            lock (PersistentLock)
            {
                if (_persistentCache == null) return false;
                if (!_persistentCache.TryGetValue(gameName, out var inner)) return false;
                if (!inner.TryGetValue(carId, out var encoded)) return false;
                if (encoded == EvSentinel)
                {
                    result = new Result { Cylinders = 4, IsElectric = true, Source = "cache-ev" };
                    return true;
                }
                if (encoded < 1 || encoded > 16) return false;
                result = new Result { Cylinders = encoded, IsElectric = false, Source = "cache" };
                return true;
            }
        }

        private static void WritePersistent(string gameName, string carId, Result r)
        {
            if (r == null) return;
            lock (PersistentLock)
            {
                if (_persistentCache == null) return;
                if (!_persistentCache.TryGetValue(gameName, out var inner))
                {
                    inner = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    _persistentCache[gameName] = inner;
                }
                inner[carId] = r.IsElectric ? EvSentinel : r.Cylinders;
            }
        }

        // --------- Assetto Corsa heuristic ---------

        private static string _acRootCached;
        private static bool   _acRootResolved;

        private static Result TryAcHeuristic(string carId)
        {
            string root = GetAcInstallRoot();
            if (string.IsNullOrEmpty(root)) return null;

            string uiPath = Path.Combine(root, "content", "cars", carId, "ui", "ui_car.json");
            if (!File.Exists(uiPath)) return null;

            string raw;
            try
            {
                raw = File.ReadAllText(uiPath);
                if (raw.Length > 0 && raw[0] == '﻿') raw = raw.Substring(1);
            }
            catch { return null; }

            // Extract the three fields we care about. Use raw regex rather
            // than JSON parsing — some Kunos files have stray control chars
            // that trip strict parsers.
            string name = ExtractStringField(raw, "name");
            string desc = ExtractStringField(raw, "description");
            string[] tags = ExtractTagsArray(raw);
            string haystack = (name ?? "") + " " + string.Join(" ", tags ?? Array.Empty<string>())
                              + " " + (desc ?? "") + " " + carId;

            // Strip HTML entities so they don't fragment word-boundary matches
            haystack = Regex.Replace(haystack, "&[a-zA-Z]+;", " ");

            // 1. Tag layout (V8, I6, F4, etc.) — exact tag match
            if (tags != null)
            {
                foreach (var rawTag in tags)
                {
                    string t = (rawTag ?? "").Trim();
                    if (t.Length == 0) continue;
                    if (Regex.IsMatch(t, @"^(?i)(rotary|wankel)$"))
                    {
                        // Most AC rotaries are 13B 2-rotor → 4 effective cyl
                        return new Result { Cylinders = 4, Source = "tag-rotary" };
                    }
                    if (Regex.IsMatch(t, @"^(?i)(electric|ev|bev)$"))
                    {
                        return new Result { Cylinders = 4, IsElectric = true, Source = "tag-ev" };
                    }
                    var m = Regex.Match(t, @"^(?i)(?<L>[VILFBWS])(?<C>2|3|4|5|6|8|10|12|16)$");
                    if (m.Success)
                    {
                        return new Result
                        {
                            Cylinders = int.Parse(m.Groups["C"].Value),
                            Source = "tag",
                        };
                    }
                }
            }

            // 2. "X-cylinder" / "X cylinder" explicit phrase. Strong signal.
            //    Reject "valves per cylinder" by requiring a non-letter prefix.
            var cw = Regex.Match(haystack, @"(?<![a-zA-Z])(\d{1,2})[-\s]?cylinder\b", RegexOptions.IgnoreCase);
            if (cw.Success)
            {
                int n = int.Parse(cw.Groups[1].Value);
                if (n >= 2 && n <= 16)
                    return new Result { Cylinders = n, Source = "cylword" };
            }

            // 3. Engine codename lookup (LS-series, 2JZ, RB26, EJ20, K20, etc.).
            //    Promoted ahead of the generic desc-layout pattern because
            //    explicit engine names beat ambient product references like
            //    "Wisefab V3 steering" or "Audi B5 chassis" that the layout
            //    regex would otherwise misread as a cylinder declaration.
            //    Case-insensitive — modders often use "2jz" / "rb26" lowercase.
            foreach (var (pat, cyl) in EngineCodenames)
            {
                if (Regex.IsMatch(haystack, pat, RegexOptions.IgnoreCase))
                    return new Result { Cylinders = cyl, Source = "codename" };
            }

            // 4. Rotor-count phrases: "twin rotor", "2-rotor", "triple-rotor", etc.
            //    Maps rotor count → effective cyl: rotors × 2 (per the firing-
            //    frequency derivation — see EnginePulseEffect comment).
            var rp = Regex.Match(haystack,
                @"\b(twin|two|2|triple|three|3|quad|four|4)[-\s]?rotor[s]?\b",
                RegexOptions.IgnoreCase);
            if (rp.Success)
            {
                string w = rp.Groups[1].Value.ToLowerInvariant();
                int rotors = (w == "twin" || w == "two"   || w == "2") ? 2
                          : (w == "triple"|| w == "three" || w == "3") ? 3
                          : (w == "quad"  || w == "four"  || w == "4") ? 4
                          : 0;
                if (rotors > 0)
                    return new Result { Cylinders = rotors * 2, Source = "rotor-phrase" };
            }
            // Bare "rotor" / "wankel" without a count → assume 2-rotor (the
            // overwhelmingly common case in AC's catalog).
            if (Regex.IsMatch(haystack, @"\b(rotary engine|wankel)\b", RegexOptions.IgnoreCase))
            {
                return new Result { Cylinders = 4, Source = "desc-rotary" };
            }

            // 5. Word-form layouts: "inline-six", "flat-4", "boxer four"
            //    Moved before desc-layout because the layout-name word is
            //    self-validating (no false-positive risk).
            var dw = Regex.Match(haystack,
                @"\b(?:inline|straight|flat|boxer)[-\s](?:two|three|four|five|six|eight|ten|twelve|2|3|4|5|6|8|10|12)\b",
                RegexOptions.IgnoreCase);
            if (dw.Success)
            {
                string m = dw.Value.ToLowerInvariant();
                int? n = null;
                if (m.Contains("two")) n = 2;
                else if (m.Contains("three")) n = 3;
                else if (m.Contains("four")) n = 4;
                else if (m.Contains("five")) n = 5;
                else if (m.Contains("six")) n = 6;
                else if (m.Contains("eight")) n = 8;
                else if (m.Contains("ten")) n = 10;
                else if (m.Contains("twelve")) n = 12;
                else
                {
                    var dnum = Regex.Match(m, @"\d+");
                    if (dnum.Success) n = int.Parse(dnum.Value);
                }
                if (n.HasValue) return new Result { Cylinders = n.Value, Source = "desc-word" };
            }

            // 6. Brand+chassis fallback (E36 → I6, 911 → F6, Miata → I4, etc.)
            //    Promoted ahead of desc-layout because chassis codes are far
            //    less ambiguous than the V<n>/I<n> regex. A chassis hit on
            //    "gt86" cleanly wins over an unrelated "BDC V6" mention in
            //    the description (tuner-shop name, not an engine layout).
            //    Conservative — engine swaps will give wrong answers, but
            //    swaps are usually called out by codename (LS / JZ / RB)
            //    which the codename pass above already catches.
            foreach (var (pat, cyl) in ChassisLookup)
            {
                if (Regex.IsMatch(haystack, pat, RegexOptions.IgnoreCase))
                {
                    if (cyl == -1)   // rotary sentinel
                        return new Result { Cylinders = 4, Source = "chassis-rotary" };
                    return new Result { Cylinders = cyl, Source = "chassis" };
                }
            }

            // 7. Inline description layout patterns: "V8", "5.0L V8", "I6", etc.
            //    Last-resort because V<n>/I<n>/B<n> commonly appears in non-
            //    engine contexts (mod versions like "v2", chassis codes like
            //    "B5", tuner shop names like "BDC V6"). To suppress those, we
            //    require the match to be near a word that signals an engine
            //    description (engine, motor, swap, liter, HP, BHP, turbo,
            //    supercharged, etc.). Per-letter plausibility filter applies:
            //    no V2/V3 (real V3 engines don't exist in passenger cars),
            //    but I3 stays valid for Daihatsu Copen / GR Yaris.
            var dl = Regex.Match(haystack, @"\b([VILFBW])[-\s]?(2|3|4|5|6|8|10|12|16)\b");
            while (dl.Success)
            {
                string layout = dl.Groups[1].Value;
                int n = int.Parse(dl.Groups[2].Value);
                bool plausible =
                    (layout == "V" && (n == 4 || n == 6 || n == 8 || n == 10 || n == 12 || n == 16))
                 || (layout == "I" && (n == 3 || n == 4 || n == 5 || n == 6 || n == 8))
                 || (layout == "L" && (n == 4 || n == 5 || n == 6 || n == 8))
                 || (layout == "F" && (n == 4 || n == 6 || n == 8 || n == 12))
                 || (layout == "B" && (n == 4 || n == 6))
                 || (layout == "W" && (n == 12 || n == 16));
                if (plausible && HasEngineContext(haystack, dl.Index, dl.Length))
                    return new Result { Cylinders = n, Source = "desc" };
                dl = dl.NextMatch();
            }

            return null;
        }

        // True when the V<n>/I<n>/B<n> match is within ~40 chars of a word
        // that signals an engine description, so we don't false-positive on
        // tuner-shop names ("BDC V6"), version strings ("v2"), or chassis
        // codes ("F5") that share the same regex shape.
        private static bool HasEngineContext(string haystack, int matchIndex, int matchLen)
        {
            int from = Math.Max(0, matchIndex - 40);
            int to = Math.Min(haystack.Length, matchIndex + matchLen + 40);
            string ctx = haystack.Substring(from, to - from).ToLowerInvariant();
            // Whole-word checks via Contains are good enough — these tokens
            // rarely appear coincidentally in ~80-char windows.
            return ctx.Contains("engine") || ctx.Contains("motor")
                || ctx.Contains("powered") || ctx.Contains("power - ")
                || ctx.Contains("liter")  || ctx.Contains("litre")
                || ctx.Contains("turbo")  || ctx.Contains("supercharged")
                || ctx.Contains("naturally") || ctx.Contains("aspirated")
                || ctx.Contains("cylinder") || ctx.Contains("hp")
                || ctx.Contains("bhp") || ctx.Contains("displacement")
                || ctx.Contains("swap") || ctx.Contains("swapped")
                || Regex.IsMatch(ctx, @"\b\d+\.?\d*\s?[lL]\b");  // "5.0L", "4.0 L"
        }

        // ----- Field extractors (regex-based, JSON-tolerant) -----

        private static string ExtractStringField(string raw, string key)
        {
            // "key": "value with \"escapes\" and stuff"
            var m = Regex.Match(raw,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string[] ExtractTagsArray(string raw)
        {
            var m = Regex.Match(raw, "\"tags\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (!m.Success) return Array.Empty<string>();
            var inner = m.Groups[1].Value;
            var entries = Regex.Matches(inner, "\"((?:[^\"\\\\]|\\\\.)*)\"");
            var result = new List<string>(entries.Count);
            foreach (Match e in entries) result.Add(e.Groups[1].Value);
            return result.ToArray();
        }

        // ----- Lookup tables (must mirror probe_ac_cylinders.ps1) -----

        // Order matters: more-specific patterns first so "2JZ-GTE" matches
        // before generic "2JZ", and "LS3" before "LS" with no number.
        private static readonly (string Pat, int Cyl)[] EngineCodenames = new (string, int)[]
        {
            // JDM straight-six (I6)
            (@"\b2JZ(?:[-\s]?GTE|[-\s]?GE)?\b",   6),
            (@"\b1JZ(?:[-\s]?GTE|[-\s]?GE)?\b",   6),
            (@"\bRB2[567](?:DET{1,2})?\b",        6),
            (@"\bRB30\b",                         6),
            (@"\b7M[-\s]?GTE\b",                  6),
            (@"\bM5[02]B[0-9]+\b",                6),  // BMW M50/M52
            (@"\bS5[024]B[0-9]+\b",               6),  // BMW S50/S52/S54
            (@"\bN5[24]B[0-9]+\b",                6),  // BMW N52/N54
            (@"\bB58\b",                          6),
            // JDM straight-four (I4)
            (@"\b4[AG][-\s]?GE\b",                4),  // Toyota 4A-GE
            (@"\bSR20(?:DET|VE|DE)?\b",           4),
            (@"\bCA18(?:DET|DE)?\b",              4),
            (@"\bKA24(?:DE|E)?\b",                4),
            (@"\b4G6[34]\b",                      4),  // Mitsubishi 4G63/4G64
            (@"\bK20[ACZ]?\b",                    4),  // Honda K20
            (@"\bK24[AZ]?\b",                     4),
            (@"\bB1[68][AB][0-9]?\b",             4),  // Honda B-series
            (@"\bF20C\b",                         4),
            (@"\bH22A\b",                         4),
            (@"\b3SGTE\b",                        4),
            (@"\bS14[BE][0-9]+\b",                4),  // BMW S14 (E30 M3)
            // Subaru flat-four (F4)
            (@"\bEJ20[57]?\b",                    4),
            (@"\bEJ25[57]?\b",                    4),
            (@"\bFA20\b",                         4),
            // USDM V8
            (@"\bLS[1-9]X?\b",                    8),
            (@"\b5\.3[\s-]?LS\b",                 8),
            (@"\bLT[1-5]\b",                      8),
            (@"\bCoyote\b",                       8),
            (@"\bHEMI\b",                         8),
            (@"\bHellcat\b",                      8),
            // Nissan V6
            (@"\bVQ3[57]\b",                      6),
            (@"\bVR38\b",                         6),
            // Porsche
            (@"\bMezger\b",                       6),
            // Mazda rotary — rotor count × 2 = effective cyl in our formula:
            // 13B / Renesis (2-rotor) → 4, 20B (3-rotor) → 6, 26B (4-rotor) → 8.
            // 13B-MSP and 13B-REW are common variant names; include both.
            (@"\b13B(?:[-\s]?(?:REW|MSP|T))?\b",   4),
            (@"\bRenesis\b",                      4),
            (@"\b20B(?:[-\s]?REW)?\b",            6),
            (@"\b26B\b",                          8),
        };

        // -1 sentinel = rotary (treated as cyl=4 in the cascade above).
        private static readonly (string Pat, int Cyl)[] ChassisLookup = new (string, int)[]
        {
            // BMW chassis codes — straight-6 by default
            (@"\bE3[06]\b|\bE3[06][_\s-]",       6),
            (@"\bE36\b|\bE36[_\s-]",             6),
            (@"\bE46\b|\bE46[_\s-]",             6),
            (@"\bE9[02]\b|\bE9[02][_\s-]",       6),
            (@"\bF8[02]\b|\bF8[02][_\s-]",       6),
            (@"\bG8[02]\b|\bG8[02][_\s-]",       6),
            // Nissan
            (@"\b350Z\b|\bfairlady[_\s]?350|\bz33\b",                    6),
            (@"\b370Z\b|\bz34\b",                                         6),
            (@"\b300Z\b|\bz3[12]\b",                                      6),
            (@"\b240sx\b",                                                4),
            (@"\bskyline[_\s]?gtr?[_\s]?r3[234]|\bbnr3[24]\b|\bbcnr-?33\b|\br3[234][_\s-]?gtr?\b", 6),
            (@"\b(?:r3[1234]|hr3[12]|hcr3[12])\b",                        6),
            (@"\bs1[345]\b|\bsilvia[_\s]?s1[345]",                        4),
            (@"\b180sx\b|\bsil80\b|\bsileighty\b",                        4),
            (@"\blaurel\b|\bcefiro\b|\bstagea\b",                         6),
            // Mazda
            (@"\bmiata\b|\bmx[-_\s]?5\b|\bmx5\b",                         4),
            (@"\brx[-_\s]?[78]\b",                                       -1),  // rotary
            // Subaru
            (@"\bimpreza|\bwrx\b|\bsti\b|\bgrb\b|\bgda\b|\bgdb\b",        4),
            (@"\bbrz\b|\bgt86\b|\bft86\b|\b86\b",                         4),
            // Toyota
            (@"\bsupra[_\s]?(?:mk4|a80)|\bjza80\b",                       6),
            (@"\bsupra[_\s]?(?:mk5|a90|gr)\b",                            6),
            (@"\b(?:celica[_\s]?)?supra[_\s]?(?:mk2|a60)|\bma6[01]\b",    6),
            (@"\bae86\b|\bcorolla[_\s]?levin|\btrueno",                   4),
            (@"\bjzx[789]\d\b|\bcresta\b|\bchaser\b|\bsoarer\b|\bmark[_\s]?ii\b", 6),
            // Mitsubishi
            (@"\b(?:lancer[_\s])?evo(?:lution)?[_\s]?(?:[ivx]+|\d+)\b|\bevo[_\s]?[ivx]+\b", 4),
            // British / European
            (@"\bmini[_\s]?(?:cooper|hatch|clubman|countryman)\b|\baustin[_\s]?mini\b", 4),
            // USDM
            (@"\bviper\b|\brt[/_\s-]?10\b",                               10),
            (@"\bf-?150\b|\bsilverado\b|\bram[_\s]?(?:1500|2500)\b",       8),
            // Porsche
            (@"\b911\b|\bcayman\b|\bboxster\b|\b993\b|\b996\b|\b997\b|\b991\b|\b992\b|\b964\b", 6),
            // Honda
            (@"\bs2000\b|\bap[12]\b",                                     4),
            (@"\bcivic\b|\bintegra\b|\btype[-_\s]?r\b",                   4),
            (@"\bnsx\b",                                                  6),
            // Ford / Chevy / Dodge — usually V8 in AC mods
            (@"\bmustang\b",                                              8),
            (@"\bcamaro\b|\bcorvette\b|\bvette\b|\bc[5678]\b",            8),
            (@"\bchallenger\b|\bcharger\b",                               8),
        };

        // ----- AC install root discovery (Steam VDF) -----

        public static string GetAcInstallRoot()
        {
            if (_acRootResolved) return _acRootCached;
            _acRootCached = FindAcInstallRoot();
            _acRootResolved = true;
            return _acRootCached;
        }

        private static string FindAcInstallRoot()
        {
            // 1. Find Steam install dir via registry (HKCU first, then HKLM).
            string steamPath = ReadRegistryString(Registry.CurrentUser,
                @"Software\Valve\Steam", "SteamPath");
            if (string.IsNullOrEmpty(steamPath))
            {
                steamPath = ReadRegistryString(Registry.LocalMachine,
                    @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            }
            if (string.IsNullOrEmpty(steamPath)) return null;

            // 2. Read libraryfolders.vdf and probe each "path" for AC.
            string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath)) return TryDirectAcUnder(steamPath);

            string vdf;
            try { vdf = File.ReadAllText(vdfPath); }
            catch { return TryDirectAcUnder(steamPath); }

            // VDF has shape:
            //   "libraryfolders" { "0" { "path" "C:\\Steam" "apps" { "244210" "..." } } "1" {...} }
            // We want each "path" entry, then check it for AC.
            var paths = Regex.Matches(vdf, "\"path\"\\s*\"([^\"]+)\"");
            foreach (Match pm in paths)
            {
                string libPath = pm.Groups[1].Value.Replace("\\\\", "\\");
                string candidate = Path.Combine(libPath, "steamapps", "common", "assettocorsa");
                if (Directory.Exists(candidate)) return candidate;
            }

            return TryDirectAcUnder(steamPath);
        }

        private static string TryDirectAcUnder(string steamPath)
        {
            string candidate = Path.Combine(steamPath, "steamapps", "common", "assettocorsa");
            return Directory.Exists(candidate) ? candidate : null;
        }

        private static string ReadRegistryString(RegistryKey root, string subKey, string valueName)
        {
            try
            {
                using (var k = root.OpenSubKey(subKey))
                {
                    return k?.GetValue(valueName) as string;
                }
            }
            catch { return null; }
        }
    }
}
