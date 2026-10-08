using HarmonyLib;
using System;
using Xunit;

namespace HarmonyIntegration.Tests
{
    /// <summary>
    /// Toggling a mod in the game's mod list (staticVars._settings.SwitchModStatus postfix) patches
    /// or unpatches its HarmonyID straight away. These call the game's real SwitchModStatus with the
    /// plugin's patch applied.
    /// </summary>
    public class SwitchModStatusTests : IDisposable
    {
        private const string TestHarmonyID = "tests.HarmonyIntegration.SwitchModStatus";

        private readonly TestGame game = new();
        private readonly Harmony harmony = new(TestHarmonyID);

        public SwitchModStatusTests()
        {
            harmony.CreateClassProcessor(PluginState.PatchClass("StaticVars__settings_SwitchModStatus_P")).Patch();
        }

        public void Dispose()
        {
            harmony.UnpatchSelf();
            game.Dispose();
        }

        private void Toggle(Mods._mod mod) => staticVars.Settings.SwitchModStatus(mod.ModName);

        [Fact]
        public void DisablingAMod_UnpatchesIt()
        {
            Mods._mod mod = game.AddMod("test.a", FakeMods.Tagging("A"));
            PluginState.SyncAll();
            game.Log.Clear();

            Toggle(mod);

            Assert.Equal(Target.Base, Target.Describe());
            Assert.False(mod.Enabled);
            Assert.Null(PluginState.ActiveSource("test.a"));
            Assert.Equal(new[] { "Mod patch unloaded: test.a" }, game.Log.Infos);
        }

        [Fact]
        public void EnablingAMod_PatchesIt()
        {
            Mods._mod mod = game.AddMod("test.a", FakeMods.Tagging("A"), enabled: false);
            PluginState.SyncAll();

            Toggle(mod);

            Assert.Equal("base[A]", Target.Describe());
            Assert.True(mod.Enabled);
            Assert.Contains(game.Log.Infos, m => m.StartsWith("Mod patch loaded: Tag_A,"));
        }

        [Fact]
        public void TogglingTwice_RestoresThePatchOnce()
        {
            Mods._mod mod = game.AddMod("test.a", FakeMods.Tagging("A"));
            PluginState.SyncAll();

            Toggle(mod);
            Toggle(mod);

            Assert.Equal("base[A]", Target.Describe());
        }

        [Fact]
        public void TogglingOneMod_LeavesOtherModsAlone()
        {
            Mods._mod a = game.AddMod("test.a", FakeMods.Tagging("A"));
            game.AddMod("test.b", FakeMods.Tagging("B"));
            PluginState.SyncAll();

            Toggle(a);

            Assert.Equal("base[B]", Target.Describe());
        }

        [Fact]
        public void DisablingTheWorkshopCopy_KeepsTheEnabledLocalCopy()
        {
            game.AddMod("test.dup", FakeMods.Tagging("Local"));
            Mods._mod workshop = game.AddMod("test.dup", FakeMods.Tagging("Workshop"), workshop: true);
            PluginState.SyncAll();
            game.Log.Clear();

            Toggle(workshop);

            Assert.Equal("base[Local]", Target.Describe());
            Assert.Empty(game.Log.Entries);
        }

        [Fact]
        public void DisablingTheLocalCopy_SwitchesToTheWorkshopCopy()
        {
            Mods._mod local = game.AddMod("test.dup", FakeMods.Tagging("Local"));
            Mods._mod workshop = game.AddMod("test.dup", FakeMods.Tagging("Workshop"), workshop: true);
            PluginState.SyncAll();

            Toggle(local);

            Assert.Equal("base[Workshop]", Target.Describe());
            Assert.Equal(TestGame.DllOf(workshop, "test.dup"), PluginState.ActiveSource("test.dup"));
        }

        [Fact]
        public void EnablingASecondCopy_LogsWhichCopyWon()
        {
            Mods._mod local = game.AddMod("test.dup", FakeMods.Tagging("Local"), enabled: false);
            game.AddMod("test.dup", FakeMods.Tagging("Workshop"), workshop: true);
            PluginState.SyncAll();

            Toggle(local);

            Assert.Equal("base[Local]", Target.Describe());
            Assert.Contains("(2 copies); preferring LocalLow source", Assert.Single(game.Log.Warnings));
        }

        [Fact]
        public void DisablingTheLastCopy_UnpatchesTheID()
        {
            Mods._mod local = game.AddMod("test.dup", FakeMods.Tagging("Local"));
            Mods._mod workshop = game.AddMod("test.dup", FakeMods.Tagging("Workshop"), workshop: true);
            PluginState.SyncAll();

            Toggle(local);
            Toggle(workshop);

            Assert.Equal(Target.Base, Target.Describe());
            Assert.False(Harmony.HasAnyPatches("test.dup"));
        }

        [Fact]
        public void TogglingANonHarmonyMod_DoesNothing()
        {
            Mods._mod plain = game.AddMod(null, name: "PlainMod");
            game.AddMod("test.a", FakeMods.Tagging("A"));
            PluginState.SyncAll();
            game.Log.Clear();

            Toggle(plain);

            Assert.False(plain.Enabled);
            Assert.Equal("base[A]", Target.Describe());
            Assert.Empty(game.Log.Entries);
        }

        [Fact]
        public void TogglingAnUnknownMod_IsWarnedAbout()
        {
            staticVars.Settings.SwitchModStatus("NotInstalled");

            Assert.Equal("SwitchModStatus could not find mod: NotInstalled", Assert.Single(game.Log.Warnings));
        }

        [Fact]
        public void SyncModPatch_IgnoresNull()
        {
            PluginState.SyncMod(null);

            Assert.Empty(game.Log.Entries);
        }
    }
}
