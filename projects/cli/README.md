# LunaPack CLI

Luna is the command-line interface for LunaPack. Install it with npm or the
.NET tool:

```powershell
npm install --global @lunarisdigitalsolutions/lunapack
dotnet tool install --global Lunaris.Lunapack.Luna
```

Start in a project by initializing it,
adding a pack source,
discovering a pack,
and installing it:

```powershell
luna init
luna sources add github lunapack lunarisdigitalsolutions/lunapack
luna pack discover
luna pack install <pack-id>
```

Luna records which pack owns each managed file. You can preview changes before
installation, update selected packs, and remove managed files without losing
track of project state.

Visit the [LunaPack website](https://lunapack.dev) or read the [developer
documentation](https://lunapack.dev/developer/).
