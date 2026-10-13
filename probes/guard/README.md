# Guard interception feasibility probe

This isolated M1 experiment tests standard-mod runtime patching before choosing an engine modification. It is not a guard implementation and does not ship with Living Cities.

Acceptance sequence:

1. Obtain pinned Harmony 2.3.6 fat release from its official GitHub release. Extract the net472 assembly into ignored `work/Harmony`. Record its source and hash. The DFU player's Mono backend and .NET 4.x compatibility profile are inspected in the pinned project settings.
2. Compile this probe against the installed player's assemblies and Harmony. Package both assemblies as `.dll.bytes` in a separate temporary standard DFU mod. DFU's source compiler skips assembly references loaded from bytes, so the probe must be precompiled instead of referencing Harmony from `.cs.txt`.
3. Launch only the isolated test player with `-livingcities-guard-probe`. Patch both public entry points; prove immediate/deferred calls are intercepted and the single-guard call returns the supplied sentinel. Never execute original spawn behavior in the test.
4. Remove only this probe's patches, confirm removal, preserve log evidence, stop the owned test process, and remove the temporary package. The main mod and Basic Roads remain installed.
5. A passing probe proves method interception in the tested Mono player only. Actual roster dispatch, all crime/quest callers, arrests and return-value expectations still require behavioral integration tests.

Primary sources: [Harmony release](https://github.com/pardeike/Harmony/releases/tag/v2.3.6.0), [manual patching](https://harmony.pardeike.net/v2/articles/basics.html), [prefix semantics](https://harmony.pardeike.net/v2/articles/patching-prefix.html). Harmony is MIT licensed; preserve its license if later redistributed.

## Verified result, 13 October 2026

Passed on the pinned Windows Mono player. `work/guard-probe-player.log` contains `LIVING_CITIES_GUARD_PROBE_PASS`; Unity bundle build exited 0. Only the owned test process was stopped. The temporary `.dfmod` and its Unity staging directory were removed afterward. Main Living Cities and Basic Roads remain installed.

The net472 `0Harmony.dll` local SHA-256 is `7051d2c1154db0cba40fc0347caae76e64817f32d3faa4d420a9a19048d32842`. This is a local fingerprint, not an independently published checksum. The release's license is preserved as `Harmony-LICENSE.txt`.

Reproduction: extract the pinned fat release to `work/Harmony`; run `dotnet build probes/guard/GuardProbe.csproj -c Release`, deploy the editor helper with `tools/Deploy.ps1`, then Unity's `-executeMethod LivingCitiesBuild.BuildGuardProbe`. Run `tools/SmokeTest.ps1 -GuardProbe`. Remove the temporary `livingcitiesguardprobe.dfmod` afterward. Never package this probe as part of the gameplay mod.
