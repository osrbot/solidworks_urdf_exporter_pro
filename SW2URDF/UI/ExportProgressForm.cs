using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    internal sealed class ExportProgressForm : Form
    {
        private readonly Label labelStage;
        private readonly Label labelElapsed;
        private readonly ProgressBar progressBar;

        public ExportProgressForm()
        {
            Text = ChineseUiText.Translate("Exporting URDF package", "正在导出 URDF 功能包");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ControlBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(460, 118);

            labelStage = new Label
            {
                AutoEllipsis = true,
                Location = new Point(16, 16),
                Size = new Size(428, 20),
                Text = ChineseUiText.Translate("Preparing export", "正在准备导出")
            };
            progressBar = new ProgressBar
            {
                Location = new Point(16, 44),
                Size = new Size(428, 20),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 24
            };
            labelElapsed = new Label
            {
                AutoSize = true,
                Location = new Point(16, 78),
                Text = ChineseUiText.Translate("Elapsed: 0 s", "用时：0 秒")
            };

            Controls.Add(labelStage);
            Controls.Add(progressBar);
            Controls.Add(labelElapsed);
            ChineseUiText.Apply(this);
        }

        public void UpdateProgress(ExportProgressEventArgs progress)
        {
            if (progress == null || IsDisposed)
            {
                return;
            }
            labelStage.Text = progress.Stage;
            labelElapsed.Text = String.Format(
                ChineseUiText.Translate("Elapsed: {0}", "用时：{0}"),
                OperationHeartbeat.FormatElapsed(progress.Elapsed));
            Refresh();
        }
    }
}
