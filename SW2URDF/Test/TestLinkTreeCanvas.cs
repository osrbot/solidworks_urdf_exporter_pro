using Moq;
using SW2URDF.UI.LinkTreeCanvas;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Xunit;
using Component2 = SolidWorks.Interop.sldworks.Component2;
using SwAttribute = SolidWorks.Interop.sldworks.Attribute;
using SwParameter = SolidWorks.Interop.sldworks.Parameter;

namespace SW2URDF.Test
{
    public class TestLinkTreeCanvas
    {
        [Fact]
        public void DocumentRejectsDuplicateNamesAndCycles()
        {
            LinkTreeDocument document = CreateSampleDocument();
            document.Nodes[1].Name = document.Nodes[0].Name;
            document.Nodes[0].ParentId = document.Nodes[1].Id;

            string errors = string.Join(" ", document.Validate());

            Assert.Contains("根节点", errors);
            Assert.Contains("不能重复", errors);
            Assert.Contains("循环", errors);
        }

        [Fact]
        public void DocumentRejectsDuplicateNodeIdsWithoutThrowing()
        {
            LinkTreeDocument document = CreateSampleDocument();
            document.Nodes[1].Id = document.Nodes[0].Id;

            string errors = string.Join(" ", document.Validate());

            Assert.Contains("标识不能重复", errors);
        }

        [Fact]
        public void BranchClipboardIncludesEveryDescendantOfTheSelectedNode()
        {
            LinkTreeDocument document = CreateSampleDocument();
            LinkTreeNode chassis = document.Nodes.Single(node => node.Name == "chassis_link");

            IList<LinkTreeNode> clipboard = document.CreateBranchClipboard(new[] { chassis.Id });

            Assert.Equal(5, clipboard.Count);
            Assert.Contains(clipboard, node => node.Name == "lidar_link");
            Assert.Contains(clipboard, node => node.Name == "right_wheel_link");
            Assert.DoesNotContain(clipboard, node => node.Name == "base_link");
            Assert.All(clipboard, node => Assert.NotSame(document.Find(node.Id), node));
        }

        [Fact]
        public void BranchClipboardMergesOverlappingSelectionsWithoutDuplicates()
        {
            LinkTreeDocument document = CreateSampleDocument();
            LinkTreeNode chassis = document.Nodes.Single(node => node.Name == "chassis_link");
            LinkTreeNode lidar = document.Nodes.Single(node => node.Name == "lidar_link");

            IList<LinkTreeNode> clipboard = document.CreateBranchClipboard(
                new[] { chassis.Id, lidar.Id });

            Assert.Equal(5, clipboard.Count);
            Assert.Equal(clipboard.Count, clipboard.Select(node => node.Id).Distinct().Count());
        }

        [Fact]
        public void BranchClipboardDoesNotCopyTheRootNode()
        {
            LinkTreeDocument document = CreateSampleDocument();

            IList<LinkTreeNode> clipboard = document.CreateBranchClipboard(
                new[] { document.Root.Id });

            Assert.Empty(clipboard);
        }

        [Fact]
        public void RootLinkDoesNotRequireAParentJointType()
        {
            ExportPropertyManager manager = (ExportPropertyManager)
                FormatterServices.GetUninitializedObject(typeof(ExportPropertyManager));
            LinkNode root = new LinkNode
            {
                IsBaseNode = true,
                Text = "base_link"
            };
            root.Link.Name = "base_link";
            root.Link.Joint.Type = string.Empty;
            root.Link.SWComponents.Add(new Mock<Component2>().Object);

            manager.CheckNodeComplete(root);

            Assert.False(root.IsIncomplete);
            Assert.DoesNotContain("joint type", root.WhyIncomplete);
        }

        [Fact]
        public void PropertyManagerControlIdsAreUnique()
        {
            FieldInfo[] idFields = typeof(ExportPropertyManager)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(int) &&
                    (field.Name.EndsWith("ID", StringComparison.Ordinal) ||
                     field.Name.StartsWith("ID", StringComparison.Ordinal) ||
                     field.Name == "dotNetTree"))
                .ToArray();

            Assert.Equal(
                idFields.Length,
                idFields.Select(field => (int)field.GetRawConstantValue()).Distinct().Count());
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
        public void HostAppliesStructureWithoutMutatingOriginalProjection()
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
            Assert.NotSame(originalChildLink, appliedChild.Link);
            Assert.Equal("sensor_link", originalChildLink.Name);
            Assert.Equal("lidar_link", appliedChild.Link.Name);
            Assert.Equal("lidar_joint", appliedChild.Link.Joint.Name);
            Assert.Equal("base_link", appliedChild.Link.Joint.Parent.Name);
            Assert.Equal("lidar_link", appliedChild.Link.Joint.Child.Name);
        }

        [Fact]
        public void LinkCloneParentsPointIntoTheClonedTree()
        {
            Link root = new Link { Name = "base_link" };
            Link child = new Link(root) { Name = "child_link" };
            root.Children.Add(child);

            Link clone = root.Clone();

            Assert.NotSame(root, clone);
            Assert.Same(clone, clone.Children.Single().Parent);
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
            Assert.True(applied.Link.isIncomplete);
            Assert.True(host.RequiresJointKinematicsRecompute);
            Assert.True(host.RequiresJointLimitsRecompute);
        }

        [Fact]
        public void SessionPreservesNodeIdsAcrossLegacyProjectionCapture()
        {
            LinkTreeSession session = new LinkTreeSession(CreateTree());
            LinkTreeDocument before = session.LoadTree();
            LinkNode projection = session.CreateActiveProjection();
            ((LinkNode)projection.Nodes[0]).Link.Joint.Type = "continuous";

            session.CaptureTree(projection);
            LinkTreeDocument after = session.LoadTree();

            Assert.Equal(
                before.Nodes.OrderBy(node => node.Name).Select(node => node.Id),
                after.Nodes.OrderBy(node => node.Name).Select(node => node.Id));
            Assert.Equal(
                "continuous",
                after.Nodes.Single(node => node.Name == "sensor_link").JointType);
            Assert.True(session.RequiresJointKinematicsRecompute);
            Assert.True(session.RequiresJointLimitsRecompute);
        }

        [Fact]
        public void LegacyTreeAdditionRequiresJointKinematicsRecompute()
        {
            LinkTreeSession session = new LinkTreeSession(CreateTree());
            LinkNode visible = session.CreateActiveProjection();
            AddChild(visible, "camera_link", "camera_joint");

            session.CaptureTree(visible);

            Assert.True(session.RequiresJointKinematicsRecompute);
            Assert.True(session.RequiresJointLimitsRecompute);
        }

        [Fact]
        public void DetachedProjectionDoesNotReplaceActiveNodeIdentityMap()
        {
            LinkNode visibleTree = CreateTree();
            LinkTreeSession session = new LinkTreeSession(visibleTree);
            Guid[] before = session.LoadTree().Nodes
                .OrderBy(node => node.Name)
                .Select(node => node.Id)
                .ToArray();

            session.CreateProjection();
            session.CaptureTree(visibleTree);

            Guid[] after = session.LoadTree().Nodes
                .OrderBy(node => node.Name)
                .Select(node => node.Id)
                .ToArray();
            Assert.Equal(before, after);
        }

        [Fact]
        public void SessionRejectsComputedDetachedProjection()
        {
            LinkTreeSession session = new LinkTreeSession(CreateTree());
            session.CreateActiveProjection();
            LinkNode detached = session.CreateProjection();

            Assert.Throws<InvalidOperationException>(() =>
                session.AcceptComputedProjection(detached));
        }

