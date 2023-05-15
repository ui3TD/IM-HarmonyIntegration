using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.IO;
using System.Reflection;
using SimpleJSON;

namespace HarmonyIntegration
{
    [BepInPlugin("com.name.HarmonyIntegration", "HarmonyIntegration", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            Log = Logger;
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            var harmony = new Harmony("com.name.HarmonyIntegration");
            harmony.PatchAll();
        }

        public static ManualLogSource Log;

    }

    [HarmonyPatch(typeof(Mods), "ReEnableMods")]
    public class Mods_ReEnableMods_P
    {
        static void Postfix()
        {
            Plugin.Log.LogInfo($"Postfix Injected Successfully.");
            foreach (Mods._mod mod in Mods._Mods)
            {
                mod.Enabled = staticVars.Settings.IsModEnabled(mod.ModName);
                string modDir = mod.Path;
                string modName = mod.ModName;
                string modTitle = mod.Title;

                string modInfoFile = Path.Combine(modDir.TrimEnd(new char[] { Path.DirectorySeparatorChar }), "info.json");
                JSONNode modInfo = mainScript.ProcessInboundData(File.ReadAllText(modInfoFile));
                string modHarmonyID = modInfo["HarmonyID"];

                var patchFile = Path.Combine(modDir.TrimEnd(new char[] { Path.DirectorySeparatorChar }), modHarmonyID + ".dll");

                if (modHarmonyID != null && File.Exists(patchFile))
                {
                    if (mod.Enabled)
                    {
                        Assembly patchDll = Assembly.LoadFrom(patchFile);
                        Harmony.UnpatchID(modHarmonyID);
                        Harmony.CreateAndPatchAll(patchDll, modHarmonyID);

                        Plugin.Log.LogInfo($"Mod patch loaded: " + patchDll.FullName);
                    }
                    else
                    {
                        Plugin.Log.LogInfo($"Mod patch not enabled: " + modTitle);
                    }
                }
                else
                {
                    Plugin.Log.LogInfo($"No patch found: " + modTitle);
                }
            }
        }
    }


    [HarmonyPatch(typeof(staticVars._settings), "SwitchModStatus")]
    public class StaticVars__settings_SwitchModStatus_P
    {
        static void Postfix(string ModName)
        {
            Plugin.Log.LogInfo($"Postfix Injected Successfully.");
            Plugin.Log.LogInfo($"ModName: " + ModName);
            Mods._mod mod = Mods.GetMod(ModName);

            mod.Enabled = staticVars.Settings.IsModEnabled(mod.ModName);
            string modDir = mod.Path;
            string modTitle = mod.Title;
            Plugin.Log.LogInfo($"modDir: " + modDir);

            string modInfoFile = Path.Combine(modDir.TrimEnd(new char[] { Path.DirectorySeparatorChar }), "info.json");
            JSONNode modInfo = mainScript.ProcessInboundData(File.ReadAllText(modInfoFile));
            string modHarmonyID = modInfo["HarmonyID"];

            var patchFile = Path.Combine(modDir.TrimEnd(new char[] { Path.DirectorySeparatorChar }), modHarmonyID + ".dll");

            if (modHarmonyID != null && File.Exists(patchFile))
            {
                if (mod.Enabled)
                {
                    var patchDll = Assembly.LoadFrom(patchFile);
                    Harmony.UnpatchID(modHarmonyID);
                    Harmony.CreateAndPatchAll(patchDll, modHarmonyID);

                    Plugin.Log.LogInfo($"Mod patch loaded: " + patchDll.FullName);
                }
                else
                {
                    Harmony.UnpatchID(modHarmonyID);
                    Plugin.Log.LogInfo($"Mod patch unloaded: " + modHarmonyID);
                }
            }
            else
            {
                Plugin.Log.LogInfo($"No patch found: " + modTitle);
            }
        }
    }
}
