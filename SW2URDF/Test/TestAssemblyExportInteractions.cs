using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public class TestAssemblyExportInteractions
    {
        [Theory]
        [InlineData(DialogResult.No, true)]
        [InlineData(DialogResult.Cancel, false)]
        public void ClosingWithoutSavingNeverCapturesOrValidatesInvalidEdits(
            DialogResult choice, bool closed)
        {
            Assert.Equal(closed, ConfigurationSaveInteraction.TryClose(() => choice,
                () => { throw new InvalidOperationException("Invalid draft must not be saved"); }));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SaveChoiceClosesOnlyAfterSuccessfulSave(bool saved)
        {
            int calls = 0;
            Assert.Equal(saved, ConfigurationSaveInteraction.TryClose(() => DialogResult.Yes,
                () => { calls++; return saved; }));
            Assert.Equal(1, calls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedJointSaveRestoresNodesAndSelection(bool throws)
        {
            using (var form = NewForm())
            {
                var root = new LinkNode { IsBaseNode = true };
                root.Link.Name = "base_link";
                var a = new LinkNode();
                var b = new LinkNode();
                a.Link.Joint.Name = "a";
                b.Link.Joint.Name = "b";
                a.Link.Joint.Mimic.Update("b", "1", "0");
                b.Link.Joint.Mimic.Update("a", "1", "0");
                root.Nodes.Add(a);
                root.Nodes.Add(b);
                SetField(form, "BaseNode", root);
                form.FillJointTree();
                var tree = Field<TreeView>(form, "treeViewJointTree");
                using (Field<TreeSelectionUpdateGuard>(form, "treeSelectionUpdateGuard").Suppress())
                    tree.SelectedNode = b;
                SetField(form, "previouslySelectedNode", b);

                Func<bool> save = () =>
                {
                    Assert.Empty(tree.Nodes.Cast<TreeNode>());
                    Assert.Equal(2, root.Nodes.Count);
                    Assert.NotEmpty(MimicGraphValidator.Validate(
                        root.Nodes.Cast<LinkNode>().Select(node => node.Link.Joint).ToList()));
                    if (throws) throw new InvalidOperationException("Storage failed");
                    return false;
                };
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (throws)
                        Assert.Throws<InvalidOperationException>(() =>
                            form.TrySaveConfigurationBeforeClose(true, save));
                    else
                        Assert.False(form.TrySaveConfigurationBeforeClose(true, save));
                    Assert.Equal(new TreeNode[] { a, b }, tree.Nodes.Cast<TreeNode>().ToArray());
                    Assert.Empty(root.Nodes.Cast<TreeNode>());
                    Assert.Same(b, tree.SelectedNode);
                    Assert.Same(b, Field<LinkNode>(form, "previouslySelectedNode"));
                }
                a.Link.Joint.Mimic.Clear();
                b.Link.Joint.Mimic.Clear();
                Assert.True(form.TrySaveConfigurationBeforeClose(true, () =>
                    MimicGraphValidator.Validate(root.Nodes.Cast<LinkNode>()
                        .Select(node => node.Link.Joint).ToList()).Count == 0));
                Assert.Equal(2, root.Nodes.Count);
            }
        }

        [Fact]
        public void MimicRoundTripPreservesCaseSensitiveTarget()
        {
            using (var form = NewForm())
            {
                var helper = (ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));
                var catalog = (ReferenceGeometryCatalog)FormatterServices.GetUninitializedObject(
                    typeof(ReferenceGeometryCatalog));
                SetField(catalog, "entries", new List<ReferenceGeometryEntry>());
                SetField(helper, "referenceGeometryCatalog", catalog);
                helper.URDFRobot = new Robot();
                form.Exporter = helper;
                SetField(form, "jointBoxes", new Control[0]);
                Link root = helper.URDFRobot.BaseLink;
                foreach (string name in new[] { "joint_a", "JOINT_A", "follower" })
                {
                    var child = new Link(root) { Name = name + "_link" };
                    child.Joint.Name = name;
                    child.Joint.Type = "continuous";
                    child.Joint.Limit.Effort = 1;
                    child.Joint.Limit.Velocity = 1;
                    root.Children.Add(child);
                }
                Link follower = root.Children[2];
                follower.Joint.Mimic.Update("JOINT_A", "2", "0.5");
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    form.FillJointPropertyBoxes(follower);
                    Assert.Equal("JOINT_A", Field<ComboBox>(form, "MimicJointComboBox").SelectedItem);
                    form.SaveJointDataFromPropertyBoxes(follower);
                    Assert.Equal("JOINT_A", follower.Joint.Mimic.JointName);
                    Assert.Equal(2, follower.Joint.Mimic.Multiplier);
                    Assert.Equal(0.5, follower.Joint.Mimic.Offset);
                }
            }
        }

        [Theory]
        [InlineData("email")]
        [InlineData("version")]
        [InlineData("control")]
        public void TargetSpecificErrorsDoNotBlockSharedExportEntry(string invalidField)
        {
            ExportTargetOptions options = ExportTargetOptions.RecommendedDefaults("audit_robot");
            if (invalidField == "email") options.MaintainerEmail = "invalid-email";
            if (invalidField == "version") options.PackageVersion = "invalid-version";
            if (invalidField == "control") options.Ros2ControlProfileFile = "missing-profile-" + Guid.NewGuid() + ".json";
            Assert.Empty(options.ValidateSharedFindings());
            Assert.NotEmpty(options.ValidateFindings());
            Assert.Empty(V2ExportBridge.ForTarget(options, "OpenUSD").ValidateFindings());
            Assert.Empty(V2ExportBridge.ForTarget(options, "MuJoCo MJCF").ValidateFindings());
            Assert.NotEmpty(V2ExportBridge.ForTarget(options, "ROS 2").ValidateFindings());
        }

        [Fact]
        public void MissingTargetStillBlocksExportEntry()
        {
            var options = new ExportTargetOptions { UseV2Pipeline = true };
            Assert.Equal("TARGET_REQUIRED", Assert.Single(options.ValidateSharedFindings()).Code);
            Assert.Empty(ExportTargetOptions.LegacyCompatibilityDefaults().ValidateSharedFindings());
        }

        private static AssemblyExportForm NewForm()
        {
            return (AssemblyExportForm)Activator.CreateInstance(typeof(AssemblyExportForm), true);
        }

        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }
    }
}
