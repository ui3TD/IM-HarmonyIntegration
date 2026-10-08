using HarmonyLib;
using System;
using Xunit;

namespace HarmonyIntegration.Tests
{
    /// <summary>
    /// Loading every enabled Harmony mod after the game loads its mods (Mods.LoadMods postfix).
    /// </summary>
    public class SyncAllTests : IDisposable
    {
        private readonly TestGame game = new();

        public void Dispose() => game.Dispose();

        [Fact]
        public void EnabledMod_IsPatchedIn()
        {
            Mods._mod mod = game.AddMod("test.a", FakeMods.Tagging("A"));

            PluginState.SyncAll();

            Assert.Equal("base[A]", Target.Describe());
            Assert.True(Harmony.HasAnyPatches("test.a"));
            Assert.Equal(TestGame.DllOf(mod, "test.a"), PluginState.ActiveSource("test.a"));
            Assert.Contains(game.Log.Infos, m => m.StartsWith("Mod patch loaded: Tag_A,"));
            Assert.Contains("Harmony mod sync complete: 1 patched, 0 unpatched.", game.Log.Infos);
            Assert.Empty(game.Log.Warnings);
            Assert.Empty(game.Log.Errors);
        }

        [Fact]
        public void DisabledMod_IsNotPatched()
        {
            game.AddMod("test.a", FakeMods.Tagging("A"), enabled: false);

            PluginState.SyncAll();

            Assert.Equal(Target.Base, Target.Describe());
            Assert.False(Harmony.HasAnyPatches("test.a"));
            Assert.Empty(game.Log.Entries);
        }

        [Fact]
        public void EnabledState_ComesFromTheSettingsNotTheCachedField()
        {
            Mods._mod mod = game.AddMod("test.a", FakeMods.Tagging("A"));
            mod.Enabled = false;

            PluginState.SyncAll();

            Assert.Equal("base[A]", Target.Describe());

            game.Log.Clear();
            mod.Enabled = true;
            TestGame.SetEnabled(mod, false);
            PluginState.SyncAll();

            Assert.Equal(Target.Base, Target.Describe());
        }

        [Fact]
        public void EnabledState_FallsBackToTheCachedField_WhenTheSettingsFail()
        {
            game.AddMod("test.a", FakeMods.Tagging("A"), enabled: true);
            game.AddMod("test.b", FakeMods.Tagging("B"), enabled: false);
            staticVars._settings settings = staticVars.Settings;
            staticVars.Settings = null;
            try
            {
                PluginState.SyncAll();
            }
            finally
            {
                staticVars.Settings = settings;
            }

            Assert.Equal("base[A]", Target.Describe());
        }

        [Fact]
        public void SeveralMods_AreAllPatchedIn_InListOrder()
        {
            game.AddMod("test.a", FakeMods.Tagging("A"));
            game.AddMod("test.b", FakeMods.Tagging("B"));

            PluginState.SyncAll();

            Assert.Equal("base[A][B]", Target.Describe());
            Assert.Contains("Harmony mod sync complete: 2 patched, 0 unpatched.", game.Log.Infos);
        }

        [Fact]
        public void SyncingAgain_LeavesPatchedModsAlone()
        {
            game.AddMod("test.a", FakeMods.Tagging("A"));
            PluginState.SyncAll();
            game.Log.Clear();

            PluginState.SyncAll();

            Assert.Equal("base[A]", Target.Describe());
            Assert.Empty(game.Log.Entries);
        }

        [Fact]
        public void ModDisabledBeforeReload_IsUnpatched()
        {
            Mods._mod a = game.AddMod("test.a", FakeMods.Tagging("A"));
            game.AddMod("test.b", FakeMods.Tagging("B"));
            PluginState.SyncAll();
            game.Log.Clear();

            TestGame.SetEnabled(a, false);
            PluginState.SyncAll();

            Assert.Equal("base[B]", Target.Describe());
            Assert.Null(PluginState.ActiveSource("test.a"));
            Assert.Contains("Mod patch unloaded: test.a", game.Log.Infos);
            Assert.Contains("Harmony mod sync complete: 0 patched, 1 unpatched.", game.Log.Infos);
        }

        [Fact]
        public void ModRemovedFromTheList_IsUnpatched()
        {
            Mods._mod a = game.AddMod("test.a", FakeMods.Tagging("A"));
            PluginState.SyncAll();

            Mods._Mods.Remove(a);
            PluginState.SyncAll();

            Assert.Equal(Target.Base, Target.Describe());
            Assert.False(Harmony.HasAnyPatches("test.a"));
        }

        [Fact]
        public void NullEntries_AreSkipped()
        {
            Mods._Mods.Add(null);
            game.AddMod("test.a", FakeMods.Tagging("A"));

            PluginState.SyncAll();

            Assert.Equal("base[A]", Target.Describe());
        }

        [Fact]
        public void NonHarmonyMods_AreIgnored()
        {
            game.AddMod(null, name: "PlainMod");
            game.AddMod(null, name: "NoHarmonyID", infoJson: "{\"Title\": \"No Harmony\"}");
            game.AddMod(null, name: "BlankHarmonyID", infoJson: "{\"Title\": \"Blank\", \"HarmonyID\": \"  \"}");
            Mods._Mods.Add(new Mods._mod { ModName = "NoPath", Path = "" });

            PluginState.SyncAll();

            Assert.Equal(Target.Base, Target.Describe());
            Assert.Empty(game.Log.Entries);
        }

