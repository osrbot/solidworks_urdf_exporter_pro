using SW2URDF.URDF;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public class TestExportSessionCloseCoordinator
    {
        [Fact]
        public void ExternalClosePreservesDraftInsteadOfBlockingSolidWorks()
        {
            ExportSessionCloseCoordinator coordinator =
                new ExportSessionCloseCoordinator();
            LinkNode projection = CreateProjection();

            coordinator.Capture(0, projection, false);

            ExportSessionCloseAction action = coordinator.BeginFinalization(
                1,
                out LinkNode capturedProjection);

            Assert.Equal(ExportSessionCloseAction.SaveDraft, action);
            Assert.Same(projection, capturedProjection);
        }

        [Fact]
        public void OkayCloseRequestsFormalConfigurationSave()
        {
            ExportSessionCloseCoordinator coordinator =
                new ExportSessionCloseCoordinator();
            coordinator.Capture(7, CreateProjection(), false);

            ExportSessionCloseAction action = coordinator.BeginFinalization(
                7,
                out LinkNode capturedProjection);

            Assert.Equal(ExportSessionCloseAction.SaveConfiguration, action);
            Assert.NotNull(capturedProjection);
        }

        [Fact]
        public void SuccessfulExportDoesNotRepeatPersistenceDuringPageClose()
        {
            ExportSessionCloseCoordinator coordinator =
                new ExportSessionCloseCoordinator();
            coordinator.Capture(7, CreateProjection(), true);

            ExportSessionCloseAction action = coordinator.BeginFinalization(
                7,
                out LinkNode capturedProjection);

            Assert.Equal(ExportSessionCloseAction.None, action);
            Assert.NotNull(capturedProjection);
        }

        [Fact]
        public void CloseFinalizationAndNotificationAreIdempotent()
        {
            ExportSessionCloseCoordinator coordinator =
                new ExportSessionCloseCoordinator();
            coordinator.Capture(0, CreateProjection(), false);

            Assert.Equal(
                ExportSessionCloseAction.SaveDraft,
                coordinator.BeginFinalization(1, out LinkNode firstProjection));
            Assert.NotNull(firstProjection);
            Assert.Equal(
                ExportSessionCloseAction.None,
                coordinator.BeginFinalization(1, out LinkNode secondProjection));
            Assert.Same(firstProjection, secondProjection);
            Assert.True(coordinator.TryClaimClosedNotification());
            Assert.False(coordinator.TryClaimClosedNotification());
        }

        [Fact]
        public void MissingProjectionProducesNoPersistenceAction()
        {
            ExportSessionCloseCoordinator coordinator =
                new ExportSessionCloseCoordinator();
            coordinator.Capture(0, null, false);

            Assert.Equal(
                ExportSessionCloseAction.None,
                coordinator.BeginFinalization(1, out LinkNode capturedProjection));
            Assert.Null(capturedProjection);
        }

        private static LinkNode CreateProjection()
        {
            LinkNode root = new LinkNode
            {
                IsBaseNode = true,
                Name = "base_link",
                Text = "base_link"
            };
            root.Link.Name = "base_link";
            return root;
        }
    }
}
