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
            Plugin.Log.LogInfo($"==============================");
            Plugin.Log.LogInfo($"Postfix Injected Successfully.");
            Plugin.Log.LogInfo($"POSTFIX LOGGING START");
            Plugin.Log.LogInfo($"==============================");
            int modcount = 0;
            foreach (Mods._mod mod in Mods._Mods)
            {
                
                mod.Enabled = staticVars.Settings.IsModEnabled(mod.ModName);
                string modDir = mod.Path;
                string modName = mod.ModName;
                string modTitle = mod.Title;

                Plugin.Log.LogInfo($"==============================");
                Plugin.Log.LogInfo($"MOD INDEX: [" + modcount + "] - CURRENT MOD: " + modTitle);
                modcount++;

                string modInfoFile = Path.Combine(modDir.TrimEnd(new char[] { Path.DirectorySeparatorChar }), "info.json");
                JSONNode modInfo = mainScript.ProcessInboundData(File.ReadAllText(modInfoFile));
                
                var modHarmonyID = modInfo["HarmonyID"];
                var dlls = modHarmonyID.Count;
                
                if (modHarmonyID != null && dlls > 0)
                {
                    if (mod.Enabled)
                    {
                        for (int i = 0; i < dlls; i++)
                        {
                            var patchFile = Path.Combine(modDir.TrimEnd(new char[] { Path.DirectorySeparatorChar }), modHarmonyID[i] + ".dll");

                            if (File.Exists(patchFile))
                            {
                                Assembly patchDll = Assembly.LoadFrom(patchFile);
                                Harmony.UnpatchID(modHarmonyID[i]);
                                Harmony.CreateAndPatchAll(patchDll, modHarmonyID[i]);

                                Plugin.Log.LogInfo($"Mod patch loaded: " + modTitle + " - " + patchDll.FullName + " - Module No: " + i);
                            }
                            else
                            {
                                Plugin.Log.LogInfo($"Module index not found: " + i + " | Mod: " + modTitle);
                            }
                        }
                    }
                    else
                    {
                        Plugin.Log.LogInfo($"Mod patch not enabled: " + modTitle);
                    }
                }
                else if (modHarmonyID != null)
                {
                    if (mod.Enabled)
                    {
                        var patchFile = Path.Combine(modDir.TrimEnd(new char[] { Path.DirectorySeparatorChar }), modHarmonyID + ".dll");
                        var patchDll = Assembly.LoadFrom(patchFile);
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
                    Plugin.Log.LogInfo($"No mod patch available for: " + modTitle);
                }             
            }
            Plugin.Log.LogInfo($"******************************");
            Plugin.Log.LogInfo($"POSTFIX LOGGING END");
            Plugin.Log.LogInfo($"******************************");
        }
    }


    [HarmonyPatch(typeof(staticVars._settings), "SwitchModStatus")]
    public class StaticVars__settings_SwitchModStatus_P
    {
        static void Postfix(string ModName)
        {
            Mods._mod mod = Mods.GetMod(ModName);

            mod.Enabled = staticVars.Settings.IsModEnabled(mod.ModName);
            string modDir = mod.Path;
            string modTitle = mod.Title;
            Plugin.Log.LogInfo($"==============================");
            Plugin.Log.LogInfo($"Postfix Injected Successfully.");
            Plugin.Log.LogInfo($"POSTFIX LOGGING START");
            Plugin.Log.LogInfo($"==============================");
            Plugin.Log.LogInfo($"CURRENT MOD: " + modTitle);
            Plugin.Log.LogInfo($"modDir: " + modDir);

            string modInfoFile = Path.Combine(modDir.TrimEnd(new char[] { Path.DirectorySeparatorChar }), "info.json");
            JSONNode modInfo = mainScript.ProcessInboundData(File.ReadAllText(modInfoFile));

            var modHarmonyID = modInfo["HarmonyID"];
            var dlls = modHarmonyID.Count;
            
            if (modHarmonyID != null && dlls > 0)
            {
                if (mod.Enabled)
                {
                    for (int i = 0; i < dlls; i++)
                    {
                        var patchFile = Path.Combine(modDir.TrimEnd(new char[] { Path.DirectorySeparatorChar }), modHarmonyID[i] + ".dll");
                        if (File.Exists(patchFile))
                        {
                            Assembly patchDll = Assembly.LoadFrom(patchFile);
                            Harmony.UnpatchID(modHarmonyID[i]);
                            Harmony.CreateAndPatchAll(patchDll, modHarmonyID[i]);

                            Plugin.Log.LogInfo($"Mod patch loaded: " + modTitle + " - " + patchDll.FullName + " - Module No: " + i);

                        }
                        else
                        {
                            Plugin.Log.LogInfo($"Module index not found: " + i + " | Mod: " + modTitle);
                        }
                    }
                }
                else
                {
                    Plugin.Log.LogInfo($"Mod patch not enabled: " + modTitle);
                }
            }
            else if (modHarmonyID != null)
            {
                if (mod.Enabled)
                {
                    var patchFile = Path.Combine(modDir.TrimEnd(new char[] { Path.DirectorySeparatorChar }), modHarmonyID + ".dll");
                    var patchDll = Assembly.LoadFrom(patchFile);
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
                Plugin.Log.LogInfo($"No mod patch available for: " + modTitle);
            }
            Plugin.Log.LogInfo($"******************************");
            Plugin.Log.LogInfo($"POSTFIX LOGGING END");
            Plugin.Log.LogInfo($"******************************");
        }
    }
}