        [Fact]
        public void MissingDll_IsWarnedAboutOnce()
        {
            Mods._mod mod = game.AddMod("test.missing", title: "Missing Mod");

            PluginState.SyncAll();
            PluginState.SyncAll();
            PluginState.SyncMod(mod);

            string warning = Assert.Single(game.Log.Warnings);
            Assert.StartsWith("HarmonyID is defined but its DLL was not found for Missing Mod: ", warning);
            Assert.EndsWith("test.missing.dll", warning);
            Assert.Empty(game.Log.Infos);
        }

        [Fact]
        public void MissingDll_WarningNamesTheModFolder_WhenTheModHasNoTitle()
        {
            Mods._mod mod = game.AddMod("test.missing", name: "UntitledMod");
            mod.Title = "";

            PluginState.SyncAll();

            Assert.StartsWith("HarmonyID is defined but its DLL was not found for UntitledMod: ", Assert.Single(game.Log.Warnings));
        }

        [Fact]
        public void UnreadableInfoJson_IsLoggedOnce_AndOtherModsStillLoad()
        {
            game.AddMod(null, name: "Garbled", infoJson: "not json at all");
            game.AddMod("test.a", FakeMods.Tagging("A"));

            PluginState.SyncAll();
            PluginState.SyncAll();

            Assert.Equal("base[A]", Target.Describe());
            Assert.StartsWith("Failed to read Harmony metadata for Garbled: ", Assert.Single(game.Log.Errors));
        }

        [Fact]
        public void ModMetadata_IsReadOncePerFolder()
        {
            Mods._mod mod = game.AddMod("test.a", FakeMods.Tagging("A"), enabled: false);
            PluginState.SyncAll();

            // The cache keeps the first reading, so a changed info.json needs a game restart.
            System.IO.File.WriteAllText(System.IO.Path.Combine(mod.Path, "info.json"), "{\"HarmonyID\": \"test.other\"}");
            TestGame.SetEnabled(mod, true);
            PluginState.SyncAll();

            Assert.Equal("base[A]", Target.Describe());
            Assert.NotNull(PluginState.ActiveSource("test.a"));
        }

        [Fact]
        public void DllThatIsNotAnAssembly_IsLoggedAsAFailure_AndOtherModsStillLoad()
        {
            string bogus = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IMHI.Tests", Guid.NewGuid().ToString("N") + ".dll");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(bogus));
            System.IO.File.WriteAllText(bogus, "not a dll");
            game.AddMod("test.bogus", bogus, title: "Bogus Mod");
            game.AddMod("test.a", FakeMods.Tagging("A"));

            PluginState.SyncAll();

            Assert.Equal("base[A]", Target.Describe());
            Assert.Null(PluginState.ActiveSource("test.bogus"));
            Assert.StartsWith("Failed to synchronize Harmony patch for Bogus Mod (HarmonyID: test.bogus): ", Assert.Single(game.Log.Errors));
            Assert.Contains("Harmony mod sync complete: 1 patched, 0 unpatched.", game.Log.Infos);
        }

        [Fact]
        public void ModThatFailsHalfwayThroughPatching_IsCleanedUp()
        {
            game.AddMod("test.broken", FakeMods.PartiallyBroken(), title: "Broken Mod");

            PluginState.SyncAll();

            Assert.Equal(Target.Base, Target.Other());
            Assert.False(Harmony.HasAnyPatches("test.broken"));
            Assert.Null(PluginState.ActiveSource("test.broken"));
            Assert.StartsWith("Failed to synchronize Harmony patch for Broken Mod (HarmonyID: test.broken): ", Assert.Single(game.Log.Errors));
        }

        [Fact]
        public void FailedMod_IsRetriedOnTheNextSync()
        {
            game.AddMod("test.broken", FakeMods.PartiallyBroken(), title: "Broken Mod");
            PluginState.SyncAll();
            game.Log.Clear();

            PluginState.SyncAll();

            Assert.Single(game.Log.Errors);
            Assert.Equal(Target.Base, Target.Other());
        }

        [Fact]
        public void DllWithNoPatches_IsWarnedAbout_AndNotTracked()
        {
            game.AddMod("test.empty", FakeMods.NoPatches(), title: "Empty Mod");

            PluginState.SyncAll();

            Assert.Equal("Harmony DLL loaded but no patches were registered for Empty Mod (HarmonyID: test.empty).", Assert.Single(game.Log.Warnings));
            Assert.Null(PluginState.ActiveSource("test.empty"));
            Assert.DoesNotContain(game.Log.Infos, m => m.StartsWith("Harmony mod sync complete"));
        }

        [Fact]
        public void HarmonyIDs_AreCaseSensitive()
        {
            game.AddMod("test.case", FakeMods.Tagging("Lower"));
            game.AddMod("TEST.CASE", FakeMods.Tagging("Upper"));

            PluginState.SyncAll();

            Assert.Equal("base[Lower][Upper]", Target.Describe());
            Assert.Empty(game.Log.Warnings);
        }

        [Fact]
        public void PatchesUnderTheModsOwnHarmonyID()
        {
            game.AddMod("test.a", FakeMods.Tagging("A"));

            PluginState.SyncAll();

            Patches info = Harmony.GetPatchInfo(AccessTools.Method(typeof(Target), nameof(Target.Describe)));
            Assert.Equal(new[] { "test.a" }, info.Owners);
        }
    }
}
