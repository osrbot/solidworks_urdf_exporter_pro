using System;
using System.Collections.Generic;
using System.Text;

namespace SW2URDF.UI
{
    internal sealed class UrdfExportTutorialStep
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string Goal { get; private set; }
        public string Instructions { get; private set; }
        public string Verification { get; private set; }

        public UrdfExportTutorialStep(
            string id,
            string title,
            string goal,
            string instructions,
            string verification)
        {
            Id = id;
            Title = title;
            Goal = goal;
            Instructions = instructions;
            Verification = verification;
        }

        public override string ToString()
        {
            return Title;
        }

        internal string BuildDisplayText(bool chinese)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(Title);
            builder.AppendLine(new String('=', Math.Min(48, Math.Max(8, Title.Length))));
            builder.AppendLine();
            builder.AppendLine(chinese ? "目标" : "Goal");
            builder.AppendLine(Goal);
            builder.AppendLine();
            builder.AppendLine(chinese ? "操作" : "Actions");
            builder.AppendLine(Instructions);
            builder.AppendLine();
            builder.AppendLine(chinese ? "通过标准" : "Pass criteria");
            builder.AppendLine(Verification);
            return builder.ToString();
        }
    }

    internal static class UrdfExportTutorialContent
    {
        public static IList<UrdfExportTutorialStep> Build(bool chinese)
        {
            return chinese ? BuildChinese() : BuildEnglish();
        }

        private static IList<UrdfExportTutorialStep> BuildChinese()
        {
            return new List<UrdfExportTutorialStep>
            {
                new UrdfExportTutorialStep(
                    "prepare",
                    "1. 准备装配体和质量属性",
                    "让 SolidWorks 装配体处于可重复计算、可安全导出的状态。教程只提供检查清单，不会自动修改模型。",
                    "1. 建议先另存一个用于 URDF 的装配体副本。\r\n" +
                    "2. 解析所有需要导出的零件，抑制与机器人无关的工装、环境和重复外观件。\r\n" +
                    "3. 为实体指定真实材料或密度；无密度的实体无法产生可信的质量和惯性。\r\n" +
                    "4. 在 SolidWorks 的质量属性窗口检查总质量、重心和惯性张量，然后执行完全重建并保存。\r\n" +
                    "5. 一个刚体只应归属一个 Link，避免同一实体被两个 Link 重复计入质量。",
                    "装配体没有未解析组件；质量不是 0；重建没有阻断性错误；文件已经保存。"),
                new UrdfExportTutorialStep(
                    "frames",
                    "2. 建立坐标系与关节轴",
                    "在 CAD 中明确 URDF 根坐标系、每个 Joint 原点和运动轴，避免模型方向、关节位置和惯性坐标错位。",
                    "1. 创建根坐标系 Origin_global。机器人常用约定是 X 向前、Y 向左、Z 向上，并保持右手系。\r\n" +
                    "2. 为每个 Joint 创建坐标系，例如 Origin_left_wheel_joint。坐标系原点放在真实转轴或滑动基准处。\r\n" +
                    "3. 为 revolute、continuous、prismatic Joint 创建或选择 Axis；轴方向决定 URDF 正方向。\r\n" +
                    "4. fixed Joint 仍需要正确的父子位姿，但不需要运动限位。\r\n" +
                    "5. 旋转装配体检查各坐标系，确认没有镜像轴、左手系或毫米/米概念混淆。",
                    "Origin_global 唯一；每个运动 Joint 都有可识别的原点和轴；轴方向与预期正运动一致。"),
                new UrdfExportTutorialStep(
                    "links",
                    "3. 构建 Link 树",
                    "用 Link 表示刚体，用树结构表达父子刚体关系，并把 SolidWorks 实体分配到正确的 Link。",
                    "1. 在 SolidWorks 中选择 工具 > 导出为 URDF。首次开始教程后，真实导出界面会继续打开。\r\n" +
                    "2. 在属性管理器中打开 编辑 Link 树，使用自由画布增加子 Link、拖拽调整父子关系。\r\n" +
                    "3. 大型结构可用 大纲编辑 一次写完整层级：\r\n\r\n" +
                    "# base_link\r\n## camera_link\r\n## left_steering_link\r\n### left_front_wheel_link\r\n\r\n" +
                    "4. # 数量代表 Link 深度。新 Link 自动得到 Joint 名称（例如 camera_joint），但类型保持待选择。\r\n" +
                    "5. 回到画布为每个新 Joint 明确选择类型，再到属性页分配 CAD 组件。复制结构不会复制组件绑定。",
                    "只有一个根 Link；Link 名称唯一且适合 ROS；每个实体只绑定一次；没有空的关键刚体。"),
                new UrdfExportTutorialStep(
                    "joints",
                    "4. 配置 Joint",
                    "为每条父子连接设置正确的类型、名称、位姿、轴、限制和可选 Mimic 关系。",
                    "1. 检查自动生成的 *_joint 名称，也可以在图形编辑器中手动修改。\r\n" +
                    "2. fixed 用于刚性连接；continuous 用于无限旋转轮；revolute 用于有限角度转动；prismatic 用于直线滑动。\r\n" +
                    "3. STEP、导入模型或固定装配必须手动选择类型；0 DOF 不代表 URDF 语义一定是 fixed。\r\n" +
                    "4. “尝试从 SolidWorks Mate 识别”仅用于保留正确 Mate 的原生可动装配；识别失败时改用显式类型，运动关节同时使用显式参考几何。\r\n" +
                    "5. 选择对应的 Origin_* 坐标系和 Axis，确认父 Link、子 Link 没有接反。\r\n" +
                    "6. revolute/prismatic 填写 lower、upper、effort、velocity；continuous 不填写上下位置限位。\r\n" +
                    "7. damping、friction、safety controller 只在有明确模型依据时填写。\r\n" +
                    "8. Mimic Joint 先选已存在的源 Joint，再设置 multiplier 和 offset，避免循环引用。",
                    "所有非 fixed Joint 都有有效轴；有限运动 Joint 有完整限位；Joint 名称唯一；Mimic 不成环。"),
                new UrdfExportTutorialStep(
                    "inertia",
                    "5. 校验质量、重心和惯性",
                    "让每个 Link 的 URDF inertial 数据与 SolidWorks 质量属性在同一坐标约定下吻合。",
                    "1. 对每个 Link 检查质量是否来自已分配实体和真实材料。\r\n" +
                    "2. inertial origin 应表示该 Link 重心相对 Link 坐标系的位置，单位为米。\r\n" +
                    "3. 惯性张量必须是重心处、以 Link 惯性坐标轴表达的 kg*m^2 数值。\r\n" +
                    "4. 确认矩阵对称、主惯量为正，并满足刚体三角不等式。\r\n" +
                    "5. 点击 显示惯性等效长方体，检查其中心、方向和尺度是否符合质量与惯量参数。\r\n" +
                    "6. 传感器、薄片和小零件尤其要检查数量级，防止错误密度或单位造成异常。",
                    "惯性预览有效；质量和重心合理；没有负主惯量或三角不等式错误；张量数量级与尺寸匹配。"),
                new UrdfExportTutorialStep(
                    "geometry",
                    "6. 选择可视与碰撞几何",
                    "保留足够的视觉细节，同时使用适合仿真的轻量碰撞模型和可控大小的 STL。",
                    "1. Visual 保留机器人外观；Collision 优先服务接触计算，不必复刻全部细节。\r\n" +
                    "2. 默认优先尝试 ComponentBoxes；箱体用 BoxPrimitive，轮子和圆柱壳用 CylinderPrimitive，球面用 SpherePrimitive。\r\n" +
                    "3. 单一复杂外形可用 ConvexHull；原语不适用时用 SimplifiedMesh；只有确需完整接触细节时才用 AccurateMesh。\r\n" +
                    "4. STL 精简比例 0 表示不额外精简，数值越大通常文件越小，但必须检查轮廓和孔槽是否还能接受。\r\n" +
                    "5. 导出器生成策略失败时会回退到 VisualMesh，并在报告中记录 effective strategy。",
                    "碰撞体覆盖真实接触区域且没有明显穿透；轮廓满足用途；网格大小和三角面数量可接受。"),
                new UrdfExportTutorialStep(
                    "export",
                    "7. 导出 Robot Bundle 与目标功能包",
                    "以可校验 Robot Bundle 为规范资产，并派生所选 ROS、Gazebo 和 Isaac 配置。",
                    "1. ROS 包名使用小写字母、数字和下划线，例如 rover_description。\r\n" +
                    "2. 在 Link 属性页完成颜色、材质、网格格式、碰撞策略和精简比例设置。\r\n" +
                    "3. 需要完整交付时点击 导出 URDF 和网格；只有调试 XML 时才选择 导出 URDF（不含网格），该轻量兼容路径不生成 Bundle、Isaac 或新 profile。\r\n" +
                    "4. 选择一个可写的新目录，等待所有 Link 的网格和报告完成后再关闭 SolidWorks。\r\n" +
                    "5. 检查 Bundle/<包名>.osurdf，以及已选的 ROS1/<包名> 和 ROS2/<包名>。",
                    "Bundle 校验通过；所有 mesh URI 都能找到对应文件；所选派生包完整；导出错误已被阻断或明确记录。"),
                new UrdfExportTutorialStep(
                    "validate",
                    "8. 检查报告并在查看器验证",
                    "在进入 Gazebo、RViz 或控制代码之前发现缺失网格、错误惯性、坐标方向和关节配置问题。",
                    "1. 打开 config/export_report.md，先处理 error 和 warning，再确认每个 Link 的有效碰撞策略。\r\n" +
                    "2. 打开 config/inertial_validation.csv，对照 SW 质量、重心、COM 惯性张量、URDF 值和误差百分比。\r\n" +
                    "3. 打开 config/mesh_manifest.csv，确认每个 visual/collision 网格的路径、大小、精简和 fallback。\r\n" +
                    "4. 在 URDF 查看器中分别切换 Visual、Collision、Inertia、COM、Axes 和 Joint Axes。\r\n" +
                    "5. 驱动所有可动 Joint 到上下限，确认旋转中心、方向、Mimic 和几何没有脱离。\r\n" +
                    "6. 最后在目标 ROS1/ROS2 环境运行 URDF 解析或 robot_state_publisher，再把包交给仿真。",
                    "解析无错误；视觉与碰撞齐全；COM/惯性等效长方体位置合理；关节方向和限位正确；ROS1/ROS2 结果一致。")
            };
        }

        private static IList<UrdfExportTutorialStep> BuildEnglish()
        {
            return new List<UrdfExportTutorialStep>
            {
                new UrdfExportTutorialStep("prepare", "1. Prepare assembly and mass properties", "Put the SolidWorks assembly in a repeatable, safe-to-export state. The tutorial never modifies the model automatically.", "1. Work from an export copy when possible.\r\n2. Resolve required components and suppress fixtures or environment geometry.\r\n3. Assign real materials or densities.\r\n4. Inspect SolidWorks mass, center of mass, and inertia, then fully rebuild and save.\r\n5. Assign each rigid body to exactly one Link.", "No unresolved components; non-zero mass; no blocking rebuild errors; document saved."),
                new UrdfExportTutorialStep("frames", "2. Create frames and joint axes", "Define the root frame, Joint origins, and motion axes in CAD.", "1. Create Origin_global, preferably X forward, Y left, Z up in a right-handed frame.\r\n2. Create an Origin_<joint> coordinate system at every real pivot or slider origin.\r\n3. Create or select an Axis for revolute, continuous, and prismatic Joints.\r\n4. Fixed Joints still need the correct parent-child pose.\r\n5. Inspect frames for mirrored axes and unit mistakes.", "One Origin_global; every moving Joint has an origin and axis; positive motion matches intent."),
                new UrdfExportTutorialStep("links", "3. Build the Link tree", "Represent rigid bodies as Links and define their parent-child hierarchy.", "1. Select Tools > Export as URDF.\r\n2. Open Edit Link Tree and add, move, or copy Link groups on the canvas.\r\n3. Use Outline Edit for large trees:\r\n\r\n# base_link\r\n## camera_link\r\n## left_steering_link\r\n### left_front_wheel_link\r\n\r\n4. Heading depth defines Link depth. New Links receive generated Joint names, such as camera_joint, but their types remain unconfigured.\r\n5. Choose every new Joint type on the canvas, then assign CAD components to new or pasted Links on the property page.", "One root; unique ROS-friendly Link names; each body assigned once; no empty critical rigid bodies."),
                new UrdfExportTutorialStep("joints", "4. Configure Joints", "Set the type, name, pose, axis, limits, and optional Mimic relation for every connection.", "1. Review generated *_joint names.\r\n2. Use fixed, continuous, revolute, or prismatic according to the real mechanism.\r\n3. Select types explicitly for STEP, imported, or fixed assemblies; zero remaining DOFs does not prove fixed URDF semantics.\r\n4. Try SolidWorks Mate detection only on a native movable assembly with correct Mates. If it cannot find one unique DOF, use an explicit type and explicit reference geometry for moving Joints.\r\n5. Select Origin_* and Axis and verify parent/child direction.\r\n6. Add lower, upper, effort, and velocity for finite Joints; continuous has no position bounds.\r\n7. Add dynamics only when values are known.\r\n8. Configure Mimic after its source Joint and avoid cycles.", "Every moving Joint has an axis; finite Joints have limits; names are unique; Mimic graph is acyclic."),
                new UrdfExportTutorialStep("inertia", "5. Validate mass, COM, and inertia", "Match every URDF inertial block to SolidWorks using the same frame convention.", "1. Verify mass comes from assigned bodies and real materials.\r\n2. inertial origin is the COM relative to the Link frame in metres.\r\n3. Inertia is the COM tensor in kg*m^2.\r\n4. Check symmetry, positive principal moments, and rigid-body triangle inequalities.\r\n5. Show the equivalent inertia cuboid and inspect its center, orientation, and scale.\r\n6. Check order of magnitude for sensors, plates, and small parts.", "Valid equivalent cuboid; plausible mass and COM; no negative principal moments or triangle inequality errors."),
                new UrdfExportTutorialStep("geometry", "6. Choose visual and collision geometry", "Keep useful visual detail while producing stable, lightweight collision geometry.", "1. Visual represents appearance; Collision serves contact computation.\r\n2. Start with ComponentBoxes. Use BoxPrimitive for boxes, CylinderPrimitive for wheels, and SpherePrimitive for spheres.\r\n3. Use ConvexHull for one complex body, SimplifiedMesh when primitives do not fit, and AccurateMesh only when detail is essential.\r\n4. STL reduction 0 adds no reduction; larger values usually reduce size but require shape inspection.\r\n5. Failed strategies fall back to VisualMesh and are recorded as the effective strategy.", "Collision covers contact areas without severe penetration; shape and mesh size suit the target simulator."),
                new UrdfExportTutorialStep("export", "7. Export the Robot Bundle and target packages", "Create a verified Robot Bundle as the canonical asset, then derive the selected ROS, Gazebo, and Isaac profiles.", "1. Use a lowercase package name with digits and underscores, such as rover_description.\r\n2. Finish materials, mesh format, collision strategy, and reduction settings.\r\n3. Choose Export URDF and Meshes for a complete delivery. Export URDF Without Meshes is a lightweight XML-debug compatibility path and does not create the Bundle, Isaac, or new profiles.\r\n4. Select a writable directory and wait for all Link exports and reports.\r\n5. Check Bundle/<package>.osurdf and each selected ROS1/<package> or ROS2/<package> output.", "Bundle verification passes; every mesh URI resolves; selected derived packages are complete; export errors are blocked or explicitly recorded."),
                new UrdfExportTutorialStep("validate", "8. Review reports and validate in a viewer", "Catch mesh, inertia, frame, and Joint errors before simulation or control integration.", "1. Review config/export_report.md.\r\n2. Compare SolidWorks and URDF values in config/inertial_validation.csv.\r\n3. Inspect paths, sizes, reduction, and fallback in config/mesh_manifest.csv.\r\n4. Toggle Visual, Collision, Inertia, COM, Axes, and Joint Axes in a URDF viewer.\r\n5. Move every Joint through its limits and verify pivot, direction, Mimic, and geometry.\r\n6. Parse the package or run robot_state_publisher in the target ROS1/ROS2 environment.", "No parser errors; complete visual/collision geometry; plausible COM/inertia; correct Joint motion; ROS1/ROS2 parity.")
            };
        }
    }
}
