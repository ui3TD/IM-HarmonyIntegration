using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SimpleJSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace HarmonyIntegration
{
    /// <summary>
    /// Constants used by the Harmony bootstrap and patch synchronization layer.
    ///
    /// Version 1.2.0 intentionally keeps this plugin focused on loading and unloading
    /// Harmony mods. Mod-management UI belongs in a normal Harmony mod so it can be
    /// distributed and updated independently through Steam Workshop.
    /// </summary>
    internal static class IntegrationConstants
    {
        internal const string PluginGuid = "com.name.HarmonyIntegration";
        internal const string PluginDisplayName = "HarmonyIntegration";
        internal const string PluginLoadedLogFormat = "Plugin {0} is loaded!";

        internal const string WorkshopSourceName = "Workshop";
        internal const string LocalOverrideSourceName = "LocalLow";

        internal const string ModsLoadMethodName = "LoadMods";
        internal const string SwitchModStatusMethodName = "SwitchModStatus";

        internal const string ModInfoFileName = "info.json";
        internal const string HarmonyIdJsonKey = "HarmonyID";
        internal const string ManagedPatchAssemblyExtension = ".dll";

        internal const string SyncCompleteLogFormat =
            "Harmony mod sync complete: {0} patched, {1} unpatched.";
        internal const string DuplicateSelectionLogFormat =
            "Multiple enabled mod copies share HarmonyID {0} ({1} copies); preferring {2} source: {3}";
        internal const string ReplacePatchFailureLogFormat =
            "Failed to replace existing Harmony patch source for {0}: {1}";
        internal const string PatchLoadedLogFormat = "Mod patch loaded: {0}";
        internal const string NoPatchesRegisteredLogFormat =
            "Harmony DLL loaded but no patches were registered for {0} (HarmonyID: {1}).";
        internal const string PartialPatchCleanupFailureLogFormat =
            "Failed to clean up partial Harmony patches for {0}: {1}";
        internal const string PatchSyncFailureLogFormat =
            "Failed to synchronize Harmony patch for {0} (HarmonyID: {1}): {2}";
        internal const string PatchUnloadedLogFormat = "Mod patch unloaded: {0}";
        internal const string PatchUnloadFailureLogFormat =
            "Failed to unload Harmony patch {0}: {1}";
        internal const string MissingHarmonyDllLogFormat =
            "HarmonyID is defined but its DLL was not found for {0}: {1}";
        internal const string HarmonyMetadataReadFailureLogFormat =
            "Failed to read Harmony metadata for {0}: {1}";
        internal const string MissingSwitchedModLogFormat =
            "SwitchModStatus could not find mod: {0}";
    }

    [BepInPlugin(
        IntegrationConstants.PluginGuid,
        IntegrationConstants.PluginDisplayName,
        PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private const int InitialPatchChangeCount = 0;
        private const int EnabledCopyCountIncrement = 1;
        private const int DuplicateCopyThreshold = 1;

        public static ManualLogSource Log;

        // Harmony ownership is keyed by HarmonyID rather than by individual Mods._mod
        // entries. Local and Workshop copies may legitimately share one HarmonyID.
        private static readonly Dictionary<string, string> ActivePatchSources =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // Mod metadata is stable during a normal Idol Manager session. Cache positive
        // and negative lookups by physical directory so ordinary non-Harmony mods do
        // not repeatedly incur file I/O.
        private static readonly Dictionary<string, HarmonyModData> ModDataCache =
            new Dictionary<string, HarmonyModData>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes logging and applies only HarmonyIntegration's bootstrap patches.
        /// </summary>
        private void Awake()
        {
            Log = Logger;
            Logger.LogInfo(string.Format(
                IntegrationConstants.PluginLoadedLogFormat,
                IntegrationConstants.PluginGuid));

            var harmony = new Harmony(IntegrationConstants.PluginGuid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private sealed class HarmonyModData
        {
            public string Title;
            public string HarmonyID;
            public string PatchFile;
            public Assembly PatchAssembly;
            public bool HasHarmonyPatch;
            public bool WarningLogged;
            public bool IsWorkshop;
        }

        // Build the desired state once per HarmonyID. LocalLow copies deliberately
        // take precedence over Workshop copies when both are enabled. Within a source
        // tier, the later Mods._Mods entry retains the historical last-loaded priority.
        /// <summary>
        /// Synchronizes all enabled Harmony mods after Idol Manager finishes loading mods.
        /// </summary>
        internal static void SyncAllModPatches()
        {
            var desiredByID = new Dictionary<string, HarmonyModData>(StringComparer.Ordinal);
            var desiredOrder = new List<string>();
            var enabledCopyCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (Mods._mod mod in Mods._Mods)
            {
                if (mod == null)
                {
                    continue;
                }

                HarmonyModData data = GetHarmonyModData(mod);
                if (data == null || !data.HasHarmonyPatch || !IsEnabledByGame(mod))
                {
                    continue;
                }

                int count;
                enabledCopyCounts.TryGetValue(data.HarmonyID, out count);
                enabledCopyCounts[data.HarmonyID] = count + EnabledCopyCountIncrement;

                HarmonyModData current;
                if (!desiredByID.TryGetValue(data.HarmonyID, out current))
                {
                    desiredByID[data.HarmonyID] = data;
                    desiredOrder.Add(data.HarmonyID);
                }
                else if (ShouldPreferCandidate(current, data))
                {
                    desiredByID[data.HarmonyID] = data;
                    desiredOrder.Remove(data.HarmonyID);
                    desiredOrder.Add(data.HarmonyID);
                }
            }

            int patched = InitialPatchChangeCount;
            int unpatched = InitialPatchChangeCount;

            // Only unload a HarmonyID when no enabled copy remains. Disabling one
            // duplicate must never unload another enabled local/Workshop sibling.
            var activeIDs = new List<string>(ActivePatchSources.Keys);
            foreach (string harmonyID in activeIDs)
            {
                if (!desiredByID.ContainsKey(harmonyID))
                {
                    UnpatchActiveID(harmonyID, ref unpatched);
                }
            }

            foreach (string harmonyID in desiredOrder)
            {
                SyncDesiredPatch(desiredByID[harmonyID], ref patched, ref unpatched);
            }

            foreach (KeyValuePair<string, int> pair in enabledCopyCounts)
            {
                if (pair.Value > DuplicateCopyThreshold)
                {
                    HarmonyModData selected = desiredByID[pair.Key];
                    LogDuplicateSelection(pair.Key, selected, pair.Value);
                }
            }

            if (patched != InitialPatchChangeCount || unpatched != InitialPatchChangeCount)
            {
                Log.LogInfo(string.Format(
                    IntegrationConstants.SyncCompleteLogFormat,
                    patched,
                    unpatched));
            }
        }

        /// <summary>
        /// Re-synchronizes the HarmonyID associated with a mod whose enabled state changed.
        /// </summary>
        internal static void SyncModPatch(Mods._mod changedMod)
        {
            if (changedMod == null)
            {
                return;
            }

            HarmonyModData changedData = GetHarmonyModData(changedMod);
            if (changedData == null || !changedData.HasHarmonyPatch)
            {
                return;
            }

            SyncHarmonyID(changedData.HarmonyID);
        }

        /// <summary>
        /// Selects the preferred enabled copy for one HarmonyID and synchronizes its patch state.
        /// </summary>
        private static void SyncHarmonyID(string harmonyID)
        {
            if (string.IsNullOrWhiteSpace(harmonyID))
            {
                return;
            }

            HarmonyModData desired = null;
            int enabledCopies = InitialPatchChangeCount;

            foreach (Mods._mod mod in Mods._Mods)
            {
                if (mod == null)
                {
                    continue;
                }

                HarmonyModData data = GetHarmonyModData(mod);
                if (data == null ||
                    !data.HasHarmonyPatch ||
                    !string.Equals(data.HarmonyID, harmonyID, StringComparison.Ordinal))
                {
                    continue;
                }

                if (IsEnabledByGame(mod))
                {
                    enabledCopies++;
                    if (desired == null || ShouldPreferCandidate(desired, data))
                    {
                        desired = data;
                    }
                }
            }

            int patched = InitialPatchChangeCount;
            int unpatched = InitialPatchChangeCount;

            if (desired == null)
            {
                UnpatchActiveID(harmonyID, ref unpatched);
            }
            else
            {
                SyncDesiredPatch(desired, ref patched, ref unpatched);

                if (enabledCopies > DuplicateCopyThreshold)
                {
                    LogDuplicateSelection(harmonyID, desired, enabledCopies);
                }
            }
        }

        /// <summary>
        /// Reads the game's authoritative enabled state and falls back to the cached field
        /// only if the vanilla accessor fails.
        /// </summary>
        private static bool IsEnabledByGame(Mods._mod mod)
        {
            if (mod == null)
            {
                return false;
            }

            try
            {
                return mod.IsEnabled();
            }
            catch
            {
                return mod.Enabled;
            }
        }

        /// <summary>
        /// Prefers LocalLow over Workshop and preserves later-entry priority within a tier.
        /// </summary>
        private static bool ShouldPreferCandidate(
            HarmonyModData current,
            HarmonyModData candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (current == null)
            {
                return true;
            }

            if (current.IsWorkshop != candidate.IsWorkshop)
            {
                return !candidate.IsWorkshop;
            }

            return true;
        }

        /// <summary>
        /// Logs which physical copy won when multiple enabled copies share one HarmonyID.
        /// </summary>
        private static void LogDuplicateSelection(
            string harmonyID,
            HarmonyModData selected,
            int count)
        {
            if (selected == null)
            {
                return;
            }

            string tier = selected.IsWorkshop
                ? IntegrationConstants.WorkshopSourceName
                : IntegrationConstants.LocalOverrideSourceName;

            Log.LogWarning(string.Format(
                IntegrationConstants.DuplicateSelectionLogFormat,
                harmonyID,
                count,
                tier,
                selected.PatchFile));
        }

        /// <summary>
        /// Ensures one desired physical mod copy owns a HarmonyID.
        /// </summary>
        private static void SyncDesiredPatch(
            HarmonyModData desired,
            ref int patched,
            ref int unpatched)
        {
            if (desired == null || !desired.HasHarmonyPatch)
            {
                return;
            }

            string desiredPath = NormalizePath(desired.PatchFile);
            string activePath;

            if (ActivePatchSources.TryGetValue(desired.HarmonyID, out activePath))
            {
                if (string.Equals(
                    activePath,
                    desiredPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                try
                {
                    Harmony.UnpatchID(desired.HarmonyID);
                    ActivePatchSources.Remove(desired.HarmonyID);
                    unpatched++;
                }
                catch (Exception ex)
                {
                    Log.LogError(string.Format(
                        IntegrationConstants.ReplacePatchFailureLogFormat,
                        desired.HarmonyID,
                        ex));
                    return;
                }
            }

            try
            {
                if (desired.PatchAssembly == null)
                {
                    desired.PatchAssembly = Assembly.LoadFrom(desired.PatchFile);
                }

                Harmony.CreateAndPatchAll(desired.PatchAssembly, desired.HarmonyID);
                if (Harmony.HasAnyPatches(desired.HarmonyID))
                {
                    ActivePatchSources[desired.HarmonyID] = desiredPath;
                    patched++;
                    Log.LogInfo(string.Format(
                        IntegrationConstants.PatchLoadedLogFormat,
                        desired.PatchAssembly.FullName));
                }
                else
                {
                    Log.LogWarning(string.Format(
                        IntegrationConstants.NoPatchesRegisteredLogFormat,
                        desired.Title,
                        desired.HarmonyID));
                }
            }
            catch (Exception ex)
            {
                try
                {
                    Harmony.UnpatchID(desired.HarmonyID);
                    ActivePatchSources.Remove(desired.HarmonyID);
                }
                catch (Exception cleanupEx)
                {
                    Log.LogError(string.Format(
                        IntegrationConstants.PartialPatchCleanupFailureLogFormat,
                        desired.Title,
                        cleanupEx));
                }

                Log.LogError(string.Format(
                    IntegrationConstants.PatchSyncFailureLogFormat,
                    desired.Title,
                    desired.HarmonyID,
                    ex));
            }
        }

        /// <summary>
        /// Unpatches a tracked HarmonyID and removes its active source record.
        /// </summary>
        private static void UnpatchActiveID(string harmonyID, ref int unpatched)
        {
            if (!ActivePatchSources.ContainsKey(harmonyID))
            {
                return;
            }

            try
            {
                Harmony.UnpatchID(harmonyID);
                ActivePatchSources.Remove(harmonyID);
                unpatched++;
                Log.LogInfo(string.Format(
                    IntegrationConstants.PatchUnloadedLogFormat,
                    harmonyID));
            }
            catch (Exception ex)
            {
                Log.LogError(string.Format(
                    IntegrationConstants.PatchUnloadFailureLogFormat,
                    harmonyID,
                    ex));
            }
        }

        /// <summary>
        /// Returns a canonical path without trailing separators where possible.
        /// </summary>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path;
            }
        }

        /// <summary>
        /// Reads and caches Harmony metadata for a physical Idol Manager mod directory.
        /// </summary>
        private static HarmonyModData GetHarmonyModData(Mods._mod mod)
        {
            string modDir = mod.Path;
            if (string.IsNullOrEmpty(modDir))
            {
                return null;
            }

            modDir = NormalizePath(modDir);

            HarmonyModData cached;
            if (ModDataCache.TryGetValue(modDir, out cached))
            {
                return cached;
            }

            HarmonyModData data = new HarmonyModData
            {
                Title = string.IsNullOrEmpty(mod.Title) ? mod.ModName : mod.Title,
                HasHarmonyPatch = false,
                IsWorkshop = mod.IsWorkshop()
            };

            // Cache negative results too so ordinary Idol Manager mods are not reparsed.
            ModDataCache[modDir] = data;

            try
            {
                string modInfoFile = Path.Combine(
                    modDir,
                    IntegrationConstants.ModInfoFileName);

                if (!File.Exists(modInfoFile))
                {
                    return data;
                }

                JSONNode modInfo = mainScript.ProcessInboundData(
                    File.ReadAllText(modInfoFile));
                string harmonyID = modInfo[IntegrationConstants.HarmonyIdJsonKey];

                if (string.IsNullOrWhiteSpace(harmonyID))
                {
                    return data;
                }

                string patchFile = Path.Combine(
                    modDir,
                    harmonyID + IntegrationConstants.ManagedPatchAssemblyExtension);

                if (!File.Exists(patchFile))
                {
                    if (!data.WarningLogged)
                    {
                        Log.LogWarning(string.Format(
                            IntegrationConstants.MissingHarmonyDllLogFormat,
                            data.Title,
                            patchFile));
                        data.WarningLogged = true;
                    }

                    return data;
                }

                data.HarmonyID = harmonyID;
                data.PatchFile = patchFile;
                data.HasHarmonyPatch = true;
                return data;
            }
            catch (Exception ex)
            {
                if (!data.WarningLogged)
                {
                    Log.LogError(string.Format(
                        IntegrationConstants.HarmonyMetadataReadFailureLogFormat,
                        data.Title,
                        ex));
                    data.WarningLogged = true;
                }

                return data;
            }
        }
    }

    // Initial activation runs after vanilla Mods.LoadMods(), which finishes by applying
    // the game's final enabled/disabled state to every Mods._mod entry.
    [HarmonyPatch(typeof(Mods), IntegrationConstants.ModsLoadMethodName)]
    internal static class Mods_LoadMods_P
    {
        private static void Postfix()
        {
            Plugin.SyncAllModPatches();
        }
    }

    // Re-evaluate the complete HarmonyID group after one vanilla mod entry is toggled.
    // This matters when LocalLow and Workshop copies share the same HarmonyID.
    [HarmonyPatch(
        typeof(staticVars._settings),
        IntegrationConstants.SwitchModStatusMethodName)]
    internal static class StaticVars__settings_SwitchModStatus_P
    {
        private static void Postfix(string ModName)
        {
            Mods._mod mod = Mods.GetMod(ModName);
            if (mod == null)
            {
                Plugin.Log.LogWarning(string.Format(
                    IntegrationConstants.MissingSwitchedModLogFormat,
                    ModName));
                return;
            }

            mod.Enabled = staticVars.Settings.IsModEnabled(mod.ModName);
            Plugin.SyncModPatch(mod);
        }
    }
}
