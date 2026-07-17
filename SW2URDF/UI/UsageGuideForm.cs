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
            builder.AppendLine("快速功能索引");
            builder.AppendLine();
            builder.AppendLine("- 自动加载 Link 树配置: 如果装配体里存在 SolidWorks 特征 URDF Export Configuration (v1.4)，插件启动导出时会恢复上次保存的 Link/Joint 树、命名、父子关系和已保存属性。");
            builder.AppendLine("- 保存配置: 导出或关闭配置页时，插件会提示是否保存变化；保存后配置写回当前装配体文件，而不是单独散落在外部目录。");
            builder.AppendLine("- 组件重连: 加载旧配置时会用保存的 SolidWorks component PID 重新关联零件；如果某些零件被删除、替换或另存导致 PID 失效，会弹窗列出需要人工检查的 link。");
            builder.AppendLine("- Load Configuration...: 从 CSV 导入旧 URDF 配置，并通过合并窗口选择是否采用 CSV 里的质量/惯性、视觉/碰撞、Joint 运动学和其他 Joint 参数。适合复用旧项目参数，不等同于自动加载装配体内置配置。");
            builder.AppendLine("- 编辑 Link 树...: 打开自由画布，可增加子 Link、拖拽调整父子关系、框选并复制/粘贴对称结构。画布在副本上编辑；取消不修改原树，应用后才提交。新增或粘贴的 Link 不复制 SolidWorks 组件绑定，需要回到属性页重新分配组件。");
            builder.AppendLine("- 导出报告: 导出后查看 config/export_report.md、config/inertial_validation.csv 和 config/mesh_manifest.csv，可快速确认惯性误差、网格大小、fallback 和最终碰撞策略。");
            builder.AppendLine();
            builder.AppendLine("推荐使用流程");
            builder.AppendLine();
            builder.AppendLine("1. 首次导出时，先在 SolidWorks 里建好 Origin_global、各 Joint 坐标系和 Axis。");
            builder.AppendLine("2. 在配置树里整理 Link/Joint 层级，命名尽量保持 ROS 友好: 小写、下划线、无空格。");
            builder.AppendLine("   复杂层级可点 编辑 Link 树... 进入自由画布；结构确认后点应用，再回属性页配置组件、坐标系和轴。");
            builder.AppendLine("3. 需要复用旧项目参数时，先点 Load Configuration... 导入 CSV，再在合并窗口选择保留哪些旧值。");
            builder.AppendLine("4. 选好碰撞策略、材质名、网格精简比例后导出。");
            builder.AppendLine("5. 重新打开同一个装配体时，插件会优先加载保存在装配体内的 Link 树配置；不用从头重新建树。");
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
            builder.AppendLine("Quick feature index");
            builder.AppendLine();
            builder.AppendLine("- Automatic Link tree loading: when the assembly contains the SolidWorks feature URDF Export Configuration (v1.4), the exporter restores the saved Link/Joint tree, names, parent-child structure, and saved properties at startup.");
            builder.AppendLine("- Save configuration: when the configuration changes, the exporter prompts to save it back into the current assembly document instead of scattering separate sidecar files.");
            builder.AppendLine("- Component reconnect: saved SolidWorks component PIDs are resolved when an old tree is loaded. If parts were deleted, replaced, or saved as new files, the exporter reports the links that need inspection.");
            builder.AppendLine("- Load Configuration...: imports values from a CSV export and opens a merge window. You can choose whether CSV values should override mass/inertia, visual/collision, joint kinematics, and other joint properties. This is for reusing old project data; it is separate from automatic assembly configuration loading.");
            builder.AppendLine("- Edit Link Tree...: opens a free canvas for adding child Links, drag-to-reparent, box selection, and structure copy/paste. The canvas edits a working copy: Cancel discards changes and Apply commits them. New or pasted Links do not inherit SolidWorks component bindings and must be assigned on the property page.");
            builder.AppendLine("- Export diagnostics: after export, check config/export_report.md, config/inertial_validation.csv, and config/mesh_manifest.csv for inertia error, mesh size, fallback, and effective collision strategy.");
            builder.AppendLine();
            builder.AppendLine("Recommended workflow");
            builder.AppendLine();
            builder.AppendLine("1. For a first export, create Origin_global, joint coordinate systems, and axes in SolidWorks.");
            builder.AppendLine("2. Arrange the Link/Joint hierarchy in the configuration tree. Keep names ROS-friendly: lowercase, underscores, no spaces.");
            builder.AppendLine("   For a complex hierarchy, use Edit Link Tree..., apply the structure, then return to the property page to assign components, coordinate systems, and axes.");
            builder.AppendLine("3. To reuse old project values, click Load Configuration..., import a CSV, and choose which loaded values to keep in the merge window.");
            builder.AppendLine("4. Pick collision strategy, material name, color, and STL reduction ratio, then export.");
            builder.AppendLine("5. When the same assembly is reopened, the saved Link tree configuration is loaded from the assembly so the tree does not have to be rebuilt from scratch.");
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
