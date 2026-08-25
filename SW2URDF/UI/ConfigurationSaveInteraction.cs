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
            bool confirmChanges)
        {
            if (saveOperation == null)
            {
                throw new ArgumentNullException("saveOperation");
            }

            ConfigurationSaveResult result = saveOperation(!confirmChanges);
            if (result.Status == ConfigurationSaveStatus.ConfirmationRequired)
            {
                DialogResult answer = MessageBox.Show(
                    "The configuration has changed. Would you like to save it?",
                    "Save Export Configuration",
                    MessageBoxButtons.YesNo);
                if (answer != DialogResult.Yes)
                {
                    return true;
                }
                result = saveOperation(true);
            }

            if (result.Status == ConfigurationSaveStatus.Failed)
            {
                MessageBox.Show(result.ErrorMessage, "SW2URDF");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(result.InformationMessage))
            {
                logger.Info(result.InformationMessage);
            }
            return true;
        }
    }
}