        [Fact]
        public void ComputationProjectionDoesNotReplaceActiveNodeIdentityMap()
        {
            LinkTreeSession session = new LinkTreeSession(CreateTree());
            LinkNode visible = session.CreateActiveProjection();
            Guid[] before = session.LoadTree().Nodes
                .OrderBy(node => node.Name)
                .Select(node => node.Id)
                .ToArray();

            session.CreateComputationProjection();
            session.CaptureTree(visible);

            Guid[] after = session.LoadTree().Nodes
                .OrderBy(node => node.Name)
                .Select(node => node.Id)
                .ToArray();
            Assert.Equal(before, after);
        }

        [Fact]
        public void AcceptedComputationProjectionPreservesNodeIdentity()
        {
            LinkTreeSession session = new LinkTreeSession(CreateTree());
            Guid[] before = session.LoadTree().Nodes
                .OrderBy(node => node.Name)
                .Select(node => node.Id)
                .ToArray();
            LinkNode computation = session.CreateComputationProjection();

            session.AcceptComputedProjection(computation);

            Guid[] after = session.LoadTree().Nodes
                .OrderBy(node => node.Name)
                .Select(node => node.Id)
                .ToArray();
            Assert.Equal(before, after);
        }

        [Fact]
        public void AcceptedComputationProjectionPreservesIdentityWhenExporterReplacesLinks()
        {
            LinkTreeSession session = new LinkTreeSession(CreateTree());
            Guid[] before = session.LoadTree().Nodes
                .OrderBy(node => node.Name)
                .Select(node => node.Id)
                .ToArray();
            LinkNode computation = session.CreateComputationProjection();

            ReplaceProjectedLinks(computation, null);
            ((LinkNode)computation.Nodes[0]).Link.Inertial.Mass.Value = 2.5;
            session.AcceptComputedProjection(computation);

            Guid[] after = session.LoadTree().Nodes
                .OrderBy(node => node.Name)
                .Select(node => node.Id)
                .ToArray();
            LinkNode projectedChild = (LinkNode)session.CreateProjection().Nodes[0];
            Assert.Equal(before, after);
            Assert.Equal(2.5, projectedChild.Link.Inertial.Mass.Value);
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
            LinkNode projection = session.CreateActiveProjection();
            session.CaptureTree(projection);
            LinkTreeDocument captured = session.LoadTree();

            Assert.Equal(3, captured.Nodes.Count);
            Assert.Equal(added.Id, captured.Nodes.Single(node => node.Name == "camera_link").Id);
            Assert.Equal(2, session.Revision);
        }

        [Fact]
        public void RenamingJointMigratesMimicReferences()
        {
            LinkNode root = CreateTree();
            ((LinkNode)root.Nodes[0]).Link.Joint.Type = "continuous";
            LinkNode follower = AddChild(root, "follower_link", "follower_joint");
            follower.Link.Joint.Type = "continuous";
            follower.Link.Joint.Mimic.Update("sensor_joint", "1.0", "0.0");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            edited.Nodes.Single(node => node.JointName == "sensor_joint").JointName = "renamed_joint";

            session.ApplyTree(edited);

            LinkNode projection = session.CreateProjection();
            LinkNode projectedFollower = projection.Nodes
                .Cast<LinkNode>()
                .Single(node => node.Link.Name == "follower_link");
            Assert.Equal("renamed_joint", projectedFollower.Link.Joint.Mimic.JointName);
        }

        [Fact]
        public void SwappingJointNamesMigratesMimicReferencesFromOriginalNames()
        {
            LinkNode root = CreateTree();
            ((LinkNode)root.Nodes[0]).Link.Joint.Type = "continuous";
            AddChild(root, "other_link", "other_joint");
            LinkNode follower = AddChild(root, "follower_link", "follower_joint");
            follower.Link.Joint.Type = "continuous";
            follower.Link.Joint.Mimic.Update("sensor_joint", "1.0", "0.0");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            edited.Nodes.Single(node => node.JointName == "sensor_joint").JointName = "other_joint";
            edited.Nodes.Single(node => node.Name == "other_link").JointName = "sensor_joint";

            session.ApplyTree(edited);

            LinkNode projectedFollower = session.CreateProjection().Nodes
                .Cast<LinkNode>()
                .Single(node => node.Link.Name == "follower_link");
            Assert.Equal("other_joint", projectedFollower.Link.Joint.Mimic.JointName);
        }

        [Fact]
        public void ChangingOnlyJointNameCaseMigratesMimicReference()
        {
            LinkNode root = CreateTree();
            ((LinkNode)root.Nodes[0]).Link.Joint.Type = "continuous";
            LinkNode follower = AddChild(root, "follower_link", "follower_joint");
            follower.Link.Joint.Type = "continuous";
            follower.Link.Joint.Mimic.Update("sensor_joint", "1.0", "0.0");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            edited.Nodes.Single(node => node.JointName == "sensor_joint").JointName = "Sensor_joint";

            session.ApplyTree(edited);

            LinkNode projectedFollower = session.CreateProjection().Nodes
                .Cast<LinkNode>()
                .Single(node => node.Link.Name == "follower_link");
            Assert.Equal("Sensor_joint", projectedFollower.Link.Joint.Mimic.JointName);
        }

        [Fact]
        public void DeletingReferencedJointRejectsWholeTransaction()
        {
            LinkNode root = CreateTree();
            ((LinkNode)root.Nodes[0]).Link.Joint.Type = "continuous";
            LinkNode follower = AddChild(root, "follower_link", "follower_joint");
            follower.Link.Joint.Type = "continuous";
            follower.Link.Joint.Mimic.Update("sensor_joint", "1.0", "0.0");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            LinkTreeNode target = edited.Nodes.Single(node => node.JointName == "sensor_joint");
            edited.Nodes.Remove(target);

            Assert.Throws<InvalidOperationException>(() => session.ApplyTree(edited));
            Assert.Equal(3, session.LoadTree().Nodes.Count);
        }

        [Fact]
        public void DeletedMimicTargetCannotBeReplacedByReusingItsJointName()
        {
            LinkNode root = CreateTree();
            ((LinkNode)root.Nodes[0]).Link.Joint.Type = "continuous";
            LinkNode replacement = AddChild(root, "replacement_link", "replacement_joint");
            replacement.Link.Joint.Type = "continuous";
            LinkNode follower = AddChild(root, "follower_link", "follower_joint");
            follower.Link.Joint.Type = "continuous";
            follower.Link.Joint.Mimic.Update("sensor_joint", "1.0", "0.0");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            LinkTreeNode deletedTarget = edited.Nodes.Single(
                node => node.JointName == "sensor_joint");
            edited.Nodes.Remove(deletedTarget);
            edited.Nodes.Single(node => node.Name == "replacement_link").JointName =
                "sensor_joint";

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => session.ApplyTree(edited));

