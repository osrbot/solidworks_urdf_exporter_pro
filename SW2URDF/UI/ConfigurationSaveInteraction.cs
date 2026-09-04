using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    internal static class ConfigurationSaveInteraction
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        public static bool Save(
            Func<bool, ConfigurationSaveResult> saveOperation,
            bool confirmChanges, out bool persisted)
        {
            return Save(saveOperation, confirmChanges,
                () => MessageBox.Show(
                    "The configuration has changed. Would you like to save it?",
                    "Save Export Configuration", MessageBoxButtons.YesNo) == DialogResult.Yes,
                error => MessageBox.Show(error, "SW2URDF"), out persisted);
        }

        internal static bool Save(
            Func<bool, ConfigurationSaveResult> saveOperation,
            bool confirmChanges, Func<bool> confirm, Action<string> showError, out bool persisted)
        {
            persisted = false;
            if (saveOperation == null)
            {
                throw new ArgumentNullException("saveOperation");
            }

            ConfigurationSaveResult result = saveOperation(!confirmChanges);
            if (result.Status == ConfigurationSaveStatus.ConfirmationRequired)
            {
                if (!confirm())
                {
                    return true;
                }
                result = saveOperation(true);
            }

            if (result.Status == ConfigurationSaveStatus.Failed)
            {
                showError(result.ErrorMessage);
                return false;
            }

            persisted = result.Status == ConfigurationSaveStatus.Saved ||
                result.Status == ConfigurationSaveStatus.Unchanged;
            if (!persisted)
                return false;

            if (!string.IsNullOrWhiteSpace(result.InformationMessage))
            {
                logger.Info(result.InformationMessage);
            }
            return true;
        }
    }
}
