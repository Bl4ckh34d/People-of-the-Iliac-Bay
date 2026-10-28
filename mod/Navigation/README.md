# Baked native navigation inputs

This catalogue and its 120 referenced Unity NavMesh assets are required build inputs,
recovered unchanged from the existing Unity project on 28 October 2026. They cover
122 module records and occupy approximately 5.86 MiB. Keep them with the mod source,
outside disposable `work/` output.

`Deploy.ps1` copies these assets into the Unity project. `NativeNavigationBake`
prefers an explicit `work/navigation-geometry/navigation-manifest.json` capture,
when present, and otherwise uses this catalogue. Existing native geometry,
variant and provider validation remains enabled at runtime.

Raw `.navsource` geometry was not recovered. The baked assets can be packaged and
used unchanged; changing their geometry requires the normal explicit native capture
and bake workflow. Do not present reuse as a fresh geometry capture or gameplay
acceptance. After a deliberate capture/build update, retain its verified catalogue
and referenced assets here before cleaning disposable capture files.
