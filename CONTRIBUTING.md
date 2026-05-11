# Contributing

Contributions are welcome. The project is small and pre-1.0 so most of
the conventions below are guidance, not rules.

## Reporting bugs

Hardware-dependent bugs are nearly impossible to debug without context.
When opening an issue, please include:

- SimHub version
- Wheel model + USB ID (in the plugin's Status section, or from G HUB /
  USBPcap)
- Game and version
- Whether G HUB was running
- USBPcap version
- Relevant lines from `SimHub.log`

## Submitting a change

- Open an issue first if you're planning a non-trivial change, so we
  can agree on scope before you sink time into it. Small fixes and
  obvious improvements are fine to PR directly.
- Match existing style; no formal style guide.
- The plugin targets `net48` (SimHub is 32-bit) and the audio helper
  targets `net8.0`. Don't introduce dependencies that pull in heavy
  transitive libs.
- If you have a supported wheel, please test on real hardware before
  opening the PR. There is no automated hardware suite.
- Default presets are tuned values exported from real driving sessions.
  If you're proposing changes to a default preset, mention which car /
  track / driving conditions you tuned it under.

## Building locally

You'll need .NET 8 SDK.

```powershell
dotnet build src\TrueforceForAll.Plugin\TrueforceForAll.Plugin.csproj -c Release
dotnet publish src\TrueforceForAll.LoopbackHelper\TrueforceForAll.LoopbackHelper.csproj -c Release -r win-x64
```

The plugin csproj resolves SimHub assemblies via `$(SimHubPath)`, defaulting to `C:\Program Files (x86)\SimHub`. Override with `-p:SimHubPath="..."` if SimHub lives elsewhere. Drop the build outputs into your SimHub install folder and reload the plugin in SimHub. To produce a full installer artifact, see `Cutting a release` below.

## Cutting a release

Releases are built **locally** because the CI runner image doesn't have
SimHub installed and the plugin csproj references SimHub's redistributable
DLLs by hint path. The `release.yml` workflow exists but is
`workflow_dispatch` only and skipped by default; see the comment at the
top of that file for what would need to change to re-enable it.

Three places hold the version string and they must stay in sync. Two read
the runtime version from assembly metadata (which the csproj `<Version>`
populates automatically) and one is a hard-coded attribute that doesn't.

For each release:

1. Bump `<Version>X.Y.Z</Version>` in
   `src/TrueforceForAll.Plugin/TrueforceForAll.Plugin.csproj`. This drives
   the assembly version, the in-panel header readout, the auto-updater's
   "current version," and the User-Agent it sends to GitHub.
2. Update the version in the `[PluginDescription]` attribute at the top of
   `src/TrueforceForAll.Plugin/TrueforcePlugin.cs` (the `(vX.Y.Z)` suffix).
   This is the version SimHub shows in its add/remove plugins UI.
3. Update `README.md` if any user-visible feature changed (especially the
   supported-games or wheels tables, install steps, known limitations).
4. Hardware-validate any new telemetry source or game-detection change on
   the rig before tagging.
5. Commit. Tag `vX.Y.Z` matching the csproj version exactly. Push the
   branch and the tag.
6. Build the installer locally:

   ```powershell
   dotnet build src\TrueforceForAll.Plugin\TrueforceForAll.Plugin.csproj -c Release
   dotnet publish src\TrueforceForAll.LoopbackHelper\TrueforceForAll.LoopbackHelper.csproj -c Release -r win-x64
   # confirm installer\vendor\USBPcapSetup.exe is present
   & "C:\Program Files (x86)\Inno Setup 6\iscc.exe" installer\TrueforceForAll.iss
   ```

   The artifact lands at `installer\output\TrueforceForAll-Setup.exe`.
7. Upload to a draft GitHub release:

   ```powershell
   gh release create vX.Y.Z installer\output\TrueforceForAll-Setup.exe `
       --draft --title "Trueforce For All vX.Y.Z" --generate-notes
   ```
8. On GitHub, open the draft release. Edit the auto-generated notes if
   needed. Tick "Set as the latest release." Click Publish. Until you do
   this, the auto-updater will not see it because `/releases/latest`
   skips drafts and pre-releases.
9. After publishing, reload the plugin in SimHub on a test machine to
   confirm the update banner appears and the installer downloads.

## License

By submitting a change, you agree that your contribution is licensed
under the same GPL-2.0-only terms as the rest of the project.
