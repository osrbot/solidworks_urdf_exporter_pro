using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    internal sealed class UsageGuideForm : Form
    {
        internal const string ProjectUrl =
            "https://github.com/osrbot/solidworks_urdf_exporter_pro";

        internal static readonly string[] CommonMaterialNames = new string[]
        {
            "black",
            "white",
            "gray",
            "dark_gray",
            "red",
            "green",
            "blue",
            "yellow",
            "orange",
            "silver",
            "aluminum",
            "steel",
            "plastic_black",
            "rubber_black",
            "transparent_blue"
        };

        private readonly RichTextBox guideTextBox;
        private readonly LinkLabel projectLinkLabel;
        private readonly Button closeButton;

        public UsageGuideForm()
        {
            bool chinese = ChineseUiText.ShouldUseChinese();
            Text = chinese ? "SW2URDF 使用说明" : "SW2URDF Guide";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(840, 680);
            MinimumSize = new Size(680, 520);
            BackColor = ModernWinFormsTheme.Background;

            TableLayoutPanel shell = new TableLayoutPanel
            {
                Name = "usageGuideShell",
                BackColor = ModernWinFormsTheme.Background,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0),
                RowCount = 3
            };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));

            TableLayoutPanel header = new TableLayoutPanel
            {
                Name = "usageGuideHeader",
                BackColor = ModernWinFormsTheme.Surface,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(24, 14, 24, 12),
                RowCount = 2
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            header.Paint += ModernWinFormsTheme.DrawBottomBorder;
            Label titleLabel = ModernWinFormsTheme.CreateTextLabel(
                chinese ? "使用说明" : "Usage guide",
                15F,
                FontStyle.Bold);
            Label subtitleLabel = ModernWinFormsTheme.CreateTextLabel(
                chinese
                    ? "工作流、输出目标、惯性与碰撞策略参考"
                    : "Workflow, output targets, inertia, and collision strategy reference",
                9F,
                FontStyle.Regular);
            subtitleLabel.ForeColor = ModernWinFormsTheme.MutedText;
            header.Controls.Add(titleLabel, 0, 0);
            header.Controls.Add(subtitleLabel, 0, 1);

            guideTextBox = new RichTextBox
            {
                Name = "usageGuideTextBox",
                BackColor = ModernWinFormsTheme.Surface,
                BorderStyle = BorderStyle.None,
                DetectUrls = false,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                TabStop = false,
                WordWrap = true
            };

            TableLayoutPanel guideCard = ModernWinFormsTheme.CreateCard("usageGuideCard");
            guideCard.AutoSize = false;
            guideCard.Dock = DockStyle.Fill;
            guideCard.Margin = new Padding(24, 18, 24, 18);
            guideCard.Padding = new Padding(18);
            guideCard.RowCount = 1;
            guideCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            guideCard.Controls.Add(guideTextBox, 0, 0);

            projectLinkLabel = new LinkLabel
            {
                Name = "usageGuideProjectLink",
                AutoSize = true,
                Dock = DockStyle.Fill,
                LinkColor = ModernWinFormsTheme.Accent,
                Margin = new Padding(0),
                TabStop = true,
                Text = ProjectUrl
            };
            projectLinkLabel.LinkClicked += ProjectLinkLabelLinkClicked;

            closeButton = new Button
            {
                Name = "usageGuideCloseButton",
                Anchor = AnchorStyles.Right,
                DialogResult = DialogResult.Cancel,
                Margin = new Padding(16, 0, 0, 0),
                Size = new Size(96, 34),
                Text = chinese ? "关闭" : "Close",
                UseVisualStyleBackColor = false
            };
            closeButton.Click += (sender, e) => Close();

            TableLayoutPanel footer = new TableLayoutPanel
            {
                Name = "usageGuideFooter",
                BackColor = ModernWinFormsTheme.Surface,
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(24, 10, 24, 10),
                RowCount = 1
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            footer.Paint += ModernWinFormsTheme.DrawTopBorder;
            footer.Controls.Add(projectLinkLabel, 0, 0);
            footer.Controls.Add(closeButton, 1, 0);

            shell.Controls.Add(header, 0, 0);
            shell.Controls.Add(guideCard, 0, 1);
            shell.Controls.Add(footer, 0, 2);
            Controls.Add(shell);
            CancelButton = closeButton;
            ModernWinFormsTheme.Apply(this);
            ModernWinFormsTheme.StyleSecondaryButton(closeButton);
            guideTextBox.BorderStyle = BorderStyle.None;
            ApplyGuideFormatting(chinese);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            guideTextBox.Select(0, 0);
            ActiveControl = closeButton;
            closeButton.Select();
        }

        private void ApplyGuideFormatting(bool chinese)
        {
            string text = BuildGuideText(chinese);
            guideTextBox.Text = text;
            ModernWinFormsTheme.SetFont(guideTextBox, 9.5F, FontStyle.Regular);
            guideTextBox.ForeColor = ModernWinFormsTheme.Text;

            using (Font headingFont = new Font(
                guideTextBox.Font.FontFamily,
                11F,
                FontStyle.Bold,
                GraphicsUnit.Point))
            using (Font leadFont = new Font(
                guideTextBox.Font.FontFamily,
                9.5F,
                FontStyle.Bold,
                GraphicsUnit.Point))
            {
                int lineStart = 0;
                while (lineStart < text.Length)
                {
                    int newline = text.IndexOf('\n', lineStart);
                    int lineEnd = newline < 0 ? text.Length : newline;
                    int lineLength = lineEnd - lineStart;
                    if (lineLength > 0 && text[lineEnd - 1] == '\r')
                    {
                        lineLength--;
                    }
                    string line = text.Substring(lineStart, lineLength);

                    if (IsGuideHeading(line, chinese))
                    {
                        guideTextBox.Select(lineStart, lineLength);
                        guideTextBox.SelectionFont = headingFont;
                        guideTextBox.SelectionColor = ModernWinFormsTheme.Accent;
                        guideTextBox.SelectionIndent = 0;
                        guideTextBox.SelectionHangingIndent = 0;
                    }
                    else if (line.StartsWith("- ", StringComparison.Ordinal))
                    {
                        guideTextBox.Select(lineStart, lineLength);
                        guideTextBox.SelectionIndent = 16;
                        guideTextBox.SelectionHangingIndent = -12;
                        int colon = line.IndexOf(':');
                        if (colon < 0)
                        {
                            colon = line.IndexOf('\uff1a');
                        }
                        if (colon > 2)
                        {
                            guideTextBox.Select(lineStart + 2, colon - 1);
                            guideTextBox.SelectionFont = leadFont;
                        }
                    }
                    else if (IsNumberedGuideLine(line))
                    {
                        guideTextBox.Select(lineStart, lineLength);
                        guideTextBox.SelectionIndent = 18;
                        guideTextBox.SelectionHangingIndent = -18;
                    }

                    if (newline < 0)
                    {
                        break;
                    }
                    lineStart = newline + 1;
                }
            }

            guideTextBox.Select(0, 0);
        }

        private static bool IsNumberedGuideLine(string line)
        {
            return line.Length > 2 && Char.IsDigit(line[0]) &&
                line[1] == '.' && line[2] == ' ';
        }

        private static bool IsGuideHeading(string line, bool chinese)
        {
            string[] headings = chinese
                ? new string[]
                {
                    "开始之前",
                    "快速功能索引",
                    "推荐使用流程",
                    "碰撞策略怎么选",
                    "当前实现限制",
                    "常用材质名称"
                }
                : new string[]
                {
                    "Before you start",
                    "Quick feature index",
                    "Recommended workflow",
                    "Choosing a collision strategy",
                    "Current implementation limits",
                    "Common material names"
                };
            return Array.IndexOf(headings, line) >= 0;
        }

        internal static string BuildGuideText(bool chinese)
        {
            return chinese ? BuildChineseGuideText() : BuildEnglishGuideText();
        }

        private static string BuildChineseGuideText()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("开始之前");
            builder.AppendLine();
            builder.AppendLine("\u6bcf\u4e2a Link \u5fc5\u987b\u5728 SolidWorks \u4e2d\u624b\u52a8\u521b\u5efa\u5e76\u9009\u62e9\u81ea\u8eab\u5750\u6807\u7cfb\uff1b\u975e\u6839 Link \u4f7f\u7528\u5176\u5b50 Joint \u5750\u6807\u7cfb\uff0c\u6839 Link \u4f7f\u7528 Origin_global \u6216\u660e\u786e\u9009\u62e9\u7684\u6839\u5750\u6807\u7cfb\u3002\u4fee\u6539 Link \u5750\u6807\u7cfb\u540e\uff0c\u63d2\u4ef6\u4f1a\u91cd\u7b97 COM\u3001\u8d28\u5fc3\u60ef\u6027\u5f20\u91cf\u548c\u76f8\u90bb Joint \u539f\u70b9\u3002");
            builder.AppendLine();
            builder.AppendLine("首次导出时可启动 8 步完整导出教程；教程会伴随真实导出界面，覆盖装配体准备、坐标系、Link 树、Joint、惯性、碰撞网格、ROS1/ROS2 输出和结果校验。之后可从 SolidWorks 的 工具 > URDF 导出教程 随时重开。");
            builder.AppendLine();
            builder.AppendLine("快速功能索引");
            builder.AppendLine();
            builder.AppendLine("- 自动加载 Link 树配置: 如果装配体里存在 SolidWorks 特征 URDF Export Configuration (v2)，插件启动导出时会恢复上次保存的 Link/Joint 树、命名、父子关系和已保存属性。v2 使用组件实例 PID + 参考几何特征 PID，可区分任意装配深度中的 Unicode 或同名坐标系/参考轴。v1.x 名称型配置不会自动迁移，必须重新创建并审核。");
            builder.AppendLine("- 保存配置: 导出或关闭配置页时，插件会提示是否保存变化；保存后配置写回当前装配体文件，而不是单独散落在外部目录。");
            builder.AppendLine("- PID 重连: 加载当前 v2 配置时，会分别用保存的 SolidWorks 组件实例 PID 和特征 PID 重新关联零件与参考几何；显示名称和配置名称只用于界面提示，不参与身份判断。如果对象被删除、替换或另存导致 PID 失效，会列出需要人工检查的 Link。");
            builder.AppendLine("- 编辑 Link 树...: 打开自由画布，可增加子 Link、拖拽调整父子关系、框选并复制/粘贴对称结构。画布在副本上编辑；取消不修改原树，应用后才提交。新增或粘贴的 Link 不复制 SolidWorks 组件绑定，需要回到属性页重新分配组件。");
            builder.AppendLine("- Link 树大纲编辑: 在自由画布中点击 大纲编辑，用 Markdown 标题一次编辑整棵树。# 是根 Link，## 是二级 Link，### 是三级 Link。支持 #base_link 和 # base_link 两种写法；同名旧 Link 保留原 Joint、参数和 CAD 绑定。新增 Link 自动生成 Joint 名称，例如 camera_link 生成 camera_joint，但 Joint 类型保持待选择；回到画布明确选择后才能应用。格式错误不会覆盖画布。");
            builder.AppendLine("- Joint 类型: STEP、导入模型或固定装配请手动选择。尝试从 SolidWorks Mate 识别仅适用于保留正确 Mate 的原生可动装配；0 DOF 不会再被自动当成 fixed。");
            builder.AppendLine("- 导出报告: 导出后查看 config/export_report.md、config/inertial_validation.csv 和 config/mesh_manifest.csv，可快速确认惯性误差、网格大小、fallback 和最终碰撞策略。");
            builder.AppendLine();
            builder.AppendLine("推荐使用流程");
            builder.AppendLine();
            builder.AppendLine("1. 首次导出时，先在 SolidWorks 里建好 Origin_global、各 Joint 坐标系和 Axis。");
            builder.AppendLine("2. 在配置树里整理 Link/Joint 层级，命名尽量保持 ROS 友好: 小写、下划线、无空格。");
            builder.AppendLine("   复杂层级可点 编辑 Link 树... 进入自由画布，再点 大纲编辑，用 #/##/### 批量写出父子层级。");
            builder.AppendLine("3. 在画布为每个新增 Joint 明确选择类型并检查名称；点击应用后，再回属性页配置新增 Link 的组件、坐标系和轴。");
            builder.AppendLine("4. 可用“自动配色”为整棵 Link 树生成稳定层级颜色，再按需手动修改单个 Link 的材质 ID 或 RGBA。");
            builder.AppendLine("5. 选好碰撞策略、材质 ID、网格精简比例后导出。");
            builder.AppendLine("6. 重新打开同一个装配体时，插件会优先加载保存在装配体内的 Link 树配置；不用从头重新建树。");
            builder.AppendLine();
            builder.AppendLine("碰撞策略怎么选");
            builder.AppendLine();
            builder.AppendLine("- 推荐默认: ComponentBoxes。复杂装配体优先用组件包围盒集合，通常比完整 STL 更稳定、更快。");
            builder.AppendLine("- 盒状机身、电池盒、支架: BoxPrimitive 或 Primitive。");
            builder.AppendLine("- 轮子、圆柱外壳、雷达桶: CylinderPrimitive。");
            builder.AppendLine("- 球形脚轮或球壳: SpherePrimitive。");
            builder.AppendLine("- 复杂但只需要单个近似体: ConvexHull。");
            builder.AppendLine("- 只做模型查看或最大兼容: VisualMesh。");
            builder.AppendLine("- 必须保留完整接触细节: AccurateMesh，但文件和碰撞计算成本最高。");
            builder.AppendLine("- primitive 不合适但想减小碰撞 STL: SimplifiedMesh。");
            builder.AppendLine();
            builder.AppendLine("当前实现限制");
            builder.AppendLine();
            builder.AppendLine("- 原生 box/cylinder/sphere/component boxes、凸包和简化碰撞 STL 依赖 STL 导出模式。");
            builder.AppendLine("- 如果策略生成失败，导出器会回退到 VisualMesh，并在 export_report.md 和 mesh_manifest.csv 中记录 effective strategy。");
            builder.AppendLine("- ROS/Gazebo 工作流优先用 STL；3dxml 更偏向带颜色查看，不适合作为严肃碰撞格式。");
            builder.AppendLine();
            builder.AppendLine("常用材质名称");
            builder.AppendLine();
            builder.AppendLine(String.Join(", ", CommonMaterialNames));
            return builder.ToString();
        }

        private static string BuildEnglishGuideText()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Before you start");
            builder.AppendLine();
            builder.AppendLine("The first assembly export can open an eight-step companion tutorial covering assembly preparation, frames, the Link tree, Joints, inertia, collision meshes, ROS1/ROS2 output, and final validation. Reopen it at any time from Tools > URDF Export Tutorial.");
            builder.AppendLine();
            builder.AppendLine("Quick feature index");
            builder.AppendLine();
            builder.AppendLine("- Automatic Link tree loading: when the assembly contains the SolidWorks feature URDF Export Configuration (v2), the exporter restores the saved Link/Joint tree, names, parent-child structure, and saved properties at startup. Version 2 binds each reference with a component-instance PID plus a reference-feature PID, so Unicode or duplicate coordinate-system and axis names remain distinct at any assembly depth. Name-based v1.x configurations are not migrated automatically; recreate and review them explicitly.");
            builder.AppendLine("- Save configuration: when the configuration changes, the exporter prompts to save it back into the current assembly document instead of scattering separate sidecar files.");
            builder.AppendLine("- PID reconnect: when a current v2 configuration is loaded, saved SolidWorks component-instance and feature PIDs reconnect components and reference geometry independently. Display and configuration names are UI metadata, not identity. If an object was deleted, replaced, or saved as a new file, the exporter reports the Links that need inspection.");
            builder.AppendLine("- Edit Link Tree...: opens a free canvas for adding child Links, drag-to-reparent, box selection, and structure copy/paste. The canvas edits a working copy: Cancel discards changes and Apply commits them. New or pasted Links do not inherit SolidWorks component bindings and must be assigned on the property page.");
            builder.AppendLine("- Link tree outline editing: click Outline Edit in the canvas to edit the complete hierarchy with Markdown headings. # is the root Link, ## is a second-level Link, and ### is a third-level Link. Existing Links matched by name keep Joint data, reusable properties, and CAD bindings. New Links receive generated Joint names such as camera_joint, but their types remain unconfigured until explicitly selected on the canvas. Invalid text never replaces the canvas document.");
            builder.AppendLine("- Joint types: choose explicit types for STEP, imported, or fixed assemblies. Try SolidWorks Mate detection only for a native movable assembly with correct Mates; zero remaining DOFs is no longer treated as fixed.");
            builder.AppendLine("- Link frames and inertia: create each Link frame as a SolidWorks coordinate-system feature, then select it on the Link properties page. Non-root Links use their child-Joint frame; the root uses Origin_global or another explicit root frame. Changing the frame recomputes COM, the COM inertia tensor, and adjacent Joint origins without a parallel-axis shift.");
            builder.AppendLine("- Export diagnostics: after export, check config/export_report.md, config/inertial_validation.csv, and config/mesh_manifest.csv for inertia error, mesh size, fallback, and effective collision strategy.");
            builder.AppendLine();
            builder.AppendLine("Recommended workflow");
            builder.AppendLine();
            builder.AppendLine("1. For a first export, create Origin_global, joint coordinate systems, and axes in SolidWorks.");
            builder.AppendLine("2. Arrange the Link/Joint hierarchy in the configuration tree. Keep names ROS-friendly: lowercase, underscores, no spaces.");
            builder.AppendLine("   For a complex hierarchy, open Edit Link Tree..., then use Outline Edit to write parent-child levels with #/##/### headings.");
            builder.AppendLine("3. Explicitly choose every new Joint type and review its name on the canvas. Apply the tree, then assign components, coordinate systems, and axes for new Links on the property page.");
            builder.AppendLine("4. Optionally use Auto Links for stable level-based colors, then override any Link material ID or RGBA manually.");
            builder.AppendLine("5. Pick collision strategy, material ID, color, and STL reduction ratio, then export.");
            builder.AppendLine("6. When the same assembly is reopened, the saved Link tree configuration is loaded from the assembly so the tree does not have to be rebuilt from scratch.");
            builder.AppendLine();
            builder.AppendLine("Choosing a collision strategy");
            builder.AppendLine();
            builder.AppendLine("- Recommended default: ComponentBoxes. Use it for complex assemblies when stable and fast simulation matters.");
            builder.AppendLine("- Box-like chassis, battery boxes, brackets: BoxPrimitive or Primitive.");
            builder.AppendLine("- Wheels, cylinder housings, lidar barrels: CylinderPrimitive.");
            builder.AppendLine("- Ball casters or spherical shells: SpherePrimitive.");
            builder.AppendLine("- Complex shape with a single approximate body: ConvexHull.");
            builder.AppendLine("- Viewer-only export or maximum compatibility: VisualMesh.");
            builder.AppendLine("- Full contact detail: AccurateMesh, with the highest file and collision cost.");
            builder.AppendLine("- Use SimplifiedMesh when primitives do not fit but collision STL should stay smaller.");
            builder.AppendLine();
            builder.AppendLine("Current implementation limits");
            builder.AppendLine();
            builder.AppendLine("- Native box/cylinder/sphere/component boxes, convex hull, and simplified collision STL require STL export mode.");
            builder.AppendLine("- If a strategy fails, the exporter falls back to VisualMesh and records the effective strategy in export_report.md and mesh_manifest.csv.");
            builder.AppendLine("- Prefer STL for ROS/Gazebo workflows; 3dxml is mostly useful for colored viewing, not serious collision geometry.");
            builder.AppendLine();
            builder.AppendLine("Common material names");
            builder.AppendLine();
            builder.AppendLine(String.Join(", ", CommonMaterialNames));
            return builder.ToString();
        }

        private static void ProjectLinkLabelLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });
            }
            catch
            {
                Clipboard.SetText(ProjectUrl);
                MessageBox.Show(ProjectUrl, "Project URL");
            }
        }
    }
}
