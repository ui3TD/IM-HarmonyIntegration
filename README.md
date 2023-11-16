# IM-HarmonyIntegration
This [BepInEx](https://github.com/BepInEx/BepInEx) plugin integrates [Harmony](https://github.com/pardeike/Harmony) into Idol Manager. You need this installed to activate any Harmony mods for Idol Manager on Steam Workshop.

The in-game mod manager has been patched to search for and load _HarmonyID_.dll in each mod's folder, where "_HarmonyID_" is a field in the mod's info.json file. Toggling mods in-game will enable/disable the Harmony patches as expected. 

Mod devs must make sure that the .dll is a C# library built on .NET 4.6 that includes Harmony compatible patches. Do not initiate the Harmony instance in your mod (i.e. do not include an Awake() method). IM-HarmonyIntegration controls all patching/unpatching operations.

See [IM-FastForward](https://github.com/ui3TD/IM-FastForward) for an example and tutorial for creating compatible mods.

## INSTALL INSTRUCTIONS (Windows x64): 

These instructions re for Windows x64. MacOS or Linux users should see the section below instead. 

1. Download IM-HarmonyIntegration.zip **[HERE](https://github.com/ui3TD/IM-HarmonyIntegration/releases)**
2. Paste everything into the Idol Manager directory

Find the Idol Manager directory by right clicking Idol Manager in your Steam Library and selecting "Browse Local Files" like so:
<p align="left">
<img src="https://i.imgur.com/RnD3WjU.jpg" />
</p>

## INSTALL INSTRUCTIONS (MacOS/Linux): 

1. Install BepInEx by following **Steps 1-3** from the official instructions [here](https://docs.bepinex.dev/articles/advanced/steam_interop.html?tabs=tabid-2).
2. After completing step 3 in the instructions linnked above, open run_bepinex.sh and edit the line `export DOORSTOP_CORLIB_OVERRIDE_PATH=""` to be `export DOORSTOP_CORLIB_OVERRIDE_PATH="$BASEDIR/UnstrippedLibs"`
3. Download IM-HarmonyIntegration.zip **[HERE](https://github.com/ui3TD/IM-HarmonyIntegration/releases)**
4. Paste everything into the Idol Manager directory where you installed BepInEx.

## UNINSTALL INSTRUCTIONS:
Delete the following files and directories from your Idol Manager directory...
1. HarmonyIntegration README.txt
2. BepInEx
3. UnstrippedLibs
4. winhttp.dll
5. doorstop_config.ini

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
