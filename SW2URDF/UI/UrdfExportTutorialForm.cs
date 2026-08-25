using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    internal sealed class UrdfExportTutorialForm : Form
    {
        private readonly bool chinese;
        private readonly IUrdfExportTutorialStateStore stateStore;
        private readonly bool trackProgress;
        private readonly IList<UrdfExportTutorialStep> steps;
        private readonly ListBox stepList;
        private readonly RichTextBox contentBox;
        private readonly Label progressLabel;
        private readonly Button previousButton;
        private readonly Button nextButton;
        private readonly Button completeButton;

        internal UrdfExportTutorialForm(
            IUrdfExportTutorialStateStore stateStore,
            UrdfExportTutorialProgress progress,
            bool trackProgress)
        {
            if (stateStore == null)
            {
                throw new ArgumentNullException("stateStore");
            }
            if (progress == null)
            {
                throw new ArgumentNullException("progress");
            }

            chinese = ChineseUiText.ShouldUseChinese();
            this.stateStore = stateStore;
            this.trackProgress = trackProgress;
            steps = UrdfExportTutorialContent.Build(chinese);

            Text = chinese ? "SW2URDF 完整导出教程" : "SW2URDF Complete Export Tutorial";
            StartPosition = FormStartPosition.CenterScreen;
            ShowIcon = false;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(980, 700);
            MinimumSize = new Size(780, 560);
            if (chinese)
            {
                Font = new Font("Microsoft YaHei UI", Font.Size, Font.Style);
            }

            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 66,
                Padding = new Padding(16, 12, 16, 8)
            };
            Label titleLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 26,
                Font = new Font(Font, FontStyle.Bold),
                Text = chinese ? "在真实导出界面旁完成以下步骤" : "Complete these steps alongside the real exporter"
            };
            progressLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Bottom,
                Height = 22
            };
            header.Controls.Add(progressLabel);
            header.Controls.Add(titleLabel);

            Panel footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                Padding = new Padding(12, 12, 12, 10)
            };
            previousButton = CreateButton(chinese ? "上一步" : "Previous", 90);
            nextButton = CreateButton(chinese ? "下一步" : "Next", 90);
            completeButton = CreateButton(chinese ? "完成教程" : "Complete", 100);
            Button dismissButton = CreateButton(chinese ? "不再提示" : "Do not remind", 108);
            Button closeButton = CreateButton(chinese ? "暂时关闭" : "Close for now", 100);

            previousButton.Click += (sender, e) => SelectStep(stepList.SelectedIndex - 1);
            nextButton.Click += (sender, e) => SelectStep(stepList.SelectedIndex + 1);
            completeButton.Click += CompleteButtonClick;
            dismissButton.Click += DismissButtonClick;
            closeButton.Click += (sender, e) => Close();

            FlowLayoutPanel rightButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };
            rightButtons.Controls.Add(previousButton);
            rightButtons.Controls.Add(nextButton);
            rightButtons.Controls.Add(completeButton);
            rightButtons.Controls.Add(closeButton);
            footer.Controls.Add(rightButtons);

            FlowLayoutPanel leftButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };
            leftButtons.Controls.Add(dismissButton);
            footer.Controls.Add(leftButtons);

            SplitContainer body = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Size = new Size(940, 570),
                FixedPanel = FixedPanel.Panel1,
                IsSplitterFixed = false,
                SplitterDistance = 270,
                SplitterWidth = 6,
                Panel1MinSize = 220,
                Panel2MinSize = 420
            };
            body.Panel1.Padding = new Padding(12, 4, 6, 8);
            body.Panel2.Padding = new Padding(6, 4, 12, 8);

            stepList = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                Font = new Font(Font.FontFamily, Font.Size + 0.5F, FontStyle.Regular)
            };
            foreach (UrdfExportTutorialStep step in steps)
            {
                stepList.Items.Add(step);
            }
            stepList.SelectedIndexChanged += StepListSelectedIndexChanged;

            contentBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                DetectUrls = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
                Font = new Font(Font.FontFamily, Font.Size + 0.5F, FontStyle.Regular),
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            body.Panel1.Controls.Add(stepList);
            body.Panel2.Controls.Add(contentBox);
            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);

            int initialIndex = Math.Min(steps.Count - 1, Math.Max(0, progress.StepIndex));
            stepList.SelectedIndex = initialIndex;
        }

        internal int CurrentStepIndex
        {
            get { return stepList.SelectedIndex; }
        }

        internal int StepCount
        {
            get { return steps.Count; }
        }

        private static Button CreateButton(string text, int width)
        {
            return new Button
            {
                AutoSize = false,
                Size = new Size(width, 30),
                Margin = new Padding(4, 0, 0, 0),
                Text = text,
                UseVisualStyleBackColor = true
            };
        }

        private void StepListSelectedIndexChanged(object sender, EventArgs e)
        {
            int index = stepList.SelectedIndex;
            if (index < 0 || index >= steps.Count)
            {
                return;
            }

            contentBox.Text = steps[index].BuildDisplayText(chinese);
            contentBox.SelectionStart = 0;
            contentBox.ScrollToCaret();
            progressLabel.Text = String.Format(
                chinese ? "进度：第 {0}/{1} 步。关闭后可从工具菜单继续。" :
                    "Progress: step {0} of {1}. Reopen it later from the Tools menu.",
                index + 1,
                steps.Count);
            previousButton.Enabled = index > 0;
            nextButton.Enabled = index < steps.Count - 1;
            completeButton.Enabled = index == steps.Count - 1;

            if (trackProgress)
            {
                stateStore.Save(new UrdfExportTutorialProgress(
                    UrdfExportTutorialStatus.InProgress,
                    index));
            }
        }

        private void SelectStep(int index)
        {
            if (index >= 0 && index < steps.Count)
            {
                stepList.SelectedIndex = index;
            }
        }

        private void CompleteButtonClick(object sender, EventArgs e)
        {
            stateStore.Save(new UrdfExportTutorialProgress(
                UrdfExportTutorialStatus.Completed,
                Math.Max(0, steps.Count - 1)));
            MessageBox.Show(
                chinese ?
                    "教程已完成。以后仍可从 SolidWorks 的工具菜单重新打开。" :
                    "Tutorial completed. You can reopen it from the SolidWorks Tools menu.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Close();
        }

        private void DismissButtonClick(object sender, EventArgs e)
        {
            stateStore.Save(new UrdfExportTutorialProgress(
                UrdfExportTutorialStatus.Dismissed,
                Math.Max(0, stepList.SelectedIndex)));
            Close();
        }
    }
}
