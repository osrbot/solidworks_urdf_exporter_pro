using System;
using System.Drawing;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    internal enum UrdfExportTutorialPromptChoice
    {
        Start,
        SkipOnce,
        Dismiss
    }

    internal sealed class UrdfExportTutorialController : IDisposable
    {
        private readonly IUrdfExportTutorialStateStore stateStore;
        private UrdfExportTutorialForm activeForm;

        public UrdfExportTutorialController()
            : this(new FileUrdfExportTutorialStateStore())
        {
        }

        internal UrdfExportTutorialController(IUrdfExportTutorialStateStore stateStore)
        {
            if (stateStore == null)
            {
                throw new ArgumentNullException("stateStore");
            }
            this.stateStore = stateStore;
        }

        public void OfferBeforeAssemblyExport()
        {
            UrdfExportTutorialProgress progress = stateStore.Load();
            if (progress.Status == UrdfExportTutorialStatus.InProgress)
            {
                Show(progress, true);
                return;
            }
            if (progress.Status != UrdfExportTutorialStatus.NotStarted)
            {
                return;
            }

            UrdfExportTutorialPromptChoice choice = UrdfExportTutorialPromptForm.Ask();
            if (choice == UrdfExportTutorialPromptChoice.Start)
            {
                progress = new UrdfExportTutorialProgress(UrdfExportTutorialStatus.InProgress, 0);
                stateStore.Save(progress);
                Show(progress, true);
            }
            else if (choice == UrdfExportTutorialPromptChoice.Dismiss)
            {
                stateStore.Save(new UrdfExportTutorialProgress(
                    UrdfExportTutorialStatus.Dismissed,
                    0));
            }
        }

        public void ShowExplicitly()
        {
            UrdfExportTutorialProgress progress = ResolveExplicitProgress(stateStore.Load());
            bool trackProgress = progress.Status == UrdfExportTutorialStatus.NotStarted ||
                progress.Status == UrdfExportTutorialStatus.InProgress;
            if (progress.Status == UrdfExportTutorialStatus.NotStarted)
            {
                progress = new UrdfExportTutorialProgress(UrdfExportTutorialStatus.InProgress, 0);
                stateStore.Save(progress);
            }
            Show(progress, trackProgress);
        }

        internal static UrdfExportTutorialProgress ResolveExplicitProgress(
            UrdfExportTutorialProgress progress)
        {
            if (progress == null)
            {
                return UrdfExportTutorialProgress.NotStarted();
            }
            if (progress.Status == UrdfExportTutorialStatus.Completed ||
                progress.Status == UrdfExportTutorialStatus.Dismissed)
            {
                return new UrdfExportTutorialProgress(progress.Status, 0);
            }
            return progress;
        }

        public void Dispose()
        {
            if (activeForm != null)
            {
                activeForm.FormClosed -= ActiveFormClosed;
                activeForm.Close();
                activeForm.Dispose();
                activeForm = null;
            }
        }

        private void Show(UrdfExportTutorialProgress progress, bool trackProgress)
        {
            if (activeForm != null && !activeForm.IsDisposed)
            {
                if (activeForm.WindowState == FormWindowState.Minimized)
                {
                    activeForm.WindowState = FormWindowState.Normal;
                }
                activeForm.Show();
                activeForm.Activate();
                return;
            }

            activeForm = new UrdfExportTutorialForm(stateStore, progress, trackProgress);
            activeForm.FormClosed += ActiveFormClosed;
            activeForm.Show();
            activeForm.Activate();
        }

        private void ActiveFormClosed(object sender, FormClosedEventArgs e)
        {
            UrdfExportTutorialForm closedForm = activeForm;
            activeForm = null;
            if (closedForm != null)
            {
                closedForm.FormClosed -= ActiveFormClosed;
                closedForm.Dispose();
            }
        }
    }

    internal sealed class UrdfExportTutorialPromptForm : Form
    {
        private UrdfExportTutorialPromptChoice choice = UrdfExportTutorialPromptChoice.SkipOnce;

        private UrdfExportTutorialPromptForm()
        {
            bool chinese = ChineseUiText.ShouldUseChinese();
            Text = chinese ? "SW2URDF 首次使用" : "SW2URDF First Use";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(650, 245);
            if (chinese)
            {
                Font = new Font("Microsoft YaHei UI", Font.Size, Font.Style);
            }

            Label title = new Label
            {
                AutoSize = false,
                Location = new Point(20, 18),
                Size = new Size(610, 28),
                Font = new Font(Font, FontStyle.Bold),
                Text = chinese ? "是否打开完整 URDF 导出教程？" : "Open the complete URDF export tutorial?"
            };
            Label explanation = new Label
            {
                AutoSize = false,
                Location = new Point(20, 54),
                Size = new Size(610, 92),
                Text = chinese ?
                    "教程会在真实导出界面旁显示 8 个步骤，覆盖坐标系、Link 树、Joint、质量惯性、碰撞网格、ROS1/ROS2 导出和结果校验。它不会自动点击界面，也不会修改模型。选择“跳过本次”后，下次导出仍会询问。" :
                    "The tutorial stays beside the real exporter and covers frames, the Link tree, Joints, mass and inertia, collision meshes, ROS1/ROS2 output, and validation. It never clicks controls or modifies the model. Skip once asks again on a later export."
            };

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Location = new Point(20, 176),
                Size = new Size(610, 42)
            };
            Button start = CreateChoiceButton(chinese ? "开始快速教程" : "Start tutorial", 142);
            Button skip = CreateChoiceButton(chinese ? "跳过本次" : "Skip once", 112);
            Button dismiss = CreateChoiceButton(chinese ? "不再提示" : "Do not remind", 122);
            start.Click += (sender, e) => Finish(UrdfExportTutorialPromptChoice.Start);
            skip.Click += (sender, e) => Finish(UrdfExportTutorialPromptChoice.SkipOnce);
            dismiss.Click += (sender, e) => Finish(UrdfExportTutorialPromptChoice.Dismiss);
            buttons.Controls.Add(start);
            buttons.Controls.Add(skip);
            buttons.Controls.Add(dismiss);

            AcceptButton = start;
            CancelButton = skip;
            Controls.Add(title);
            Controls.Add(explanation);
            Controls.Add(buttons);
        }

        public static UrdfExportTutorialPromptChoice Ask()
        {
            using (UrdfExportTutorialPromptForm form = new UrdfExportTutorialPromptForm())
            {
                form.ShowDialog();
                return form.choice;
            }
        }

        private static Button CreateChoiceButton(string text, int width)
        {
            return new Button
            {
                Size = new Size(width, 32),
                Margin = new Padding(8, 0, 0, 0),
                Text = text,
                UseVisualStyleBackColor = true
            };
        }

        private void Finish(UrdfExportTutorialPromptChoice selectedChoice)
        {
            choice = selectedChoice;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
