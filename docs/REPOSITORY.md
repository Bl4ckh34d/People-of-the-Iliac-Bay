# Repository setup and development baseline

Repository: https://github.com/Bl4ckh34d/People-of-the-Iliac-Bay

The existing local Git history predates the GitHub repository. Initialization preserves that history and the remote README history; it does not reset the project to a new empty repository. The initial publication is a development baseline containing unfinished work, not a release tag.

## Local layout

The current Windows scripts expect this sibling layout by default:

```text
DFU/
  LivingCities/       # this repository; retain this folder name for current scripts
  daggerfall-unity/   # separate DFU 1.1.1 source checkout
  Player/            # separately installed DFU player and portable app data
  GameFiles/         # user's game data
  downloads/         # external mod archives
  work/              # machine-local build evidence
```

Clone with an explicit folder name if setting up a new machine:

```sh
git clone https://github.com/Bl4ckh34d/People-of-the-Iliac-Bay.git LivingCities
```

Core tests require only the .NET 8 SDK and restored NuGet packages. The .NET Framework runtime/hook projects additionally require assemblies from the sibling player. `tools/Deploy.ps1` expects `work/Harmony/net472/0Harmony.dll` and the built hook assembly. This baseline does not yet provide a complete clean-machine dependency bootstrap. A fresh clone alone cannot package the Unity mod.

`tools/Build.ps1` uses Unity 2019.4.40f1 at its configured Windows Hub path, deploys into the separate DFU project's ignored mod directory, verifies and packages the mod. It refuses to overwrite a mod loaded by a running game. `tools/RuntimeCompile/RuntimeCompile.csproj` provides a faster source compilation check against installed assemblies.

## What is excluded

Runtime logs, local `work/`, `dist/`, `bin/`, `obj/`, downloaded bundles/archives, model weights, credentials and extracted native reference PNG directories are excluded. External mods remain separate installations. Decompiled external source and extracted quest scripts remain under ignored local audit directories; only findings and source identifiers are committed.

Generated mod artwork and its provenance metadata remain part of the project. Rejected pilots are identified explicitly in their art READMEs and must not be mistaken for accepted animation coverage. Historical commits are retained.

## Verification boundaries

The first GitHub baseline passes 647 core tests and runtime source compilation. It does not imply every included development change has a new Unity build or live acceptance. In particular, the saved-door survey and current/next destination changes require their remaining targeted native checks. Do not infer game readiness from the repository being clean.

Track future behavior changes in focused commits. Keep compatibility claims tied to named tests and distinguish source implementation, compilation, bundle installation and gameplay verification.
