# Local DFU engine patches

`editor-profiling.patch` preserves the two local engine changes used by the
profiling tools. It applies to Interkarma/daggerfall-unity commit
`fb2627082fabe473bf108c816263b71c6e26ace6`.

From that engine checkout:

```powershell
git apply --check ../LivingCities/tools/engine-patches/editor-profiling.patch
git apply ../LivingCities/tools/engine-patches/editor-profiling.patch
```

The patch excludes open generic types from effect discovery and permits the
release location cache in the editor only with the explicit
`-peopleoftheiliacbay-editor-profile` argument. It does not modify the upstream
repository or package third-party game installations. If already applied,
`git apply --reverse --check` verifies the current patch state.
