using SW2URDF.URDF;

namespace SW2URDF.URDFExport
{
    internal enum ExportSessionCloseAction
    {
        None,
        SaveConfiguration,
        SaveDraft
    }

    internal sealed class ExportSessionCloseCoordinator
    {
        private bool finalizationStarted;
        private bool closedNotificationClaimed;
        private bool closingAfterSuccessfulExport;
        private int closeReason;
        private LinkNode projection;

        internal void Capture(
            int reason,
            LinkNode currentProjection,
            bool successfulExport)
        {
            if (finalizationStarted)
            {
                return;
            }

            closeReason = reason;
            projection = currentProjection;
            closingAfterSuccessfulExport = successfulExport;
        }

        internal ExportSessionCloseAction BeginFinalization(
            int okayReason,
            out LinkNode capturedProjection)
        {
            capturedProjection = projection;
            if (finalizationStarted)
            {
                return ExportSessionCloseAction.None;
            }

            finalizationStarted = true;
            if (closingAfterSuccessfulExport || capturedProjection == null)
            {
                return ExportSessionCloseAction.None;
            }

            return closeReason == okayReason
                ? ExportSessionCloseAction.SaveConfiguration
                : ExportSessionCloseAction.SaveDraft;
        }

        internal bool TryClaimClosedNotification()
        {
            if (closedNotificationClaimed)
            {
                return false;
            }

            closedNotificationClaimed = true;
            return true;
        }
    }
}
