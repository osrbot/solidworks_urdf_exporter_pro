using SW2URDF.UI.LinkTreeCanvas;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Linq;
using Xunit;

namespace SW2URDF.Test
{
    public class TestLinkTreeCanvas
    {
        [Fact]
        public void DocumentRejectsDuplicateNamesAndCycles()
        {
            LinkTreeDocument document = LinkTreeDocument.CreateSample();
            document.Nodes[1].Name = document.Nodes[0].Name;
            document.Nodes[0].ParentId = document.Nodes[1].Id;

            string errors = string.Join(" ", document.Validate());

            Assert.Contains("根节点", errors);
            Assert.Contains("不能重复", errors);
            Assert.Contains("循环", errors);
        }

        [Fact]
        public void HostDoesNotMutateLegacyTreeBeforeApply()
        {
            LinkNode root = CreateTree();
            LinkTreeSession host = new LinkTreeSession(root);
            LinkTreeDocument workingCopy = host.LoadTree();
            workingCopy.Root.Name = "renamed_base";

            Assert.Equal("base_link", root.Link.Name);
            Assert.Null(host.AppliedRoot);
        }

        [Fact]
        public void HostAppliesStructureAndPreservesExistingLinkObjects()
        {
            LinkNode root = CreateTree();
            Link originalChildLink = ((LinkNode)root.Nodes[0]).Link;
            LinkTreeSession host = new LinkTreeSession(root);
            LinkTreeDocument document = host.LoadTree();
            LinkTreeNode child = document.Nodes.Single(node => node.Name == "sensor_link");
            child.Name = "lidar_link";
            child.JointName = "lidar_joint";

            host.ApplyTree(document);

            LinkNode appliedChild = (LinkNode)host.AppliedRoot.Nodes[0];
            Assert.Same(originalChildLink, appliedChild.Link);
            Assert.Equal("lidar_link", appliedChild.Link.Name);
            Assert.Equal("lidar_joint", appliedChild.Link.Joint.Name);
            Assert.Equal("base_link", appliedChild.Link.Joint.Parent.Name);
            Assert.Equal("lidar_link", appliedChild.Link.Joint.Child.Name);
        }

        [Fact]
        public void NewCanvasNodeStartsWithoutSolidWorksComponentBindings()
        {
            LinkNode root = CreateTree();
            LinkTreeSession host = new LinkTreeSession(root);
            LinkTreeDocument document = host.LoadTree();
            LinkTreeNode added = LinkTreeDocument.NewNode(
                "right_sensor_link",
                document.Root.Id,
                500,
                300);
            added.JointName = "right_sensor_joint";
            document.Nodes.Add(added);

            host.ApplyTree(document);

            LinkNode applied = host.AppliedRoot.Nodes
                .Cast<LinkNode>()
                .Single(node => node.Link.Name == "right_sensor_link");
            Assert.Empty(applied.Link.SWComponents);
            Assert.True(applied.IsIncomplete);
        }

        [Fact]
        public void SessionPreservesNodeIdsAcrossLegacyProjectionCapture()
        {
            LinkTreeSession session = new LinkTreeSession(CreateTree());
            LinkTreeDocument before = session.LoadTree();
            LinkNode projection = session.CreateProjection();
            ((LinkNode)projection.Nodes[0]).Link.Joint.Type = "continuous";

            session.CaptureTree(projection);
            LinkTreeDocument after = session.LoadTree();

            Assert.Equal(
                before.Nodes.OrderBy(node => node.Name).Select(node => node.Id),
                after.Nodes.OrderBy(node => node.Name).Select(node => node.Id));
            Assert.Equal(
                "continuous",
                after.Nodes.Single(node => node.Name == "sensor_link").JointType);
        }

        [Fact]
        public void AppliedCanvasStructureRemainsCanonicalAfterProjectionRefresh()
        {
            LinkTreeSession session = new LinkTreeSession(CreateTree());
            LinkTreeDocument edited = session.LoadTree();
            LinkTreeNode added = LinkTreeDocument.NewNode(
                "camera_link",
                edited.Root.Id,
                500,
                420);
            added.JointName = "camera_joint";
            edited.Nodes.Add(added);

            session.ApplyTree(edited);
            LinkNode projection = session.CreateProjection();
            session.CaptureTree(projection);
            LinkTreeDocument captured = session.LoadTree();

            Assert.Equal(3, captured.Nodes.Count);
            Assert.Equal(added.Id, captured.Nodes.Single(node => node.Name == "camera_link").Id);
            Assert.Equal(2, session.Revision);
        }

        private static LinkNode CreateTree()
        {
            LinkNode root = new LinkNode();
            root.Link.Name = "base_link";
            root.Name = root.Link.Name;
            root.Text = root.Link.Name;
            root.IsBaseNode = true;

            LinkNode child = new LinkNode();
            child.Link.Name = "sensor_link";
            child.Link.Joint.Name = "sensor_joint";
            child.Link.Joint.Type = "fixed";
            child.Name = child.Link.Name;
            child.Text = child.Link.Name;
            root.Nodes.Add(child);
            return root;
        }
    }
}
