# Memory-preserving save portability plan

Status: implementation plan plus targeted transfer-tool checkpoint, 21 October 2026.

## Current behavior

`WorldStateStore.SnapshotForSave()` commits pending conversation records to immutable, parent-linked chunks and stores only `ArchiveId` plus `Head` in the save checkpoint (`src/PeopleOfTheIliacBay.Core/ConversationArchiveState.cs`). `SharedConversationArchive` hashes uncompressed chunk contents with SHA-256, verifies the full selected ancestry on load/export, and refuses missing or corrupt history rather than substituting an empty journal (`src/PeopleOfTheIliacBay.Core/SharedConversationArchive.cs`). A save therefore depends on `PeopleMemory` beside DFU's configured `Saves` directory; copying only `SAVE<n>` loses its committed history.

`tools/MemoryArchiveTool` already exports a save and only its referenced ancestry. Import requires a new slot, validates ZIP paths and the staged ancestry, keeps existing chunks, and refuses to run while DFU is open (`tools/MemoryArchiveTool/Program.cs`). Chunks are shared by content-addressed path, so identical ancestry does not need another copy. Historical real-save transfer acceptance is recorded in `docs/TESTING.md`; `tools/TestMemoryArchiveTransfer.ps1` now builds temporary save/archive fixtures and covers manifest integrity, legacy ZIP warning, no-overwrite, traversal, missing chunks and corrupt collisions. `docs/INSTALLATION.md#memory-backups` documents full `Saves` backup and source-workspace CLI commands.

The ordinary-player workflow remains incomplete. The tool requires the source workspace and .NET 8 SDK, asks users to supply paths and a destination slot, and has no self-contained offline package or guided recovery flow. The transfer tool writes a versioned manifest and validates its exact file set, lengths, checksums, checkpoint and ancestry before publishing chunks. It retains strict legacy ZIP import with a clear warning that save-file hashes are unavailable. `tools/TestMemoryArchiveTransfer.ps1` passes against temporary fixtures only; no player save or archive was opened. Import stages the incoming package, then publishes immutable memory chunks before moving the save slot; a failed final move can leave harmless unreferenced chunks. A whole-directory `Saves` backup is the current simple recovery method. This repository currently has no published release bundle or complete end-user installer (`docs/INSTALLATION.md`).

## Minimal implementation

Keep the shared archive format and save checkpoint model. Make the portable package the supported single-save handoff:

1. Keep one ZIP containing `Save/`, `PeopleMemory/`, and `manifest.json`. Include the exact `ArchiveId`, saved `Head`, ordered ancestry hashes, package format version, source save schema/mod version, and a relative-path, byte-length and SHA-256 entry for every packaged file. Include only ancestry reachable from that checkpoint. Never include sibling branches, the whole `PeopleMemory` directory, machine-specific absolute paths, or credentials. Existing per-chunk content hashes remain authoritative for ancestry validation.
2. Export only while DFU is closed. Copy the save and validated ancestry into a staging directory, write the manifest, verify every listed file, then publish the ZIP by atomic rename. A failed export leaves no partial destination package. Do not mutate the source save or archive.
3. Import only into a user-selected DFU `Saves` root and unused `SAVE<n>` slot. Validate the complete manifest, paths, sizes, package checksums, checkpoint and full ancestry before changing destination state. Stage missing chunks on the destination volume. For each existing content-addressed chunk, verify its content and reuse it; reject a corrupt collision and never overwrite it. Publish immutable missing chunks, then atomically move the staged save directory into the unused slot. A crash may leave only unreferenced immutable chunks, which are safe; retry verifies and reuses them. Never delete shared chunks during import or slot replacement.
4. Ship a self-contained offline Windows helper with the Nexus download, with file pickers for source save, package destination, package and destination save root. Show the resolved slot before import, reject occupied slots, explain that DFU must be closed, and report success only after the imported save and exact history are available. The helper reads the selected paths at run time; it stores no developer path, assumed install path or machine-specific configuration. Keep CLI entry points for support and automation.
5. On missing/corrupt history, preserve the current fail-closed behavior and show a direct recovery instruction: restore the matching full `Saves` backup, or import the matching portable package into a new slot. Do not invent repair, switch to another branch, discard the bad checkpoint or silently load without history. Keep the original save untouched until recovery is complete.

## Acceptance

- Export a real save with multiple divergent same-date branches. Package contains only chosen save's ancestry; manifest has no absolute paths. Verify payload and every checksum.
- Import offline into a clean save root and load it in DFU. Compare exact archive checkpoint and recall with source. Keep sibling/future branch history inaccessible from imported save.
- Test a shared chunk already present, a corrupt collision, missing/reordered/modified package entries, bad ancestry, traversal paths, oversized package, occupied destination and failed publication. Failure must preserve the source save and any existing destination save.
- Interrupt export before package rename and import before save-directory rename. No partial final package or partial save slot may appear. Orphaned content-addressed chunks are acceptable and must not be garbage-collected by this workflow.
- Restore an older full-directory backup and a single-save package without network access or installed .NET SDK. Exercise missing/corrupt archive guidance and confirm no history is silently replaced.
- Keep existing schema migration, checkpoint branch isolation, chunk SHA-256, export/import and legacy tests passing. Add focused tests for manifest validation, same-volume staged publication, idempotent retry and recovery UI. Native full-game confirmation remains separate from file-level validation.

## References

- `docs/ROADMAP.md`: IMM-05, IMM-06, IMM-20; “Portable saves and long-term memory”; “Active memory implementation contract”.
- `src/PeopleOfTheIliacBay.Core/ConversationArchiveState.cs`: save checkpoint and pending-memory publication.
- `src/PeopleOfTheIliacBay.Core/SharedConversationArchive.cs`: ancestry closure, chunk checksums, validation and immutable publish.
- `tools/MemoryArchiveTool/Program.cs` and `tools/TestMemoryArchiveTransfer.ps1`: current transfer behavior and regression coverage.
- `docs/INSTALLATION.md#memory-backups` and `docs/TESTING.md#prior-acceptance--shared-memory-archive-17-october-2026`: user instructions and verified limits.
