# PeopleOfTheIliacBay naming refactor

**Goal:** Apply the user's chosen name throughout active identifiers, source/project/script filenames, package, tools and current documentation. Convert the owned test fixture offline; personal-save compatibility is out of scope.

**Architecture:** This is a coordinated rename. Preserve the stable mod GUID. After the initial plan was written, the user narrowed scope to owned test fixtures only and rejected compatibility aliases or personal-save migration. Parent converts the owned fixture offline with a backup. Preserve historical evidence and raw logs exactly as recorded. Keep the external checkout path unchanged.

**Tech stack:** C# 7.3 / netstandard2.0, DFU runtime net472, Unity 2019, PowerShell and Python.

## Constraints

- The user explicitly requests this refactor after the current action fix passed; the user later narrowed persistence work to owned fixtures only.
- Use one explicitly selected Luna/high worker for the coordinated rename, with parent review and independent verification.
- Preserve the integrated dirty checkout; do not reset, clean, broadly stage or commit unrelated work.
- Do not rewrite raw `work/` evidence or historical docs. The parent converts only the owned native test fixture offline; active source and tools accept only the new names.
- Do not edit or deploy the external DFU/Player directories from the worker. Parent owns guarded deployment and removal/archive of obsolete managed artifacts.

## Steps

- [x] Inventory naming and compatibility hazards. Approximately 698 active text files contain old naming; mod GUID is `e527783b-394c-4f43-b5f4-0cabd3d05dd4`.
- [x] Save a hash-verified source checkpoint and the tested package before any rename.
- [x] Rename active namespaces/types, source/project/test directories and filenames, build references, tools/probes and live branding together. Use `PeopleOfTheIliacBay` for identifiers and `People of the Iliac Bay` for prose.
- [x] Verify active persistence paths, assembly lookup and package identity. Parent converts the owned fixture data offline; do not add legacy-reader migration or aliases. Preserve the mod GUID.
- [x] Update current documentation and commands. Keep truthful historical evidence paths unchanged and document any remaining old-name exceptions.
- [x] Parent reviews the rename manifest and all compatibility changes; run Core, adapter, Python, runtime compile and tool self-tests affected by renamed project references.
- [x] Parent archives obsolete managed external assets/packages only after resolving and verifying exact target paths, then builds and installs the renamed package.
- [x] Run a native acceptance test loading an existing disposable save under the renamed mod, including physical movement/Wait. Verify that only one main package with the stable GUID is active.
- [x] Record verified results and remaining exceptions in documentation and Obsidian; leave the broader roadmap incomplete. See `../specs/2026-10-20-branding-rename-acceptance.md`.
