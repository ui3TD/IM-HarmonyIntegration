# IM-HarmonyIntegration
This [BepInEx](https://github.com/BepInEx/BepInEx) plugin integrates [Harmony](https://github.com/pardeike/Harmony) into Idol Manager. You need this installed to activate any Harmony mods for Idol Manager on Steam Workshop.

The in-game mod manager has been patched to search for and load `<HarmonyID>.dll` in each mod's folder, where `HarmonyID` is a field in the mod's `info.json` file. Toggling mods in-game will enable/disable the Harmony patches as expected. 

[The Steam Workshop page of IM-HarmonyIntegration](https://steamcommunity.com/sharedfiles/filedetails/?id=3102983863) done absolutely nothing to the game, it was simply act as a prerequisites items for mods that uses this plugin and due to the fact there is no way to actually copy/modify a game's installation folder with Steam Workshop.

# INSTALL INSTRUCTIONS
## Windows x64: 
These instructions re for Windows x64. MacOS or Linux users should see the section below instead. 

1. Download IM-HarmonyIntegration.zip **[HERE](https://github.com/ui3TD/IM-HarmonyIntegration/releases)**
2. Unzip everything into the Idol Manager directory

Find the Idol Manager directory by right clicking Idol Manager in your Steam Library and selecting "Browse Local Files" like so:
<p align="left">
<img src="https://i.imgur.com/RnD3WjU.jpg" />
</p>

## MacOS/Linux: 
1. Install BepInEx by following **Steps 1-3** from the official instructions [here](https://docs.bepinex.dev/articles/advanced/steam_interop.html?tabs=tabid-2).
2. After completing step 3 in the instructions linked above, open run_bepinex.sh and edit the line `export DOORSTOP_CORLIB_OVERRIDE_PATH=""` to be `export DOORSTOP_CORLIB_OVERRIDE_PATH="$BASEDIR/UnstrippedLibs"`
3. Download IM-HarmonyIntegration.zip **[HERE](https://github.com/ui3TD/IM-HarmonyIntegration/releases)**
4. Unzip everything into the Idol Manager directory where you installed BepInEx.

# UNINSTALL INSTRUCTIONS:
Delete the following files and directories from your Idol Manager directory...
1. HarmonyIntegration README.txt
2. BepInEx
3. UnstrippedLibs
4. winhttp.dll
5. doorstop_config.ini

# BUILDING FROM SOURCE:
## Pre-reqs:
- .NET Framework 4.6.
- Unstripped libraries of: `mscorlib.dll`, `System.Configuration.dll`, `System.Core.dll`, `System.dll`, `UnityEngine.CoreModule.dll`, `UnityEngine.SharedInternalsModule.dll`
- Edit `HarmonyIntegration.csproj` to point to your copy of Idol Manager's `Assembly-CSharp.dll` and `Assembly-CSharp-firstpass.dll` file
## Steps
1. Put unstripped libraries into `UnstrippedLibs` folder in the Idol Manager directory
2. Obtain [BepInEx](https://github.com/BepInEx/BepInEx) and copy into game directory
3. Modify `doorstop_config.ini` to point to `UnstrippedLibs` folder
4. Run the game to initialize BepInEx
5. Build HarmonyIntegration
6. Copy the dll into BepInEx `plugin` directory

# CREATING MODS
See [IM-FastForward](https://github.com/ui3TD/IM-FastForward) for an example and tutorial for creating compatible mods.

Mod devs must make sure that the `.dll` is a `C#` library built on `.NET 4.6` that includes Harmony compatible patches. Do not initiate the Harmony instance in your mod (i.e. do not include an `Awake()` method). IM-HarmonyIntegration controls all patching/unpatching operations.

Starting on version 1.2, it is now possible to use multiple dll patches.

## USING MULTIPLE PATCHES
The only use case for this is if you are creating a large mod and want to incorporates other people patch that has its own `JSON` files, but you want to use your own version of `JSON` files.

For example, mod `ABC` adds more single's genre but their description on its `JSON` files doesn't fit with your mod, so you want to use its dll patch but not its `JSON` description, hence you use its dll in your mod and add it in `HarmonyID` field in the mod's `info.json` file.

It is a must to ask the original creator permission and you must state what other mods you used in your workshop page, so people won't have duplicate mods. 

Duplicate patches may lead to issues and/or other unexpected problems because there is absolutely no way (so far) to specifically define patches priority.

To use multiple patches, simply write the `HarmonyID` field in your mod's `info.json` file in the same way like using multiple tags.

```"HarmonyID": ["im.creator.patch1", "im.creator.patch2", "im.creator2.patch1"]```

## LOGGING
For advanced user, log files is available at `%USERS%\AppData\LocalLow\Glitch Pitch\Idol Manager\` directory, with `player.log` being the latest/live log and `player-prev.log` as the previous log.
