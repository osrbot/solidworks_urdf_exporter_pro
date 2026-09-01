using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    internal sealed class ExportDiagnosticsDialog : Form
    {
        private static readonly Regex ValidationError = new Regex(
            "\\b(?:ERROR|WARNING)\\s+(?<code>[A-Z][A-Z0-9_]+)\\s+(?<path>\\$[^:]+):",
            RegexOptions.CultureInvariant);

        private readonly string report;
        private readonly string logPath;

        private ExportDiagnosticsDialog(string title, string report, string logPath)
        {
            this.report = report ?? string.Empty;
            this.logPath = logPath ?? string.Empty;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(760, 520);
            MinimumSize = new Size(620, 420);
            BackColor = ModernWinFormsTheme.Background;

            TableLayoutPanel shell = new TableLayoutPanel
            {
                BackColor = ModernWinFormsTheme.Background,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(20),
                RowCount = 3
            };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));

            Label heading = ModernWinFormsTheme.CreateTextLabel(
                title,
                14F,
                FontStyle.Bold);
            heading.Name = "diagnosticsHeading";
            heading.Dock = DockStyle.Fill;
            heading.TextAlign = ContentAlignment.MiddleLeft;

            TextBox details = new TextBox
            {
                Name = "diagnosticsTextBox",
                AcceptsReturn = true,
                AcceptsTab = true,
                BackColor = ModernWinFormsTheme.Surface,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Text = this.report,
                WordWrap = false
            };
            TableLayoutPanel detailsCard = ModernWinFormsTheme.CreateCard(
                "diagnosticsDetailsCard");
            detailsCard.AutoSize = false;
            detailsCard.Dock = DockStyle.Fill;
            detailsCard.Margin = new Padding(0);
            detailsCard.Padding = new Padding(14);
            detailsCard.RowCount = 1;
            detailsCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            detailsCard.Controls.Add(details, 0, 0);

            Button copyButton = new Button
            {
                Name = "diagnosticsCopyButton",
                AutoSize = false,
                Size = new Size(112, 34),
                Text = ChineseUiText.Translate("Copy all", "复制全部")
            };
            copyButton.Click += CopyButtonClick;
            Button openLogButton = new Button
            {
                Name = "diagnosticsOpenLogButton",
                AutoSize = false,
                Enabled = HasLogTarget(this.logPath),
                Size = new Size(112, 34),
                Text = ChineseUiText.Translate("Open log", "打开日志")
            };
            openLogButton.Click += OpenLogButtonClick;
            Button closeButton = new Button
            {
                Name = "diagnosticsCloseButton",
                Anchor = AnchorStyles.Right,
                AutoSize = false,
                DialogResult = DialogResult.Cancel,
                Size = new Size(96, 34),
                Text = ChineseUiText.Translate("Close", "关闭")
            };

            FlowLayoutPanel commands = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Left,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                Padding = new Padding(0, 10, 0, 0),
                WrapContents = false
            };
            commands.Controls.Add(copyButton);
            commands.Controls.Add(openLogButton);
            TableLayoutPanel footer = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                RowCount = 1
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            footer.Controls.Add(commands, 0, 0);
            footer.Controls.Add(closeButton, 1, 0);

            shell.Controls.Add(heading, 0, 0);
            shell.Controls.Add(detailsCard, 0, 1);
            shell.Controls.Add(footer, 0, 2);
            Controls.Add(shell);
            CancelButton = closeButton;
            ModernWinFormsTheme.Apply(this);
            ModernWinFormsTheme.StylePrimaryButton(copyButton);
            ModernWinFormsTheme.StyleSecondaryButton(openLogButton);
            ModernWinFormsTheme.StyleSecondaryButton(closeButton);
            details.BorderStyle = BorderStyle.None;
        }

        internal static void ShowValidation(
            IWin32Window owner,
            string title,
            IEnumerable<ExportTargetValidationFinding> findings,
            string logPath)
        {
            string report = FormatValidationFindings(
                findings,
                ChineseUiText.ShouldUseChinese(),
                logPath);
            ShowReport(owner, title, report, logPath);
        }

        internal static void ShowFailure(
            IWin32Window owner,
            string title,
            string rawError,
            string logPath)
        {
            string report = FormatFailure(rawError, ChineseUiText.ShouldUseChinese(), logPath);
            ShowReport(owner, title, report, logPath);
        }

        internal static void ShowReport(
            IWin32Window owner,
            string title,
            string report,
            string logPath)
        {
            using (ExportDiagnosticsDialog dialog = new ExportDiagnosticsDialog(
                title,
                report,
                logPath))
            {
                dialog.ShowDialog(owner);
            }
        }

        internal static string FormatValidationFindings(
            IEnumerable<ExportTargetValidationFinding> findings,
            bool chinese,
            string logPath)
        {
            IList<ExportTargetValidationFinding> items = (findings ??
                Enumerable.Empty<ExportTargetValidationFinding>()).ToList();
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(chinese
                ? "请修正以下输出配置。错误码、字段名和英文原文可直接用于检索。"
                : "Correct the following output settings. Stable codes and field names are included for search.");
            builder.AppendLine();
            foreach (ExportTargetValidationFinding finding in items)
            {
                builder.Append('[').Append(finding.Code).Append("] ")
                    .AppendLine(finding.Field);
                builder.Append(chinese ? "问题: " : "Problem: ")
                    .AppendLine(finding.Message);
                builder.Append(chinese ? "处理: " : "Action: ")
                    .AppendLine(GetGuidance(finding.Code, chinese));
                builder.AppendLine();
            }
            AppendLogPath(builder, logPath, chinese);
            return builder.ToString().TrimEnd();
        }

        internal static string FormatFailure(string rawError, bool chinese, string logPath)
        {
            string source = string.IsNullOrWhiteSpace(rawError)
                ? "Unknown export failure."
                : rawError.Trim();
            Match match = ValidationError.Match(source);
            string code = match.Success ? match.Groups["code"].Value : "EXPORT_FAILED";
            string path = match.Success ? match.Groups["path"].Value : string.Empty;
            StringBuilder builder = new StringBuilder();
            builder.Append(chinese ? "错误码: " : "Code: ").AppendLine(code);
            if (!string.IsNullOrWhiteSpace(path))
            {
                builder.Append(chinese ? "数据位置: " : "Data path: ").AppendLine(path);
            }
            builder.AppendLine();
            builder.AppendLine(chinese ? "原始错误:" : "Raw error:");
            builder.AppendLine(source);
            builder.AppendLine();
            builder.AppendLine(chinese ? "建议处理:" : "Suggested action:");
            builder.AppendLine(GetGuidance(code, chinese));
            builder.AppendLine();
            AppendLogPath(builder, logPath, chinese);
            return builder.ToString().TrimEnd();
        }

        private static string GetGuidance(string code, bool chinese)
        {
            switch (code ?? string.Empty)
            {
                case "V2_BUNDLE_REQUIRED":
                    return chinese
                        ? "内部模型包配置异常；重新打开导出器后再试。"
                        : "The internal model package configuration is invalid; reopen the exporter and try again.";
                case "TARGET_REQUIRED":
                    return chinese
                        ? "返回“模型与导出”，至少勾选 ROS 1、ROS 2、USD 或 MuJoCo MJCF 中的一项。"
                        : "Return to Model and export and select at least one of ROS 1, ROS 2, USD, or MuJoCo MJCF.";
                case "PACKAGE_VERSION":
                    return chinese ? "填写完整语义版本，例如 0.1.0。" : "Enter an exact semantic version such as 0.1.0.";
                case "PACKAGE_DESCRIPTION":
                    return chinese ? "填写机器人功能包说明。" : "Enter a robot package description.";
                case "MODEL_LICENSE":
                    return chinese ? "填写模型的 SPDX 许可证；未确认时保留 NOASSERTION 并在发布前审核。" : "Enter the model SPDX license; keep NOASSERTION until it is reviewed.";
                case "MAINTAINER_NAME":
                case "MAINTAINER_EMAIL":
                case "MAINTAINER_EMAIL_FORMAT":
                    return chinese ? "填写可联系的维护者姓名与有效邮箱。" : "Enter a contactable maintainer and valid email address.";
                case "ROS2_GAZEBO_PAIR":
                    return chinese ? "选择界面提供的 ROS 2 / Gazebo 兼容组合。" : "Choose one of the offered ROS 2 / Gazebo compatibility pairs.";
                case "ROS2_CONTROL_PROFILE":
                    return chinese ? "启用 ROS 2 并选择存在的 ros2_control JSON 文件，或清空该字段。" : "Enable ROS 2 and select an existing ros2_control JSON file, or clear the field.";
                case "JOINT_LIMIT":
                    return chinese ? "在 Joint 属性 > 约束与安全 中填写 effort 和 velocity；若关节确实无限连续转动，再明确选择 continuous。" : "Set effort and velocity under Joint properties > Limits and safety, or explicitly choose continuous when appropriate.";
                case "UI_JOINT_CONFIG":
                    return chinese ? "返回 Joint 属性页，按列表逐项修正后再继续。" : "Return to Joint properties and correct each listed item.";
                case "UI_LINK_CONFIG":
                    return chinese ? "返回 Link 属性页，补齐列表中 Link 的必填绑定和参数。" : "Return to Link properties and complete the listed bindings and fields.";
                default:
                    return chinese ? "复制完整错误并结合日志检索；错误码和数据路径应一并保留。" : "Copy the complete error with its code and data path, then inspect the log.";
            }
        }

        private static void AppendLogPath(StringBuilder builder, string path, bool chinese)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            builder.Append(chinese ? "日志: " : "Log: ").AppendLine(path);
        }

        private static bool HasLogTarget(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
            return File.Exists(path) || Directory.Exists(path) ||
                Directory.Exists(Path.GetDirectoryName(path) ?? string.Empty);
        }

        private void CopyButtonClick(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(report))
            {
                Clipboard.SetText(report);
            }
        }

        private void OpenLogButtonClick(object sender, EventArgs e)
        {
            string target = File.Exists(logPath) || Directory.Exists(logPath)
                ? logPath
                : Path.GetDirectoryName(logPath);
            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
    }
}
