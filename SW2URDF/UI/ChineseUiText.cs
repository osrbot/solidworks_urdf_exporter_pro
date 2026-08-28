using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    internal static class ChineseUiText
    {
        private static readonly Dictionary<string, string> AssemblyTexts = new Dictionary<string, string>
        {
            { "buttonShowInertiaPreview", "\u663e\u793a\u60ef\u6027\u692d\u7403" },
            { "labelCollisionStrategy", "\u78b0\u649e\u7b56\u7565" },
            { "labelInertiaPreviewStatus", "\u7ea2a / \u7effb / \u84ddc\uff1a\u4e3b\u60ef\u6027\u534a\u8f74 (mm)" },
            { "labelRosPackageName", "ROS \u5305\u540d" },
            { "buttonLinksExportUrdfOnly", "仅导出 URDF..." },
            { "buttonLinksFinish", "导出 URDF 和网格..." },
            { "buttonLinksPrevious", "上一步" },
            { "buttonLinksCancel", "取消" },
            { "buttonJointNext", "下一步" },
            { "buttonJointCancel", "取消" },
            { "buttonMaterialColorPick", "选色..." },
            { "label2", "在此页面修改各 Link 的属性" },
            { "label5", "配置 Link 属性" },
            { "label15", "惯性" },
            { "label16", "Roll" },
            { "label36", "原点 (m)" },
            { "label12", "质量 (kg)" },
            { "label44", "\u60ef\u6027\u77e9\u9635 (kg*m^2)" },
            { "label45", "Pitch" },
            { "label46", "Yaw" },
            { "groupBox1", "网格格式" },
            { "radioButton3dxml", "3dxml (彩色)" },
            { "radioButtonStl", "STL (灰度)" },
            { "radioButtonFine", "精细" },
            { "radioButtonCourse", "粗略" },
            { "label10", "网格细节" },
            { "labelMeshReduction", "导出 STL 精简比例 (0-1)" },
            { "labelEstimatedMeshSize", "粗略 STL 估算：导出时写入日志" },
            { "label19", "可视与碰撞网格" },
            { "label28", "URDF 材质 ID" },
            { "label29", "颜色" },
            { "label33", "Alpha" },
            { "label32", "蓝色" },
            { "label31", "绿色" },
            { "label30", "红色" },
            { "label23", "原点 (m)" },
            { "label22", "Roll" },
            { "label21", "Pitch" },
            { "label20", "Yaw" },
            { "label7", "配置 Joint 属性" },
            { "label6", "姿态 (rad)" },
            { "label3", "位置 (m)" },
            { "labelKVelocity", "k 速度" },
            { "labelSoftLower", "软下限 (rad)" },
            { "labelSoftUpper", "软上限 (rad)" },
            { "label80", "安全控制器" },
            { "labelKPosition", "k 位置" },
            { "labelFriction", "摩擦 (N*m)" },
            { "labelDamping", "阻尼 (N*m*s*rad^-1)" },
            { "label76", "动力学" },
            { "label7CalibrationRising", "上升" },
            { "label73", "下降" },
            { "label74", "校准" },
            { "labelVelocity", "速度 (m/s)" },
            { "labelLowerLimit", "下限 (rad)" },
            { "labelLimitUpper", "上限 (rad)" },
            { "label68", "限制" },
            { "labelEffort", "力/力矩 (N-m)" },
            { "label65", "子 Link:" },
            { "label64", "父 Link:" },
            { "label63", "Joint 名称" },
            { "label62", "Joint 类型" },
            { "label60", "轴" },
            { "label54", "原点" },
            { "label51", "Roll" },
            { "label55", "Pitch" },
            { "label56", "Yaw" },
            { "label66", "坐标系" },
            { "label67", "轴" },
            { "label69", "自定义 Joint 属性。如需调整坐标系和轴，请返回 SolidWorks 修改参考几何。" },
            { "label4", "空白项不会写入 URDF。" },
            { "label27", "* 字段组为必填" },
            { "MimicCheckBox", "模仿其他 Joint" },
            { "MimicJointLabel", "要模仿的 Joint:" },
            { "MimicMultiplierLabel", "倍率" },
            { "MimicOffsetLabel", "偏移 (rad)" },
            { "MimicEquationLabel", "pos = multiplier * pos_other + offset" }
        };

        private static readonly Dictionary<string, string> PartTexts = new Dictionary<string, string>
        {
            { "button_cancel", "取消" },
            { "button_finish", "完成" },
            { "button_savename_browse", "浏览..." },
            { "label14", "配置 SolidWorks 零件导出为 URDF Link 的自定义属性" },
            { "label15", "惯性" },
            { "label2", "质量 (kg)" },
            { "label12", "\u60ef\u6027\u77e9\u9635 (kg*m^2)" },
            { "label13", "原点 (m)" },
            { "label16", "Roll" },
            { "label17", "Pitch" },
            { "label18", "Yaw" },
            { "label19", "可视" },
            { "label35", "碰撞" },
            { "label33", "Alpha" },
            { "label32", "蓝色" },
            { "label31", "绿色" },
            { "label30", "红色" },
            { "label29", "自定义颜色" },
            { "label28", "材质名称" },
            { "label23", "原点 (m)" },
            { "label22", "Roll" },
            { "label21", "Pitch" },
            { "label20", "Yaw" },
            { "label40", "原点 (m)" },
            { "label41", "Roll" },
            { "label42", "Pitch" },
            { "label43", "Yaw" },
            { "radioButton_fine", "精细" },
            { "radioButton_course", "粗略" },
            { "label1", "可视网格细节" },
            { "radioButton4", "精细" },
            { "radioButton3", "粗略" },
            { "label27", "碰撞网格细节" },
            { "label45", "保存目录" },
            { "checkBox_rotate", "旋转全局原点，使 Z 轴竖直" }
        };

        private static readonly Dictionary<string, string> DynamicJointTexts = new Dictionary<string, string>
        {
            { "lower (rad)", "下限 (rad)" },
            { "upper (rad)", "上限 (rad)" },
            { "effort (N-m)", "力矩 (N-m)" },
            { "velocity (rad/s)", "速度 (rad/s)" },
            { "friction (N-m)", "摩擦 (N-m)" },
            { "damping (N-m-s/rad)", "阻尼 (N-m-s/rad)" },
            { "soft lower limit (rad)", "软下限 (rad)" },
            { "soft upper limit (rad)", "软上限 (rad)" },
            { "lower (m)", "下限 (m)" },
            { "upper (m)", "上限 (m)" },
            { "effort (N)", "力 (N)" },
            { "velocity (m/s)", "速度 (m/s)" },
            { "friction (N)", "摩擦 (N)" },
            { "damping (N-s/m)", "阻尼 (N-s/m)" },
            { "soft lower limit (m)", "软下限 (m)" },
            { "soft upper limit (m)", "软上限 (m)" },
            { "lower", "下限" },
            { "upper", "上限" },
            { "effort", "力/力矩" },
            { "velocity", "速度" },
            { "friction", "摩擦" },
            { "damping", "阻尼" },
            { "soft lower limit", "软下限" },
            { "soft upper limit", "软上限" },
            { "k position", "k 位置" },
            { "k velocity", "k 速度" }
        };

        private static readonly Dictionary<string, string> JointTypeDescriptions =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Automatically Detect", "自动识别" },
                { "revolute", "有限角度转动" },
                { "continuous", "无约束连续转动" },
                { "prismatic", "直线滑动" },
                { "fixed", "固定连接" },
                { "floating", "六自由度运动" },
                { "planar", "平面运动" }
            };

        public static bool ShouldUseChinese()
        {
            return IsChinese(CultureInfo.CurrentUICulture) || IsChinese(CultureInfo.CurrentCulture);
        }

        public static void Apply(Form form)
        {
            if (!ShouldUseChinese())
            {
                return;
            }

            ApplyChineseFont(form);
            if (form is AssemblyExportForm)
            {
                form.Text = "SolidWorks 装配体到 URDF 导出器";
                ApplyTexts(form, AssemblyTexts);
            }
            else if (form is PartExportForm)
            {
                form.Text = "SolidWorks 零件到 URDF Link 导出器";
                ApplyTexts(form, PartTexts);
            }
        }

        public static string DynamicJointLabel(string text)
        {
            if (!ShouldUseChinese())
            {
                return text;
            }

            string translated;
            return DynamicJointTexts.TryGetValue(text, out translated) ? translated : text;
        }

        public static string Translate(string english, string chinese)
        {
            return ShouldUseChinese() ? chinese : english;
        }

        public static string JointTypeDisplay(string jointType)
        {
            return JointTypeDisplay(jointType, ShouldUseChinese());
        }

        internal static string JointTypeDisplay(string jointType, bool useChinese)
        {
            if (!useChinese || String.IsNullOrWhiteSpace(jointType))
            {
                return jointType;
            }

            string description;
            return JointTypeDescriptions.TryGetValue(jointType, out description)
                ? jointType + " / " + description
                : jointType;
        }

        public static string JointTypeValue(string displayText)
        {
            if (String.IsNullOrWhiteSpace(displayText))
            {
                return displayText;
            }

            foreach (KeyValuePair<string, string> item in JointTypeDescriptions)
            {
                if (String.Equals(
                    displayText,
                    item.Key + " / " + item.Value,
                    StringComparison.Ordinal))
                {
                    return item.Key;
                }
            }

            return displayText;
        }

        private static bool IsChinese(CultureInfo culture)
        {
            return culture != null && culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyTexts(Control root, IDictionary<string, string> texts)
        {
            string text;
            if (texts.TryGetValue(root.Name, out text))
            {
                root.Text = text;
            }

            foreach (Control child in root.Controls)
            {
                ApplyTexts(child, texts);
            }
        }

        private static void ApplyChineseFont(Control root)
        {
            root.Font = new Font("Microsoft YaHei UI", root.Font.Size, root.Font.Style);
            foreach (Control child in root.Controls)
            {
                ApplyChineseFont(child);
            }
        }
    }
}
