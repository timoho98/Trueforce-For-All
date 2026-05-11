// Catalog of effects + per-version changelog entries that drive the
// "what's new" banner and the per-effect NEW badges.
//
// Two ideas live here:
//   - KnownEffectIds is the source of truth for what's shippable today.
//     On fresh install (or first run on a pre-feature settings file) the
//     plugin pre-marks every one as already seen, so existing users don't
//     get a wall of badges for effects they've already been tuning.
//   - Versions is an append-only, chronologically-ordered list. The banner
//     shows everything strictly newer than the user's LastSeenVersion;
//     dismissing stamps LastSeenVersion to the running build so the same
//     entries don't pop up twice.
//
// To ship a new effect:
//   1. Add its stable ID string to KnownEffectIds.
//   2. Add a ChangelogVersion to the tail of Versions with one ChangelogEntry
//      per user-visible thing (EffectId = the same string for items that
//      should also trigger a per-section "NEW" badge).

using System;
using System.Collections.Generic;

namespace TrueforceForAll.Plugin
{
    public sealed class ChangelogEntry
    {
        // Optional. When set, drives the per-effect "NEW" badge on this
        // section's expander header. When null, the entry is just a note
        // surfaced in the banner modal (e.g. behavior change, default
        // tweak) with no badge.
        public string EffectId { get; set; }
        public string Headline { get; set; }
        public string Description { get; set; }
    }

    public sealed class ChangelogVersion
    {
        public Version Version { get; set; }
        public string Title { get; set; }
        public List<ChangelogEntry> Entries { get; set; } = new List<ChangelogEntry>();
    }

    public static class EffectChangelog
    {
        // Stable IDs for every effect that gets a per-effect "NEW" badge.
        // Strings match TrueforcePlugin.SectionKind names so the existing
        // dirty-tracking + revert plumbing can share the same identifiers.
        // Append-only: removing or renaming an entry invalidates any user's
        // persisted SeenEffects entry for it.
        public static readonly IReadOnlyList<string> KnownEffectIds = new[]
        {
            "Audio", "Engine", "Bumps", "Traction", "Shift",
            "Abs", "PitLimiter", "Drs", "Collision",
        };

        // Ordered oldest -> newest. Append-only.
        public static readonly IReadOnlyList<ChangelogVersion> Versions = new ChangelogVersion[]
        {
            new ChangelogVersion {
                Version = new Version(0, 1, 0),
                Title = "Per-effect 'NEW' badges + this changelog",
                Entries = new List<ChangelogEntry>
                {
                    new ChangelogEntry {
                        Headline = "Effects added after you upgrade will be flagged with a NEW badge",
                        Description = "The badge stays on each effect's section header until you expand it or change a value, so you'll always know which voices are new without having to read release notes.",
                    },
                },
            },
        };

        /// <summary>Returns every changelog version strictly newer than the
        /// given baseline. Pass null to get the full history.</summary>
        public static IReadOnlyList<ChangelogVersion> EntriesNewerThan(Version since)
        {
            var list = new List<ChangelogVersion>();
            foreach (var v in Versions)
            {
                if (since == null || v.Version > since) list.Add(v);
            }
            return list;
        }

        /// <summary>Best-effort System.Version parse. Returns false (and a
        /// null out) for null / empty / malformed strings so callers can
        /// treat any of those uniformly as "no last-seen version".</summary>
        public static bool TryParseVersion(string s, out Version v)
        {
            v = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            try { v = new Version(s); return true; }
            catch { return false; }
        }
    }
}
