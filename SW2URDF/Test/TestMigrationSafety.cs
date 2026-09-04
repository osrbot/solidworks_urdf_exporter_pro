using System;
using System.Collections.Generic;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public class TestMigrationSafety
    {
        [Fact]
        public void DecliningSaveContinuesWithoutDiscardingRecoveryDraft()
        {
            int calls = 0;
            bool continued = ConfigurationSaveInteraction.Save(overwrite =>
            {
                calls++;
                Assert.False(overwrite);
                return ConfigurationSaveResult.ConfirmationRequired();
            }, true, () => false, error => Assert.True(false, error), out bool persisted);
            Assert.True(continued);
            Assert.False(persisted);
            Assert.Equal(1, calls);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SavedAndUnchangedConfigurationsAllowDraftCleanup(bool changed)
        {
            bool continued = ConfigurationSaveInteraction.Save(overwrite => changed
                ? ConfigurationSaveResult.Saved("") : ConfigurationSaveResult.Unchanged(),
                false, () => throw new InvalidOperationException(),
                error => Assert.True(false, error), out bool persisted);
            Assert.True(continued);
            Assert.True(persisted);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ConfirmedSaveOnlyDiscardsDraftOnSuccess(bool success)
        {
            var overwrites = new List<bool>();
            string failure = null;
            bool continued = ConfigurationSaveInteraction.Save(overwrite =>
            {
                overwrites.Add(overwrite);
                return !overwrite ? ConfigurationSaveResult.ConfirmationRequired()
                    : success ? ConfigurationSaveResult.Saved("") : ConfigurationSaveResult.Failed("failure");
            }, true, () => true, error => failure = error, out bool persisted);
            Assert.Equal(success, continued);
            Assert.Equal(success, persisted);
            Assert.Equal(new[] { false, true }, overwrites);
            Assert.Equal(success ? null : "failure", failure);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MissingChildComponentOrMainComponentBlocksMigration(bool missingMain)
        {
            var root = new Link(null) { Name = "base" };
            var child = new Link(root) { Name = "wheel" };
            child.SWComponentPIDs = new List<byte[]> { new byte[] { 1 }, new byte[] { 2 } };
            child.SWMainComponentPID = new byte[] { 3 };
            root.Children.Add(child);
            var node = new LinkNode(root);
            var exception = Assert.Throws<InvalidOperationException>(() =>
                LegacyConfigurationMigration.EnsureComponentBindings(node,
                    pid => pid[0] != (missingMain ? 3 : 2)));
            Assert.Contains("wheel", exception.Message);
            Assert.Equal(2, child.SWComponentPIDs.Count);
            Assert.Equal(new byte[] { 3 }, child.SWMainComponentPID);
            LegacyConfigurationMigration.EnsureComponentBindings(node, pid => true);
        }
    }
}
