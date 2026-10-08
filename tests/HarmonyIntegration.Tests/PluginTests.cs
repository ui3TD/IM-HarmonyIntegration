using BepInEx;
using HarmonyLib;
using Mono.Cecil;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace HarmonyIntegration.Tests
{
    /// <summary>
    /// The plugin's identity and its two patches on the game.
    /// </summary>
    public class PluginTests
    {
        private static readonly Assembly PluginAssembly = PluginState.Plugin.Assembly;

        private static Type[] PatchClasses() => PluginAssembly.GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
            .ToArray();

        /// <summary>
        /// Harmony Checker finds IM-HI by this GUID to check its version, and mods can depend on it.
        /// </summary>
        [Fact]
        public void PluginGuid_IsUnchanged()
        {
            BepInPlugin info = PluginState.Plugin.GetCustomAttribute<BepInPlugin>();
            Assert.Equal("com.name.HarmonyIntegration", info.GUID);
            Assert.Equal("HarmonyIntegration", info.Name);
        }

        [Fact]
        public void PluginVersion_MatchesTheProjectVersion()
        {
            string csproj = Path.Combine(RepoRoot(), "source", "HarmonyIntegration.csproj");
            string version = XDocument.Load(csproj).Descendants("Version").Single().Value;

            Version assembly = PluginAssembly.GetName().Version;
            Assert.Equal(new Version(version), PluginState.Plugin.GetCustomAttribute<BepInPlugin>().Version);
            Assert.Equal(new Version(version), new Version(assembly.Major, assembly.Minor, assembly.Build));
        }

        [Fact]
        public void PatchesOnlyTheGamesModLoadingAndToggling()
        {
            string[] targets = PatchClasses()
                .Select(t => HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(t)))
                .Select(m => m.declaringType.FullName + "." + m.methodName)
                .OrderBy(s => s)
                .ToArray();

            Assert.Equal(new[] { "Mods.LoadMods", "staticVars+_settings.SwitchModStatus" }, targets);
        }

        [Fact]
        public void AllPatchTargetsExistInGame()
        {
            foreach (Type patchClass in PatchClasses())
            {
                HarmonyMethod info = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(patchClass));
                Assert.True(
                    AccessTools.Method(info.declaringType, info.methodName, info.argumentTypes) != null,
                    $"{patchClass.Name}: {info.declaringType}.{info.methodName} not found");
            }
        }

        /// <summary>
        /// Applying a patch also checks its injected parameters (SwitchModStatus's ModName) against the game.
        /// Mods.LoadMods reads Application.persistentDataPath, so it can't be compiled outside the game.
        /// </summary>
        [Fact]
        public void SwitchModStatusPatch_AppliesToGame()
        {
            Harmony harmony = new("tests.HarmonyIntegration.PatchesApply");
            try
            {
                harmony.CreateClassProcessor(PluginState.PatchClass("StaticVars__settings_SwitchModStatus_P")).Patch();
                Assert.Single(harmony.GetPatchedMethods());
            }
            finally
            {
                harmony.UnpatchSelf();
            }
        }

        [Fact]
        public void LoadModsPatch_RunsAfterTheGameAppliesEnabledStates()
        {
            MethodInfo postfix = AccessTools.Method(PluginState.PatchClass("Mods_LoadMods_P"), "Postfix");
            Assert.NotNull(postfix);
            Assert.Empty(postfix.GetParameters());

            // The postfix relies on LoadMods ending with ReEnableMods, which copies the settings'
            // enabled flags onto each loaded mod. Read with Cecil: LoadMods references Steam types.
            using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(Mods).Assembly.Location);
            MethodReference lastCall = module.GetType(nameof(Mods)).Methods.Single(m => m.Name == nameof(Mods.LoadMods)).Body.Instructions
                .Select(i => i.Operand as MethodReference)
                .Last(m => m != null);
            Assert.Equal(nameof(Mods.ReEnableMods), lastCall.Name);
        }

        [Fact]
        public void ReEnableMods_CopiesTheSettingsEnabledFlags()
        {
            Mods._Mods.Clear();
            staticVars.Settings._Mods.Clear();
            try
            {
                Mods._mod on = new() { ModName = "On", Enabled = false };
                Mods._mod off = new() { ModName = "Off", Enabled = true };
                Mods._Mods.Add(on);
                Mods._Mods.Add(off);
                staticVars.Settings._Mods.Add(new Mods._mod { ModName = "Off", Enabled = false });

                Mods.ReEnableMods();

                Assert.True(on.Enabled);
                Assert.False(off.Enabled);
            }
            finally
            {
                Mods._Mods.Clear();
                staticVars.Settings._Mods.Clear();
            }
        }

        internal static string RepoRoot()
        {
            DirectoryInfo dir = new(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "source", "HarmonyIntegration.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }
    }
}
