using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace HarmonyIntegration
{
    /// <summary>
    /// Centralizes identifiers, metadata keys, patch target names, and diagnostic messages shared
    /// across the plugin so implementation code contains no unexplained string literals.
    /// </summary>
    internal static class IntegrationConstants
    {
        internal const string PluginGuid = "com.name.HarmonyIntegration";
        internal const string PluginDisplayName = "HarmonyIntegration";
        internal const string PluginLoadedLogFormat = "Plugin {0} is loaded!";
        internal const string WorkshopSourceName = "Workshop";
        internal const string LocalOverrideSourceName = "LocalLow";
        internal const string LocalSourceName = "Local";

        internal const string ModsLoadMethodName = "LoadMods";
        internal const string SwitchModStatusMethodName = "SwitchModStatus";
        internal const string ModsPopupRenderMethodName = "Render";
        internal const string ModButtonRenderScreenshotMethodName = "RenderScreenshot";
        internal const string ModButtonRenderTooltipsMethodName = "RenderTooltips";
        internal const string CancelMethodName = "OnCancel";

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

    [BepInPlugin(IntegrationConstants.PluginGuid, IntegrationConstants.PluginDisplayName, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private const int InitialPatchChangeCount = 0;
        private const int EnabledCopyCountIncrement = 1;
        private const int DuplicateCopyThreshold = 1;

        public static ManualLogSource Log;

        // Harmony ownership is keyed by HarmonyID, not by Idol Manager's individual
        // Mods._mod entries. Multiple local/Workshop copies can legitimately expose
        // the same HarmonyID, so track the single patch source currently selected for
        // each ID instead of treating every Mods._mod as an independent patch state.
        private static readonly Dictionary<string, string> ActivePatchSources =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // info.json and DLL metadata do not change during a normal game session.
        // Cache both positive and negative lookups by physical mod directory.
        private static readonly Dictionary<string, HarmonyModData> ModDataCache =
            new Dictionary<string, HarmonyModData>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes logging and applies this assembly’s Harmony patches when BepInEx loads the plugin.
        /// </summary>
        private void Awake()
        {
            Log = Logger;
            Logger.LogInfo(string.Format(IntegrationConstants.PluginLoadedLogFormat, IntegrationConstants.PluginGuid));

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
            public string ModName;
            public string ModPath;
        }

        // Build the desired state once per HarmonyID. LocalLow copies deliberately
        // take precedence over Workshop copies when both are enabled. This makes a
        // local development/override copy deterministic instead of letting vanilla's
        // load order (local first, Workshop second) accidentally make Workshop win.
        // Within the same source tier, the later Mods._Mods entry still wins.
        /// <summary>
        /// Builds the desired patch source for every HarmonyID, removes stale patches, applies winners, and reports duplicate enabled copies.
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
                    // The selected source changed. Keep ordering tied to the selected
                    // source, but never let a later Workshop entry displace a local one.
                    desiredByID[data.HarmonyID] = data;
                    desiredOrder.Remove(data.HarmonyID);
                    desiredOrder.Add(data.HarmonyID);
                }
            }

            int patched = InitialPatchChangeCount;
            int unpatched = InitialPatchChangeCount;

            // Only unpatch an ID if no enabled copy of that HarmonyID remains.
            // A disabled local/Workshop duplicate must never unload an enabled sibling.
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
                Log.LogInfo(string.Format(IntegrationConstants.SyncCompleteLogFormat, patched, unpatched));
            }
        }

        // Re-evaluate one HarmonyID as a group. This is used after a single vanilla mod
        // toggle because another local/Workshop copy may share the same HarmonyID.
        /// <summary>
        /// Re-synchronizes the HarmonyID associated with a mod whose enabled state has changed.
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
        /// Selects the preferred enabled copy for one HarmonyID and makes the active Harmony patch state match that selection.
        /// </summary>
        private static void SyncHarmonyID(string harmonyID)
        {
            if (string.IsNullOrWhiteSpace(harmonyID))
            {
                return;
            }

            HarmonyModData desired = null;
            int enabledCopies = InitialPatchChangeCount;

            // Prefer an enabled LocalLow copy over Workshop. Within the same source
            // tier, later entries retain the historical last-loaded precedence.
            foreach (Mods._mod mod in Mods._Mods)
            {
                if (mod == null)
                {
                    continue;
                }

                HarmonyModData data = GetHarmonyModData(mod);
                if (data == null || !data.HasHarmonyPatch ||
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
        /// Reads the game’s authoritative mod-enabled state and falls back to the cached field only if the vanilla accessor fails.
        /// </summary>
        private static bool IsEnabledByGame(Mods._mod mod)
        {
            if (mod == null)
            {
                return false;
            }

            // Vanilla _mod.IsEnabled() consults staticVars.Settings directly. Treat that
            // as authoritative instead of the mutable Enabled field cached by ReEnableMods().
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
        /// Chooses whether a candidate mod copy should replace the current winner, preferring LocalLow over Workshop and later entries within a tier.
        /// </summary>
        private static bool ShouldPreferCandidate(HarmonyModData current, HarmonyModData candidate)
        {
            if (candidate == null)
            {
                return false;
            }
            if (current == null)
            {
                return true;
            }

            // LocalLow is the explicit user/development override tier. A Workshop copy
            // may only win when there is no enabled local copy for the same HarmonyID.
            if (current.IsWorkshop != candidate.IsWorkshop)
            {
                return !candidate.IsWorkshop;
            }

            // Same tier: preserve the old deterministic last-loaded behavior.
            return true;
        }

        /// <summary>
        /// Logs which physical mod copy won when multiple enabled copies share the same HarmonyID.
        /// </summary>
        private static void LogDuplicateSelection(string harmonyID, HarmonyModData selected, int count)
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
        /// Ensures one desired mod copy owns a HarmonyID by unloading a previous source when needed, loading the assembly, and applying its patches.
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
                // Already patched from the desired physical copy. Nothing to do.
                if (string.Equals(activePath, desiredPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // The winning enabled copy changed. Replace this HarmonyID exactly once.
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
                    Log.LogInfo(string.Format(IntegrationConstants.PatchLoadedLogFormat, desired.PatchAssembly.FullName));
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
        /// Unpatches a currently tracked HarmonyID, removes its source record, and updates the caller’s unpatch count.
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
                Log.LogInfo(string.Format(IntegrationConstants.PatchUnloadedLogFormat, harmonyID));
            }
            catch (Exception ex)
            {
                Log.LogError(string.Format(IntegrationConstants.PatchUnloadFailureLogFormat, harmonyID, ex));
            }
        }

        /// <summary>
        /// Returns a canonical path without trailing directory separators while preserving the original path if normalization fails.
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
        /// Loads and caches Harmony metadata for a mod directory, including its HarmonyID, patch DLL path, source tier, and validation state.
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
                IsWorkshop = mod.IsWorkshop(),
                ModName = mod.ModName,
                ModPath = modDir
            };

            // Cache negative results too, so non-Harmony mods are never reparsed.
            ModDataCache[modDir] = data;

            try
            {
                string modInfoFile = Path.Combine(modDir, IntegrationConstants.ModInfoFileName);
                if (!File.Exists(modInfoFile))
                {
                    return data;
                }

                JSONNode modInfo = mainScript.ProcessInboundData(File.ReadAllText(modInfoFile));
                string harmonyID = modInfo[IntegrationConstants.HarmonyIdJsonKey];

                if (string.IsNullOrWhiteSpace(harmonyID))
                {
                    return data;
                }

                string patchFile = Path.Combine(modDir, harmonyID + IntegrationConstants.ManagedPatchAssemblyExtension);
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
                    Log.LogError(string.Format(IntegrationConstants.HarmonyMetadataReadFailureLogFormat, data.Title, ex));
                    data.WarningLogged = true;
                }
                return data;
            }
        }

        /// <summary>
        /// Returns whether a mod declares a non-empty HarmonyID in its metadata.
        /// </summary>
        internal static bool IsHarmonyDeclaredMod(Mods._mod mod)
        {
            if (mod == null)
            {
                return false;
            }

            HarmonyModData data = GetHarmonyModData(mod);
            return data != null && !string.IsNullOrWhiteSpace(data.HarmonyID);
        }
    }

    // Initial Harmony mod activation belongs to the actual mod-loading operation.
    // Mods.LoadMods() finishes by calling vanilla Mods.ReEnableMods(), so this Postfix
    // sees the final enabled/disabled state without attaching expensive work to every
    // later ReEnableMods() call (notably Options -> Cancel).
    [HarmonyPatch(typeof(Mods), IntegrationConstants.ModsLoadMethodName)]
    public class Mods_LoadMods_P
    {
        /// <summary>
        /// Synchronizes all Harmony-enabled mods after vanilla Mods.LoadMods finishes establishing the final enabled state.
        /// </summary>
        static void Postfix()
        {
            Plugin.SyncAllModPatches();
        }
    }

    // Toggling one vanilla mod entry may affect a HarmonyID shared by another local or
    // Workshop copy, so synchronize the entire HarmonyID group instead of blindly
    // unpatching based on the one entry that was clicked.
    [HarmonyPatch(typeof(staticVars._settings), IntegrationConstants.SwitchModStatusMethodName)]
    public class StaticVars__settings_SwitchModStatus_P
    {
        /// <summary>
        /// Updates the toggled mod’s cached enabled flag and synchronizes its entire shared HarmonyID group.
        /// </summary>
        static void Postfix(string ModName)
        {
            Mods._mod mod = Mods.GetMod(ModName);
            if (mod == null)
            {
                Plugin.Log.LogWarning(string.Format(IntegrationConstants.MissingSwitchedModLogFormat, ModName));
                return;
            }

            mod.Enabled = staticVars.Settings.IsModEnabled(mod.ModName);
            Plugin.SyncModPatch(mod);
        }
    }

    // Vanilla Mods_Popup.Render creates every card and decodes every thumbnail in one call.
    // The paged UI keeps vanilla Mod_Button behavior, but only materializes 24 mods at a time.
    // Search filters the logical mod list before pagination, and thumbnails are loaded sequentially
    // for the current page so opening the Mods popup never allocates the entire library at once.
    [HarmonyPatch(typeof(Mods_Popup), IntegrationConstants.ModsPopupRenderMethodName)]
    public class Mods_Popup_Render_Paged_P
    {
        /// <summary>
        /// Replaces vanilla Mods_Popup.Render with paged rendering when the controller can safely initialize; otherwise allows vanilla rendering.
        /// </summary>
        static bool Prefix(Mods_Popup __instance)
        {
            if (__instance == null || __instance.Container == null || __instance.prefab_mod_button == null)
            {
                return true;
            }

            ModsPagedListController controller = ModsPagedListController.GetOrCreate(__instance);
            if (controller == null || !controller.BeginRender())
            {
                return true;
            }

            return false;
        }
    }

    // Managed page cards still use vanilla Mod_Button.Set/Render for title, description,
    // tooltips, enable state and upload controls. Only screenshot loading is deferred so the
    // current page can appear without synchronously decoding 24 PNGs in the same frame.
    [HarmonyPatch(typeof(Mod_Button), IntegrationConstants.ModButtonRenderScreenshotMethodName)]
    public class Mod_Button_RenderScreenshot_Paged_P
    {
        /// <summary>
        /// Suppresses vanilla synchronous screenshot decoding only for cards managed by the paged controller.
        /// </summary>
        static bool Prefix(Mod_Button __instance)
        {
            return !ModsPagedListController.IsManaged(__instance);
        }
    }

    // Vanilla stores the entire mod description as a ButtonDefault tooltip on every card.
    // Paged cards already display the description inline, and retaining the hover tooltip makes
    // ButtonDefault perform pointer-hover work and can instantiate/update a very large tooltip
    // while the player is scrolling. Managed cards therefore skip vanilla tooltip setup.
    [HarmonyPatch(typeof(Mod_Button), IntegrationConstants.ModButtonRenderTooltipsMethodName)]
    public class Mod_Button_RenderTooltips_Paged_P
    {
        /// <summary>
        /// Suppresses vanilla tooltip setup only for cards managed by the paged controller.
        /// </summary>
        static bool Prefix(Mod_Button __instance)
        {
            return !ModsPagedListController.IsManaged(__instance);
        }
    }

    public sealed class ModsPagedListController : MonoBehaviour
    {
        private static class UiLocalization
        {
            // Localization keys are constants because they are stable IDs shared with the embedded JSON files.
            internal const string SearchPlaceholder = "IMHI_MODS_SEARCH_PLACEHOLDER";
            internal const string Page = "IMHI_MODS_PAGE";
            internal const string Previous = "IMHI_MODS_PREVIOUS";
            internal const string Next = "IMHI_MODS_NEXT";
            internal const string Mods = "IMHI_MODS_MODS";
            internal const string Matches = "IMHI_MODS_MATCHES";
            internal const string PerPage = "IMHI_MODS_PER_PAGE";
            internal const string NoResults = "IMHI_MODS_NO_RESULTS";
            internal const string PageFormat = "IMHI_MODS_PAGE_FORMAT";
            internal const string ModCountFormat = "IMHI_MODS_COUNT_FORMAT";
            internal const string MatchCountFormat = "IMHI_MODS_MATCH_COUNT_FORMAT";
            internal const string Version = "IMHI_MODS_VERSION";
            internal const string VersionFormat = "IMHI_MODS_VERSION_FORMAT";
            internal const string Local = "IMHI_MODS_LOCAL";
            internal const string Workshop = "IMHI_MODS_WORKSHOP";
            internal const string AllMods = "IMHI_MODS_ALL_MODS";

            private const string DefaultSearchPlaceholderText = "Search mods...";
            private const string DefaultPageText = "Page";
            private const string DefaultPreviousText = "Previous";
            private const string DefaultNextText = "Next";
            private const string DefaultModsText = "mods";
            private const string DefaultMatchesText = "matches";
            private const string DefaultPerPageText = "page";
            private const string DefaultNoResultsText = "No results";
            private const string DefaultPageFormatText = "Page {0} / {1}";
            private const string DefaultModCountFormatText = "{0} mods  |  {1}/page";
            private const string DefaultMatchCountFormatText = "{0} matches  |  {1}/page";
            private const string DefaultVersionText = "version";
            private const string DefaultVersionFormatText = "version {0}";
            private const string DefaultAllModsText = "All Mods";

            private const string EnglishLanguageCode = "en";
            private const string ChineseLanguageCode = "cn";
            private const string SimplifiedChineseLanguageCode = "zh";
            private const string SimplifiedChineseRegionalCode = "zh-cn";
            private const string TraditionalChineseRegionalCode = "zh-tw";
            private const string JapaneseLanguageCode = "jp";
            private const string JapaneseIsoLanguageCode = "ja";
            private const string RussianLanguageCode = "ru";
            private const string PortugueseLanguageCode = "pt";
            private const string BrazilianPortugueseRegionalCode = "pt-br";
            private const string BrazilianPortugueseLanguageCode = "ptbr";
            private const string KoreanLanguageCode = "kr";
            private const string KoreanIsoLanguageCode = "ko";
            private const string FrenchLanguageCode = "fr";
            private const string SpanishLanguageCode = "es";

            private const string LocalizationResourcePrefix = "HarmonyIntegration.Localization.";
            private const string JsonFileExtension = ".json";
            private const string LocalizationIdJsonKey = "id";
            private const string LocalizationTextJsonKey = "text";
            private const string MissingLocalizationLogFormat =
                "Embedded HarmonyIntegration localization not found for language '{0}' ({1}). Falling back to English.";
            private const string LocalizationLoadFailureLogFormat =
                "Failed to load embedded HarmonyIntegration localization '{0}': {1}";

            private static readonly Dictionary<string, string> Values =
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { SearchPlaceholder, DefaultSearchPlaceholderText },
                    { Page, DefaultPageText },
                    { Previous, DefaultPreviousText },
                    { Next, DefaultNextText },
                    { Mods, DefaultModsText },
                    { Matches, DefaultMatchesText },
                    { PerPage, DefaultPerPageText },
                    { NoResults, DefaultNoResultsText },
                    { PageFormat, DefaultPageFormatText },
                    { ModCountFormat, DefaultModCountFormatText },
                    { MatchCountFormat, DefaultMatchCountFormatText },
                    { Version, DefaultVersionText },
                    { VersionFormat, DefaultVersionFormatText },
                    { Local, IntegrationConstants.LocalSourceName },
                    { Workshop, IntegrationConstants.WorkshopSourceName },
                    { AllMods, DefaultAllModsText }
                };

            private static readonly Dictionary<string, string> EnglishDefaults =
                new Dictionary<string, string>(Values, StringComparer.Ordinal);

            private static string loadedLanguage = string.Empty;

            /// <summary>
            /// Resets localization to English defaults, overlays the active language resource, and records the loaded language.
            /// </summary>
            internal static void Reload()
            {
                string language = NormalizeLanguage(
                    staticVars.Settings != null ? staticVars.Settings.Language : EnglishLanguageCode);

                Values.Clear();
                foreach (KeyValuePair<string, string> pair in EnglishDefaults)
                {
                    Values[pair.Key] = pair.Value;
                }

                // Load English first so a partially translated file can fall back key-by-key.
                LoadFile(EnglishLanguageCode, false);
                if (!string.Equals(language, EnglishLanguageCode, StringComparison.OrdinalIgnoreCase))
                {
                    LoadFile(language, true);
                }

                loadedLanguage = language;
            }

            /// <summary>
            /// Returns a localized value for a key, falling back to the English default and then the key itself.
            /// </summary>
            internal static string Get(string key)
            {
                EnsureLoaded();

                string value;
                if (Values.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
                {
                    return value;
                }

                if (EnglishDefaults.TryGetValue(key, out value))
                {
                    return value;
                }

                return key;
            }

            /// <summary>
            /// Formats a localized template with arguments and safely returns the unformatted template if formatting fails.
            /// </summary>
            internal static string Format(string key, params object[] args)
            {
                string template = Get(key);
                try
                {
                    return string.Format(template, args);
                }
                catch
                {
                    return template;
                }
            }

            /// <summary>
            /// Reloads localization when the game’s active language differs from the language currently cached.
            /// </summary>
            private static void EnsureLoaded()
            {
                string language = NormalizeLanguage(
                    staticVars.Settings != null ? staticVars.Settings.Language : EnglishLanguageCode);
                if (!string.Equals(loadedLanguage, language, StringComparison.OrdinalIgnoreCase))
                {
                    Reload();
                }
            }

            /// <summary>
            /// Maps game and ISO-style language codes to the localization resource codes shipped with the plugin.
            /// </summary>
            private static string NormalizeLanguage(string language)
            {
                if (string.IsNullOrEmpty(language))
                {
                    return EnglishLanguageCode;
                }

                switch (language.Trim().ToLowerInvariant())
                {
                    case ChineseLanguageCode:
                    case SimplifiedChineseLanguageCode:
                    case SimplifiedChineseRegionalCode:
                    case TraditionalChineseRegionalCode:
                        return ChineseLanguageCode;
                    case JapaneseLanguageCode:
                    case JapaneseIsoLanguageCode:
                        return JapaneseLanguageCode;
                    case RussianLanguageCode:
                        return RussianLanguageCode;
                    case PortugueseLanguageCode:
                    case BrazilianPortugueseRegionalCode:
                    case BrazilianPortugueseLanguageCode:
                        return BrazilianPortugueseLanguageCode;
                    case KoreanLanguageCode:
                    case KoreanIsoLanguageCode:
                        return KoreanLanguageCode;
                    case FrenchLanguageCode:
                        return FrenchLanguageCode;
                    case SpanishLanguageCode:
                        return SpanishLanguageCode;
                    default:
                        return EnglishLanguageCode;
                }
            }

            /// <summary>
            /// Reads one embedded localization JSON resource and merges valid id/text pairs into the active localization table.
            /// </summary>
            private static void LoadFile(string language, bool warnIfMissing)
            {
                // Localization JSON remains as ordinary source files for translators, but MSBuild
                // embeds them into HarmonyIntegration.dll. Distribution therefore needs only the
                // plugin DLL; no Localization directory is required beside it at runtime.
                string resourceName =
                    LocalizationResourcePrefix + language + JsonFileExtension;

                try
                {
                    Assembly assembly = Assembly.GetExecutingAssembly();
                    using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)
                        {
                            if (warnIfMissing && Plugin.Log != null)
                            {
                                Plugin.Log.LogWarning(string.Format(
                                    MissingLocalizationLogFormat,
                                    language,
                                    resourceName));
                            }
                            return;
                        }

                        using (StreamReader reader = new StreamReader(stream))
                        {
                            JSONNode root = mainScript.ProcessInboundData(reader.ReadToEnd());
                            foreach (object item in root.AsArray)
                            {
                                JSONNode node = (JSONNode)item;
                                string id = node[LocalizationIdJsonKey];
                                string text = node[LocalizationTextJsonKey];
                                if (!string.IsNullOrEmpty(id) && text != null)
                                {
                                    Values[id] = text;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogWarning(string.Format(
                            LocalizationLoadFailureLogFormat,
                            language,
                            ex.Message));
                    }
                }
            }
        }

        // Pagination and render pacing.
        private const int PageSize = 24;
        private const int CardsPerFrame = 4;
        private const float SearchDebounceSeconds = 0.20f;

        // Popup geometry. Smaller vertical gaps intentionally let the list consume more of the
        // enlarged popup while retaining enough breathing room to distinguish fixed controls.
        private const float HeaderHeight = 52f;
        private const float FooterHeight = 50f;
        private const float HeaderVerticalOffset = 4f;
        private const float FooterVerticalOffset = -4f;
        private const float ControlHorizontalMargin = 12f;
        private const float ControlVerticalGap = 4f;
        private const float PopupContentVerticalMargin = 4f;
        private const float PopupOuterMargin = 12f;
        private const float PopupBottomButtonGap = 10f;
        private const float MaximumGridVerticalSpacing = 4f;

        // Card metadata and generated rounded-sprite geometry.
        private const float MetadataHeight = 20f;
        private const float MetadataGap = 2f;
        private const int RoundedSpriteSize = 32;
        private const int RoundedSpriteRadius = 7;
        private const int ThumbnailCornerSpriteSize = 24;
        private const float ThumbnailCornerOverlaySize = 10f;
        private const float ThumbnailCornerRadiusInset = 0.75f;
        private const float HalfPixelOffset = 0.5f;
        private const float SpritePixelsPerUnit = 100f;
        private const uint SpriteExtrudePixels = 0u;
        private const float FullOpacity = 1f;
        private const float NoFloatOffset = 0f;
        private const float PositionChangeEpsilon = 0.01f;
        private const float ByteChannelScale = 255f;
        private const float ScrollTopNormalizedPosition = 1f;
        private const float QuarterTurnDegrees = 90f;
        private const float HalfTurnDegrees = 180f;
        private const int RectCornerCount = 4;
        private const int BottomLeftCornerIndex = 0;
        private const int TopLeftCornerIndex = 1;
        private const int TopRightCornerIndex = 2;
        private const int BottomRightCornerIndex = 3;
        private const int MinimumCardsForRibbonAlignment = 2;
        private const int SearchCharacterLimit = 160;
        private const int FirstCollectionIndex = 0;
        private const int EmptyItemCount = 0;
        private const int FirstPageIndex = 0;
        private const int LastIndexOffset = 1;
        private const int HumanPageNumberOffset = 1;
        private const int NextSiblingOffset = 1;
        private const float InputFontSize = 20f;
        private const float ResultCountFontSize = 18f;
        private const float PageLabelFontSize = 20f;
        private const float MetadataVersionFontSize = 13f;
        private const float MetadataBadgeFontSize = 12.5f;
        private const float MetadataVersionWidthPadding = 4f;
        private const float MetadataVersionMinimumWidth = 60f;
        private const float MetadataVersionMaximumWidth = 180f;
        private const float MetadataItemHorizontalGap = 6f;
        private const float MetadataBadgeWidthPadding = 18f;
        private const float MetadataBadgeMinimumWidth = 52f;
        private const float MetadataBadgeMaximumWidth = 132f;
        private const float FallbackButtonFontSize = 20f;
        private const float FallbackButtonHorizontalTextInset = 6f;
        private const float FallbackButtonVerticalTextInset = 2f;

        // Stable object names and diagnostics used by the injected UI.
        private const string ControlsOverlayObjectName = "IMHI Mods Controls Overlay";
        private const string SearchPanelObjectName = "IMHI Mods Search";
        private const string SearchInputObjectName = "Search";
        private const string SearchTextAreaObjectName = "Text Area";
        private const string TextObjectName = "Text";
        private const string SearchPlaceholderObjectName = "Placeholder";
        private const string ResultCountObjectName = "Result Count";
        private const string PaginationPanelObjectName = "IMHI Mods Pagination";
        private const string PreviousButtonObjectName = "Previous";
        private const string PageLabelObjectName = "Page Label";
        private const string NextButtonObjectName = "Next";
        private const string RoundedUiTextureName = "IMHI Rounded UI Texture";
        private const string RoundedUiSpriteName = "IMHI Rounded UI Sprite";
        private const string ThumbnailCornerTextureName = "IMHI Thumbnail Corner Cutout Texture";
        private const string ThumbnailCornerSpriteName = "IMHI Thumbnail Corner Cutout Sprite";
        private const string ThumbnailCornerTopLeftName = "IMHI Thumbnail Corner TL";
        private const string ThumbnailCornerTopRightName = "IMHI Thumbnail Corner TR";
        private const string ThumbnailCornerBottomRightName = "IMHI Thumbnail Corner BR";
        private const string ThumbnailCornerBottomLeftName = "IMHI Thumbnail Corner BL";
        private const string MetadataRowObjectName = "IMHI Mod Metadata";
        private const string VersionLabelObjectName = "Version";
        private const string SourceBadgeObjectName = "Source Badge";
        private const string SourceFilterButtonObjectName = "IMHI Mod Source Filter";
        private const string ThumbnailTextureNamePrefix = "IMHI Mod Thumbnail: ";
        private const string FileUriPrefix = "file:///";
        private const string WindowsPathSeparator = "\\";
        private const string UriPathSeparator = "/";
        private const string OneDecimalFormat = "0.0";
        private const string PaginationEnabledLogFormat =
            "Mods pagination enabled: {0} mods, {1} mods/page, search + previous/next controls active.";
        private const string MissingGridLayoutLogMessage =
            "Mods pagination disabled: Mods container has no GridLayoutGroup.";
        private const string MissingScrollRectLogMessage =
            "Mods pagination disabled: couldn't find the Mods ScrollRect.";
        private const string NativeButtonFallbackLogMessage =
            "Mods pagination could not find the vanilla Back button template; falling back to a simple Unity UI button.";
        private const string RibbonAlignmentLogFormat =
            "Aligned Mods UI ribbons to card columns: left={0}, right={1}.";
        private const string CardCreationFailureLogFormat =
            "Failed to create Mods card for {0}: {1}";

        // Named anchors, offsets, colors, and search separators keep layout code self-describing.
        private static readonly Vector2 TopLeftAnchor = new Vector2(0f, 1f);
        private static readonly Vector2 TopRightAnchor = new Vector2(1f, 1f);
        private static readonly Vector2 BottomRightAnchor = new Vector2(1f, 0f);
        private static readonly Vector2 BottomLeftAnchor = new Vector2(0f, 0f);
        private static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 TopCenterPivot = new Vector2(0.5f, 1f);
        private static readonly Vector2 BottomCenterPivot = new Vector2(0.5f, 0f);
        private static readonly Vector2 SearchInputAnchorMin = new Vector2(0f, 0.06f);
        private static readonly Vector2 SearchInputAnchorMax = new Vector2(0.72f, 0.94f);
        private static readonly Vector2 SearchInputOffsetMin = new Vector2(10f, 0f);
        private static readonly Vector2 SearchInputOffsetMax = new Vector2(-6f, 0f);
        private static readonly Vector2 SearchTextAreaOffsetMin = new Vector2(12f, 2f);
        private static readonly Vector2 SearchTextAreaOffsetMax = new Vector2(-12f, -2f);
        private static readonly Vector2 ResultCountAnchorMin = new Vector2(0.73f, 0f);
        private static readonly Vector2 ResultCountAnchorMax = new Vector2(1f, 1f);
        private static readonly Vector2 ResultCountOffsetMin = new Vector2(6f, 0f);
        private static readonly Vector2 ResultCountOffsetMax = new Vector2(-12f, 0f);
        private static readonly Vector2 PreviousButtonAnchorMin = new Vector2(0f, 0.06f);
        private static readonly Vector2 PreviousButtonAnchorMax = new Vector2(0.28f, 0.94f);
        private static readonly Vector2 PreviousButtonOffsetMin = new Vector2(10f, 0f);
        private static readonly Vector2 PreviousButtonOffsetMax = new Vector2(-6f, 0f);
        private static readonly Vector2 PageLabelAnchorMin = new Vector2(0.30f, 0f);
        private static readonly Vector2 PageLabelAnchorMax = new Vector2(0.70f, 1f);
        private static readonly Vector2 NextButtonAnchorMin = new Vector2(0.72f, 0.06f);
        private static readonly Vector2 NextButtonAnchorMax = new Vector2(1f, 0.94f);
        private static readonly Vector2 NextButtonOffsetMin = new Vector2(6f, 0f);
        private static readonly Vector2 NextButtonOffsetMax = new Vector2(-10f, 0f);
        private static readonly Vector2 MetadataBadgeAnchorMin = new Vector2(0f, 0.06f);
        private static readonly Vector2 MetadataBadgeAnchorMax = new Vector2(0f, 0.94f);
        private static readonly Vector2 FallbackButtonTextOffsetMin =
            new Vector2(FallbackButtonHorizontalTextInset, FallbackButtonVerticalTextInset);
        private static readonly Vector2 FallbackButtonTextOffsetMax =
            new Vector2(-FallbackButtonHorizontalTextInset, -FallbackButtonVerticalTextInset);
        private static readonly Color32 InputTextColor = new Color32(70, 70, 70, 255);
        private static readonly Color32 PlaceholderTextColor = new Color32(135, 135, 135, 210);
        private static readonly Color32 ResultTextColor = new Color32(85, 85, 85, 255);
        private static readonly Color32 PageTextColor = new Color32(75, 75, 75, 255);
        private static readonly Color32 SearchBackgroundColor = new Color32(255, 255, 255, 255);
        private static readonly Color32 PanelBackgroundColor = new Color32(242, 242, 242, 255);
        private static readonly Color32 MetadataVersionColor = new Color32(105, 105, 105, 255);
        private static readonly Color32 WorkshopBadgeColor = new Color32(226, 231, 247, 255);
        private static readonly Color32 LocalBadgeColor = new Color32(226, 244, 232, 255);
        private static readonly Color32 MetadataBadgeTextColor = new Color32(82, 82, 82, 255);
        private static readonly char[] SearchTermSeparators = { ' ', '\t', '\r', '\n' };

        private enum SourceFilterMode
        {
            All,
            Local,
            Workshop
        }

        private sealed class PageCard
        {
            public int FilteredIndex;
            public Mods._mod Mod;
            public GameObject Root;
            public Mod_Button Button;
            public Image ThumbnailImage;
            public Sprite RuntimeSprite;
            public Texture2D RuntimeTexture;
        }

        private static readonly Dictionary<int, ModsPagedListController> ManagedButtons =
            new Dictionary<int, ModsPagedListController>();

        private readonly List<Mods._mod> allMods = new List<Mods._mod>();
        private readonly List<Mods._mod> filteredMods = new List<Mods._mod>();
        private readonly List<PageCard> pageCards = new List<PageCard>();

        private Mods_Popup popup;
        private RectTransform content;
        private ScrollRect scrollRect;
        private RectTransform viewport;
        private GridLayoutGroup grid;
        private RectTransform popupWindowRect;
        private RectTransform scrollAreaRect;
        private RectTransform controlsOverlayRect;

        private GameObject headerRoot;
        private GameObject footerRoot;
        private RectTransform headerRect;
        private RectTransform footerRect;
        private TMP_InputField searchField;
        private TextMeshProUGUI searchPlaceholder;
        private TextMeshProUGUI resultLabel;
        private TextMeshProUGUI pageLabel;
        private Button previousButton;
        private Button nextButton;
        private Button sourceFilterButton;
        private GameObject nativeButtonTemplate;
        private Sprite roundedUiSprite;
        private Texture2D roundedUiTexture;
        private Sprite thumbnailCornerCutoutSprite;
        private Texture2D thumbnailCornerCutoutTexture;
        private TextMeshProUGUI nativeTmpTextTemplate;

        private int originalPaddingTop;
        private int originalPaddingBottom;
        private Vector2 originalGridSpacing;
        private Vector2 originalScrollAreaAnchorMin;
        private Vector2 originalScrollAreaAnchorMax;
        private Vector2 originalScrollAreaOffsetMin;
        private Vector2 originalScrollAreaOffsetMax;
        private Vector2 originalPopupAnchorMin;
        private Vector2 originalPopupAnchorMax;
        private Vector2 originalPopupOffsetMin;
        private Vector2 originalPopupOffsetMax;
        private bool popupWindowLayoutCaptured;
        private bool scrollAreaLayoutCaptured;
        private RectTransform nativeBackButtonRect;
        private Vector2 originalBackButtonAnchoredPosition;
        private bool backButtonLayoutCaptured;
        private bool layoutCaptured;
        private bool languageSubscribed;
        private bool ribbonsAligned;
        private bool initialized;
        private bool rendering;
        private int currentPage;
        private int renderGeneration;
        private SourceFilterMode sourceFilterMode = SourceFilterMode.All;
        private string activeQuery = string.Empty;
        private string pendingQuery = string.Empty;

        private Coroutine pageRenderCoroutine;
        private Coroutine thumbnailCoroutine;
        private Coroutine searchDebounceCoroutine;
        private UnityWebRequest activeThumbnailRequest;

        /// <summary>
        /// Gets the pagination controller attached to a Mods popup, adding one when needed and binding it to the popup owner.
        /// </summary>
        internal static ModsPagedListController GetOrCreate(Mods_Popup owner)
        {
            if (owner == null)
            {
                return null;
            }

            ModsPagedListController result = owner.GetComponent<ModsPagedListController>();
            if (result == null)
            {
                result = owner.gameObject.AddComponent<ModsPagedListController>();
            }
            result.popup = owner;
            return result;
        }

        /// <summary>
        /// Returns whether a Mod_Button belongs to the paged controller and should receive managed rendering behavior.
        /// </summary>
        internal static bool IsManaged(Mod_Button button)
        {
            if (button == null)
            {
                return false;
            }

            ModsPagedListController manager;
            return ManagedButtons.TryGetValue(button.GetInstanceID(), out manager) && manager != null;
        }

        /// <summary>
        /// Prepares the adaptive popup layout, refreshes the filtered mod list, resets pagination, and starts rendering the first page.
        /// </summary>
        internal bool BeginRender()
        {
            if (popup == null || popup.Container == null || popup.prefab_mod_button == null)
            {
                return false;
            }

            if (!InitializeIfNeeded())
            {
                return false;
            }

            // Re-apply the adaptive window geometry whenever the popup is rendered. This also
            // catches resolution/UI-scale changes made while the game is running.
            ResizePopupWindowToAvailableHeight();
            ResizeScrollAreaToPopupContent();
            SyncControlsOverlayToViewport();
            ribbonsAligned = false;

            rendering = true;
            ReloadAllMods();
            ApplySearchFilter();
            currentPage = FirstPageIndex;
            RenderCurrentPage();

            Plugin.Log.LogInfo(string.Format(
                PaginationEnabledLogFormat,
                allMods.Count,
                PageSize));
            return true;
        }

        /// <summary>
        /// Finds the native Mods UI components, captures restorable layout state, compacts vertical spacing, creates controls, and subscribes localization events.
        /// </summary>
        private bool InitializeIfNeeded()
        {
            if (initialized)
            {
                return true;
            }

            content = popup.Container.transform as RectTransform;
            if (content == null)
            {
                return false;
            }

            grid = popup.Container.GetComponent<GridLayoutGroup>();
            if (grid == null)
            {
                Plugin.Log.LogWarning(MissingGridLayoutLogMessage);
                return false;
            }

            scrollRect = FindScrollRect(content);
            if (scrollRect == null)
            {
                Plugin.Log.LogWarning(MissingScrollRectLogMessage);
                return false;
            }

            viewport = scrollRect.viewport != null
                ? scrollRect.viewport
                : scrollRect.transform as RectTransform;
            if (viewport == null)
            {
                return false;
            }

            popupWindowRect = FindPopupWindowRect();
            CapturePopupWindowLayout();
            scrollAreaRect = FindScrollAreaRect();
            CaptureScrollAreaLayout();
            ResizePopupWindowToAvailableHeight();
            ResizeScrollAreaToPopupContent();

            originalPaddingTop = grid.padding.top;
            originalPaddingBottom = grid.padding.bottom;
            originalGridSpacing = grid.spacing;
            layoutCaptured = true;

            // Keep the fixed header/footer reachable without carrying forward the roomier vanilla
            // top/bottom padding. The compact gap gives cards more usable vertical space.
            int compactBaseTopPadding = Mathf.Min(
                originalPaddingTop,
                Mathf.RoundToInt(PopupContentVerticalMargin));
            int compactBaseBottomPadding = Mathf.Min(
                originalPaddingBottom,
                Mathf.RoundToInt(PopupContentVerticalMargin));
            grid.padding.top = compactBaseTopPadding +
                Mathf.RoundToInt(HeaderHeight + ControlVerticalGap);
            grid.padding.bottom = compactBaseBottomPadding +
                Mathf.RoundToInt(FooterHeight + ControlVerticalGap);

            Vector2 compactGridSpacing = originalGridSpacing;
            compactGridSpacing.y = Mathf.Min(originalGridSpacing.y, MaximumGridVerticalSpacing);
            grid.spacing = compactGridSpacing;

            UiLocalization.Reload();
            CreateControls();

            Language.onReset += OnLanguageReset;
            languageSubscribed = true;

            initialized = true;
            ApplyLocalizedUi();
            return true;
        }

        /// <summary>
        /// Walks up from the mods content to locate the ScrollRect that owns it, assigning the content reference when vanilla left it unset.
        /// </summary>
        private static ScrollRect FindScrollRect(RectTransform targetContent)
        {
            Transform current = targetContent;
            while (current != null)
            {
                ScrollRect candidate = current.GetComponent<ScrollRect>();
                if (candidate != null && (candidate.content == null || candidate.content == targetContent))
                {
                    if (candidate.content == null)
                    {
                        candidate.content = targetContent;
                    }
                    return candidate;
                }
                current = current.parent;
            }
            return null;
        }

        /// <summary>
        /// Finds the white popup window branch that contains the Mods scroll area without including the separate bottom action row.
        /// </summary>
        private RectTransform FindPopupWindowRect()
        {
            if (scrollRect == null)
            {
                return null;
            }

            GameObject backButton = FindNativeButtonTemplate();
            Transform common = backButton != null
                ? FindLowestCommonAncestor(scrollRect.transform, backButton.transform)
                : popup.transform;

            if (common == null)
            {
                common = popup.transform;
            }

            // The actual white Mods window is the scroll branch immediately below the common
            // full-screen popup root. The Back button lives on a different branch below that same
            // root, so this avoids stretching the whole popup object or the Back button itself.
            Transform branch = scrollRect.transform;
            while (branch != null && branch.parent != null && branch.parent != common)
            {
                branch = branch.parent;
            }

            RectTransform result = branch as RectTransform;
            if (result == null || result == popup.transform)
            {
                return scrollRect.transform as RectTransform;
            }
            return result;
        }

        /// <summary>
        /// Finds the highest scroll-area rectangle inside the popup that can be stretched to consume newly available vertical space.
        /// </summary>
        private RectTransform FindScrollAreaRect()
        {
            if (scrollRect == null)
            {
                return null;
            }

            Transform branch = scrollRect.transform;
            if (popupWindowRect != null && branch != popupWindowRect)
            {
                while (branch.parent != null && branch.parent != popupWindowRect)
                {
                    branch = branch.parent;
                }

                if (branch.parent == popupWindowRect)
                {
                    return branch as RectTransform;
                }
            }

            if (branch == popupWindowRect && viewport != null && viewport != popupWindowRect)
            {
                return viewport;
            }

            return branch as RectTransform;
        }

        /// <summary>
        /// Finds the nearest transform that is an ancestor of both supplied transforms.
        /// </summary>
        private static Transform FindLowestCommonAncestor(Transform a, Transform b)
        {
            if (a == null || b == null)
            {
                return null;
            }

            HashSet<Transform> ancestors = new HashSet<Transform>();
            Transform current = a;
            while (current != null)
            {
                ancestors.Add(current);
                current = current.parent;
            }

            current = b;
            while (current != null)
            {
                if (ancestors.Contains(current))
                {
                    return current;
                }
                current = current.parent;
            }
            return null;
        }

        /// <summary>
        /// Stores the popup window anchors and offsets once so the original layout can be restored on teardown.
        /// </summary>
        private void CapturePopupWindowLayout()
        {
            if (popupWindowLayoutCaptured || popupWindowRect == null)
            {
                return;
            }

            originalPopupAnchorMin = popupWindowRect.anchorMin;
            originalPopupAnchorMax = popupWindowRect.anchorMax;
            originalPopupOffsetMin = popupWindowRect.offsetMin;
            originalPopupOffsetMax = popupWindowRect.offsetMax;
            popupWindowLayoutCaptured = true;
        }

        /// <summary>
        /// Stores the stretchable scroll-area anchors and offsets once so injected geometry can be fully reverted.
        /// </summary>
        private void CaptureScrollAreaLayout()
        {
            if (scrollAreaLayoutCaptured || scrollAreaRect == null)
            {
                return;
            }

            originalScrollAreaAnchorMin = scrollAreaRect.anchorMin;
            originalScrollAreaAnchorMax = scrollAreaRect.anchorMax;
            originalScrollAreaOffsetMin = scrollAreaRect.offsetMin;
            originalScrollAreaOffsetMax = scrollAreaRect.offsetMax;
            scrollAreaLayoutCaptured = true;
        }

        /// <summary>
        /// Stretches the scroll-area branch vertically inside the enlarged popup while preserving a small internal margin.
        /// </summary>
        private void ResizeScrollAreaToPopupContent()
        {
            if (scrollAreaRect == null)
            {
                scrollAreaRect = FindScrollAreaRect();
            }
            if (scrollAreaRect == null || scrollAreaRect == popupWindowRect)
            {
                return;
            }

            CaptureScrollAreaLayout();

            Vector2 anchorMin = scrollAreaRect.anchorMin;
            Vector2 anchorMax = scrollAreaRect.anchorMax;
            anchorMin.y = BottomLeftAnchor.y;
            anchorMax.y = TopLeftAnchor.y;
            scrollAreaRect.anchorMin = anchorMin;
            scrollAreaRect.anchorMax = anchorMax;

            Vector2 offsetMin = scrollAreaRect.offsetMin;
            Vector2 offsetMax = scrollAreaRect.offsetMax;
            offsetMin.y = PopupContentVerticalMargin;
            offsetMax.y = -PopupContentVerticalMargin;
            scrollAreaRect.offsetMin = offsetMin;
            scrollAreaRect.offsetMax = offsetMax;
        }

        /// <summary>
        /// Stores the native Back button position before moving the bottom action row.
        /// </summary>
        private void CaptureBackButtonLayout(RectTransform backRect)
        {
            if (backButtonLayoutCaptured || backRect == null)
            {
                return;
            }

            nativeBackButtonRect = backRect;
            originalBackButtonAnchoredPosition = backRect.anchoredPosition;
            backButtonLayoutCaptured = true;
        }

        /// <summary>
        /// Moves the native Back button row to the configured outer margin and re-aligns the mirrored source-filter button.
        /// </summary>
        private void PositionBottomActionRowAtOuterMargin()
        {
            GameObject backButton = FindNativeButtonTemplate();
            RectTransform backRect = backButton != null
                ? backButton.GetComponent<RectTransform>()
                : null;
            RectTransform parentRect = backRect != null
                ? backRect.parent as RectTransform
                : null;

            if (backRect == null || parentRect == null)
            {
                return;
            }

            CaptureBackButtonLayout(backRect);

            Vector3[] backCorners = new Vector3[RectCornerCount];
            backRect.GetWorldCorners(backCorners);
            Vector3 bottomLeft = parentRect.InverseTransformPoint(backCorners[BottomLeftCornerIndex]);
            Vector3 bottomRight = parentRect.InverseTransformPoint(backCorners[BottomRightCornerIndex]);
            float currentBottom = Mathf.Min(bottomLeft.y, bottomRight.y);
            float desiredBottom = parentRect.rect.yMin + PopupOuterMargin;
            float deltaY = desiredBottom - currentBottom;

            if (Mathf.Abs(deltaY) > PositionChangeEpsilon)
            {
                Vector2 anchored = backRect.anchoredPosition;
                anchored.y += deltaY;
                backRect.anchoredPosition = anchored;
            }

            // The source filter is a mirrored clone of Back, so moving Back is the single source
            // of truth for the entire bottom action row.
            AlignSourceFilterButtonToBack();
        }

        /// <summary>
        /// Expands the white Mods window to use the available screen height between the top margin and bottom action row.
        /// </summary>
        private void ResizePopupWindowToAvailableHeight()
        {
            if (popupWindowRect == null)
            {
                popupWindowRect = FindPopupWindowRect();
            }
            if (popupWindowRect == null)
            {
                return;
            }

            RectTransform parentRect = popupWindowRect.parent as RectTransform;
            if (parentRect == null)
            {
                return;
            }

            CapturePopupWindowLayout();
            PositionBottomActionRowAtOuterMargin();

            float bottomInset = PopupOuterMargin;
            GameObject backButton = FindNativeButtonTemplate();
            RectTransform backRect = backButton != null
                ? backButton.GetComponent<RectTransform>()
                : null;

            if (backRect != null)
            {
                Vector3[] backCorners = new Vector3[RectCornerCount];
                backRect.GetWorldCorners(backCorners);
                Vector3 backTopLeft = parentRect.InverseTransformPoint(backCorners[TopLeftCornerIndex]);
                Vector3 backTopRight = parentRect.InverseTransformPoint(backCorners[TopRightCornerIndex]);
                float backTop = Mathf.Max(backTopLeft.y, backTopRight.y);

                // Back / All Mods now sit exactly PopupOuterMargin above the bottom edge. Expand
                // the white Mods window down to the fixed gap immediately above that action row.
                // The top of the window uses the same PopupOuterMargin, giving the whole popup a
                // balanced top/bottom screen margin.
                bottomInset = Mathf.Max(
                    PopupOuterMargin,
                    backTop - parentRect.rect.yMin + PopupBottomButtonGap);
            }

            Vector2 anchorMin = popupWindowRect.anchorMin;
            Vector2 anchorMax = popupWindowRect.anchorMax;
            anchorMin.y = BottomLeftAnchor.y;
            anchorMax.y = TopLeftAnchor.y;
            popupWindowRect.anchorMin = anchorMin;
            popupWindowRect.anchorMax = anchorMax;

            Vector2 offsetMin = popupWindowRect.offsetMin;
            Vector2 offsetMax = popupWindowRect.offsetMax;
            offsetMin.y = bottomInset;
            offsetMax.y = -PopupOuterMargin;
            popupWindowRect.offsetMin = offsetMin;
            popupWindowRect.offsetMax = offsetMax;
        }

        /// <summary>
        /// Creates an unmasked overlay matching the viewport so fixed search and pagination controls are not clipped by RectMask2D.
        /// </summary>
        private RectTransform CreateControlsOverlay()
        {
            if (viewport == null)
            {
                return null;
            }

            Transform parent = viewport.parent != null ? viewport.parent : popup.transform;
            GameObject overlayObject = new GameObject(
                ControlsOverlayObjectName,
                typeof(RectTransform),
                typeof(LayoutElement));
            overlayObject.transform.SetParent(parent, false);

            LayoutElement layout = overlayObject.GetComponent<LayoutElement>();
            layout.ignoreLayout = true;

            RectTransform overlay = overlayObject.GetComponent<RectTransform>();
            SyncRectTransformLayout(viewport, overlay);
            overlay.SetAsLastSibling();
            return overlay;
        }

        /// <summary>
        /// Keeps the controls overlay aligned with the current viewport after resolution or UI-scale changes.
        /// </summary>
        private void SyncControlsOverlayToViewport()
        {
            if (viewport == null || controlsOverlayRect == null)
            {
                return;
            }

            // The overlay is intentionally outside the viewport's RectMask2D, but it must occupy
            // the exact same base rectangle so its fixed header/footer remain aligned with cards.
            if (controlsOverlayRect.parent == viewport.parent)
            {
                SyncRectTransformLayout(viewport, controlsOverlayRect);
            }
        }

        /// <summary>
        /// Copies anchors, pivot, offsets, scale, and rotation from one RectTransform to another.
        /// </summary>
        private static void SyncRectTransformLayout(RectTransform source, RectTransform target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.offsetMin = source.offsetMin;
            target.offsetMax = source.offsetMax;
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;
        }

        /// <summary>
        /// Builds the search field, result count, pagination controls, and source filter using compact named layout values.
        /// </summary>
        private void CreateControls()
        {
            controlsOverlayRect = CreateControlsOverlay();
            Transform controlsParent = controlsOverlayRect != null
                ? controlsOverlayRect
                : (viewport != null ? viewport.parent : popup.transform);

            headerRoot = CreatePanel(SearchPanelObjectName, controlsParent);
            headerRect = headerRoot.GetComponent<RectTransform>();
            headerRect.anchorMin = TopLeftAnchor;
            headerRect.anchorMax = TopRightAnchor;
            headerRect.pivot = TopCenterPivot;
            headerRect.offsetMin = new Vector2(ControlHorizontalMargin, -HeaderHeight + HeaderVerticalOffset);
            headerRect.offsetMax = new Vector2(-ControlHorizontalMargin, HeaderVerticalOffset);

            // Use TextMesh Pro for every new piece of text. Idol Manager's own UI is TMP-heavy,
            // and its SDF font rendering stays crisp when the Canvas is scaled. The previous
            // implementation used legacy UnityEngine.UI.Text, which rasterized the dynamic font
            // and looked visibly softer than the game's native labels.
            GameObject inputObject = new GameObject(
                SearchInputObjectName,
                typeof(RectTransform),
                typeof(Image),
                typeof(TMP_InputField));
            inputObject.transform.SetParent(headerRoot.transform, false);
            RectTransform inputRect = inputObject.GetComponent<RectTransform>();
            inputRect.anchorMin = SearchInputAnchorMin;
            inputRect.anchorMax = SearchInputAnchorMax;
            inputRect.offsetMin = SearchInputOffsetMin;
            inputRect.offsetMax = SearchInputOffsetMax;

            Image inputBackground = inputObject.GetComponent<Image>();
            ApplyRoundedImage(
                inputBackground,
                SearchBackgroundColor,
                true);

            searchField = inputObject.GetComponent<TMP_InputField>();
            searchField.targetGraphic = inputBackground;
            searchField.lineType = TMP_InputField.LineType.SingleLine;
            searchField.contentType = TMP_InputField.ContentType.Standard;
            searchField.characterLimit = SearchCharacterLimit;
            searchField.selectionColor = mainScript.green_light32;

            GameObject textAreaObject = new GameObject(
                SearchTextAreaObjectName,
                typeof(RectTransform),
                typeof(RectMask2D));
            textAreaObject.transform.SetParent(inputObject.transform, false);
            RectTransform textAreaRect = textAreaObject.GetComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = SearchTextAreaOffsetMin;
            textAreaRect.offsetMax = SearchTextAreaOffsetMax;

            TextMeshProUGUI inputText = CreateTmpText(
                TextObjectName,
                textAreaObject.transform,
                string.Empty,
                InputFontSize,
                TextAlignmentOptions.MidlineLeft);
            RectTransform inputTextRect = inputText.rectTransform;
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.offsetMin = Vector2.zero;
            inputTextRect.offsetMax = Vector2.zero;
            inputText.color = InputTextColor;
            inputText.richText = false;
            inputText.overflowMode = TextOverflowModes.Overflow;

            searchPlaceholder = CreateTmpText(
                SearchPlaceholderObjectName,
                textAreaObject.transform,
                UiLocalization.Get(UiLocalization.SearchPlaceholder),
                InputFontSize,
                TextAlignmentOptions.MidlineLeft);
            RectTransform placeholderRect = searchPlaceholder.rectTransform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;
            searchPlaceholder.color = PlaceholderTextColor;
            searchPlaceholder.fontStyle |= FontStyles.Italic;
            searchPlaceholder.overflowMode = TextOverflowModes.Ellipsis;

            searchField.textViewport = textAreaRect;
            searchField.textComponent = inputText;
            searchField.placeholder = searchPlaceholder;
            searchField.onValueChanged.AddListener(OnSearchValueChanged);

            resultLabel = CreateTmpText(
                ResultCountObjectName,
                headerRoot.transform,
                string.Empty,
                ResultCountFontSize,
                TextAlignmentOptions.MidlineRight);
            RectTransform resultRect = resultLabel.rectTransform;
            resultRect.anchorMin = ResultCountAnchorMin;
            resultRect.anchorMax = ResultCountAnchorMax;
            resultRect.offsetMin = ResultCountOffsetMin;
            resultRect.offsetMax = ResultCountOffsetMax;
            resultLabel.color = ResultTextColor;

            footerRoot = CreatePanel(PaginationPanelObjectName, controlsParent);
            footerRect = footerRoot.GetComponent<RectTransform>();
            footerRect.anchorMin = BottomLeftAnchor;
            footerRect.anchorMax = BottomRightAnchor;
            footerRect.pivot = BottomCenterPivot;
            footerRect.offsetMin = new Vector2(ControlHorizontalMargin, FooterVerticalOffset);
            footerRect.offsetMax = new Vector2(-ControlHorizontalMargin, FooterHeight + FooterVerticalOffset);

            previousButton = CreateNativeGameButton(
                PreviousButtonObjectName,
                footerRoot.transform,
                UiLocalization.Get(UiLocalization.Previous));
            RectTransform previousRect = previousButton.GetComponent<RectTransform>();
            previousRect.anchorMin = PreviousButtonAnchorMin;
            previousRect.anchorMax = PreviousButtonAnchorMax;
            previousRect.offsetMin = PreviousButtonOffsetMin;
            previousRect.offsetMax = PreviousButtonOffsetMax;
            previousButton.onClick.AddListener(PreviousPage);

            pageLabel = CreateTmpText(
                PageLabelObjectName,
                footerRoot.transform,
                string.Empty,
                PageLabelFontSize,
                TextAlignmentOptions.Midline);
            RectTransform pageRect = pageLabel.rectTransform;
            pageRect.anchorMin = PageLabelAnchorMin;
            pageRect.anchorMax = PageLabelAnchorMax;
            pageRect.offsetMin = Vector2.zero;
            pageRect.offsetMax = Vector2.zero;
            pageLabel.color = PageTextColor;

            nextButton = CreateNativeGameButton(
                NextButtonObjectName,
                footerRoot.transform,
                UiLocalization.Get(UiLocalization.Next));
            RectTransform nextRect = nextButton.GetComponent<RectTransform>();
            nextRect.anchorMin = NextButtonAnchorMin;
            nextRect.anchorMax = NextButtonAnchorMax;
            nextRect.offsetMin = NextButtonOffsetMin;
            nextRect.offsetMax = NextButtonOffsetMax;
            nextButton.onClick.AddListener(NextPage);

            CreateSourceFilterButton();

            // The controls live on an overlay that mirrors the viewport but is a sibling of the
            // masked viewport. This keeps the configured compact vertical offsets visible instead of
            // letting RectMask2D clip the top and bottom edges.
            if (controlsOverlayRect != null)
            {
                controlsOverlayRect.transform.SetAsLastSibling();
            }
            headerRoot.transform.SetAsLastSibling();
            footerRoot.transform.SetAsLastSibling();
        }

        /// <summary>
        /// Creates a rounded neutral panel GameObject under the requested parent.
        /// </summary>
        private GameObject CreatePanel(string name, Transform parent)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            Image image = panel.GetComponent<Image>();
            ApplyRoundedImage(
                image,
                PanelBackgroundColor,
                true);
            return panel;
        }

        /// <summary>
        /// Creates a TextMeshPro label using the game’s current font styling and the supplied text, size, and alignment.
        /// </summary>
        private TextMeshProUGUI CreateTmpText(
            string name,
            Transform parent,
            string value,
            float size,
            TextAlignmentOptions alignment)
        {
            GameObject textObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            ApplyCurrentGameTmpStyle(text);
            text.fontSize = size;
            text.enableAutoSizing = false;
            text.alignment = alignment;
            text.text = value;
            text.color = Color.white;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Truncate;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// Finds and caches a native TextMeshPro label that can supply the game’s active font and material.
        /// </summary>
        private TextMeshProUGUI GetNativeTmpTextTemplate()
        {
            if (nativeTmpTextTemplate != null)
            {
                return nativeTmpTextTemplate;
            }

            GameObject template = FindNativeButtonTemplate();
            if (template != null)
            {
                nativeTmpTextTemplate =
                    template.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (nativeTmpTextTemplate == null)
            {
                try
                {
                    TextMeshProUGUI[] allText =
                        popup.transform.root.GetComponentsInChildren<TextMeshProUGUI>(true);
                    if (allText != null && allText.Length > EmptyItemCount)
                    {
                        nativeTmpTextTemplate = allText[FirstCollectionIndex];
                    }
                }
                catch
                {
                }
            }

            return nativeTmpTextTemplate;
        }

        /// <summary>
        /// Applies the game’s current TMP font/material to a label, with a serialized Fonts component as fallback.
        /// </summary>
        private void ApplyCurrentGameTmpStyle(TextMeshProUGUI text)
        {
            if (text == null)
            {
                return;
            }

            TextMeshProUGUI template = GetNativeTmpTextTemplate();
            if (template != null && template.font != null)
            {
                text.font = template.font;
                if (template.fontSharedMaterial != null)
                {
                    text.fontSharedMaterial = template.fontSharedMaterial;
                }
                return;
            }

            // Fallback to the TMP font asset serialized on Idol Manager's Fonts component.
            try
            {
                Camera camera = Camera.main;
                mainScript main = camera != null ? camera.GetComponent<mainScript>() : null;
                Fonts fonts = main != null && main.Data != null
                    ? main.Data.GetComponent<Fonts>()
                    : null;

                if (fonts != null && fonts.FontAsset != null)
                {
                    text.font = fonts.FontAsset;
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Rebinds every injected TMP label after a language or font reset.
        /// </summary>
        private void RefreshCustomTmpFonts()
        {
            TextMeshProUGUI template = GetNativeTmpTextTemplate();

            TextMeshProUGUI[] labels =
            {
                searchPlaceholder,
                resultLabel,
                pageLabel,
                searchField != null ? searchField.textComponent as TextMeshProUGUI : null
            };

            foreach (TextMeshProUGUI label in labels)
            {
                if (label == null)
                {
                    continue;
                }

                if (template != null && template.font != null)
                {
                    label.font = template.font;
                    if (template.fontSharedMaterial != null)
                    {
                        label.fontSharedMaterial = template.fontSharedMaterial;
                    }
                }
                else
                {
                    ApplyCurrentGameTmpStyle(label);
                }
            }
        }

        /// <summary>
        /// Adds the game’s Font_Replacer to legacy Text descendants without disturbing native TMP styling.
        /// </summary>
        private void EnsureGameFontBindings(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            // Preserve the native button's own TextMeshPro styling. Legacy Text children, if a
            // localized prefab happens to use them, still get the game's Font_Replacer.
            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                if (text.GetComponent<Font_Replacer>() == null)
                {
                    text.gameObject.AddComponent<Font_Replacer>();
                }
            }
        }

        /// <summary>
        /// Lazily generates and caches a neutral nine-sliced rounded rectangle sprite for injected panels, fields, and badges.
        /// </summary>
        private Sprite GetRoundedUiSprite()
        {
            if (roundedUiSprite != null)
            {
                return roundedUiSprite;
            }

            // Do not reuse the vanilla Back button sprite for generic panels. Its purple/blue
            // artwork is baked into the sprite, so tinting it white still turns neutral ribbons
            // into giant buttons. Generate a tiny neutral white rounded rectangle instead and
            // 9-slice it for panels, search fields and badges. Thumbnail rounding is handled
            // separately with corner overlays so the vanilla screenshot Image is never moved
            // under a stencil Mask.
            roundedUiTexture = new Texture2D(
                RoundedSpriteSize,
                RoundedSpriteSize,
                TextureFormat.RGBA32,
                false);
            roundedUiTexture.name = RoundedUiTextureName;
            roundedUiTexture.wrapMode = TextureWrapMode.Clamp;
            roundedUiTexture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[RoundedSpriteSize * RoundedSpriteSize];
            float half = RoundedSpriteSize * HalfPixelOffset;
            float radius = RoundedSpriteRadius;
            float boxHalf = half - radius;

            for (int y = FirstCollectionIndex; y < RoundedSpriteSize; y++)
            {
                for (int x = FirstCollectionIndex; x < RoundedSpriteSize; x++)
                {
                    float px = Mathf.Abs((x + HalfPixelOffset) - half) - boxHalf;
                    float py = Mathf.Abs((y + HalfPixelOffset) - half) - boxHalf;
                    float ox = Mathf.Max(px, NoFloatOffset);
                    float oy = Mathf.Max(py, NoFloatOffset);
                    float outside = Mathf.Sqrt(ox * ox + oy * oy);
                    float inside = Mathf.Min(Mathf.Max(px, py), NoFloatOffset);
                    float signedDistance = outside + inside - radius;
                    byte alpha = (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(HalfPixelOffset - signedDistance) * ByteChannelScale);

                    pixels[y * RoundedSpriteSize + x] =
                        new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, alpha);
                }
            }

            roundedUiTexture.SetPixels32(pixels);
            roundedUiTexture.Apply(false, false);

            roundedUiSprite = Sprite.Create(
                roundedUiTexture,
                new Rect(NoFloatOffset, NoFloatOffset, RoundedSpriteSize, RoundedSpriteSize),
                CenterAnchor,
                SpritePixelsPerUnit,
                SpriteExtrudePixels,
                SpriteMeshType.FullRect,
                new Vector4(
                    RoundedSpriteRadius,
                    RoundedSpriteRadius,
                    RoundedSpriteRadius,
                    RoundedSpriteRadius));
            roundedUiSprite.name = RoundedUiSpriteName;
            return roundedUiSprite;
        }

        /// <summary>
        /// Applies the shared rounded sprite, requested color, and raycast behavior to a UI Image.
        /// </summary>
        private void ApplyRoundedImage(Image image, Color color, bool raycastTarget)
        {
            if (image == null)
            {
                return;
            }

            Sprite sprite = GetRoundedUiSprite();
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = sprite.border.sqrMagnitude > NoFloatOffset
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
            }

            image.color = color;
            image.raycastTarget = raycastTarget;
        }

        /// <summary>
        /// Lazily generates a quarter-circle cutout sprite used to visually round screenshot corners without stencil masking.
        /// </summary>
        private Sprite GetThumbnailCornerCutoutSprite()
        {
            if (thumbnailCornerCutoutSprite != null)
            {
                return thumbnailCornerCutoutSprite;
            }

            // Do not stencil-mask the live Mod_Button screenshot Image. Reparenting that Image
            // under a uGUI Mask caused screenshots to disappear on Idol Manager's 2019.4 UI.
            // Instead, draw four tiny card-background corner overlays above the normal thumbnail.
            // The thumbnail keeps vanilla's hierarchy/material while the overlays visually cut
            // the square corners into rounded ones.
            thumbnailCornerCutoutTexture = new Texture2D(
                ThumbnailCornerSpriteSize,
                ThumbnailCornerSpriteSize,
                TextureFormat.RGBA32,
                false);
            thumbnailCornerCutoutTexture.name = ThumbnailCornerTextureName;
            thumbnailCornerCutoutTexture.wrapMode = TextureWrapMode.Clamp;
            thumbnailCornerCutoutTexture.filterMode = FilterMode.Bilinear;

            Color32[] pixels =
                new Color32[ThumbnailCornerSpriteSize * ThumbnailCornerSpriteSize];
            float radius = ThumbnailCornerSpriteSize - ThumbnailCornerRadiusInset;
            Vector2 circleCenter =
                new Vector2(ThumbnailCornerSpriteSize, ThumbnailCornerSpriteSize);

            for (int y = FirstCollectionIndex; y < ThumbnailCornerSpriteSize; y++)
            {
                for (int x = FirstCollectionIndex; x < ThumbnailCornerSpriteSize; x++)
                {
                    Vector2 sample = new Vector2(x + HalfPixelOffset, y + HalfPixelOffset);
                    float distance = Vector2.Distance(sample, circleCenter);

                    // Outside the quarter circle is card-colored; inside is transparent so the
                    // thumbnail remains visible. One-pixel smoothing keeps the corner anti-aliased.
                    float alpha01 = Mathf.Clamp01(distance - radius + HalfPixelOffset);
                    byte alpha = (byte)Mathf.RoundToInt(alpha01 * ByteChannelScale);
                    pixels[y * ThumbnailCornerSpriteSize + x] =
                        new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, alpha);
                }
            }

            thumbnailCornerCutoutTexture.SetPixels32(pixels);
            thumbnailCornerCutoutTexture.Apply(false, false);

            thumbnailCornerCutoutSprite = Sprite.Create(
                thumbnailCornerCutoutTexture,
                new Rect(
                    NoFloatOffset,
                    NoFloatOffset,
                    ThumbnailCornerSpriteSize,
                    ThumbnailCornerSpriteSize),
                CenterAnchor,
                SpritePixelsPerUnit,
                SpriteExtrudePixels,
                SpriteMeshType.FullRect);
            thumbnailCornerCutoutSprite.name = ThumbnailCornerSpriteName;
            return thumbnailCornerCutoutSprite;
        }

        /// <summary>
        /// Returns the card root’s opaque background color, falling back to white when no Image is available.
        /// </summary>
        private static Color GetCardBackgroundColor(GameObject cardRoot)
        {
            if (cardRoot != null)
            {
                Image rootImage = cardRoot.GetComponent<Image>();
                if (rootImage != null)
                {
                    Color color = rootImage.color;
                    color.a = FullOpacity;
                    return color;
                }
            }

            return Color.white;
        }

        /// <summary>
        /// Adds four non-interactive corner cutouts over a managed thumbnail exactly once.
        /// </summary>
        private void AddRoundedThumbnailCornerOverlays(Image thumbnail, GameObject cardRoot)
        {
            if (thumbnail == null || thumbnail.rectTransform == null)
            {
                return;
            }

            if (thumbnail.transform.Find(ThumbnailCornerTopLeftName) != null)
            {
                return;
            }

            Sprite cornerSprite = GetThumbnailCornerCutoutSprite();
            if (cornerSprite == null)
            {
                return;
            }

            Color backgroundColor = GetCardBackgroundColor(cardRoot);

            CreateThumbnailCornerOverlay(
                thumbnail.transform,
                ThumbnailCornerTopLeftName,
                cornerSprite,
                backgroundColor,
                TopLeftAnchor,
                TopLeftAnchor,
                NoFloatOffset);
            CreateThumbnailCornerOverlay(
                thumbnail.transform,
                ThumbnailCornerTopRightName,
                cornerSprite,
                backgroundColor,
                TopRightAnchor,
                TopRightAnchor,
                -QuarterTurnDegrees);
            CreateThumbnailCornerOverlay(
                thumbnail.transform,
                ThumbnailCornerBottomRightName,
                cornerSprite,
                backgroundColor,
                BottomRightAnchor,
                BottomRightAnchor,
                HalfTurnDegrees);
            CreateThumbnailCornerOverlay(
                thumbnail.transform,
                ThumbnailCornerBottomLeftName,
                cornerSprite,
                backgroundColor,
                BottomLeftAnchor,
                BottomLeftAnchor,
                QuarterTurnDegrees);
        }

        /// <summary>
        /// Creates and positions one rotated thumbnail-corner overlay with the requested anchor and pivot.
        /// </summary>
        private static void CreateThumbnailCornerOverlay(
            Transform parent,
            string name,
            Sprite sprite,
            Color color,
            Vector2 anchor,
            Vector2 pivot,
            float rotation)
        {
            GameObject cornerObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image));
            cornerObject.transform.SetParent(parent, false);

            RectTransform rect = cornerObject.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta =
                new Vector2(ThumbnailCornerOverlaySize, ThumbnailCornerOverlaySize);
            rect.localRotation = Quaternion.Euler(NoFloatOffset, NoFloatOffset, rotation);
            rect.localScale = Vector3.one;

            Image image = cornerObject.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = color;
            image.raycastTarget = false;

            cornerObject.transform.SetAsLastSibling();
        }

        /// <summary>
        /// Disables redundant root and title hover tooltips on paged cards while leaving functional child buttons intact.
        /// </summary>
        private void DisableManagedCardTooltips(Mod_Button button)
        {
            if (button == null)
            {
                return;
            }

            // The root ButtonDefault exists only to provide the full-description hover tooltip.
            // Stop its per-frame hover polling entirely for paged cards. The actual Enable/Upload
            // child buttons have their own ButtonDefault components and remain fully interactive.
            ButtonDefault rootDefault = button.GetComponent<ButtonDefault>();
            if (rootDefault != null)
            {
                rootDefault.SetTooltip(null);
                rootDefault.DefaultTooltip = string.Empty;
                rootDefault.forceTooltip = false;
                rootDefault.active = false;
            }

            // Vanilla also adds a title tooltip to the title background. The title is already
            // visible on the card, so suppress that hover target as well.
            if (button.Title_BG != null)
            {
                ButtonDefault titleDefault = button.Title_BG.GetComponent<ButtonDefault>();
                if (titleDefault != null)
                {
                    titleDefault.SetTooltip(null);
                    titleDefault.DefaultTooltip = string.Empty;
                    titleDefault.forceTooltip = false;
                    titleDefault.active = false;
                }
            }
        }

        /// <summary>
        /// Adds a localized version/source metadata row above a card description and shrinks the description area to make room.
        /// </summary>
        private void CreateCardMetadata(PageCard card)
        {
            if (card == null || card.Button == null || card.Mod == null ||
                card.Button.Description == null)
            {
                return;
            }

            RectTransform descriptionRect =
                card.Button.Description.transform as RectTransform;
            if (descriptionRect == null || descriptionRect.parent == null)
            {
                return;
            }

            // Reserve a thin metadata line directly above the vanilla description. This uses the
            // description's own horizontal anchors/offsets, so it follows the prefab at different
            // resolutions instead of assuming card pixel coordinates.
            Transform parent = descriptionRect.parent;
            GameObject rowObject = new GameObject(
                MetadataRowObjectName,
                typeof(RectTransform));
            rowObject.transform.SetParent(parent, false);

            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(
                descriptionRect.anchorMin.x,
                descriptionRect.anchorMax.y);
            rowRect.anchorMax = new Vector2(
                descriptionRect.anchorMax.x,
                descriptionRect.anchorMax.y);
            rowRect.pivot = new Vector2(descriptionRect.pivot.x, TopLeftAnchor.y);
            rowRect.offsetMin = new Vector2(
                descriptionRect.offsetMin.x,
                descriptionRect.offsetMax.y - MetadataHeight);
            rowRect.offsetMax = new Vector2(
                descriptionRect.offsetMax.x,
                descriptionRect.offsetMax.y);
            rowRect.SetSiblingIndex(
                Mathf.Min(descriptionRect.GetSiblingIndex() + NextSiblingOffset, parent.childCount - LastIndexOffset));

            Vector2 descriptionTop = descriptionRect.offsetMax;
            descriptionTop.y -= MetadataHeight + MetadataGap;
            descriptionRect.offsetMax = descriptionTop;

            float cursorX = NoFloatOffset;

            if (!string.IsNullOrWhiteSpace(card.Mod.Version))
            {
                string versionText =
                    UiLocalization.Format(UiLocalization.VersionFormat, card.Mod.Version);

                TextMeshProUGUI versionLabel = CreateTmpText(
                    VersionLabelObjectName,
                    rowObject.transform,
                    versionText,
                    MetadataVersionFontSize,
                    TextAlignmentOptions.MidlineLeft);
                versionLabel.color = MetadataVersionColor;
                versionLabel.overflowMode = TextOverflowModes.Ellipsis;

                float versionWidth = Mathf.Clamp(
                    Mathf.Ceil(versionLabel.preferredWidth) + MetadataVersionWidthPadding,
                    MetadataVersionMinimumWidth,
                    MetadataVersionMaximumWidth);
                RectTransform versionRect = versionLabel.rectTransform;
                versionRect.anchorMin = BottomLeftAnchor;
                versionRect.anchorMax = TopLeftAnchor;
                versionRect.pivot = new Vector2(BottomLeftAnchor.x, CenterAnchor.y);
                versionRect.anchoredPosition = new Vector2(cursorX, NoFloatOffset);
                versionRect.sizeDelta = new Vector2(versionWidth, NoFloatOffset);

                cursorX += versionWidth + MetadataItemHorizontalGap;
            }

            string sourceText = card.Mod.IsWorkshop()
                ? UiLocalization.Get(UiLocalization.Workshop)
                : UiLocalization.Get(UiLocalization.Local);

            GameObject badgeObject = new GameObject(
                SourceBadgeObjectName,
                typeof(RectTransform),
                typeof(Image));
            badgeObject.transform.SetParent(rowObject.transform, false);

            Image badgeImage = badgeObject.GetComponent<Image>();
            Color32 badgeColor = card.Mod.IsWorkshop()
                ? WorkshopBadgeColor
                : LocalBadgeColor;
            ApplyRoundedImage(badgeImage, badgeColor, false);

            TextMeshProUGUI badgeLabel = CreateTmpText(
                TextObjectName,
                badgeObject.transform,
                sourceText,
                MetadataBadgeFontSize,
                TextAlignmentOptions.Midline);
            badgeLabel.color = MetadataBadgeTextColor;
            badgeLabel.fontStyle |= FontStyles.Bold;
            badgeLabel.overflowMode = TextOverflowModes.Ellipsis;

            float badgeWidth = Mathf.Clamp(
                Mathf.Ceil(badgeLabel.preferredWidth) + MetadataBadgeWidthPadding,
                MetadataBadgeMinimumWidth,
                MetadataBadgeMaximumWidth);

            RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
            badgeRect.anchorMin = MetadataBadgeAnchorMin;
            badgeRect.anchorMax = MetadataBadgeAnchorMax;
            badgeRect.pivot = new Vector2(BottomLeftAnchor.x, CenterAnchor.y);
            badgeRect.anchoredPosition = new Vector2(cursorX, NoFloatOffset);
            badgeRect.sizeDelta = new Vector2(badgeWidth, NoFloatOffset);

            RectTransform badgeTextRect = badgeLabel.rectTransform;
            badgeTextRect.anchorMin = Vector2.zero;
            badgeTextRect.anchorMax = Vector2.one;
            badgeTextRect.offsetMin = new Vector2(MetadataItemHorizontalGap, NoFloatOffset);
            badgeTextRect.offsetMax = new Vector2(-MetadataItemHorizontalGap, NoFloatOffset);

        }


        /// <summary>
        /// Clones or creates the source-filter action button, wires its click handler, and aligns it opposite Back.
        /// </summary>
        private void CreateSourceFilterButton()
        {
            GameObject template = FindNativeButtonTemplate();
            Transform parent = template != null && template.transform.parent != null
                ? template.transform.parent
                : popup.transform;

            sourceFilterButton = CreateNativeGameButton(
                SourceFilterButtonObjectName,
                parent,
                UiLocalization.Get(UiLocalization.AllMods));

            if (sourceFilterButton == null)
            {
                return;
            }

            sourceFilterButton.onClick.AddListener(CycleSourceFilter);
            AlignSourceFilterButtonToBack();
        }

        /// <summary>
        /// Mirrors the native Back button’s size and vertical position across the popup’s horizontal center.
        /// </summary>
        private void AlignSourceFilterButtonToBack()
        {
            if (sourceFilterButton == null)
            {
                return;
            }

            GameObject template = FindNativeButtonTemplate();
            RectTransform backRect = template != null
                ? template.GetComponent<RectTransform>()
                : null;
            RectTransform filterRect =
                sourceFilterButton.GetComponent<RectTransform>();

            if (backRect == null || filterRect == null || backRect.parent == null ||
                filterRect.parent != backRect.parent)
            {
                return;
            }

            RectTransform parentRect = backRect.parent as RectTransform;
            if (parentRect == null)
            {
                return;
            }

            // Mirror the live vanilla Back button across the horizontal center of its parent.
            // This keeps the same size and vertical position while placing the source selector
            // at the bottom-right with the same outer margin as Back has on the left.
            Vector3[] backCorners = new Vector3[RectCornerCount];
            backRect.GetWorldCorners(backCorners);

            Vector3 bottomLeft = parentRect.InverseTransformPoint(backCorners[BottomLeftCornerIndex]);
            Vector3 topRight = parentRect.InverseTransformPoint(backCorners[TopRightCornerIndex]);
            float width = Mathf.Abs(topRight.x - bottomLeft.x);
            float height = Mathf.Abs(topRight.y - bottomLeft.y);
            float leftMargin = Mathf.Max(NoFloatOffset, bottomLeft.x - parentRect.rect.xMin);
            float desiredCenterX = parentRect.rect.xMax - leftMargin - width * HalfPixelOffset;
            float desiredCenterY = (bottomLeft.y + topRight.y) * HalfPixelOffset;
            Vector2 parentCenter = new Vector2(
                (parentRect.rect.xMin + parentRect.rect.xMax) * HalfPixelOffset,
                (parentRect.rect.yMin + parentRect.rect.yMax) * HalfPixelOffset);

            filterRect.anchorMin = CenterAnchor;
            filterRect.anchorMax = CenterAnchor;
            filterRect.pivot = CenterAnchor;
            filterRect.sizeDelta = new Vector2(width, height);
            filterRect.anchoredPosition =
                new Vector2(desiredCenterX, desiredCenterY) - parentCenter;
            filterRect.localScale = Vector3.one;
        }

        /// <summary>
        /// Cycles All, Local, and Workshop filtering, then reapplies the filter and rerenders from the first page.
        /// </summary>
        private void CycleSourceFilter()
        {
            switch (sourceFilterMode)
            {
                case SourceFilterMode.All:
                    sourceFilterMode = SourceFilterMode.Local;
                    break;
                case SourceFilterMode.Local:
                    sourceFilterMode = SourceFilterMode.Workshop;
                    break;
                default:
                    sourceFilterMode = SourceFilterMode.All;
                    break;
            }

            currentPage = FirstPageIndex;
            ApplySearchFilter();
            UpdateSourceFilterButtonLabel();
            RenderCurrentPage();
        }

        /// <summary>
        /// Updates the source-filter button text to match the currently selected filter mode.
        /// </summary>
        private void UpdateSourceFilterButtonLabel()
        {
            if (sourceFilterButton == null)
            {
                return;
            }

            string label;
            switch (sourceFilterMode)
            {
                case SourceFilterMode.Local:
                    label = UiLocalization.Get(UiLocalization.Local);
                    break;
                case SourceFilterMode.Workshop:
                    label = UiLocalization.Get(UiLocalization.Workshop);
                    break;
                default:
                    label = UiLocalization.Get(UiLocalization.AllMods);
                    break;
            }

            SetNativeButtonLabel(sourceFilterButton, label);
        }


        /// <summary>
        /// Clones the native Back button to preserve game styling and behavior, falling back to a basic button if no template exists.
        /// </summary>
        private Button CreateNativeGameButton(string name, Transform parent, string label)
        {
            GameObject template = FindNativeButtonTemplate();
            if (template == null)
            {
                Plugin.Log.LogWarning(NativeButtonFallbackLogMessage);
                return CreateFallbackButton(name, parent, label);
            }

            // Clone the live vanilla Back button. Unity clones its children/components and their
            // serialized properties, so this preserves Idol Manager's normal sprite, font,
            // ButtonDefault hover/click behavior, shadowing and transition setup.
            GameObject buttonObject = UnityEngine.Object.Instantiate<GameObject>(template);
            buttonObject.name = name;
            buttonObject.transform.SetParent(parent, false);
            buttonObject.SetActive(true);

            Button button = buttonObject.GetComponent<Button>();
            if (button == null)
            {
                UnityEngine.Object.Destroy(buttonObject);
                return CreateFallbackButton(name, parent, label);
            }

            // The cloned Back button also clones its serialized OnCancel listener. Replacing the
            // ButtonClickedEvent removes that inherited callback before PreviousPage/NextPage is
            // attached by CreateControls().
            button.onClick = new Button.ButtonClickedEvent();
            button.interactable = true;
            EnsureGameFontBindings(buttonObject);

            ButtonDefault buttonDefault = buttonObject.GetComponent<ButtonDefault>();
            if (buttonDefault != null)
            {
                buttonDefault.active = true;
                buttonDefault.forceTooltip = false;
                buttonDefault.DefaultTooltip = string.Empty;
                buttonDefault.SetTooltip(null);
            }

            SetNativeButtonLabel(button, label);
            return button;
        }

        /// <summary>
        /// Replaces inherited localization bindings and writes the requested text to whichever label component the cloned button uses.
        /// </summary>
        private void SetNativeButtonLabel(Button button, string label)
        {
            if (button == null)
            {
                return;
            }

            GameObject buttonObject = button.gameObject;
            bool labelWritten = false;

            // The live Back button has already localized its label before being cloned. Clearing
            // Constant prevents that old localization key from restoring "Back" after a language
            // reset; the controller then writes its own localized Previous/Next string.
            foreach (Lang_Button lang in buttonObject.GetComponentsInChildren<Lang_Button>(true))
            {
                lang.Constant = string.Empty;
                lang.Tooltip = string.Empty;

                ExtensionMethods.SetText(lang.gameObject, label);
                if (ExtensionMethods.GetText(lang.gameObject) == label)
                {
                    labelWritten = true;
                }
            }

            ButtonDefault buttonDefault = buttonObject.GetComponent<ButtonDefault>();
            if (buttonDefault != null && buttonDefault.Text != null)
            {
                ExtensionMethods.SetText(buttonDefault.Text, label);
                if (ExtensionMethods.GetText(buttonDefault.Text) == label)
                {
                    labelWritten = true;
                }
            }

            if (!labelWritten)
            {
                Text labelText = buttonObject.GetComponentInChildren<Text>(true);
                if (labelText != null)
                {
                    labelText.text = label;
                }
            }
        }

        /// <summary>
        /// Locates and caches the popup’s native Back button by its persistent OnCancel listener rather than fragile hierarchy names.
        /// </summary>
        private GameObject FindNativeButtonTemplate()
        {
            if (nativeButtonTemplate != null)
            {
                return nativeButtonTemplate;
            }

            // The Mods popup already contains its own vanilla Back button. Rather than guessing by
            // hierarchy/name/localized text, identify it by the serialized Button.onClick target:
            // Mods_Popup.OnCancel().
            Button[] buttons = popup.GetComponentsInChildren<Button>(true);
            nativeButtonTemplate = FindCancelButton(buttons);
            if (nativeButtonTemplate != null)
            {
                return nativeButtonTemplate;
            }

            // Some prefab hierarchies keep footer controls just outside the component's immediate
            // subtree. Fall back to the popup's root, still requiring the listener target to be this
            // exact Mods_Popup instance.
            Transform root = popup.transform.root;
            if (root != null)
            {
                nativeButtonTemplate = FindCancelButton(root.GetComponentsInChildren<Button>(true));
            }

            return nativeButtonTemplate;
        }

        /// <summary>
        /// Searches a button collection for the button whose persistent click target calls this popup’s OnCancel method.
        /// </summary>
        private GameObject FindCancelButton(Button[] buttons)
        {
            if (buttons == null)
            {
                return null;
            }

            foreach (Button candidate in buttons)
            {
                if (candidate == null || candidate.onClick == null)
                {
                    continue;
                }

                int persistentCount = candidate.onClick.GetPersistentEventCount();
                for (int i = FirstCollectionIndex; i < persistentCount; i++)
                {
                    if (candidate.onClick.GetPersistentTarget(i) == popup &&
                        candidate.onClick.GetPersistentMethodName(i) == IntegrationConstants.CancelMethodName)
                    {
                        return candidate.gameObject;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a rounded Unity UI button with a TMP label when the native button template cannot be found.
        /// </summary>
        private Button CreateFallbackButton(string name, Transform parent, string label)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            Image image = buttonObject.GetComponent<Image>();
            ApplyRoundedImage(image, mainScript.blue32, true);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            TextMeshProUGUI labelText = CreateTmpText(
                TextObjectName,
                buttonObject.transform,
                label,
                PageLabelFontSize,
                TextAlignmentOptions.Midline);
            RectTransform labelRect = labelText.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = FallbackButtonTextOffsetMin;
            labelRect.offsetMax = FallbackButtonTextOffsetMax;
            labelText.color = Color.white;

            return button;
        }

        /// <summary>
        /// Reloads localization and fonts after the game resets language data, then rebuilds the visible page metadata if needed.
        /// </summary>
        private void OnLanguageReset()
        {
            UiLocalization.Reload();
            nativeTmpTextTemplate = null;
            RefreshCustomTmpFonts();
            ApplyLocalizedUi();

            // Card metadata (Version / Local / Workshop) is also localized. Language changes are
            // rare, so rebuild only the current 24-card page instead of keeping extra label state.
            if (initialized && rendering)
            {
                RenderCurrentPage();
            }
        }

        /// <summary>
        /// Writes localized search, navigation, and source-filter labels and refreshes navigation state.
        /// </summary>
        private void ApplyLocalizedUi()
        {
            if (searchPlaceholder != null)
            {
                searchPlaceholder.text = UiLocalization.Get(UiLocalization.SearchPlaceholder);
            }

            SetNativeButtonLabel(
                previousButton,
                UiLocalization.Get(UiLocalization.Previous));
            SetNativeButtonLabel(
                nextButton,
                UiLocalization.Get(UiLocalization.Next));
            UpdateSourceFilterButtonLabel();

            UpdateNavigationState();
        }

        /// <summary>
        /// Copies the game’s current non-null mod entries into the controller’s source list.
        /// </summary>
        private void ReloadAllMods()
        {
            allMods.Clear();
            foreach (Mods._mod mod in Mods._Mods)
            {
                if (mod != null)
                {
                    allMods.Add(mod);
                }
            }
        }

        /// <summary>
        /// Filters all mods by source mode and every non-empty search term, then refreshes navigation state.
        /// </summary>
        private void ApplySearchFilter()
        {
            filteredMods.Clear();
            string query = activeQuery == null ? string.Empty : activeQuery.Trim();
            string[] terms = query.Length == EmptyItemCount
                ? null
                : query.Split(
                    SearchTermSeparators,
                    StringSplitOptions.RemoveEmptyEntries);

            foreach (Mods._mod mod in allMods)
            {
                if (!MatchesSourceFilter(mod))
                {
                    continue;
                }

                if (terms == null || terms.Length == EmptyItemCount || MatchesAllTerms(mod, terms))
                {
                    filteredMods.Add(mod);
                }
            }

            UpdateNavigationState();
        }

        /// <summary>
        /// Returns whether a mod belongs to the currently selected All, Local, or Workshop source category.
        /// </summary>
        private bool MatchesSourceFilter(Mods._mod mod)
        {
            if (mod == null)
            {
                return false;
            }

            switch (sourceFilterMode)
            {
                case SourceFilterMode.Local:
                    return !mod.IsWorkshop();
                case SourceFilterMode.Workshop:
                    return mod.IsWorkshop();
                default:
                    return true;
            }
        }

        /// <summary>
        /// Returns whether every search term appears in at least one searchable mod field or tag.
        /// </summary>
        private static bool MatchesAllTerms(Mods._mod mod, string[] terms)
        {
            if (mod == null)
            {
                return false;
            }

            foreach (string term in terms)
            {
                if (ContainsIgnoreCase(mod.Title, term) ||
                    ContainsIgnoreCase(mod.ModName, term) ||
                    ContainsIgnoreCase(mod.Author, term) ||
                    ContainsIgnoreCase(mod.Description, term) ||
                    ContainsIgnoreCase(mod.Version, term) ||
                    TagsContain(mod.Tags, term))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Performs a null-safe ordinal case-insensitive substring test.
        /// </summary>
        private static bool ContainsIgnoreCase(string value, string term)
        {
            return !string.IsNullOrEmpty(value) &&
                !string.IsNullOrEmpty(term) &&
                value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= FirstCollectionIndex;
        }

        /// <summary>
        /// Returns whether any tag contains the requested term using the same case-insensitive matching rule.
        /// </summary>
        private static bool TagsContain(List<string> tags, string term)
        {
            if (tags == null)
            {
                return false;
            }

            foreach (string tag in tags)
            {
                if (ContainsIgnoreCase(tag, term))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the number of pages required for the current filtered result set.
        /// </summary>
        private int TotalPages
        {
            get
            {
                if (filteredMods.Count == EmptyItemCount)
                {
                    return EmptyItemCount;
                }
                return (filteredMods.Count + PageSize - LastIndexOffset) / PageSize;
            }
        }

        /// <summary>
        /// Stores a pending query and restarts the debounce coroutine so filtering does not rerender on every keystroke immediately.
        /// </summary>
        private void OnSearchValueChanged(string value)
        {
            pendingQuery = value ?? string.Empty;

            if (searchDebounceCoroutine != null)
            {
                StopCoroutine(searchDebounceCoroutine);
            }
            searchDebounceCoroutine = StartCoroutine(SearchDebounceCoroutine());
        }

        /// <summary>
        /// Waits for the debounce interval, applies a genuinely changed query, resets to the first page, and rerenders.
        /// </summary>
        private IEnumerator SearchDebounceCoroutine()
        {
            yield return new WaitForSecondsRealtime(SearchDebounceSeconds);
            searchDebounceCoroutine = null;

            string nextQuery = pendingQuery == null ? string.Empty : pendingQuery.Trim();
            if (string.Equals(activeQuery, nextQuery, StringComparison.Ordinal))
            {
                yield break;
            }

            activeQuery = nextQuery;
            currentPage = FirstPageIndex;
            ApplySearchFilter();
            RenderCurrentPage();
            yield break;
        }

        /// <summary>
        /// Moves to the previous page when one exists and rerenders it.
        /// </summary>
        private void PreviousPage()
        {
            if (currentPage <= FirstPageIndex)
            {
                return;
            }

            currentPage--;
            RenderCurrentPage();
        }

        /// <summary>
        /// Moves to the next page when one exists and rerenders it.
        /// </summary>
        private void NextPage()
        {
            int pages = TotalPages;
            if (pages == EmptyItemCount || currentPage >= pages - LastIndexOffset)
            {
                return;
            }

            currentPage++;
            RenderCurrentPage();
        }

        /// <summary>
        /// Clamps the current page, updates button interactability, and refreshes page/result-count labels.
        /// </summary>
        private void UpdateNavigationState()
        {
            int pages = TotalPages;
            if (pages == EmptyItemCount)
            {
                currentPage = FirstPageIndex;
            }
            else
            {
                currentPage = Mathf.Clamp(currentPage, FirstPageIndex, pages - LastIndexOffset);
            }

            if (previousButton != null)
            {
                previousButton.interactable = pages > EmptyItemCount && currentPage > FirstPageIndex;
            }
            if (nextButton != null)
            {
                nextButton.interactable = pages > EmptyItemCount && currentPage < pages - LastIndexOffset;
            }
            if (pageLabel != null)
            {
                pageLabel.text = pages == EmptyItemCount
                    ? UiLocalization.Get(UiLocalization.NoResults)
                    : UiLocalization.Format(
                        UiLocalization.PageFormat,
                        currentPage + HumanPageNumberOffset,
                        pages);
            }
            if (resultLabel != null)
            {
                resultLabel.text = string.IsNullOrEmpty(activeQuery)
                    ? UiLocalization.Format(
                        UiLocalization.ModCountFormat,
                        filteredMods.Count,
                        PageSize)
                    : UiLocalization.Format(
                        UiLocalization.MatchCountFormat,
                        filteredMods.Count,
                        PageSize);
            }
        }

        /// <summary>
        /// Measures rendered card bounds and sizes the fixed header/footer ribbons to the actual two-column card span.
        /// </summary>
        private void AlignControlRibbonsToCardColumns()
        {
            RectTransform ribbonSpace = controlsOverlayRect != null ? controlsOverlayRect : viewport;
            if (ribbonsAligned || ribbonSpace == null || headerRect == null || footerRect == null)
            {
                return;
            }

            // Measure the real card RectTransforms after uGUI lays them out. This avoids guessing
            // at prefab margins and makes the top/bottom ribbons exactly span column 1's left edge
            // through column 2's right edge at the active resolution and UI scale.
            if (pageCards.Count < MinimumCardsForRibbonAlignment)
            {
                return;
            }

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            Vector3[] corners = new Vector3[RectCornerCount];
            int measured = EmptyItemCount;

            foreach (PageCard card in pageCards)
            {
                if (card == null || card.Root == null)
                {
                    continue;
                }

                RectTransform cardRect = card.Root.transform as RectTransform;
                if (cardRect == null)
                {
                    continue;
                }

                cardRect.GetWorldCorners(corners);
                for (int i = FirstCollectionIndex; i < corners.Length; i++)
                {
                    Vector3 local = ribbonSpace.InverseTransformPoint(corners[i]);
                    minX = Mathf.Min(minX, local.x);
                    maxX = Mathf.Max(maxX, local.x);
                }

                measured++;
            }

            if (measured < MinimumCardsForRibbonAlignment || float.IsInfinity(minX) || float.IsInfinity(maxX) || maxX <= minX)
            {
                return;
            }

            float leftInset = Mathf.Max(NoFloatOffset, minX - ribbonSpace.rect.xMin);
            float rightInset = Mathf.Max(NoFloatOffset, ribbonSpace.rect.xMax - maxX);

            ApplyHorizontalInsets(headerRect, leftInset, rightInset);
            ApplyHorizontalInsets(footerRect, leftInset, rightInset);
            ribbonsAligned = true;

            if (Plugin.Log != null)
            {
                Plugin.Log.LogDebug(string.Format(
                    RibbonAlignmentLogFormat,
                    leftInset.ToString(OneDecimalFormat),
                    rightInset.ToString(OneDecimalFormat)));
            }
        }

        /// <summary>
        /// Forces a ribbon to horizontal stretch anchors and applies measured left/right insets.
        /// </summary>
        private static void ApplyHorizontalInsets(
            RectTransform rect,
            float leftInset,
            float rightInset)
        {
            if (rect == null)
            {
                return;
            }

            Vector2 anchorMin = rect.anchorMin;
            Vector2 anchorMax = rect.anchorMax;
            anchorMin.x = BottomLeftAnchor.x;
            anchorMax.x = BottomRightAnchor.x;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;

            Vector2 offsetMin = rect.offsetMin;
            Vector2 offsetMax = rect.offsetMax;
            offsetMin.x = leftInset;
            offsetMax.x = -rightInset;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        /// <summary>
        /// Cancels previous page work, clears old cards, resets scroll position, and starts an incremental render for the current page.
        /// </summary>
        private void RenderCurrentPage()
        {
            if (!initialized || !rendering)
            {
                return;
            }

            renderGeneration++;
            int generation = renderGeneration;

            StopPageWork();
            DestroyCurrentPageCards();
            UpdateNavigationState();

            if (scrollRect != null)
            {
                scrollRect.StopMovement();
                scrollRect.verticalNormalizedPosition = ScrollTopNormalizedPosition;
            }

            pageRenderCoroutine = StartCoroutine(RenderPageCoroutine(generation));
        }

        /// <summary>
        /// Builds the current page in frame-sized batches, aligns controls after layout, then starts sequential thumbnail loading.
        /// </summary>
        private IEnumerator RenderPageCoroutine(int generation)
        {
            // Let Unity actually remove the previous page before adding the next one. This prevents
            // old and new page hierarchies from coexisting for an entire layout rebuild.
            yield return null;

            if (generation != renderGeneration || !rendering)
            {
                pageRenderCoroutine = null;
                yield break;
            }

            int start = currentPage * PageSize;
            int end = Mathf.Min(filteredMods.Count, start + PageSize);
            int createdThisFrame = EmptyItemCount;

            for (int filteredIndex = start; filteredIndex < end; filteredIndex++)
            {
                if (generation != renderGeneration || !rendering)
                {
                    pageRenderCoroutine = null;
                    yield break;
                }

                CreatePageCard(filteredIndex);
                createdThisFrame++;

                if (createdThisFrame >= CardsPerFrame)
                {
                    createdThisFrame = EmptyItemCount;
                    yield return null;
                }
            }

            pageRenderCoroutine = null;

            // Let the normal uGUI layout pass finish, then derive the control-ribbon width from
            // the actual first-row card RectTransforms. No forced Canvas rebuild is required.
            yield return new WaitForEndOfFrame();
            if (generation != renderGeneration || !rendering)
            {
                yield break;
            }

            SyncControlsOverlayToViewport();
            AlignControlRibbonsToCardColumns();
            AlignSourceFilterButtonToBack();

            if (scrollRect != null)
            {
                scrollRect.StopMovement();
                scrollRect.verticalNormalizedPosition = ScrollTopNormalizedPosition;
            }

            thumbnailCoroutine = StartCoroutine(LoadPageThumbnailsCoroutine(generation));
        }

        /// <summary>
        /// Instantiates one vanilla Mod_Button, keeps its native behavior, injects metadata/rounded thumbnail corners, and registers it as managed.
        /// </summary>
        private void CreatePageCard(int filteredIndex)
        {
            if (filteredIndex < FirstCollectionIndex || filteredIndex >= filteredMods.Count)
            {
                return;
            }

            Mods._mod mod = filteredMods[filteredIndex];
            GameObject root = UnityEngine.Object.Instantiate<GameObject>(popup.prefab_mod_button);
            Mod_Button button = root != null ? root.GetComponent<Mod_Button>() : null;
            if (root == null || button == null)
            {
                if (root != null)
                {
                    UnityEngine.Object.Destroy(root);
                }
                return;
            }

            Image image = null;
            if (button.Screenshot != null)
            {
                image = button.Screenshot.GetComponent<Image>();
            }

            PageCard card = new PageCard
            {
                FilteredIndex = filteredIndex,
                Mod = mod,
                Root = root,
                Button = button,
                ThumbnailImage = image
            };

            // Register before Set() so the RenderScreenshot Harmony prefix suppresses only the
            // synchronous screenshot decode. Every other vanilla Mod_Button behavior still runs.
            ManagedButtons[button.GetInstanceID()] = this;

            try
            {
                root.SetActive(false);
                button.Set(mod);
                DisableManagedCardTooltips(button);
                CreateCardMetadata(card);
                root.transform.SetParent(content, false);

                if (image != null)
                {
                    // Keep the vanilla screenshot Image in place. Only defer its texture load;
                    // rounded corners are visual overlays and do not participate in masking.
                    AddRoundedThumbnailCornerOverlays(image, root);
                    image.sprite = null;
                    image.enabled = false;
                }

                pageCards.Add(card);
                root.SetActive(true);
            }
            catch (Exception ex)
            {
                ManagedButtons.Remove(button.GetInstanceID());
                UnityEngine.Object.Destroy(root);
                Plugin.Log.LogWarning(string.Format(CardCreationFailureLogFormat, mod.Title, ex.Message));
            }
        }

        /// <summary>
        /// Loads visible-page thumbnails sequentially with UnityWebRequest, falls back to the vanilla loader on failure, and releases stale textures.
        /// </summary>
        private IEnumerator LoadPageThumbnailsCoroutine(int generation)
        {
            // Page order already matches visual order in the vanilla horizontal GridLayoutGroup,
            // so loading from index 0 naturally prioritizes the thumbnails visible at the top.
            for (int i = FirstCollectionIndex; i < pageCards.Count; i++)
            {
                if (generation != renderGeneration || !rendering)
                {
                    thumbnailCoroutine = null;
                    yield break;
                }

                PageCard card = pageCards[i];
                if (card == null || card.Root == null || card.Mod == null || card.ThumbnailImage == null)
                {
                    continue;
                }

                string path = card.Mod.GetThumbPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    continue;
                }

                Texture2D texture = null;
                Sprite sprite = null;
                bool loaded = false;

                UnityWebRequest request = null;
                try
                {
                    request = UnityWebRequestTexture.GetTexture(ToFileUri(path), true);
                    activeThumbnailRequest = request;
                    yield return request.SendWebRequest();

                    if (generation != renderGeneration || !rendering)
                    {
                        thumbnailCoroutine = null;
                        yield break;
                    }

                    if (!request.isNetworkError && !request.isHttpError)
                    {
                        texture = DownloadHandlerTexture.GetContent(request);
                        if (texture != null)
                        {
                            texture.name = ThumbnailTextureNamePrefix + card.Mod.ModName;
                            sprite = Sprite.Create(
                                texture,
                                new Rect(NoFloatOffset, NoFloatOffset, texture.width, texture.height),
                                Vector2.zero,
                                SpritePixelsPerUnit);
                            loaded = sprite != null;
                        }
                    }
                }
                finally
                {
                    if (request != null)
                    {
                        request.Dispose();
                    }
                    if (activeThumbnailRequest == request)
                    {
                        activeThumbnailRequest = null;
                    }
                }

                if (!loaded && generation == renderGeneration && rendering)
                {
                    // Correctness fallback: use the exact vanilla loader for this one thumbnail.
                    // A failed asynchronous file request must never leave the whole page stuck blank.
                    sprite = IMG2Sprite.instance.LoadNewSprite(path, SpritePixelsPerUnit);
                    texture = sprite != null ? sprite.texture : null;
                    loaded = sprite != null;
                }

                if (loaded && generation == renderGeneration && rendering &&
                    card.Root != null && card.ThumbnailImage != null)
                {
                    card.RuntimeSprite = sprite;
                    card.RuntimeTexture = texture;
                    card.ThumbnailImage.sprite = sprite;
                    card.ThumbnailImage.enabled = true;
                }
                else
                {
                    ReleaseLooseThumbnail(sprite, texture);
                }

                // Never attach two new thumbnail textures in the same frame.
                yield return null;
            }

            thumbnailCoroutine = null;
        }

        /// <summary>
        /// Converts a local filesystem path to a file URI, with a separator-normalizing fallback when Uri construction fails.
        /// </summary>
        private static string ToFileUri(string path)
        {
            try
            {
                return new Uri(Path.GetFullPath(path)).AbsoluteUri;
            }
            catch
            {
                return FileUriPrefix + path.Replace(WindowsPathSeparator, UriPathSeparator);
            }
        }

        /// <summary>
        /// Destroys runtime thumbnail sprite and texture objects when they are no longer attached to a card.
        /// </summary>
        private static void ReleaseLooseThumbnail(Sprite sprite, Texture2D texture)
        {
            if (sprite != null)
            {
                UnityEngine.Object.Destroy(sprite);
            }
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        /// <summary>
        /// Unregisters and destroys managed page cards, releases their thumbnails, and clears any remaining vanilla content children.
        /// </summary>
        private void DestroyCurrentPageCards()
        {
            foreach (PageCard card in pageCards)
            {
                if (card == null)
                {
                    continue;
                }

                if (card.Button != null)
                {
                    ManagedButtons.Remove(card.Button.GetInstanceID());
                }

                if (card.ThumbnailImage != null && card.ThumbnailImage.sprite == card.RuntimeSprite)
                {
                    card.ThumbnailImage.sprite = null;
                    card.ThumbnailImage.enabled = false;
                }

                ReleaseLooseThumbnail(card.RuntimeSprite, card.RuntimeTexture);
                card.RuntimeSprite = null;
                card.RuntimeTexture = null;

                if (card.Root != null)
                {
                    UnityEngine.Object.Destroy(card.Root);
                }
            }
            pageCards.Clear();

            // Match vanilla Render's clear-first behavior for any pre-existing children that were
            // not created by this controller (for example, after switching from an older build).
            if (content != null)
            {
                for (int i = content.childCount - LastIndexOffset; i >= FirstCollectionIndex; i--)
                {
                    Transform child = content.GetChild(i);
                    if (child != null)
                    {
                        UnityEngine.Object.Destroy(child.gameObject);
                    }
                }
            }
        }

        /// <summary>
        /// Stops page-render and thumbnail coroutines and aborts any active thumbnail request.
        /// </summary>
        private void StopPageWork()
        {
            if (pageRenderCoroutine != null)
            {
                StopCoroutine(pageRenderCoroutine);
                pageRenderCoroutine = null;
            }
            if (thumbnailCoroutine != null)
            {
                StopCoroutine(thumbnailCoroutine);
                thumbnailCoroutine = null;
            }
            AbortActiveThumbnailRequest();
        }

        /// <summary>
        /// Aborts, disposes, and clears the active thumbnail web request if one exists.
        /// </summary>
        private void AbortActiveThumbnailRequest()
        {
            if (activeThumbnailRequest == null)
            {
                return;
            }

            try
            {
                activeThumbnailRequest.Abort();
            }
            catch
            {
            }

            activeThumbnailRequest.Dispose();
            activeThumbnailRequest = null;
        }

        /// <summary>
        /// Stops rendering when the popup hides and releases page thumbnail resources while keeping card structure for quick reopening.
        /// </summary>
        private void OnDisable()
        {
            rendering = false;
            renderGeneration++;
            StopPageWork();

            // The popup is hidden, so release its runtime thumbnail textures rather than retaining
            // a page's images in memory until the user opens Mods again.
            foreach (PageCard card in pageCards)
            {
                if (card == null)
                {
                    continue;
                }
                if (card.ThumbnailImage != null && card.ThumbnailImage.sprite == card.RuntimeSprite)
                {
                    card.ThumbnailImage.sprite = null;
                    card.ThumbnailImage.enabled = false;
                }
                ReleaseLooseThumbnail(card.RuntimeSprite, card.RuntimeTexture);
                card.RuntimeSprite = null;
                card.RuntimeTexture = null;
            }
        }

        /// <summary>
        /// Restarts thumbnail loading for existing cards when an initialized popup becomes visible again.
        /// </summary>
        private void OnEnable()
        {
            if (!initialized)
            {
                return;
            }

            rendering = true;
            if (pageCards.Count > EmptyItemCount)
            {
                int generation = ++renderGeneration;
                thumbnailCoroutine = StartCoroutine(LoadPageThumbnailsCoroutine(generation));
            }
        }

        /// <summary>
        /// Unsubscribes events, removes listeners and injected objects, restores captured native layout, releases resources, and resets grid settings.
        /// </summary>
        private void OnDestroy()
        {
            rendering = false;
            renderGeneration++;
            StopPageWork();

            if (searchDebounceCoroutine != null)
            {
                StopCoroutine(searchDebounceCoroutine);
                searchDebounceCoroutine = null;
            }

            if (languageSubscribed)
            {
                Language.onReset -= OnLanguageReset;
                languageSubscribed = false;
            }

            if (searchField != null)
            {
                searchField.onValueChanged.RemoveListener(OnSearchValueChanged);
            }
            if (previousButton != null)
            {
                previousButton.onClick.RemoveListener(PreviousPage);
            }
            if (nextButton != null)
            {
                nextButton.onClick.RemoveListener(NextPage);
            }
            if (sourceFilterButton != null)
            {
                sourceFilterButton.onClick.RemoveListener(CycleSourceFilter);
                UnityEngine.Object.Destroy(sourceFilterButton.gameObject);
                sourceFilterButton = null;
            }

            if (controlsOverlayRect != null)
            {
                UnityEngine.Object.Destroy(controlsOverlayRect.gameObject);
                controlsOverlayRect = null;
            }

            if (popupWindowLayoutCaptured && popupWindowRect != null)
            {
                popupWindowRect.anchorMin = originalPopupAnchorMin;
                popupWindowRect.anchorMax = originalPopupAnchorMax;
                popupWindowRect.offsetMin = originalPopupOffsetMin;
                popupWindowRect.offsetMax = originalPopupOffsetMax;
            }

            if (backButtonLayoutCaptured && nativeBackButtonRect != null)
            {
                nativeBackButtonRect.anchoredPosition = originalBackButtonAnchoredPosition;
            }

            if (scrollAreaLayoutCaptured && scrollAreaRect != null)
            {
                scrollAreaRect.anchorMin = originalScrollAreaAnchorMin;
                scrollAreaRect.anchorMax = originalScrollAreaAnchorMax;
                scrollAreaRect.offsetMin = originalScrollAreaOffsetMin;
                scrollAreaRect.offsetMax = originalScrollAreaOffsetMax;
            }

            foreach (PageCard card in pageCards)
            {
                if (card != null && card.Button != null)
                {
                    ManagedButtons.Remove(card.Button.GetInstanceID());
                }
                if (card != null)
                {
                    ReleaseLooseThumbnail(card.RuntimeSprite, card.RuntimeTexture);
                }
            }
            pageCards.Clear();

            if (roundedUiSprite != null)
            {
                UnityEngine.Object.Destroy(roundedUiSprite);
                roundedUiSprite = null;
            }
            if (roundedUiTexture != null)
            {
                UnityEngine.Object.Destroy(roundedUiTexture);
                roundedUiTexture = null;
            }
            if (thumbnailCornerCutoutSprite != null)
            {
                UnityEngine.Object.Destroy(thumbnailCornerCutoutSprite);
                thumbnailCornerCutoutSprite = null;
            }
            if (thumbnailCornerCutoutTexture != null)
            {
                UnityEngine.Object.Destroy(thumbnailCornerCutoutTexture);
                thumbnailCornerCutoutTexture = null;
            }

            if (grid != null && layoutCaptured)
            {
                grid.padding.top = originalPaddingTop;
                grid.padding.bottom = originalPaddingBottom;
                grid.spacing = originalGridSpacing;
            }
        }
    }


}
