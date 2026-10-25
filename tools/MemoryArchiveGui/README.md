# Guided portable saves for Windows

Keep `MemoryArchiveGui.exe` and `MemoryArchiveTool.exe` in the same folder. Start
`MemoryArchiveGui.exe`. The self-contained win-x64 package runs offline without an
installed .NET SDK or runtime.

Close Daggerfall Unity before exporting or importing. The helper refuses transfers
while the game is running. It does not select a developer's save path or remember
your choices between launches.

## Export

1. Select the existing `SAVE<n>` folder that contains `mod_peopleoftheiliacbay.txt`.
2. Select a new ZIP filename. Existing packages are never overwritten.
3. Review the resolved source and package paths, then confirm the export.
4. Wait for completion. Keep the portable ZIP with your backup.

Only this save and its referenced memory ancestry are included. The helper does
not export sibling branches or the whole shared archive.

## Import

1. Select the portable ZIP.
2. Select your configured DFU `Saves` folder. Use the correct folder for the DFU
   installation/profile where you intend to load the save.
3. Keep the suggested unused `SAVE<n>` slot or enter another unused slot. The
   helper displays the full destination before you confirm.
4. Confirm and wait for completion. Start DFU only after the helper finishes.

The CLI validates the package, checkpoint and exact ancestry before publishing
the new slot. Existing slots and packages are never replaced. Legacy packages
remain supported; their missing save-file manifest checks are reported in the
completion details. A failed import can leave harmless unreferenced immutable
memory chunks; retry verifies and reuses them. Do not delete shared chunks.

## Recovery

Read the error details. Close DFU if it is running. Choose an unused slot or new
package filename when an existing destination is reported. For missing or corrupt
history, restore the matching full `Saves` backup, or import the matching portable
package into a new slot. Keep the original save until recovery is complete. Never
discard the checkpoint or delete `PeopleMemory` to force a load.

If the GUI cannot start the transfer tool, restore both executables from the same
download into one folder. The CLI remains available for support and automation.

## Build and checks

From the repository root with the .NET 8 SDK or newer:

```powershell
./tools/MemoryArchiveGui/Publish.ps1
dotnet run --project tests/MemoryArchiveGui.Regression -c Release -- tools/MemoryArchiveTool/bin/Release/net8.0/MemoryArchiveTool.dll
dotnet run --project tests/MemoryArchiveGui.Rendering -c Release -- work/roadmap-20261023/gui-render
```

The publisher defaults to `work/MemoryArchiveGui/win-x64` and uses the existing
CLI publisher's output containment checks. It builds both self-contained EXEs.
Regression checks use temporary fixtures only. When DFU is running, actual CLI
fixture transfers are skipped and the process guard is preserved. The rendering
harness checks both tabs using offscreen Windows Forms; it opens no player saves
and starts no transfer. Native file-picker interaction and loading the resulting
save in DFU remain separate manual acceptance checks.
