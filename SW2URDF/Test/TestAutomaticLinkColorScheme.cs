using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Text.RegularExpressions;
using Xunit;

namespace SW2URDF.Test
{
    public class TestAutomaticLinkColorScheme
    {
        [Fact]
        public void LeftAndRightCounterpartsReceiveTheSameStableAssignment()
        {
            AutomaticLinkColorAssignment left = AutomaticLinkColorScheme.GetAssignment(
                "left_front_wheel_link",
                2,
                2);
            AutomaticLinkColorAssignment right = AutomaticLinkColorScheme.GetAssignment(
                "right_front_wheel_link",
                2,
                2);

            Assert.Equal("front_wheel_link", AutomaticLinkColorScheme.CanonicalizeName(
                "left_front_wheel_link"));
            Assert.Equal(left.MaterialId, right.MaterialId);
            Assert.Equal(left.Rgba, right.Rgba);
            Assert.NotSame(left.Rgba, right.Rgba);
        }

        [Fact]
        public void LinkLevelMovesFromCoolToWarmColors()
        {
            AutomaticLinkColorAssignment root = AutomaticLinkColorScheme.GetAssignment(
                "base_link",
                0,
                4);
            AutomaticLinkColorAssignment terminal = AutomaticLinkColorScheme.GetAssignment(
                "tool_link",
                4,
                4);

            Assert.True(root.Rgba[2] > root.Rgba[0]);
            Assert.True(terminal.Rgba[0] > terminal.Rgba[2]);
            Assert.NotEqual(root.MaterialId, terminal.MaterialId);
        }

        [Fact]
        public void AssignmentsAreOpaqueBoundedAndValidUrdfMaterialIds()
        {
            AutomaticLinkColorAssignment assignment = AutomaticLinkColorScheme.GetAssignment(
                "Right Front Wheel/Link",
                3,
                7);

            Assert.Matches(new Regex("^[a-zA-Z_][a-zA-Z0-9_]*$"), assignment.MaterialId);
            Assert.Equal(4, assignment.Rgba.Length);
            Assert.Equal(1.0, assignment.Rgba[3], 12);
            foreach (double channel in assignment.Rgba)
            {
                Assert.InRange(channel, 0.0, 1.0);
            }
        }

        [Fact]
        public void ApplyingSchemePersistsMaterialAndRgbaWithoutChangingTreeOrTexture()
        {
            Link root = NamedLink("base_link");
            Link left = NamedLink("left_front_wheel_link");
            Link right = NamedLink("right_front_wheel_link");
            left.Visual.Material.Texture.wFilename = "legacy.png";
            root.Children.Add(left);
            root.Children.Add(right);

            int count = AutomaticLinkColorScheme.Apply(root);

            Assert.Equal(3, count);
            Assert.Same(left, root.Children[0]);
            Assert.Same(right, root.Children[1]);
            Assert.Equal(left.Visual.Material.Name, right.Visual.Material.Name);
            Assert.Equal(
                left.Visual.Material.Color.GetColor(),
                right.Visual.Material.Color.GetColor());
            Assert.False(left.Visual.Material.AppearanceAutomaticallyResolved);
            Assert.Equal("legacy.png", left.Visual.Material.Texture.wFilename);
        }

        [Fact]
        public void AssemblyTreeApplicationUsesCurrentUiHierarchy()
        {
            LinkNode root = new LinkNode(NamedLink("base_link"));
            LinkNode steering = new LinkNode(NamedLink("left_steering_hinge_link"));
            LinkNode wheel = new LinkNode(NamedLink("left_front_wheel_link"));
            root.Nodes.Add(steering);
            steering.Nodes.Add(wheel);

            int count = AssemblyExportForm.ApplyAutomaticLinkColors(root);

            Assert.Equal(3, count);
            Assert.StartsWith("auto_l00_", root.Link.Visual.Material.Name);
            Assert.StartsWith("auto_l01_", steering.Link.Visual.Material.Name);
            Assert.StartsWith("auto_l02_", wheel.Link.Visual.Material.Name);
            Assert.False(wheel.Link.Visual.Material.AppearanceAutomaticallyResolved);
        }

        [Fact]
        public void AutomaticColorsSurviveConfigurationSerialization()
        {
            LinkNode root = new LinkNode(NamedLink("base_link"));
            LinkNode left = new LinkNode(NamedLink("left_front_wheel_link"));
            LinkNode right = new LinkNode(NamedLink("right_front_wheel_link"));
            root.Nodes.Add(left);
            root.Nodes.Add(right);
            AssemblyExportForm.ApplyAutomaticLinkColors(root);

            string payload = ConfigurationSerialization.SerializeDraftPayload(root);
            LinkNode restored = ConfigurationSerialization.DeserializeDraftPayload(payload);
            LinkNode restoredLeft = (LinkNode)restored.Nodes[0];
            LinkNode restoredRight = (LinkNode)restored.Nodes[1];

            Assert.Equal(left.Link.Visual.Material.Name,
                restoredLeft.Link.Visual.Material.Name);
            Assert.Equal(
                left.Link.Visual.Material.Color.GetColor(),
                restoredLeft.Link.Visual.Material.Color.GetColor());
            Assert.Equal(
                restoredLeft.Link.Visual.Material.Name,
                restoredRight.Link.Visual.Material.Name);
        }

        [Fact]
        public void EmptyAndNonAsciiNamesHaveDeterministicFallback()
        {
            AutomaticLinkColorAssignment empty = AutomaticLinkColorScheme.GetAssignment(
                String.Empty,
                1,
                1);
            AutomaticLinkColorAssignment nonAscii = AutomaticLinkColorScheme.GetAssignment(
                "\u5de6\u8f6e",
                1,
                1);

            Assert.Equal("auto_l01_link", empty.MaterialId);
            Assert.Equal(empty.MaterialId, nonAscii.MaterialId);
            Assert.Equal(empty.Rgba, nonAscii.Rgba);
        }

        private static Link NamedLink(string name)
        {
            Link link = new Link();
            link.Name = name;
            return link;
        }
    }
}
