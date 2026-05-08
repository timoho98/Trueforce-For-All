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
  opening the PR. CI builds the installer on tag pushes but does not
  run a hardware suite (there isn't one).
- Default presets are tuned values exported from real driving sessions.
  If you're proposing changes to a default preset, mention which car /
  track / driving conditions you tuned it under.

## Building locally

See `Build from source` in the [README](README.md).

## License

By submitting a change, you agree that your contribution is licensed
under the same GPL-2.0-only terms as the rest of the project.
