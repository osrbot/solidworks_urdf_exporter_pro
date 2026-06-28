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
        internal const string VersionMaintainer = "kitso666 <kitso@osrbot.com>";

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

        private readonly TextBox guideTextBox;
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
            Size = new Size(760, 620);
            MinimumSize = new Size(640, 480);
            if (chinese)
            {
                Font = new Font("Microsoft YaHei UI", Font.Size, Font.Style);
            }

            guideTextBox = new TextBox
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(12, 12),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Size = new Size(ClientSize.Width - 24, ClientSize.Height - 92),
                Text = BuildGuideText(chinese)
            };

            projectLinkLabel = new LinkLabel
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
                Location = new Point(12, ClientSize.Height - 68),
                Size = new Size(ClientSize.Width - 24, 22),
                Text = ProjectUrl
            };
            projectLinkLabel.LinkClicked += ProjectLinkLabelLinkClicked;

            Label maintainerLabel = new Label
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
                Location = new Point(12, ClientSize.Height - 42),
                Size = new Size(ClientSize.Width - 128, 22),
                Text = (chinese ? "此版本维护作者: " : "Maintainer for this version: ") +
                    VersionMaintainer
            };

            closeButton = new Button
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(ClientSize.Width - 96, ClientSize.Height - 38),
                Size = new Size(84, 26),
                Text = chinese ? "关闭" : "Close",
                UseVisualStyleBackColor = true
            };
            closeButton.Click += (sender, e) => Close();

            Controls.Add(guideTextBox);
            Controls.Add(projectLinkLabel);
            Controls.Add(maintainerLabel);
            Controls.Add(closeButton);
        }

        internal static string BuildGuideText(bool chinese)
        {
            return chinese ? BuildChineseGuideText() : BuildEnglishGuideText();
        }

        private static string BuildChineseGuideText()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("碰撞策略怎么选");
            builder.AppendLine();
            builder.AppendLine("- 推荐默认: ComponentBoxes。复杂装配体优先用组件包围盒集合，通常比完整 STL 更稳、更快。");
            builder.AppendLine("- 盒状机身、电池盒、支架: BoxPrimitive 或 Primitive。");
            builder.AppendLine("- 轮子、圆柱外壳、雷达桶: CylinderPrimitive。");
            builder.AppendLine("- 球形脚轮或球壳: SpherePrimitive。");
            builder.AppendLine("- 复杂但需要单个近似体: ConvexHull。");
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
            builder.AppendLine();
            builder.AppendLine("最小导出流程");
            builder.AppendLine();
            builder.AppendLine("1. 在 Link 页面选择材质名称和颜色。材质名称只是 URDF 命名模板，不等于物理材质库。");
            builder.AppendLine("2. 网格格式优先选 STL；需要小文件时调低 STL 精简比例。");
            builder.AppendLine("3. 先按上面的碰撞策略建议给每个 link 选策略。");
            builder.AppendLine("4. 导出后查看 config/export_report.md 和 config/mesh_manifest.csv，确认 fallback、mesh 大小和有效碰撞策略。");
            return builder.ToString();
        }

        private static string BuildEnglishGuideText()
        {
            StringBuilder builder = new StringBuilder();
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
            builder.AppendLine();
            builder.AppendLine("Minimal export workflow");
            builder.AppendLine();
            builder.AppendLine("1. Pick a material name and color on the Link page. The name is a URDF naming template, not a physical material library.");
            builder.AppendLine("2. Prefer STL mesh format; reduce the STL ratio when file size matters.");
            builder.AppendLine("3. Select a collision strategy per link using the guidance above.");
            builder.AppendLine("4. After export, check config/export_report.md and config/mesh_manifest.csv for fallbacks, mesh size, and effective collision strategy.");
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
