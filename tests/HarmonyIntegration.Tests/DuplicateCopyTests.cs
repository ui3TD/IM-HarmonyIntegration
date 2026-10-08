using HarmonyLib;
using System;
using Xunit;

namespace HarmonyIntegration.Tests
{
    /// <summary>
    /// Several enabled copies of one mod (same HarmonyID): only one is patched in. A LocalLow copy
    /// beats a Workshop copy; within a tier the later Mods._Mods entry wins.
    /// </summary>
    public class DuplicateCopyTests : IDisposable
    {
        private readonly TestGame game = new();

        public void Dispose() => game.Dispose();

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LocalCopy_BeatsWorkshopCopy_InEitherOrder(bool localFirst)
        {
            Mods._mod local = null;
            if (localFirst)
                local = game.AddMod("test.dup", FakeMods.Tagging("Local"));
            game.AddMod("test.dup", FakeMods.Tagging("Workshop"), workshop: true);
            if (!localFirst)
                local = game.AddMod("test.dup", FakeMods.Tagging("Local"));

            PluginState.SyncAll();

            Assert.Equal("base[Local]", Target.Describe());
            Assert.Equal(TestGame.DllOf(local, "test.dup"), PluginState.ActiveSource("test.dup"));
            Assert.Equal(
                $"Multiple enabled mod copies share HarmonyID test.dup (2 copies); preferring LocalLow source: {TestGame.DllOf(local, "test.dup")}",
                Assert.Single(game.Log.Warnings));
            Assert.Contains("Harmony mod sync complete: 1 patched, 0 unpatched.", game.Log.Infos);
        }

        [Fact]
        public void TwoWorkshopCopies_LaterEntryWins()
        {
            game.AddMod("test.dup", FakeMods.Tagging("First"), workshop: true);
            Mods._mod second = game.AddMod("test.dup", FakeMods.Tagging("Second"), workshop: true);

            PluginState.SyncAll();

            Assert.Equal("base[Second]", Target.Describe());
            Assert.Equal(
                $"Multiple enabled mod copies share HarmonyID test.dup (2 copies); preferring Workshop source: {TestGame.DllOf(second, "test.dup")}",
                Assert.Single(game.Log.Warnings));
        }

        [Fact]
        public void TwoLocalCopies_LaterEntryWins()
        {
            game.AddMod("test.dup", FakeMods.Tagging("First"));
            game.AddMod("test.dup", FakeMods.Tagging("Second"));
            game.AddMod("test.dup", FakeMods.Tagging("Workshop"), workshop: true);

            PluginState.SyncAll();

            Assert.Equal("base[Second]", Target.Describe());
            Assert.Contains("(3 copies); preferring LocalLow source", Assert.Single(game.Log.Warnings));
        }

        [Fact]
        public void DisabledCopies_DontCountAsDuplicates()
        {
            game.AddMod("test.dup", FakeMods.Tagging("Local"), enabled: false);
            game.AddMod("test.dup", FakeMods.Tagging("Workshop"), workshop: true);

            PluginState.SyncAll();

            Assert.Equal("base[Workshop]", Target.Describe());
            Assert.Empty(game.Log.Warnings);
        }

        [Fact]
        public void CopyWithoutItsDll_DoesntCountAsADuplicate()
        {
            game.AddMod("test.dup");
            game.AddMod("test.dup", FakeMods.Tagging("Workshop"), workshop: true);

            PluginState.SyncAll();

            Assert.Equal("base[Workshop]", Target.Describe());
            Assert.StartsWith("HarmonyID is defined but its DLL was not found", Assert.Single(game.Log.Warnings));
        }

        [Fact]
        public void LocalCopyEnabledLater_ReplacesTheWorkshopCopy()
        {
            Mods._mod local = game.AddMod("test.dup", FakeMods.Tagging("Local"), enabled: false);
            game.AddMod("test.dup", FakeMods.Tagging("Workshop"), workshop: true);
            PluginState.SyncAll();
            game.Log.Clear();

            TestGame.SetEnabled(local, true);
            PluginState.SyncAll();

            Assert.Equal("base[Local]", Target.Describe());
            Assert.Equal(TestGame.DllOf(local, "test.dup"), PluginState.ActiveSource("test.dup"));
            Assert.Contains("Harmony mod sync complete: 1 patched, 1 unpatched.", game.Log.Infos);
        }

        [Fact]
        public void LocalCopyDisabledLater_HandsOverToTheWorkshopCopy()
        {
            Mods._mod local = game.AddMod("test.dup", FakeMods.Tagging("Local"));
            Mods._mod workshop = game.AddMod("test.dup", FakeMods.Tagging("Workshop"), workshop: true);
            PluginState.SyncAll();
            game.Log.Clear();

            TestGame.SetEnabled(local, false);
            PluginState.SyncAll();

            Assert.Equal("base[Workshop]", Target.Describe());
            Assert.Equal(TestGame.DllOf(workshop, "test.dup"), PluginState.ActiveSource("test.dup"));
            Assert.Empty(game.Log.Warnings);
        }

        [Fact]
        public void SameCopy_IsNotRepatched_WhenItsSiblingIsDisabled()
        {
            Mods._mod local = game.AddMod("test.dup", FakeMods.Tagging("Local"));
            Mods._mod workshop = game.AddMod("test.dup", FakeMods.Tagging("Workshop"), workshop: true);
            PluginState.SyncAll();
            game.Log.Clear();

            TestGame.SetEnabled(workshop, false);
            PluginState.SyncAll();

            Assert.Equal("base[Local]", Target.Describe());
            Assert.Empty(game.Log.Entries);
        }

        [Fact]
        public void OnlyOneOwnerIsPatched_ForASharedID()
        {
            game.AddMod("test.dup", FakeMods.Tagging("Local"));
            game.AddMod("test.dup", FakeMods.Tagging("Workshop"), workshop: true);

            PluginState.SyncAll();

            Patches info = Harmony.GetPatchInfo(AccessTools.Method(typeof(Target), nameof(Target.Describe)));
            Assert.Single(info.Postfixes);
        }
    }
}
