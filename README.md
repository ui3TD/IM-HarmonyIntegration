
# IM-HarmonyIntegration

IM-HarmonyIntegration is a [BepInEx](https://github.com/BepInEx/BepInEx) plugin that integrates [Harmony](https://github.com/pardeike/Harmony) into Idol Manager. You need IM-HarmonyIntegration installed to activate any Harmony mods for Idol Manager on Steam Workshop.

Visit the [Idol Manager Official Discord](https://discord.com/invite/83ywHbP) to discuss.

## INSTALL INSTRUCTIONS (Windows)

These instructions are for Windows (x64). MacOS or Linux users should see the section below instead.

1. Download IM-HarmonyIntegration for x64 **[HERE](https://github.com/ui3TD/IM-HarmonyIntegration/releases/latest/download/IM-HarmonyIntegration.x64.zip)**
2. Extract the contents of the zip file into the Idol Manager directory.

You can find the Idol Manager directory by right clicking Idol Manager in your Steam Library and selecting "Browse Local Files" like so:
<p align="left">
<img src="https://i.imgur.com/RnD3WjU.jpg" />
</p>

If done correctly, your Idol Manager directory should look like this:
<p align="left">
<img src="https://i.imgur.com/KL0fxbU.png" />
</p>

## INSTALL INSTRUCTIONS (MacOS/Linux)

1. Download IM-HarmonyIntegration for MacOS or Linux **[HERE](https://github.com/ui3TD/IM-HarmonyIntegration/releases/latest)**
2. Find the directory where the game executable file or bundle is located.
    - On Linux, it may vary by distribution but it is usually `~/.steam/steam/steamapps/common/Idol Manager/`
    - On MacOS, it is `~/Library/Application Support/Steam/steamapps/common/Idol Manager/`
3. Extract the contents of the zip file into the directory.
4. MacOS only: macOS blocks files downloaded from the internet from being loaded into the game. Open Terminal and run:
    ```
    xattr -dr com.apple.quarantine "$HOME/Library/Application Support/Steam/steamapps/common/Idol Manager"
    ```
5. In your Steam Library, right click Idol Manager, select "Properties", and under "Launch Options" enter:
    - On Linux: `./run_bepinex.sh %command%`
    - On MacOS, the full path to the script in quotes, for example: `"/Users/<your user name>/Library/Application Support/Steam/steamapps/common/Idol Manager/run_bepinex.sh" %command%`

If done correctly, you should see `run_bepinex.sh` in the same folder as the Idol Manager executable file (`Idol Manager.app` or `IdolManager.x86_64`). If the game doesn't start, make the script executable by running `chmod +x run_bepinex.sh` in that folder.

Proton: on Linux, if the Idol Manager directory has `IM.exe` instead of `IdolManager.x86_64`, Steam is running the Windows version of the game through Proton. Use the [Windows (x64) release of IM-HarmonyIntegration](https://github.com/ui3TD/IM-HarmonyIntegration/releases/latest/download/IM-HarmonyIntegration.x64.zip) instead and follow BepInEx's guide for Proton [here](https://docs.bepinex.dev/articles/advanced/proton_wine.html). If BepInEx still doesn't load, set the Launch Options to `WINEDLLOVERRIDES="winhttp=n,b" %command%`.

The Linux release has been tested with the native Linux version of Idol Manager. The MacOS release uses the same setup but hasn't been tested on a Mac yet. If something doesn't work, please report it on the Discord or open an issue here.

## UNINSTALL INSTRUCTIONS (Windows)

Delete the following files and directories from your Idol Manager directory...

1. HarmonyIntegration README.txt
2. BepInEx
3. UnstrippedLibs
4. winhttp.dll
5. doorstop_config.ini

## UNINSTALL INSTRUCTIONS (MacOS/Linux)

Delete the following files and directories from your Idol Manager directory...

1. BepInEx
2. UnstrippedLibs
3. doorstop_libs
4. run_bepinex.sh
5. changelog.txt

Then clear the Launch Options you entered for Idol Manager in Steam.

## HOW IT WORKS

This plugin patches the in-game mod manager to search for and load _HarmonyID_.dll in each mod's folder, where "_HarmonyID_" is a field in the mod's info.json file. Toggling mods in-game will enable/disable the Harmony patches as expected.

Mod devs must make sure that the .dll is a C# library built on .NET 4.6 that includes Harmony compatible patches. Do not initiate the Harmony instance in your mod (i.e. do not include an Awake() method). IM-HarmonyIntegration controls all patching/unpatching operations.

See [IM-FastForward](https://github.com/ui3TD/IM-FastForward) for an example and tutorial for creating compatible mods.

## BUILDING FROM SOURCE

Pre-reqs:

- .NET Framework 4.6.
- Unstripped libraries of the following, included in the latest [release](https://github.com/ui3TD/IM-HarmonyIntegration/releases) download:
  - `mscorlib.dll`
  - `System.Configuration.dll`
  - `System.Core.dll`
  - `System.dll`
  - `UnityEngine.CoreModule.dll`
  - `UnityEngine.SharedInternalsModule.dll`

1. Put unstripped libraries into UnstrippedLibs folder in the Idol Manager directory.
2. Obtain [BepInEx](https://github.com/BepInEx/BepInEx) and copy into game directory.
3. Modify `doorstop_config.ini` to point to UnstrippedLibs.
4. Run the game to initialize BepInEx.
5. Copy `Assembly-CSharp.dll` and `Assembly-CSharp-firstpass.dll` from `Idol Manager\IM_Data\Managed` into `source\dll`. Alternatively, put them in a `dll` folder next to the repo folder (shared with other mod projects), or build with `-p:GameDllDir=<path>`.
6. Build `HarmonyIntegration.dll`.
7. Copy `HarmonyIntegration.dll` into your `Idol Manager\BepInEx\plugins` directory.

## RUNNING TESTS

`tests/HarmonyIntegration.Tests` checks the built plugin outside the game. It compiles small fake Harmony mods, lays out mod folders with `info.json` files, and runs the plugin's loading and toggling code against the game's real mod lists.

The tests need these game DLLs in the same folder as the build (`source\dll` or the shared `dll` folder beside the repo): `Assembly-CSharp.dll`, `Assembly-CSharp-firstpass.dll`, `Steamworks.NET.dll` and `UnityEngine.dll` from `Idol Manager\IM_Data\Managed`, plus the unstripped `UnityEngine.CoreModule.dll` and `UnityEngine.SharedInternalsModule.dll`.

```
dotnet test tests/HarmonyIntegration.Tests
```