            Assert.Contains("was deleted", error.Message);
            Assert.Equal(4, session.LoadTree().Nodes.Count);
        }

        [Fact]
        public void DeletingAnUnreferencedJointRemainsValid()
        {
            LinkNode root = CreateTree();
            AddChild(root, "unused_link", "unused_joint");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            edited.Nodes.Remove(edited.Nodes.Single(node => node.JointName == "unused_joint"));

            session.ApplyTree(edited);

            Assert.Equal(2, session.LoadTree().Nodes.Count);
        }

        [Fact]
        public void LegacyCaptureRejectsDanglingMimicWithoutReplacingSessionState()
        {
            LinkNode root = CreateTree();
            ((LinkNode)root.Nodes[0]).Link.Joint.Type = "continuous";
            LinkNode follower = AddChild(root, "follower_link", "follower_joint");
            follower.Link.Joint.Type = "continuous";
            follower.Link.Joint.Mimic.Update("sensor_joint", "1.0", "0.0");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkNode visible = session.CreateActiveProjection();
            visible.Nodes.RemoveAt(0);

            Assert.Throws<InvalidOperationException>(() => session.CaptureTree(visible));
            Assert.Equal(3, session.LoadTree().Nodes.Count);
        }

        [Fact]
        public void LegacyCaptureMigratesRenamedMimicTarget()
        {
            LinkNode root = CreateTree();
            ((LinkNode)root.Nodes[0]).Link.Joint.Type = "continuous";
            LinkNode follower = AddChild(root, "follower_link", "follower_joint");
            follower.Link.Joint.Type = "continuous";
            follower.Link.Joint.Mimic.Update("sensor_joint", "1.0", "0.0");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkNode visible = session.CreateActiveProjection();
            ((LinkNode)visible.Nodes[0]).Link.Joint.Name = "renamed_joint";

            session.CaptureTree(visible);

            LinkNode projectedFollower = session.CreateProjection().Nodes
                .Cast<LinkNode>()
                .Single(node => node.Link.Name == "follower_link");
            Assert.Equal("renamed_joint", projectedFollower.Link.Joint.Mimic.JointName);
        }

        [Fact]
        public void MimicRequiresANonEmptyTarget()
        {
            LinkNode root = CreateTree();
            LinkNode follower = AddChild(root, "follower_link", "follower_joint");
            follower.Link.Joint.Type = "continuous";
            follower.Link.Joint.Mimic.Update(string.Empty, "1.0", "0.0");
            LinkTreeSession session = new LinkTreeSession(root);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => session.ApplyTree(session.LoadTree()));

            Assert.Contains("must select a target Joint", error.Message);
        }

        [Fact]
        public void MimicTargetCanBeClearedWithoutThrowing()
        {
            Mimic mimic = new Mimic();
            mimic.JointName = "leader_joint";

            mimic.JointName = null;

            Assert.Null(mimic.JointName);
        }

        [Fact]
        public void MimicCannotReferenceItsOwnJoint()
        {
            LinkNode root = CreateTree();
            LinkNode follower = AddChild(root, "follower_link", "follower_joint");
            follower.Link.Joint.Type = "continuous";
            follower.Link.Joint.Mimic.Update("follower_joint", "1.0", "0.0");
            LinkTreeSession session = new LinkTreeSession(root);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => session.ApplyTree(session.LoadTree()));

            Assert.Contains("cannot reference itself", error.Message);
        }

        [Fact]
        public void MimicReferenceGraphCannotContainCycles()
        {
            LinkNode root = CreateTree();
            LinkNode sensor = (LinkNode)root.Nodes[0];
            sensor.Link.Joint.Type = "continuous";
            LinkNode follower = AddChild(root, "follower_link", "follower_joint");
            follower.Link.Joint.Type = "continuous";
            sensor.Link.Joint.Mimic.Update("follower_joint", "1.0", "0.0");
            follower.Link.Joint.Mimic.Update("sensor_joint", "1.0", "0.0");
            LinkTreeSession session = new LinkTreeSession(root);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => session.ApplyTree(session.LoadTree()));

            Assert.Contains("contain a cycle", error.Message);
        }

        [Fact]
        public void ReparentingRequiresJointKinematicsRecomputation()
        {
            LinkNode root = CreateTree();
            AddChild(root, "mount_link", "mount_joint");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            LinkTreeNode sensor = edited.Nodes.Single(node => node.Name == "sensor_link");
            LinkTreeNode mount = edited.Nodes.Single(node => node.Name == "mount_link");
            sensor.ParentId = mount.Id;

            session.ApplyTree(edited);

            Assert.True(session.RequiresJointKinematicsRecompute);
            Assert.True(session.RequiresJointLimitsRecompute);
        }

        [Fact]
        public void ReparentingInvalidatesTheMovedJointSubtree()
        {
            LinkNode root = CreateTree();
            LinkNode sensor = (LinkNode)root.Nodes[0];
            LinkNode camera = AddChild(sensor, "camera_link", "camera_joint");
            AddChild(root, "mount_link", "mount_joint");
            sensor.Link.JointKinematicsDirty = false;
            sensor.Link.JointLimitsDirty = false;
            camera.Link.JointKinematicsDirty = false;
            camera.Link.JointLimitsDirty = false;
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            edited.Nodes.Single(node => node.Name == "sensor_link").ParentId =
                edited.Nodes.Single(node => node.Name == "mount_link").Id;

            session.ApplyTree(edited);

            LinkNode projection = session.CreateProjection();
            LinkNode projectedMount = projection.Nodes.Cast<LinkNode>()
                .Single(node => node.Link.Name == "mount_link");
            LinkNode projectedSensor = projectedMount.Nodes.Cast<LinkNode>()
                .Single(node => node.Link.Name == "sensor_link");
            LinkNode projectedCamera = projectedSensor.Nodes.Cast<LinkNode>()
                .Single(node => node.Link.Name == "camera_link");
            Assert.True(projectedSensor.Link.JointKinematicsDirty);
            Assert.True(projectedSensor.Link.JointLimitsDirty);
            Assert.True(projectedCamera.Link.JointKinematicsDirty);
            Assert.True(projectedCamera.Link.JointLimitsDirty);
        }

        [Fact]
        public void ReparentingInvalidationSurvivesConfigurationProjectionReload()
        {
            LinkNode root = CreateTree();
            AddChild(root, "mount_link", "mount_joint");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            edited.Nodes.Single(node => node.Name == "sensor_link").ParentId =
                edited.Nodes.Single(node => node.Name == "mount_link").Id;
            session.ApplyTree(edited);

            LinkTreeSession restored = new LinkTreeSession(session.CreateProjection());

            Assert.True(restored.RequiresJointKinematicsRecompute);
            Assert.True(restored.RequiresJointLimitsRecompute);
        }

        [Fact]
        public void ConfigurationSerializationPreservesRecomputeStateAndAdditionalCollisions()
        {
            LinkNode root = CreateTree();
            root.Link.JointKinematicsDirty = true;
            root.Link.JointLimitsDirty = true;
            root.Link.Joint.CoordinateSystemName = "全局原点";
            root.Link.AddAdditionalCollision(new Collision());
            MethodInfo serialize = typeof(ConfigurationSerialization).GetMethod(
                "SerializeToString",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo deserialize = typeof(ConfigurationSerialization).GetMethod(
                "DeserializeFromString",
                BindingFlags.NonPublic | BindingFlags.Static);

            string data = (string)serialize.Invoke(null, new object[] { root });
            LinkNode restored = (LinkNode)deserialize.Invoke(null, new object[] { data });

            Assert.True(restored.Link.JointKinematicsDirty);
            Assert.True(restored.Link.JointLimitsDirty);
            Assert.Equal("全局原点", restored.Link.Joint.CoordinateSystemName);
            Assert.Single(restored.Link.AdditionalCollisions);
        }

        [Fact]
        public void ConfigurationSerializationDoesNotMutateTheExportProjection()
        {
            LinkNode root = CreateTree();
            root.Name = "saved_base_link";
            root.Link.Name = "export_base_link";
            LinkNode child = (LinkNode)root.Nodes[0];
            child.Name = "saved_sensor_link";
            child.Link.Name = "export_sensor_link";
            MethodInfo serialize = typeof(ConfigurationSerialization).GetMethod(
                "SerializeToString",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo deserialize = typeof(ConfigurationSerialization).GetMethod(
                "DeserializeFromString",
                BindingFlags.NonPublic | BindingFlags.Static);

            string data = (string)serialize.Invoke(null, new object[] { root });
            LinkNode restored = (LinkNode)deserialize.Invoke(null, new object[] { data });

            Assert.Equal("export_base_link", root.Link.Name);
            Assert.Equal("export_sensor_link", child.Link.Name);
            Assert.Equal("saved_base_link", restored.Link.Name);
            Assert.Equal("saved_sensor_link", ((LinkNode)restored.Nodes[0]).Link.Name);
        }

        [Fact]
        public void DocumentRejectsUnsupportedJointTypes()
        {
            LinkTreeDocument document = CreateSampleDocument();
            document.Nodes.First(node => node.ParentId.HasValue).JointType = "unsupported";

            string errors = string.Join(" ", document.Validate());

            Assert.Contains("Joint 类型", errors);
        }

        [Fact]
        public void DocumentRejectsNonCanonicalJointTypeCasing()
        {
            LinkTreeDocument document = CreateSampleDocument();
            document.Nodes.First(node => node.ParentId.HasValue).JointType = "Fixed";

            string errors = string.Join(" ", document.Validate());

            Assert.Contains("Joint 类型", errors);
        }

        [Fact]
        public void DocumentAcceptsAutomaticJointDetectionConfiguration()
        {
            LinkTreeDocument document = CreateSampleDocument();
            document.Nodes.First(node => node.ParentId.HasValue).JointType =
                Joint.AutomaticallyDetectType;

            Assert.Empty(document.Validate());
        }

        [Fact]
        public void AutomaticJointTypeRequiresKinematicsAndIsNotUrdfReady()
        {
            LinkNode root = CreateTree();
            LinkNode sensor = (LinkNode)root.Nodes[0];
            sensor.Link.Joint.Type = Joint.AutomaticallyDetectType;
            sensor.Link.JointKinematicsDirty = false;

            LinkTreeSession session = new LinkTreeSession(root);

            Assert.True(session.RequiresJointKinematicsRecompute);
            Assert.True(session.RequiresJointLimitsRecompute);
            Assert.False(sensor.Link.Joint.ElementContainsData());
            Assert.False(sensor.Link.Joint.AreRequiredFieldsSatisfied());
        }

        [Fact]
        public void LegacyAutomaticJointTypeIsNormalizedOnCapture()
        {
            LinkNode root = CreateTree();
            ((LinkNode)root.Nodes[0]).Link.Joint.Type = "Automatically Generate";

            LinkNode projected = (LinkNode)new LinkTreeSession(root).CreateProjection().Nodes[0];

            Assert.Equal(Joint.AutomaticallyDetectType, projected.Link.Joint.Type);
        }

        [Fact]
        public void ChangingJointToFixedClearsMotionSpecificConfiguration()
        {
            LinkNode root = CreateTree();
            LinkNode sensor = (LinkNode)root.Nodes[0];
            sensor.Link.Joint.Type = "revolute";
            sensor.Link.Joint.Axis.SetXYZ(new[] { 1.0, 0.0, 0.0 });
            sensor.Link.Joint.Limit.Lower = -1.0;
            sensor.Link.Joint.Limit.Upper = 1.0;
            sensor.Link.Joint.Limit.Effort = 5.0;
            sensor.Link.Joint.Limit.Velocity = 2.0;
            sensor.Link.Joint.Dynamics.Damping = 0.1;
            sensor.Link.Joint.Safety.KVelocity = 1.0;
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            edited.Nodes.Single(node => node.Name == "sensor_link").JointType = "fixed";

            session.ApplyTree(edited);

            Joint joint = ((LinkNode)session.CreateProjection().Nodes[0]).Link.Joint;
            Assert.False(joint.Axis.ElementContainsData());
            Assert.False(joint.Limit.ElementContainsData());
            Assert.False(joint.Dynamics.ElementContainsData());
            Assert.False(joint.Safety.ElementContainsData());
            Assert.True(session.RequiresJointKinematicsRecompute);
            Assert.True(session.RequiresJointLimitsRecompute);
        }

        [Fact]
        public void ApplyingFixedJointTypeClearsLegacyMotionConfiguration()
        {
            LinkNode root = CreateTree();
            LinkNode sensor = (LinkNode)root.Nodes[0];
            sensor.Link.Joint.Axis.SetXYZ(new[] { 1.0, 0.0, 0.0 });
            sensor.Link.Joint.Dynamics.Damping = 0.1;
            LinkTreeSession session = new LinkTreeSession(root);

            session.ApplyTree(session.LoadTree());

            Joint joint = ((LinkNode)session.CreateProjection().Nodes[0]).Link.Joint;
            Assert.False(joint.Axis.ElementContainsData());
            Assert.False(joint.Dynamics.ElementContainsData());
        }

        [Fact]
        public void CapturedFixedJointConfigurationIsNormalizedBeforeProjection()
        {
            LinkNode root = CreateTree();
            LinkNode sensor = (LinkNode)root.Nodes[0];
            sensor.Link.Joint.Axis.SetXYZ(new[] { 1.0, 0.0, 0.0 });
            sensor.Link.Joint.Dynamics.Damping = 0.1;

            Joint joint = ((LinkNode)new LinkTreeSession(root).CreateProjection().Nodes[0]).Link.Joint;

            Assert.False(joint.Axis.ElementContainsData());
            Assert.False(joint.Dynamics.ElementContainsData());
        }

        [Fact]
        public void ChangingJointToContinuousPreservesVelocityConfigurationAndMimic()
        {
            LinkNode root = CreateTree();
            LinkNode sensor = (LinkNode)root.Nodes[0];
            sensor.Link.Joint.Type = "revolute";
            sensor.Link.Joint.Limit.Lower = -1.0;
            sensor.Link.Joint.Limit.Upper = 1.0;
            sensor.Link.Joint.Limit.Effort = 5.0;
            sensor.Link.Joint.Limit.Velocity = 2.0;
            sensor.Link.Joint.Mimic.Update("driver_joint", "1.0", "0.0");
            AddChild(root, "driver_link", "driver_joint").Link.Joint.Type = "continuous";
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            edited.Nodes.Single(node => node.Name == "sensor_link").JointType = "continuous";

            session.ApplyTree(edited);

            Joint joint = ((LinkNode)session.CreateProjection().Nodes[0]).Link.Joint;
            Assert.Equal(5.0, joint.Limit.Effort);
            Assert.Equal(2.0, joint.Limit.Velocity);
            Assert.False(joint.Limit.HasPositionBounds());
            Assert.Equal("driver_joint", joint.Mimic.JointName);
            Assert.True(session.RequiresJointKinematicsRecompute);
            Assert.True(session.RequiresJointLimitsRecompute);
        }

        [Fact]
        public void ChangingJointToPrismaticClearsStalePositionBounds()
        {
            LinkNode root = CreateTree();
            LinkNode sensor = (LinkNode)root.Nodes[0];
            sensor.Link.Joint.Type = "revolute";
            sensor.Link.Joint.Limit.Lower = -1.0;
            sensor.Link.Joint.Limit.Upper = 1.0;
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            edited.Nodes.Single(node => node.Name == "sensor_link").JointType = "prismatic";

            session.ApplyTree(edited);

            Joint joint = ((LinkNode)session.CreateProjection().Nodes[0]).Link.Joint;
            Assert.False(joint.Limit.HasPositionBounds());
        }

        [Fact]
        public void LegacyCaptureMarksChangedJointGeometryInputsStale()
        {
            LinkNode root = CreateTree();
            LinkNode sensor = (LinkNode)root.Nodes[0];
            sensor.Link.Joint.CoordinateSystemName = "origin_a";
            sensor.Link.Joint.AxisName = "axis_a";
            LinkTreeSession session = new LinkTreeSession(root);
            LinkNode visible = session.CreateActiveProjection();
            ((LinkNode)visible.Nodes[0]).Link.Joint.AxisName = "axis_b";

            session.CaptureTree(visible);

            Assert.True(session.RequiresJointKinematicsRecompute);
            Assert.True(session.RequiresJointLimitsRecompute);
        }

        [Fact]
        public void LegacyCaptureMarksChangedCadBindingStale()
        {
            LinkNode root = CreateTree();
            LinkNode sensor = (LinkNode)root.Nodes[0];
            sensor.Link.SWMainComponentPID = new byte[] { 1, 2, 3 };
            LinkTreeSession session = new LinkTreeSession(root);
            LinkNode visible = session.CreateActiveProjection();
            ((LinkNode)visible.Nodes[0]).Link.SWMainComponentPID = new byte[] { 4, 5, 6 };

            session.CaptureTree(visible);

            Assert.True(session.RequiresJointKinematicsRecompute);
            Assert.True(session.RequiresJointLimitsRecompute);
        }

        [Fact]
        public void RootCoordinateFrameChangeInvalidatesDescendantJoints()
        {
            LinkNode root = CreateTree();
            root.Link.Joint.CoordinateSystemName = "global_a";
            LinkTreeSession session = new LinkTreeSession(root);
            LinkNode visible = session.CreateActiveProjection();
            visible.Link.Joint.CoordinateSystemName = "global_b";

            session.CaptureTree(visible);

            Assert.True(session.RequiresJointKinematicsRecompute);
            Assert.True(session.RequiresJointLimitsRecompute);
        }

        [Fact]
        public void CopyKeepsUrdfConfigurationButClearsCadBindingState()
        {
            LinkNode root = CreateTree();
            LinkNode sourceNode = (LinkNode)root.Nodes[0];
            sourceNode.Link.Joint.AxisName = "sensor_axis";
            sourceNode.Link.Joint.CoordinateSystemName = "sensor_origin";
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            LinkTreeNode source = edited.Nodes.Single(node => node.Name == "sensor_link");
            LinkTreeNode copy = source.Clone();
            copy.Id = Guid.NewGuid();
            copy.CopySourceId = source.Id;
            copy.Name = "sensor_copy_link";
            copy.JointName = "sensor_copy_joint";
            edited.Nodes.Add(copy);

            session.ApplyTree(edited);

            LinkNode projectedCopy = session.CreateProjection().Nodes
                .Cast<LinkNode>()
                .Single(node => node.Link.Name == "sensor_copy_link");
            Assert.Equal("sensor_axis", projectedCopy.Link.Joint.AxisName);
            Assert.Equal("sensor_origin", projectedCopy.Link.Joint.CoordinateSystemName);
            Assert.Empty(projectedCopy.Link.SWComponents);
            Assert.True(projectedCopy.IsIncomplete);
            Assert.True(projectedCopy.Link.isIncomplete);
            Assert.True(session.RequiresJointKinematicsRecompute);
            Assert.True(session.RequiresJointLimitsRecompute);
        }

        [Fact]
        public void AcceptedComputationPreservesActualDirtyMarkers()
        {
            LinkNode root = CreateTree();
            ((LinkNode)root.Nodes[0]).Link.JointKinematicsDirty = true;
            ((LinkNode)root.Nodes[0]).Link.JointLimitsDirty = true;
            LinkTreeSession session = new LinkTreeSession(root);
            LinkNode computation = session.CreateComputationProjection();
            LinkNode computedChild = (LinkNode)computation.Nodes[0];
            computedChild.Link.JointKinematicsDirty = false;
            computedChild.Link.JointLimitsDirty = true;

            session.AcceptComputedProjection(computation);

            Assert.False(session.RequiresJointKinematicsRecompute);
            Assert.True(session.RequiresJointLimitsRecompute);
        }

        [Fact]
        public void ComputationProjectionCannotDeleteLinks()
        {
            LinkTreeSession session = new LinkTreeSession(CreateTree());
            LinkNode computation = session.CreateComputationProjection();
            computation.Nodes.RemoveAt(0);

            Assert.Throws<InvalidOperationException>(() =>
                session.AcceptComputedProjection(computation));
            Assert.Equal(2, session.LoadTree().Nodes.Count);
        }

        [Fact]
        public void ComputationProjectionCannotAddLinks()
        {
            LinkTreeSession session = new LinkTreeSession(CreateTree());
            LinkNode computation = session.CreateComputationProjection();
            AddChild(computation, "unexpected_link", "unexpected_joint");

            Assert.Throws<InvalidOperationException>(() =>
                session.AcceptComputedProjection(computation));
            Assert.Equal(2, session.LoadTree().Nodes.Count);
        }

        [Fact]
        public void ComputationProjectionCannotReparentLinks()
        {
            LinkNode root = CreateTree();
            AddChild(root, "mount_link", "mount_joint");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkNode computation = session.CreateComputationProjection();
            LinkNode sensor = computation.Nodes.Cast<LinkNode>()
                .Single(node => node.Link.Name == "sensor_link");
            LinkNode mount = computation.Nodes.Cast<LinkNode>()
                .Single(node => node.Link.Name == "mount_link");
            sensor.Remove();
            mount.Nodes.Add(sensor);

            Assert.Throws<InvalidOperationException>(() =>
                session.AcceptComputedProjection(computation));
            Assert.Equal(3, session.LoadTree().Nodes.Count);
        }

        [Fact]
        public void LinkNodeConstructorRestoresPersistedCompleteness()
        {
            Link complete = new Link { Name = "complete_link", isIncomplete = false };
            Link incomplete = new Link { Name = "incomplete_link", isIncomplete = true };

            Assert.False(new LinkNode(complete).IsIncomplete);
            Assert.True(new LinkNode(incomplete).IsIncomplete);
        }

        [Fact]
        public void IncompleteTraversalContinuesAcrossSiblingBranches()
        {
            LinkNode root = CreateTree();
            ((LinkNode)root.Nodes[0]).IsIncomplete = false;
            LinkNode incomplete = AddChild(root, "camera_link", "camera_joint");
            incomplete.IsIncomplete = true;

            LinkNode result = ExportPropertyManager.FindNextLinkToVisit(root);

            Assert.Same(incomplete, result);
        }

        [Fact]
        public void RepeatedCopiedGroupsKeepMimicReferencesInsideEachPasteBatch()
        {
            LinkNode root = CreateTree();
            ((LinkNode)root.Nodes[0]).Link.Joint.Type = "continuous";
            LinkNode follower = AddChild(root, "follower_link", "follower_joint");
            follower.Link.Joint.Type = "continuous";
            follower.Link.Joint.Mimic.Update("sensor_joint", "1.0", "0.0");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            LinkTreeNode sourceLeader = edited.Nodes.Single(node => node.Name == "sensor_link");
            LinkTreeNode sourceFollower = edited.Nodes.Single(node => node.Name == "follower_link");

            Guid firstBatch = Guid.NewGuid();
            AddCopiedNode(edited, sourceLeader, firstBatch, "sensor_copy_1", "sensor_copy_1_joint");
            AddCopiedNode(edited, sourceFollower, firstBatch, "follower_copy_1", "follower_copy_1_joint");
            Guid secondBatch = Guid.NewGuid();
            AddCopiedNode(edited, sourceLeader, secondBatch, "sensor_copy_2", "sensor_copy_2_joint");
            AddCopiedNode(edited, sourceFollower, secondBatch, "follower_copy_2", "follower_copy_2_joint");

            session.ApplyTree(edited);

            LinkNode projection = session.CreateProjection();
            LinkNode firstFollower = projection.Nodes.Cast<LinkNode>()
                .Single(node => node.Link.Name == "follower_copy_1");
            LinkNode secondFollower = projection.Nodes.Cast<LinkNode>()
                .Single(node => node.Link.Name == "follower_copy_2");
            Assert.Equal("sensor_copy_1_joint", firstFollower.Link.Joint.Mimic.JointName);
            Assert.Equal("sensor_copy_2_joint", secondFollower.Link.Joint.Mimic.JointName);
        }

        [Fact]
        public void RenamingOriginalJointDoesNotRetargetCopiedMimicGroup()
        {
            LinkNode root = CreateTree();
            ((LinkNode)root.Nodes[0]).Link.Joint.Type = "continuous";
            LinkNode follower = AddChild(root, "follower_link", "follower_joint");
            follower.Link.Joint.Type = "continuous";
            follower.Link.Joint.Mimic.Update("sensor_joint", "1.0", "0.0");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            LinkTreeNode sourceLeader = edited.Nodes.Single(node => node.Name == "sensor_link");
            LinkTreeNode sourceFollower = edited.Nodes.Single(node => node.Name == "follower_link");
            sourceLeader.JointName = "renamed_sensor_joint";

            Guid copyBatch = Guid.NewGuid();
            AddCopiedNode(edited, sourceLeader, copyBatch, "sensor_copy", "sensor_joint");
            AddCopiedNode(
                edited,
                sourceFollower,
                copyBatch,
                "follower_copy",
                "follower_copy_joint");

            session.ApplyTree(edited);

            LinkNode projection = session.CreateProjection();
            LinkNode originalFollower = projection.Nodes.Cast<LinkNode>()
                .Single(node => node.Link.Name == "follower_link");
            LinkNode copiedFollower = projection.Nodes.Cast<LinkNode>()
                .Single(node => node.Link.Name == "follower_copy");
            Assert.Equal(
                "renamed_sensor_joint",
                originalFollower.Link.Joint.Mimic.JointName);
            Assert.Equal("sensor_joint", copiedFollower.Link.Joint.Mimic.JointName);
        }

        [Fact]
        public void CopiedMimicFollowerCannotRetargetADeletedJointByNameReuse()
        {
            LinkNode root = CreateTree();
            ((LinkNode)root.Nodes[0]).Link.Joint.Type = "continuous";
            LinkNode replacement = AddChild(root, "replacement_link", "replacement_joint");
            replacement.Link.Joint.Type = "continuous";
            LinkNode follower = AddChild(root, "follower_link", "follower_joint");
            follower.Link.Joint.Type = "continuous";
            follower.Link.Joint.Mimic.Update("sensor_joint", "1.0", "0.0");
            LinkTreeSession session = new LinkTreeSession(root);
            LinkTreeDocument edited = session.LoadTree();
            LinkTreeNode target = edited.Nodes.Single(node => node.JointName == "sensor_joint");
            LinkTreeNode sourceFollower = edited.Nodes.Single(
                node => node.JointName == "follower_joint");
            AddCopiedNode(
                edited,
                sourceFollower,
                Guid.NewGuid(),
                "follower_copy",
                "follower_copy_joint");
            edited.Nodes.Remove(target);
            edited.Nodes.Remove(sourceFollower);
            edited.Nodes.Single(node => node.Name == "replacement_link").JointName =
                "sensor_joint";

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => session.ApplyTree(edited));

            Assert.Contains("Copied Mimic target", error.Message);
            Assert.Equal(4, session.LoadTree().Nodes.Count);
        }

        [Fact]
        public void SharedMimicValidatorRejectsFinalFormCycles()
        {
            Joint leader = new Joint { Name = "leader_joint", Type = "continuous" };
            Joint follower = new Joint { Name = "follower_joint", Type = "continuous" };
            leader.Mimic.Update("follower_joint", "1.0", "0.0");
            follower.Mimic.Update("leader_joint", "1.0", "0.0");

            string errors = string.Join(" ", MimicGraphValidator.Validate(
                new[] { leader, follower }));

            Assert.Contains("contain a cycle", errors);
        }

        [Fact]
        public void DetectedJointTypeDoesNotOverrideAnExplicitType()
        {
            Assert.Equal(
                "revolute",
                JointConfigurationPolicy.ResolveDetectedType("revolute", "prismatic"));
            Assert.Equal(
                "prismatic",
                JointConfigurationPolicy.ResolveDetectedType(
                    Joint.AutomaticallyDetectType,
                    "prismatic"));
        }

        [Fact]
        public void LimitRecomputationClearsStaleBoundsForTheSameJointType()
        {
            Joint joint = new Joint { Type = "revolute" };
            joint.Limit.Lower = -1.0;
            joint.Limit.Upper = 1.0;
            joint.Limit.Effort = 4.0;
            joint.Limit.Velocity = 2.0;

            JointConfigurationPolicy.PrepareLimitRecomputation(joint);

            Assert.False(joint.Limit.HasPositionBounds());
            Assert.Equal(4.0, joint.Limit.Effort);
            Assert.Equal(2.0, joint.Limit.Velocity);
        }

        [Fact]
        public void JointUnitTransitionClearsDimensionedValuesButKeepsMimicTarget()
        {
            Joint joint = new Joint { Type = "revolute" };
            joint.Limit.Lower = -1.0;
            joint.Limit.Upper = 1.0;
            joint.Limit.Effort = 4.0;
            joint.Limit.Velocity = 2.0;
            joint.Calibration.Rising = 0.2;
            joint.Dynamics.Damping = 0.3;
            joint.Safety.SoftLower = -0.8;
            joint.Mimic.Update("leader_joint", "-1.0", "0.4");

            JointConfigurationPolicy.Apply(joint, "prismatic");

            Assert.Equal("prismatic", joint.Type);
            Assert.False(joint.Limit.ElementContainsData());
            Assert.False(joint.Calibration.ElementContainsData());
            Assert.False(joint.Dynamics.ElementContainsData());
            Assert.False(joint.Safety.ElementContainsData());
            Assert.Equal("leader_joint", joint.Mimic.JointName);
            Assert.Equal(-1.0, joint.Mimic.Multiplier);
            FieldInfo offsetField = typeof(Mimic).GetField(
                "OffsetAttribute",
                BindingFlags.NonPublic | BindingFlags.Instance);
            URDFAttribute offset = (URDFAttribute)offsetField.GetValue(joint.Mimic);
            Assert.Null(offset.Value);
        }

        [Fact]
        public void JointMotionUnitAndAxisPoliciesMatchUrdfJointTypes()
        {
            Assert.True(JointConfigurationPolicy.ChangesMotionUnits(
                "revolute",
                "prismatic"));
            Assert.True(JointConfigurationPolicy.ChangesMotionUnits(
                "prismatic",
                "continuous"));
            Assert.False(JointConfigurationPolicy.ChangesMotionUnits(
                "revolute",
                "continuous"));
            Assert.False(JointConfigurationPolicy.RequiresMotionAxis("fixed"));
            Assert.False(JointConfigurationPolicy.RequiresMotionAxis("floating"));
            Assert.True(JointConfigurationPolicy.RequiresMotionAxis("revolute"));
            Assert.True(JointConfigurationPolicy.RequiresMotionAxis("prismatic"));
            Assert.True(JointConfigurationPolicy.RequiresMotionAxis("planar"));
        }

        [Fact]
        public void MotionJointsRequireAFiniteNonzeroAxis()
        {
            Joint joint = new Joint { Name = "wheel_joint", Type = "continuous" };

            Assert.Equal(new[] { 1.0, 0.0, 0.0 }, joint.Axis.GetXYZ());
            Assert.True(joint.AreRequiredFieldsSatisfied());

            joint.Axis.SetXYZ(new[] { 0.0, 0.0, 0.0 });
            Assert.False(joint.AreRequiredFieldsSatisfied());

            joint.Axis.SetXYZ(new[] { double.NaN, 0.0, 0.0 });
            Assert.False(joint.AreRequiredFieldsSatisfied());
        }

        [Fact]
        public void AxislessJointRemainsValidAfterMotionConfigurationIsCleared()
        {
            Joint joint = new Joint { Name = "mount_joint", Type = "continuous" };

            JointConfigurationPolicy.Apply(joint, "fixed");

            Assert.False(joint.Axis.ElementContainsData());
            Assert.True(joint.AreRequiredFieldsSatisfied());
        }

        [Fact]
        public void TopLevelReferenceAxisIsPreservedWithoutAComponentTransform()
        {
            MethodInfo method = typeof(ExportHelper).GetMethods(
                    BindingFlags.NonPublic | BindingFlags.Static)
                .Single(item => item.Name == "GlobalAxis" &&
                    item.GetParameters()[1].ParameterType.Name.StartsWith("Matrix"));

            double[] transformed = (double[])method.Invoke(
                null,
                new object[] { new[] { 0.0, 1.0, 0.0 }, null });

            Assert.Equal(new[] { 0.0, 1.0, 0.0 }, transformed);
        }

        [Fact]
        public void JointDofClassifierRejectsUnknownAndMultiAxisResults()
        {
            string detectedType;
            Assert.True(JointConfigurationPolicy.TryClassifyDetectedType(
                0, 0, 0, 0, 0, out detectedType));
            Assert.Equal("fixed", detectedType);
            Assert.True(JointConfigurationPolicy.TryClassifyDetectedType(
                0, 1, 0, 0, 0, out detectedType));
            Assert.Equal("continuous", detectedType);
            Assert.True(JointConfigurationPolicy.TryClassifyDetectedType(
                0, 0, 0, 1, 0, out detectedType));
            Assert.Equal("prismatic", detectedType);
            Assert.False(JointConfigurationPolicy.TryClassifyDetectedType(
                1, 0, 0, 0, 0, out detectedType));
            Assert.False(JointConfigurationPolicy.TryClassifyDetectedType(
                0, 1, 1, 0, 0, out detectedType));
            Assert.False(JointConfigurationPolicy.TryClassifyDetectedType(
                0, 1, 0, 1, 0, out detectedType));
            Assert.False(JointConfigurationPolicy.TryClassifyDetectedType(
                0, 2, 0, 0, 0, out detectedType));
        }

        [Fact]
        public void ExplicitMotionTypeMustMatchDetectedDegreeOfFreedom()
        {
            Assert.True(JointConfigurationPolicy.IsDetectedTypeCompatible(
                "revolute", "continuous"));
            Assert.True(JointConfigurationPolicy.IsDetectedTypeCompatible(
                Joint.AutomaticallyDetectType, "prismatic"));
            Assert.True(JointConfigurationPolicy.IsDetectedTypeCompatible(
                "floating", "fixed"));
            Assert.False(JointConfigurationPolicy.IsDetectedTypeCompatible(
                "revolute", "prismatic"));
            Assert.False(JointConfigurationPolicy.IsDetectedTypeCompatible(
                "planar", "fixed"));
        }

        [Fact]
        public void AxislessJointTypesClearUrdfAxisButPreserveCadReferenceMetadata()
        {
            Joint joint = new Joint { Type = "revolute", AxisName = "Axis_wheel" };
            joint.Axis.SetXYZ(new[] { 1.0, 0.0, 0.0 });

            JointConfigurationPolicy.Apply(joint, "floating");

            Assert.Equal("Axis_wheel", joint.AxisName);
            Assert.False(joint.Axis.ElementContainsData());
        }

        [Fact]
        public void ClipboardValidationRejectsSourcesDeletedAfterCopy()
        {
            LinkTreeDocument document = CreateSampleDocument();
            LinkTreeNode copied = document.Nodes.First(node => node.ParentId.HasValue).Clone();
            document.Nodes.RemoveAll(node => node.Id == copied.Id);

            IList<string> errors = document.ValidateClipboardSources(new[] { copied });

            Assert.Contains(errors, error => error.Contains("复制源已不存在"));
        }

        [Fact]
        public void ClipboardValidationRejectsSourcesChangedAfterCopy()
        {
            LinkTreeDocument document = CreateSampleDocument();
            LinkTreeNode source = document.Nodes.First(node => node.ParentId.HasValue);
            LinkTreeNode copied = source.Clone();
            source.JointType = "continuous";

            IList<string> errors = document.ValidateClipboardSources(new[] { copied });

            Assert.Contains(errors, error => error.Contains("复制源已修改"));
        }

        [Fact]
        public void FailedJointComputationMarksKinematicsAndLimitsStale()
        {
            Link link = new Link
            {
                JointKinematicsDirty = false,
                JointLimitsDirty = false
            };

            ExportHelper.ApplyJointComputationResult(link, false);

            Assert.True(link.JointKinematicsDirty);
            Assert.True(link.JointLimitsDirty);
        }

        [Fact]
        public void SolidWorksSuppressionStateSupportsScalarAndArrayResults()
        {
            Assert.True(ExportHelper.ReadSuppressionState(true));
            Assert.False(ExportHelper.ReadSuppressionState(new[] { false }));
            Assert.Throws<InvalidOperationException>(() =>
                ExportHelper.ReadSuppressionState("suppressed"));
        }

        [Fact]
        public void UnexpectedComObjectsAreSkippedWithoutInvalidCastFailures()
        {
            object[] values = { new object(), null, "not a SolidWorks proxy" };

            Assert.Empty(CommonSwOperations.EnumerateComObjects<Component2>(
                values,
                "unit test"));
            Assert.Null(CommonSwOperations.TryCastComObject<Component2>(
                new object(),
                "unit test"));
        }

        [Fact]
        public void LiveCadSelectionChangeCannotBeHiddenByOldPersistentIds()
        {
            Component2 original = new Mock<Component2>().Object;
            Component2 replacement = new Mock<Component2>().Object;
            Link baseline = new Link
            {
                SWComponents = new List<Component2> { original },
                SWComponentPIDs = new List<byte[]> { new byte[] { 1, 2, 3 } }
            };
            CadBindingState state = CadBindingState.FromLink(baseline);
            Link candidate = new Link
            {
                SWComponents = new List<Component2> { replacement },
                SWComponentPIDs = new List<byte[]> { new byte[] { 1, 2, 3 } }
            };

            Assert.False(state.Matches(candidate));
        }

        [Fact]
        public void ConfigurationPayloadReadabilityRejectsCorruptCurrentData()
        {
            MethodInfo method = typeof(ConfigurationSerialization).GetMethod(
                "IsConfigurationPayloadReadable",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            Assert.False((bool)method.Invoke(null, new object[] { "<broken", 1.5 }));
        }

        [Fact]
        public void IncompleteConfigurationAttributeCannotProduceARollbackSnapshot()
        {
            Type snapshotType = typeof(ConfigurationSerialization).GetNestedType(
                "SaveAttributeSnapshot",
                BindingFlags.NonPublic);
            MethodInfo method = snapshotType.GetMethod(
                "TryCapture",
                BindingFlags.Public | BindingFlags.Static);
            Mock<SwAttribute> attribute = new Mock<SwAttribute>();
            object[] arguments = { attribute.Object, null };

            Assert.False((bool)method.Invoke(null, arguments));
            Assert.Null(arguments[1]);
        }

        [Fact]
        public void ReadableConfigurationWithoutOptionalMetadataCanBeRolledBack()
        {
            MethodInfo serialize = typeof(ConfigurationSerialization).GetMethod(
                "SerializeToString",
                BindingFlags.NonPublic | BindingFlags.Static);
            string payload = (string)serialize.Invoke(null, new object[] { CreateTree() });
            Mock<SwParameter> data = new Mock<SwParameter>();
            data.Setup(parameter => parameter.GetStringValue()).Returns(payload);
            Mock<SwParameter> version = new Mock<SwParameter>();
            version.Setup(parameter => parameter.GetDoubleValue()).Returns(1.5);
            Mock<SwAttribute> attribute = new Mock<SwAttribute>();
            attribute.Setup(item => item.GetParameter("data")).Returns(data.Object);
            attribute.Setup(item => item.GetParameter("exporterVersion"))
                .Returns(version.Object);
            Type snapshotType = typeof(ConfigurationSerialization).GetNestedType(
                "SaveAttributeSnapshot",
                BindingFlags.NonPublic);
            MethodInfo capture = snapshotType.GetMethod(
                "TryCapture",
                BindingFlags.Public | BindingFlags.Static);
            object[] arguments = { attribute.Object, null };

            Assert.True((bool)capture.Invoke(null, arguments));
            Assert.NotNull(arguments[1]);
            Assert.False((bool)snapshotType.GetProperty("IsComplete")
                .GetValue(arguments[1], null));
            Assert.Equal(
                payload,
                snapshotType.GetProperty("Data").GetValue(arguments[1], null));
        }

        [Fact]
        public void CorruptConfigurationPayloadStillProducesARollbackSnapshot()
        {
            Mock<SwParameter> data = new Mock<SwParameter>();
            data.Setup(parameter => parameter.GetStringValue()).Returns("<broken");
            Mock<SwParameter> version = new Mock<SwParameter>();
            version.Setup(parameter => parameter.GetDoubleValue()).Returns(1.5);
            Mock<SwAttribute> attribute = new Mock<SwAttribute>();
            attribute.Setup(item => item.GetParameter("data")).Returns(data.Object);
            attribute.Setup(item => item.GetParameter("exporterVersion"))
                .Returns(version.Object);
            Type snapshotType = typeof(ConfigurationSerialization).GetNestedType(
                "SaveAttributeSnapshot",
                BindingFlags.NonPublic);
            MethodInfo capture = snapshotType.GetMethod(
                "TryCapture",
                BindingFlags.Public | BindingFlags.Static);
            object[] arguments = { attribute.Object, null };

            Assert.True((bool)capture.Invoke(null, arguments));
            Assert.False((bool)snapshotType.GetProperty("IsComplete")
                .GetValue(arguments[1], null));
            Assert.Equal(
                "<broken",
                snapshotType.GetProperty("Data").GetValue(arguments[1], null));
        }

        [Fact]
        public void DirtyJointLimitsBlockTheExportProjection()
        {
            LinkNode root = CreateTree();
            LinkNode child = (LinkNode)root.Nodes[0];
            child.Link.JointLimitsDirty = true;
            MethodInfo method = typeof(ExportHelper).GetMethod(
                "FindJointComputationError",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            string error = (string)method.Invoke(null, new object[] { root });
            Assert.Contains("limits could not be recomputed", error);
        }

        [Fact]
        public void DirtyJointKinematicsBlockTheExportProjection()
        {
            LinkNode root = CreateTree();
            LinkNode child = (LinkNode)root.Nodes[0];
            child.Link.JointKinematicsDirty = true;
            MethodInfo method = typeof(ExportHelper).GetMethod(
                "FindJointComputationError",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            string error = (string)method.Invoke(null, new object[] { root });
            Assert.Contains("kinematics could not be recomputed", error);
        }

        [Fact]
        public void InvalidMotionAxisBlocksTheExportProjection()
        {
            LinkNode root = CreateTree();
            LinkNode child = (LinkNode)root.Nodes[0];
            child.Link.Joint.Type = "continuous";
            child.Link.Joint.Axis.SetXYZ(new[] { 0.0, 0.0, 0.0 });
            MethodInfo method = typeof(ExportHelper).GetMethod(
                "FindJointDataError",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            string error = (string)method.Invoke(null, new object[] { root });
            Assert.Contains("finite, nonzero axis", error);
        }

        [Fact]
        public void DefaultJointNamePolicyDoesNotClaimCustomJointNames()
        {
            Assert.True(LinkTreeDocument.UsesDefaultJointName(
                "sensor_joint",
                "base_link",
                "sensor_link"));
            Assert.True(LinkTreeDocument.UsesDefaultJointName(
                "base_link_sensor_link_joint",
                "base_link",
                "sensor_link"));
            Assert.False(LinkTreeDocument.UsesDefaultJointName(
                "steering_axis",
                "base_link",
                "sensor_link"));
        }

        [Theory]
        [InlineData("camera_link", "camera_joint")]
        [InlineData("imu", "imu_joint")]
        [InlineData("Left_front_wheel_link", "Left_front_wheel_joint")]
        public void DefaultJointNameIsDerivedOnlyFromLinkName(string linkName, string expected)
        {
            Assert.Equal(expected, LinkTreeDocument.BuildDefaultJointName(linkName));
        }

        private static LinkTreeDocument CreateSampleDocument()
        {
            LinkTreeDocument document = new LinkTreeDocument();
            LinkTreeNode root = LinkTreeDocument.NewNode("base_link", null, 90, 330);
            LinkTreeNode chassis = LinkTreeDocument.NewNode("chassis_link", root.Id, 390, 250);
            LinkTreeNode lidar = LinkTreeDocument.NewNode("lidar_link", chassis.Id, 710, 115);
            LinkTreeNode imu = LinkTreeDocument.NewNode("imu_link", chassis.Id, 710, 250);
            LinkTreeNode leftWheel = LinkTreeDocument.NewNode("left_wheel_link", chassis.Id, 710, 385);
            leftWheel.JointType = "continuous";
            LinkTreeNode rightWheel = LinkTreeDocument.NewNode("right_wheel_link", chassis.Id, 710, 520);
            rightWheel.JointType = "continuous";
            document.Nodes.AddRange(new[] { root, chassis, lidar, imu, leftWheel, rightWheel });
            return document;
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

        private static LinkNode AddChild(LinkNode parent, string linkName, string jointName)
        {
            LinkNode child = new LinkNode();
            child.Link.Name = linkName;
            child.Link.Joint.Name = jointName;
            child.Link.Joint.Type = "fixed";
            child.Name = linkName;
            child.Text = linkName;
            parent.Nodes.Add(child);
            return child;
        }

        private static void ReplaceProjectedLinks(LinkNode node, Link parent)
        {
            Link replacement = new Link();
            replacement.SetElement(node.Link);
            replacement.SetSWComponents(node.Link);
            replacement.Parent = parent;
            replacement.Children.Clear();
            node.Link = replacement;

            foreach (LinkNode child in node.Nodes)
            {
                ReplaceProjectedLinks(child, replacement);
                replacement.Children.Add(child.Link);
            }
        }

        private static void AddCopiedNode(
            LinkTreeDocument document,
            LinkTreeNode source,
            Guid copyBatchId,
            string linkName,
            string jointName)
        {
            LinkTreeNode copy = source.Clone();
            copy.Id = Guid.NewGuid();
            copy.CopySourceId = source.Id;
            copy.CopyBatchId = copyBatchId;
            copy.Name = linkName;
            copy.JointName = jointName;
            document.Nodes.Add(copy);
        }
    }
}
