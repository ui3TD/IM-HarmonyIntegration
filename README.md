# IM-HarmonyIntegration
This [BepInEx](https://github.com/BepInEx/BepInEx) plugin integrates [Harmony](https://github.com/pardeike/Harmony) into Idol Manager. You need this installed to activate any Harmony mods for Idol Manager on Steam Workshop.

The in-game mod manager has been patched to search for "patch.dll" in each mod's root directory and load it through Harmony. Toggling mods in-game will enable/disable the Harmony patches as expected. 

Mod devs must make sure that "patch.dll" is a library that includes Harmony compatible patches.

## INSTALL INSTRUCTIONS: 
1. Download the latest release of IM-HarmonyIntegration [HERE](https://github.com/ui3TD/IM-HarmonyIntegration/releases)
2. Paste everything into the Idol Manager folder

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

1. Obtain [BepInEx](https://github.com/BepInEx/BepInEx) and copy into game directory
2. Run the game to initialize BepInEx
3. Build HarmonyIntegration
4. Copy the dll into BepInEx plugin directory
5. Put unstripped libraries into UnstrippedLibs folder in the Idol Manager directory
