# IM-HarmonyIntegration
Idol Manager Harmony Integration using BepInEx

The in-game mod manager has been patched to search for "patch.dll" in each mod's root directory and load it through Harmony. Enabling and disabling mods in-game will enable/disable the Harmony patches as expected.

Mod devs must make sure that "patch.dll" is a class library that includes Harmony compatible patches.

This package is based on BepInEx v5.4.21 and a plugin called HarmonyIntegration.

## INSTALL INSTRUCTIONS: 
Extract all files into the Idol Manager directory (where the IM.exe executable is)

## UNINSTALL INSTRUCTIONS:
Delete the following files and directories...
1. HarmonyIntegration README.txt
2. BepInEx
3. UnstrippedLibs
4. winhttp.dll
5. doorstop_config.ini

## BUILDING FROM SOURCE:
.NET 4.6 is required
Edit HarmonyIntegration.csproj to point to the game's Assembly-CSharp.dll file
