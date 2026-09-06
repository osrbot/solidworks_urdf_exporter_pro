using System;
using System.Linq;
using SW2URDF.UI.LinkTreeCanvas;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public class TestMimicCaptureRegressions
    {
        [Fact]
        public void RenameThenCaptureTwiceKeepsTheSameActiveProjectionConsistent()
        {
            LinkTreeSession session = CreateSession();
            LinkNode visible = session.CreateActiveProjection();
            Guid?[] ids = visible.Nodes.Cast<LinkNode>()
                .Select(node => session.GetProjectionNodeId(node.Link)).ToArray();
            Mimic mimic = Child(visible, 1).Joint.Mimic;
            Child(visible, 0).Joint.Name = "renamed_joint";

            for (int capture = 1; capture <= 2; capture++)
            {
                session.CaptureTree(visible);
                AssertMimic(session, visible, "renamed_joint", 2.0, 0.5);
                Assert.Same(mimic, Child(visible, 1).Joint.Mimic);
                Assert.Equal(ids, visible.Nodes.Cast<LinkNode>()
                    .Select(node => session.GetProjectionNodeId(node.Link)).ToArray());
                Assert.Equal(capture, session.Revision);
            }
        }

        [Fact]
        public void OpenCancelAndReopenEquivalentCapturesDoNotRestoreTheOldReference()
        {
            LinkTreeSession session = CreateSession();
            LinkNode visible = session.CreateActiveProjection();
            Child(visible, 0).Joint.Name = "renamed_joint";

            for (int open = 0; open < 2; open++)
            {
                // The canvas captures the active tree, edits a detached draft, then
                // discards that draft on cancel without replacing the active tree.
                session.CaptureTree(visible);
                LinkTreeDocument cancelled = session.LoadTree();
                cancelled.Nodes.Single(node => node.Name == "sensor_link").JointName = "cancelled_joint";
                session.ValidateTree(cancelled);
                AssertMimic(session, visible, "renamed_joint", 2.0, 0.5);
                Assert.Equal("renamed_joint", session.LoadTree().Nodes
                    .Single(node => node.Name == "sensor_link").JointName);
                Assert.Equal(open + 1, session.Revision);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExplicitReferenceAndParameterEditsArePreserved(bool afterCapture)
        {
            LinkTreeSession session = CreateSession();
            LinkNode visible = session.CreateActiveProjection();
            Child(visible, 0).Joint.Name = "renamed_joint";
            if (afterCapture) session.CaptureTree(visible);
            Child(visible, 1).Joint.Mimic.Update("other_joint", "3.0", "-0.25");
            Child(visible, 1).Inertial.Mass.Value = 9.0;

            for (int capture = 0; capture < 2; capture++)
            {
                session.CaptureTree(visible);
                AssertMimic(session, visible, "other_joint", 3.0, -0.25);
                Assert.Equal(9.0, Child(visible, 1).Inertial.Mass.Value);
                Assert.Equal(9.0, Child(session.CreateProjection(), 1).Inertial.Mass.Value);
            }
        }

        [Fact]
        public void RenameMigrationPreservesExplicitMultiplierAndOffsetEdits()
        {
            LinkTreeSession session = CreateSession();
            LinkNode visible = session.CreateActiveProjection();
            Child(visible, 0).Joint.Name = "renamed_joint";
            Child(visible, 1).Joint.Mimic.Multiplier = 4.0;
            Child(visible, 1).Joint.Mimic.Offset = -2.0;
            session.CaptureTree(visible);
            session.CaptureTree(visible);
            AssertMimic(session, visible, "renamed_joint", 4.0, -2.0);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExplicitMimicClearIsNotUndone(bool afterCapture)
        {
            LinkTreeSession session = CreateSession();
            LinkNode visible = session.CreateActiveProjection();
            Child(visible, 0).Joint.Name = "renamed_joint";
            if (afterCapture) session.CaptureTree(visible);
            Child(visible, 1).Joint.Mimic.Clear();
            session.CaptureTree(visible);
            session.CaptureTree(visible);
            Assert.Null(Child(visible, 1).Joint.Mimic.JointName);
            Assert.Null(Child(session.CreateProjection(), 1).Joint.Mimic.JointName);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedCaptureLeavesStoreProjectionAndRevisionUnchanged(bool invalidReference)
        {
            LinkTreeSession session = CreateSession();
            LinkNode visible = session.CreateActiveProjection();
            string before = LinkTreeOutline.Serialize(session.LoadTree());
            Guid? followerId = session.GetProjectionNodeId(Child(visible, 1));
            Child(visible, 0).Joint.Name = "renamed_joint";
            Child(visible, 1).Inertial.Mass.Value = 9.0;
            if (invalidReference)
                Child(visible, 2).Joint.Mimic.Update("missing_joint", "1.0", "0.0");
            else
                Child(visible, 2).Name = Child(visible, 1).Name;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                Assert.Throws<InvalidOperationException>(() => session.CaptureTree(visible));
                Assert.Equal(before, LinkTreeOutline.Serialize(session.LoadTree()));
                Assert.Equal(0, session.Revision);
                Assert.Same(visible, session.AppliedRoot);
                Assert.Equal(followerId, session.GetProjectionNodeId(Child(visible, 1)));
                Assert.Equal("sensor_joint", Child(visible, 1).Joint.Mimic.JointName);
                Assert.Equal("renamed_joint", Child(visible, 0).Joint.Name);
                Assert.Equal(9.0, Child(visible, 1).Inertial.Mass.Value);
                LinkNode stored = session.CreateProjection();
                Assert.Equal("sensor_joint", Child(stored, 0).Joint.Name);
                Assert.Equal("sensor_joint", Child(stored, 1).Joint.Mimic.JointName);
                Assert.Equal(2.5, Child(stored, 1).Inertial.Mass.Value);
            }

            if (invalidReference) Child(visible, 2).Joint.Mimic.Clear();
            else Child(visible, 2).Name = "other_link";
            session.CaptureTree(visible);
            session.CaptureTree(visible);
            AssertMimic(session, visible, "renamed_joint", 2.0, 0.5);
        }

        [Theory]
        [InlineData("cancel")]
        [InlineData("edit")]
        [InlineData("validate")]
        [InlineData("publish")]
        public void RejectedTransactionDoesNotLeakCandidateMigrationIntoSharedProjection(string failure)
        {
            LinkTreeSession session = CreateSession();
            LinkNode visible = session.CreateActiveProjection();
            string before = LinkTreeOutline.Serialize(session.LoadTree());
            Child(visible, 0).Joint.Name = "renamed_joint";
            Action<LinkTreeDocument> edit = document =>
            {
                if (failure == "edit") throw new InvalidOperationException("edit failed");
                if (failure == "validate")
                    document.Nodes.Single(node => node.Name == "sensor_link").JointName = string.Empty;
            };
            Action<LinkTreeSession> publish = candidate =>
            {
                Assert.Equal("renamed_joint", Child(candidate.AppliedRoot, 1).Joint.Mimic.JointName);
                Assert.Equal("sensor_joint", Child(visible, 1).Joint.Mimic.JointName);
                throw new InvalidOperationException("publish failed");
            };

            if (failure == "cancel")
                Assert.False(session.EditTree(visible, edit, document => false, publish));
            else
                Assert.Throws<InvalidOperationException>(() => session.EditTree(visible, edit, publish: publish));

            Assert.Equal(before, LinkTreeOutline.Serialize(session.LoadTree()));
            Assert.Equal(0, session.Revision);
            Assert.Same(visible, session.AppliedRoot);
            Assert.Equal("sensor_joint", Child(visible, 1).Joint.Mimic.JointName);
            Assert.Equal("renamed_joint", Child(visible, 0).Joint.Name);
            Assert.Equal("sensor_joint", Child(session.CreateProjection(), 1).Joint.Mimic.JointName);
            session.CaptureTree(visible);
            session.CaptureTree(visible);
            AssertMimic(session, visible, "renamed_joint", 2.0, 0.5);
        }

        [Fact]
        public void SuccessfulTransactionPublishesMigratedProjectionWithoutMutatingOldTree()
        {
            LinkTreeSession session = CreateSession();
            LinkNode original = session.CreateActiveProjection();
            Child(original, 0).Joint.Name = "renamed_joint";
            Assert.True(session.EditTree(original, document => { }));
            LinkNode active = session.AppliedRoot;
            Assert.NotSame(original, active);
            Assert.Equal("sensor_joint", Child(original, 1).Joint.Mimic.JointName);
            session.CaptureTree(active);
            session.CaptureTree(active);
            AssertMimic(session, active, "renamed_joint", 2.0, 0.5);
        }

        private static void AssertMimic(LinkTreeSession session, LinkNode visible,
            string reference, double multiplier, double offset)
        {
            Assert.Same(visible, session.AppliedRoot);
            foreach (LinkNode root in new[] { visible, session.CreateProjection() })
            {
                Mimic mimic = Child(root, 1).Joint.Mimic;
                Assert.Equal(reference, mimic.JointName);
                Assert.Equal(multiplier, mimic.Multiplier);
                Assert.Equal(offset, mimic.Offset);
            }
        }

        private static Link Child(LinkNode root, int index)
        {
            return ((LinkNode)root.Nodes[index]).Link;
        }

        private static LinkTreeSession CreateSession()
        {
            LinkNode root = new LinkNode { IsBaseNode = true };
            root.Link.Name = "base_link";
            foreach (string name in new[] { "sensor", "follower", "other" })
            {
                LinkNode child = new LinkNode();
                child.Link.Name = name + "_link";
                child.Link.Joint.Name = name + "_joint";
                child.Link.Joint.Type = "continuous";
                child.Link.Inertial.Mass.Value = 2.5;
                root.Nodes.Add(child);
            }
            Child(root, 1).Joint.Mimic.Update("sensor_joint", "2.0", "0.5");
            return new LinkTreeSession(root);
        }
    }
}
