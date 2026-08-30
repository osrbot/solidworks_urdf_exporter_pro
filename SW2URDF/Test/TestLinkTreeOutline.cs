using System;
using System.Linq;
using SW2URDF.UI.LinkTreeCanvas;
using Xunit;

namespace SW2URDF.Test
{
    public class TestLinkTreeOutline
    {
        [Fact]
        public void RoundTripPreservesExistingNodeIdentityAndJointData()
        {
            LinkTreeDocument source = CreateDocument();
            LinkTreeNode camera = source.Nodes.Single(node => node.Name == "camera_link");
            Guid cameraId = camera.Id;
            camera.JointName = "camera_mount_joint";
            camera.JointType = "revolute";

            LinkTreeOutlineParseResult result = LinkTreeOutline.Parse(
                LinkTreeOutline.Serialize(source),
                source);

            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
            LinkTreeNode parsedCamera = result.Document.Nodes.Single(node => node.Name == "camera_link");
            Assert.Equal(cameraId, parsedCamera.Id);
            Assert.Equal("camera_mount_joint", parsedCamera.JointName);
            Assert.Equal("revolute", parsedCamera.JointType);
        }

        [Fact]
        public void ParsesCompactMarkdownHeadingHierarchy()
        {
            LinkTreeDocument source = CreateDocument();

            LinkTreeOutlineParseResult result = LinkTreeOutline.Parse(
                "#base_link\n##camera_link\n##left_steering_link\n###left_wheel_link",
                source);

            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
            LinkTreeNode steering = result.Document.Nodes.Single(node => node.Name == "left_steering_link");
            LinkTreeNode wheel = result.Document.Nodes.Single(node => node.Name == "left_wheel_link");
            Assert.Equal(steering.Id, wheel.ParentId);
            Assert.Equal("left_steering_joint", steering.JointName);
            Assert.Equal(string.Empty, steering.JointType);
            Assert.Contains("尚未选择", string.Join(" ", result.Document.Validate()));
            Assert.Equal("continuous", wheel.JointType);
        }

        [Fact]
        public void ReparentingByOutlinePreservesCustomJointConfiguration()
        {
            LinkTreeDocument source = CreateDocument();
            LinkTreeNode wheel = source.Nodes.Single(node => node.Name == "left_wheel_link");
            wheel.JointName = "left_wheel_axis";
            wheel.JointType = "continuous";

            LinkTreeOutlineParseResult result = LinkTreeOutline.Parse(
                "# base_link\n## camera_link\n### left_wheel_link",
                source);

            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
            LinkTreeNode parsedWheel = result.Document.Nodes.Single(node => node.Name == "left_wheel_link");
            Assert.Equal("left_wheel_axis", parsedWheel.JointName);
            Assert.Equal("continuous", parsedWheel.JointType);
            Assert.Equal(
                result.Document.Nodes.Single(node => node.Name == "camera_link").Id,
                parsedWheel.ParentId);
        }

        [Fact]
        public void InvalidOutlineDoesNotMutateSourceDocument()
        {
            LinkTreeDocument source = CreateDocument();
            string original = LinkTreeOutline.Serialize(source);

            LinkTreeOutlineParseResult result = LinkTreeOutline.Parse(
                "# base_link\n### skipped_level",
                source);

            Assert.False(result.IsValid);
            Assert.Contains("层级跳跃", string.Join(" ", result.Errors));
            Assert.Equal(original, LinkTreeOutline.Serialize(source));
        }

        [Fact]
        public void RejectsDuplicateNamesAndMultipleRoots()
        {
            LinkTreeDocument source = CreateDocument();

            LinkTreeOutlineParseResult result = LinkTreeOutline.Parse(
                "# base_link\n## camera_link\n# camera_link",
                source);

            Assert.False(result.IsValid);
            string errors = string.Join(" ", result.Errors);
            Assert.Contains("第二个根", errors);
            Assert.Contains("不能重复", errors);
        }

        [Fact]
        public void NewLinkDoesNotStealExistingAutomaticJointName()
        {
            LinkTreeDocument source = CreateDocument();

            LinkTreeOutlineParseResult result = LinkTreeOutline.Parse(
                "# base_link\n## camera\n## camera_link\n## left_wheel_link",
                source);

            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
            Assert.Equal(
                "camera_joint_1",
                result.Document.Nodes.Single(node => node.Name == "camera").JointName);
            Assert.Equal(
                "camera_joint",
                result.Document.Nodes.Single(node => node.Name == "camera_link").JointName);
        }

        [Fact]
        public void AllowsWhitespaceBeforeMarkdownHeadings()
        {
            LinkTreeDocument source = CreateDocument();

            LinkTreeOutlineParseResult result = LinkTreeOutline.Parse(
                "  # base_link\n\t## camera_link",
                source);

            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
            Assert.Equal(2, result.Document.Nodes.Count);
        }

        [Fact]
        public void PlainTextRenamePreservesNodeIdentityAndCadOwnershipBoundary()
        {
            LinkTreeDocument source = CreateDocument();
            LinkTreeNode camera = source.Nodes.Single(node => node.Name == "camera_link");
            Guid originalId = camera.Id;

            LinkTreeOutlineParseResult result = LinkTreeOutline.Parse(
                "# base_link\n## vision_link\n## left_wheel_link",
                source);

            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
            LinkTreeNode renamed = result.Document.Nodes.Single(node => node.Name == "vision_link");
            Assert.Equal(originalId, renamed.Id);
            Assert.Equal("vision_joint", renamed.JointName);
            Assert.Equal("fixed", renamed.JointType);
        }

        [Fact]
        public void AddingSiblingDoesNotRenameAnExistingNode()
        {
            LinkTreeDocument source = CreateDocument();
            Guid cameraId = source.Nodes.Single(node => node.Name == "camera_link").Id;

            LinkTreeOutlineParseResult result = LinkTreeOutline.Parse(
                "# base_link\n## imu_link\n## camera_link\n## left_wheel_link",
                source);

            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
            Assert.Equal(cameraId, result.Document.Nodes.Single(node => node.Name == "camera_link").Id);
            LinkTreeNode added = result.Document.Nodes.Single(node => node.Name == "imu_link");
            Assert.Null(source.Find(added.Id));
            Assert.Equal(string.Empty, added.JointType);
        }

        private static LinkTreeDocument CreateDocument()
        {
            LinkTreeDocument document = new LinkTreeDocument();
            LinkTreeNode root = LinkTreeDocument.NewNode("base_link", null, 80, 200);
            LinkTreeNode camera = LinkTreeDocument.NewNode("camera_link", root.Id, 380, 120);
            camera.JointName = "camera_joint";
            camera.JointType = "fixed";
            LinkTreeNode wheel = LinkTreeDocument.NewNode("left_wheel_link", root.Id, 380, 280);
            wheel.JointName = "left_wheel_joint";
            wheel.JointType = "continuous";
            document.Nodes.Add(root);
            document.Nodes.Add(camera);
            document.Nodes.Add(wheel);
            return document;
        }
    }
}
