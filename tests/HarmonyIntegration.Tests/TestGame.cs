using BepInEx.Logging;
using HarmonyLib;
using Microsoft.CSharp;
using Steamworks;
using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

// Every test shares the plugin's static state, the game's mod lists and Target's patches.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HarmonyIntegration.Tests
{
    /// <summary>
    /// The method the fake mods patch. Each applied mod appends its tag, so Describe() shows
    /// exactly which mod copies are patched in, and how many times.
    /// </summary>
    public static class Target
    {
        public const string Base = "base";

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Describe() => Base;

        /// <summary>
        /// A second target, so a mod can patch more than one method.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Other() => Base;
    }

    /// <summary>
    /// The plugin's private state and methods, reached by reflection.
    /// </summary>
    internal static class PluginState
    {
        public static readonly Type Plugin = typeof(global::HarmonyIntegration.Plugin);

        public static IDictionary ActivePatchSources =>
            (IDictionary)AccessTools.Field(Plugin, "ActivePatchSources").GetValue(null);

        public static IDictionary ModDataCache =>
            (IDictionary)AccessTools.Field(Plugin, "ModDataCache").GetValue(null);

        public static void SyncAll() => Invoke("SyncAllModPatches");

        public static void SyncMod(Mods._mod mod) => Invoke("SyncModPatch", mod);

        public static Type PatchClass(string name) =>
            AccessTools.TypeByName("HarmonyIntegration." + name);

        public static void Invoke(string method, params object[] args)
        {
            try
            {
                AccessTools.Method(Plugin, method).Invoke(null, args);
            }
            catch (TargetInvocationException e)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            }
        }

        /// <summary>
        /// The physical DLL path the plugin records as the source of a HarmonyID, or null.
        /// </summary>
        public static string ActiveSource(string harmonyID) =>
            ActivePatchSources.Contains(harmonyID) ? (string)ActivePatchSources[harmonyID] : null;
    }

    /// <summary>
    /// Records what the plugin logs.
    /// </summary>
    internal sealed class LogRecorder
    {
        public readonly List<(LogLevel Level, string Text)> Entries = new();

        public LogRecorder()
        {
            ManualLogSource source = new("HarmonyIntegration.Tests");
            source.LogEvent += (_, e) => Entries.Add((e.Level, e.Data?.ToString()));
            global::HarmonyIntegration.Plugin.Log = source;
        }

        public List<string> At(LogLevel level) => Entries.Where(e => e.Level == level).Select(e => e.Text).ToList();

        public List<string> Warnings => At(LogLevel.Warning);
        public List<string> Errors => At(LogLevel.Error);
        public List<string> Infos => At(LogLevel.Info);

        public void Clear() => Entries.Clear();
    }

    /// <summary>
    /// Fake Harmony mods, compiled once per run. Each patches Target.Describe to append "[tag]".
    /// </summary>
    internal static class FakeMods
    {
        private static readonly Dictionary<string, string> Built = new();
        private static readonly string BuildDir = Path.Combine(Path.GetTempPath(), "IMHI.Tests", "build", Guid.NewGuid().ToString("N"));

        /// <summary>
        /// A mod with one postfix on Target.Describe.
        /// </summary>
        public static string Tagging(string tag) => Build("Tag_" + tag, $@"
namespace FakeMod_{tag}
{{
    [HarmonyLib.HarmonyPatch(typeof(HarmonyIntegration.Tests.Target), ""Describe"")]
    public static class Describe_P
    {{
        public static void Postfix(ref string __result) {{ __result += ""[{tag}]""; }}
    }}
}}");

        /// <summary>
        /// A mod that patches Target.Other first, then fails on a method that doesn't exist,
        /// leaving a partial patch behind for the plugin to clean up.
        /// </summary>
        public static string PartiallyBroken() => Build("PartiallyBroken", @"
namespace FakeMod_Broken
{
    [HarmonyLib.HarmonyPatch(typeof(HarmonyIntegration.Tests.Target), ""Other"")]
    public static class A_Other_P
    {
        public static void Postfix(ref string __result) { __result += ""[broken]""; }
    }

    [HarmonyLib.HarmonyPatch(typeof(HarmonyIntegration.Tests.Target), ""NoSuchMethod"")]
    public static class B_Missing_P
    {
        public static void Postfix() { }
    }
}");

        /// <summary>
        /// A valid .NET library with no Harmony patches in it.
        /// </summary>
        public static string NoPatches() => Build("NoPatches", @"
namespace FakeMod_Empty
{
    public static class Nothing { }
}");

        private static string Build(string name, string source)
        {
            if (Built.TryGetValue(name, out string path))
                return path;

            Directory.CreateDirectory(BuildDir);
            path = Path.Combine(BuildDir, name + ".dll");
            CompilerParameters options = new()
            {
                OutputAssembly = path,
                GenerateInMemory = false,
                TreatWarningsAsErrors = false,
            };
            options.ReferencedAssemblies.Add("System.dll");
            options.ReferencedAssemblies.Add(typeof(Harmony).Assembly.Location);
            options.ReferencedAssemblies.Add(typeof(Target).Assembly.Location);

            using CSharpCodeProvider compiler = new();
            CompilerResults results = compiler.CompileAssemblyFromSource(options, source);
            Assert.False(results.Errors.HasErrors, string.Join(Environment.NewLine, results.Errors.Cast<CompilerError>()));
            Built[name] = path;
            return path;
        }
    }

    /// <summary>
    /// Mod folders on disk plus the game's mod lists (Mods._Mods and the settings' enabled flags).
    /// </summary>
    internal sealed class TestGame : IDisposable
    {
        public readonly LogRecorder Log = new();
        private readonly string root = Path.Combine(Path.GetTempPath(), "IMHI.Tests", "mods", Guid.NewGuid().ToString("N"));
        private readonly HashSet<string> harmonyIDs = new();
        private int folders;

        public TestGame()
        {
            Reset();
        }

        public void Dispose()
        {
            Reset();
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
                // Loaded mod DLLs stay locked until the test process exits.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void Reset()
        {
            foreach (string id in harmonyIDs)
                Harmony.UnpatchID(id);
            foreach (string id in PluginState.ActivePatchSources.Keys.Cast<string>().ToList())
                Harmony.UnpatchID(id);
            PluginState.ActivePatchSources.Clear();
            PluginState.ModDataCache.Clear();
            Mods._Mods.Clear();
            staticVars.Settings._Mods.Clear();
        }

        /// <summary>
        /// Adds a mod folder with an info.json and, when given, a patch DLL copied in as HarmonyID.dll.
        /// The game lists it in Mods._Mods; disabled mods are also recorded in the settings, as the game does.
        /// </summary>
        public Mods._mod AddMod(
            string harmonyID,
            string dll = null,
            bool workshop = false,
            bool enabled = true,
            string name = null,
            string title = null,
            string infoJson = null)
        {
            folders++;
            name ??= "Mod" + folders;
            string dir = Path.Combine(root, workshop ? "Workshop" : "LocalLow", folders + "_" + name);
            Directory.CreateDirectory(dir);

            if (infoJson != null)
                File.WriteAllText(Path.Combine(dir, "info.json"), infoJson);
            else if (harmonyID != null)
                File.WriteAllText(Path.Combine(dir, "info.json"), $"{{\"Title\": \"{title ?? name}\", \"HarmonyID\": \"{harmonyID}\"}}");

            if (dll != null)
                File.Copy(dll, Path.Combine(dir, harmonyID + ".dll"));
            if (!string.IsNullOrWhiteSpace(harmonyID))
                harmonyIDs.Add(harmonyID);

            Mods._mod mod = new()
            {
                Path = dir,
                ModName = workshop ? (name + "_ws") : name,
                Title = title ?? name,
                Enabled = enabled,
            };
            if (workshop)
                mod.SteamID = new PublishedFileId_t((ulong)(1000 + folders));

            Mods._Mods.Add(mod);
            SetEnabled(mod, enabled);
            return mod;
        }

        /// <summary>
        /// Sets the settings' enabled flag, which IsEnabled() reads, without touching mod.Enabled.
        /// </summary>
        public static void SetEnabled(Mods._mod mod, bool enabled)
        {
            Mods._mod setting = staticVars.Settings._Mods.FirstOrDefault(m => m.ModName == mod.ModName);
            if (setting == null)
            {
                setting = new Mods._mod { ModName = mod.ModName };
                staticVars.Settings._Mods.Add(setting);
            }

            setting.Enabled = enabled;
        }

        /// <summary>
        /// Path of the patch DLL in a mod's folder.
        /// </summary>
        public static string DllOf(Mods._mod mod, string harmonyID) =>
            Path.GetFullPath(Path.Combine(mod.Path, harmonyID + ".dll"));
    }
}
