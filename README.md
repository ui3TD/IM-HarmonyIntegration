
# IM-HarmonyIntegration
IM-HarmonyIntegration is a [BepInEx](https://github.com/BepInEx/BepInEx) plugin that integrates [Harmony](https://github.com/pardeike/Harmony) into Idol Manager. You need IM-HarmonyIntegration installed to activate any Harmony mods for Idol Manager on Steam Workshop. 

Visit the [Idol Manager Official Discord](https://discord.com/invite/83ywHbP) to discuss.

## INSTALL INSTRUCTIONS (Windows): 

These instructions are for Windows (x64). MacOS or Linux users should see the section below instead. 

1. Download IM-HarmonyIntegration for x64 **[HERE](https://github.com/ui3TD/IM-HarmonyIntegration/releases/download/1.1/IM-HarmonyIntegration.x64.zip)**
2. Extract the contents of the zip file into the Idol Manager directory. 

You can find the Idol Manager directory by right clicking Idol Manager in your Steam Library and selecting "Browse Local Files" like so:
<p align="left">
<img src="https://i.imgur.com/RnD3WjU.jpg" />
</p>

If done correctly, your Idol Manager directory should look like this:
<p align="left">
<img src="https://i.imgur.com/ugUG24I.png" />
</p>

## INSTALL INSTRUCTIONS (MacOS/Linux): 

1. Download IM-HarmonyIntegration for MacOS or Linux **[HERE](https://github.com/ui3TD/IM-HarmonyIntegration/releases/tag/1.1)**
2. Find the directory where the game executable file is located.
	1. On Linux, it may vary by distribution but it is usually `~/.steam/steam/SteamApps/common/Idol Manager/`
	2. On MacOS, it is `~/Library/Application Support/Steam/steamapps/common/Idol Manager/Idol Manager.app/Contents/MacOS/`
3. Extract the contents of the zip file into the executable directory.
4. Follow **Steps 2 and 3** from the official instructions [here](https://docs.bepinex.dev/articles/advanced/steam_interop.html?tabs=tabid-2).

If done correctly, you should see `run_bepinex.sh` in the same folder as the Idol Manager executable file.

## UNINSTALL INSTRUCTIONS (Windows):
Delete the following files and directories from your Idol Manager directory...
1. HarmonyIntegration README.txt
2. BepInEx
3. UnstrippedLibs
4. winhttp.dll
5. doorstop_config.ini

## HOW IT WORKS

This plugin patches the in-game mod manager to search for and load _HarmonyID_.dll in each mod's folder, where "_HarmonyID_" is a field in the mod's info.json file. Toggling mods in-game will enable/disable the Harmony patches as expected. 

Mod devs must make sure that the .dll is a C# library built on .NET 4.6 that includes Harmony compatible patches. Do not initiate the Harmony instance in your mod (i.e. do not include an Awake() method). IM-HarmonyIntegration controls all patching/unpatching operations.

See [IM-FastForward](https://github.com/ui3TD/IM-FastForward) for an example and tutorial for creating compatible mods.

## BUILDING FROM SOURCE:
Pre-reqs:
- .NET Framework 4.6.
- Unstripped libraries of: mscorlib.dll, System.Configuration.dll, System.Core.dll, System.dll, UnityEngine.CoreModule.dll, UnityEngine.SharedInternalsModule.dll
- Edit HarmonyIntegration.csproj to point to your copy of Idol Manager's Assembly-CSharp.dll file

1. Put unstripped libraries into UnstrippedLibs folder in the Idol Manager directory
2. Obtain [BepInEx](https://github.com/BepInEx/BepInEx) and copy into game directory
3. Modify doorstop_config.ini to point to UnstrippedLibs
4. Run the game to initialize BepInEx
5. Build HarmonyIntegration
6. Copy the dll into BepInEx plugin directory
