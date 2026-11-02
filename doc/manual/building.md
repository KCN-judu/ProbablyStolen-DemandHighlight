# Building


## Requirements

- *Probably Stolen*, game version 0.46D. The build compiles against the game's own assemblies in `Probably Stolen_Data/Managed`.
- Git Bash or another POSIX shell on Windows.
- The Roslyn C# compiler from Visual Studio Build Tools. The C# 5 compiler that ships with Windows cannot read some of the game's types.

## Build

```bash
./build.sh            # out/demand_highlight
./build.sh --install  # also copies it to the game's Mods folder
```

Two environment variables override the defaults:

| Variable | Default |
| --- | --- |
| `GAME` | `/d/SteamLibrary/steamapps/common/Probably Stolen` |
| `CSC` | the Roslyn `csc.exe` of Visual Studio 18 Build Tools |

## Install by hand

Copy the folder `out/demand_highlight` into the game's `Mods` folder, start the game, and enable the mod in the Mods menu. Mods load when the game starts, so restart after enabling or updating one.

The mod declares no capabilities, so the game enables it without a permission prompt.
