using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using SolidWorks.Interop.swpublished;
using SW2URDF.UI;
using SW2URDF.UI.LinkTreeCanvas;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public class TestLinkTreeTransactions
    {
        [Fact]
        public void ProgrammaticCallbacksAndUnchangedCountLeaveRootAndSessionUntouched()
        {
            LinkTreeSession session = CreateSession(7);
            using (TreeView tree = new TreeView())
            {
                LinkNode root = session.CreateActiveProjection();
                tree.Nodes.Add(root);
                tree.SelectedNode = root;
                ExportPropertyManager manager = CreateManager(tree, session);
                manager.previouslySelectedNode = root;
                TreeSelectionUpdateGuard guard = GetGuard(manager);
                IPropertyManagerPage2Handler9 callbacks = manager;
                using (guard.Suppress())
                {
                    callbacks.OnNumberboxChanged(7, 8);
                    callbacks.OnTextboxChanged(2, "child_value");
                    callbacks.OnTextboxChanged(4, "child_joint");
                    manager.SaveActiveNode();
                }
                // No property controls are supplied: unchanged count must return before reading them.
                callbacks.OnNumberboxChanged(7, 7);
                Assert.Equal("base_link", root.Link.Name);
                Assert.Equal(string.Empty, root.Link.Joint.Name);
                Assert.Equal(7, root.Nodes.Count);
                Assert.Equal(0, session.Revision);
            }
        }

        [Fact]
        public void FailedLegacyUiPublicationRestoresOriginalTreeAndSelection()
        {
            LinkTreeSession session = CreateSession(1);
            using (TreeView tree = new TreeView())
            {
                LinkNode root = session.CreateActiveProjection();
                tree.Nodes.Add(root);
                tree.SelectedNode = root;
                ExportPropertyManager manager = CreateManager(tree, session);
                MethodInfo publish = typeof(ExportPropertyManager).GetMethod("PublishLinkTree", BindingFlags.Instance | BindingFlags.NonPublic);
                using (GetGuard(manager).Suppress())
                {
                    Assert.Throws<TargetInvocationException>(() => session.EditTree(root,
                        candidate => candidate.AddChild(candidate.Root.Id),
                        publish: candidate => publish.Invoke(manager, new object[] { candidate, (Guid?)session.LoadTree().Root.Id })));
                }
                Assert.Same(root, tree.Nodes[0]);
                Assert.Same(root, tree.SelectedNode);
                Assert.Equal(1, root.Nodes.Count);
                Assert.Equal(0, session.Revision);
                Assert.Same(session, typeof(ExportPropertyManager).GetField("linkTreeSession", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager));
            }
        }

        [Fact]
        public void CanvasApplyIsPendingUntilLegacyUiCanPublish()
        {
            LinkTreeSession session = CreateSession(1);
            Type pendingType = typeof(ExportPropertyManager).GetNestedType("PendingCanvasEdit", BindingFlags.NonPublic);
            ILinkTreeCanvasHost pending = (ILinkTreeCanvasHost)Activator.CreateInstance(pendingType, new object[] { session });
            LinkTreeDocument document = pending.LoadTree();
            document.AddChild(document.Root.Id);
            pending.ApplyTree(document);
            Assert.Equal(0, session.Revision);
            Assert.Equal(2, session.LoadTree().Nodes.Count);
            document.Nodes.Clear();
            LinkTreeDocument accepted = (LinkTreeDocument)pendingType.GetProperty("Document").GetValue(pending);
            Assert.Equal(3, accepted.Nodes.Count);
        }

        [Fact]
        public void SevenVisibleLinksBecomeEightInBothProjectionAndSession()
        {
            LinkTreeSession session = CreateSession(6);
            LinkNode visible = session.CreateActiveProjection();
            Assert.Equal(7, session.LoadTree().Nodes.Count);
            session.EditTree(visible, candidate => candidate.AddChild(candidate.Root.Id),
                publish: candidate => visible = candidate.CreateActiveProjection());
            Assert.Equal(8, session.LoadTree().Nodes.Count);
            Assert.Equal(8, visible.Nodes.Count + 1);
            Assert.Equal(string.Empty, ((LinkNode)visible.Nodes[6]).Link.Joint.Type);
            Assert.False(string.IsNullOrWhiteSpace(((LinkNode)visible.Nodes[6]).Link.Joint.Name));
        }

        [Fact]
        public void SevenToEightAndRepeatedAddsPublishMatchingDrafts()
        {
            LinkTreeSession session = CreateSession(7);
            using (TreeView tree = new TreeView())
            {
                tree.Nodes.Add(session.CreateActiveProjection());
                Guid rootId = session.LoadTree().Root.Id;
                for (int count = 8; count <= 11; count++)
                {
                    Assert.True(session.EditTree((LinkNode)tree.Nodes[0],
                        candidate => candidate.SetChildCount(rootId, count),
                        publish: candidate =>
                        {
                            tree.Nodes.Clear();
                            tree.Nodes.Add(candidate.CreateActiveProjection());
                        }));
                    Assert.Equal(count, tree.Nodes[0].Nodes.Count);
                    Assert.Equal(count + 1, session.LoadTree().Nodes.Count);
                }
                LinkTreeDocument result = session.LoadTree();
                Assert.Equal(result.Nodes.Count, result.Nodes.Select(node => node.Name).Distinct().Count());
                Assert.Equal(11, result.Nodes.Where(node => node.ParentId.HasValue)
                    .Select(node => node.JointName).Distinct().Count());
                Assert.All(result.Nodes.Skip(8), node => Assert.Equal(string.Empty, node.JointType));
                Assert.NotEmpty(session.DraftDiagnostics);
            }
        }

        [Fact]
        public void DeclinedRemovalPreservesVisibleNodesSessionAndRevision()
        {
            LinkTreeSession session = CreateSession(7);
            LinkNode visible = session.CreateActiveProjection();
            LinkTreeDocument before = session.LoadTree();
            int revision = session.Revision;
            bool published = false;
            Assert.False(session.EditTree(visible,
                candidate => candidate.SetChildCount(candidate.Root.Id, 2),
                candidate => false, candidate => published = true));
            Assert.False(published);
            Assert.Equal(7, visible.Nodes.Count);
            Assert.Equal(revision, session.Revision);
            Assert.Equal(LinkTreeOutline.Serialize(before), LinkTreeOutline.Serialize(session.LoadTree()));
            Assert.Same(visible, session.AppliedRoot);
        }

        [Fact]
        public void FailedPublisherDoesNotCommitCandidateOrConfigurationEdits()
        {
            LinkTreeSession session = CreateSession(1);
            LinkNode visible = session.CreateActiveProjection();
            LinkTreeDocument before = session.LoadTree();
            int revision = session.Revision;
            ((LinkNode)visible.Nodes[0]).Link.Inertial.Mass.Value = 9;
            Assert.Throws<InvalidOperationException>(() => session.EditTree(visible,
                candidate => candidate.AddChild(candidate.Root.Id),
                publish: candidate => { throw new InvalidOperationException("publish failed"); }));
            Assert.Equal(1, visible.Nodes.Count);
            Assert.Equal(revision, session.Revision);
            Assert.Equal(LinkTreeOutline.Serialize(before), LinkTreeOutline.Serialize(session.LoadTree()));
            Assert.Equal(2.5, ((LinkNode)session.CreateProjection().Nodes[0]).Link.Inertial.Mass.Value);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ReferencedTargetRemovalNeverPublishesOrMutatesVisibleTree(bool countChange)
        {
            LinkTreeSession session = CreateMimicSession();
            LinkNode visible = session.CreateActiveProjection();
            Guid targetId = session.LoadTree().Nodes.Last().Id;
            bool published = false;
            int revision = session.Revision;
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => session.EditTree(visible,
                candidate =>
                {
                    if (countChange) candidate.SetChildCount(candidate.Root.Id, 1);
                    else candidate.DeleteBranch(targetId);
                }, publish: candidate => published = true));
            Assert.Contains("was deleted", error.Message);
            Assert.False(published);
            Assert.Equal(2, visible.Nodes.Count);
            Assert.Equal(3, session.LoadTree().Nodes.Count);
            Assert.Equal(revision, session.Revision);
        }

        [Fact]
        public void RootSelfAndDescendantMovesAreRejectedWithoutPublication()
        {
            LinkTreeSession session = CreateSession(2);
            session.EditTree(null, candidate => candidate.AddChild(candidate.Nodes[1].Id));
            LinkTreeDocument before = session.LoadTree();
            Guid root = before.Root.Id;
            Guid child = before.Nodes[1].Id;
            Guid descendant = before.Nodes.Last().Id;
            foreach (Guid[] move in new[] { new[] { root, child }, new[] { child, child }, new[] { child, descendant } })
            {
                bool published = false;
                Assert.Throws<InvalidOperationException>(() => session.EditTree(null,
                    candidate => candidate.Reparent(move[0], move[1]),
                    publish: candidate => published = true));
                Assert.False(published);
                Assert.Equal(LinkTreeOutline.Serialize(before), LinkTreeOutline.Serialize(session.LoadTree()));
            }
        }

        [Fact]
        public void DescendantLookupOnMalformedCycleTerminates()
        {
            LinkTreeDocument document = CreateSession(2).LoadTree();
            document.Nodes[1].ParentId = document.Nodes[2].Id;
            document.Nodes[2].ParentId = document.Nodes[1].Id;
            Assert.Throws<InvalidOperationException>(() => document.IsDescendant(document.Nodes[1].Id, document.Root.Id));
        }

        [Fact]
        public void MissingNamesAndTypesStayDraftOnlyWithStableIds()
        {
            LinkTreeSession session = CreateSession(2);
            Guid[] ids = session.LoadTree().Nodes.Select(node => node.Id).ToArray();
            LinkNode visible = session.CreateActiveProjection();
            foreach (LinkNode child in visible.Nodes)
            {
                child.Link.Name = string.Empty;
                child.Link.Joint.Name = string.Empty;
                child.Link.Joint.Type = string.Empty;
            }
            session.CaptureTree(visible);
            session.ApplyTree(session.LoadTree());
            Assert.Equal(ids, session.LoadTree().Nodes.Select(node => node.Id));
            Assert.NotEmpty(session.DraftDiagnostics);
            Assert.NotEmpty(session.LoadTree().Validate());
            Assert.All(session.LoadTree().Nodes.Skip(1), node => Assert.Equal(string.Empty, node.JointType));
        }

        [Fact]
        public void ReferencedJointCannotLoseItsName()
        {
            LinkTreeSession session = CreateMimicSession();
            LinkTreeDocument before = session.LoadTree();
            Assert.Throws<InvalidOperationException>(() => session.EditTree(null,
                candidate => candidate.Nodes.Last().JointName = string.Empty));
            Assert.Equal(LinkTreeOutline.Serialize(before), LinkTreeOutline.Serialize(session.LoadTree()));
        }

        [Fact]
        public void RenameAndAddUsingHeadingIdentityRetainsCadAndConfiguration()
        {
            LinkTreeSession session = CreateSession(1);
            LinkTreeDocument before = session.LoadTree();
            string text = LinkTreeOutline.Serialize(before).Replace("part0_link", "renamed_link") + "\n## extra_link";
            LinkTreeOutlineParseResult parsed = LinkTreeOutline.Parse(text, before);
            Assert.True(parsed.IsValid, string.Join(" ", parsed.Errors));
            Assert.Equal(before.Nodes[1].Id, parsed.Document.Nodes.Single(node => node.Name == "renamed_link").Id);
            session.ApplyTree(parsed.Document);
            LinkNode renamed = session.CreateProjection().Nodes.Cast<LinkNode>().Single(node => node.Link.Name == "renamed_link");
            Assert.Equal(2.5, renamed.Link.Inertial.Mass.Value);
            Assert.Equal(new byte[] { 1, 2, 3 }, renamed.Link.SWMainComponentPID);
            Assert.Equal("configured_joint", renamed.Link.Joint.Name);
        }

        [Theory]
        [InlineData("# base_link\n## renamed_link\n## added_link")]
        [InlineData("# base_link\n## renamed_link")]
        public void UnmarkedRenameIsRejectedInsteadOfInventingIdentity(string text)
        {
            LinkTreeDocument source = CreateSession(1).LoadTree();
            string before = LinkTreeOutline.Serialize(source);
            LinkTreeOutlineParseResult parsed = LinkTreeOutline.Parse(text, source);
            Assert.False(parsed.IsValid);
            Assert.Contains("link-id", string.Join(" ", parsed.Errors));
            Assert.Equal(before, LinkTreeOutline.Serialize(source));
        }

        [Fact]
        public void EqualCountsDoNotInferMultipleRenamesByPosition()
        {
            LinkTreeDocument source = CreateSession(2).LoadTree();
            LinkTreeOutlineParseResult parsed = LinkTreeOutline.Parse("# base_link\n## x_link\n## y_link", source);
            Assert.False(parsed.IsValid);
            Assert.Contains("Ambiguous", string.Join(" ", parsed.Errors));
        }

        [Fact]
        public void StableIdsReserveRenamedCustomJointBeforeGeneratedJoint()
        {
            LinkTreeDocument source = CreateSession(2).LoadTree();
            source.Nodes[1].Name = "a_link";
            source.Nodes[1].JointName = "a_joint";
            source.Nodes[2].Name = "b_link";
            source.Nodes[2].JointName = "x_joint";
            string text = LinkTreeOutline.Serialize(source).Replace("a_link", "x_link").Replace("b_link", "y_link");
            LinkTreeOutlineParseResult parsed = LinkTreeOutline.Parse(text, source);
            Assert.True(parsed.IsValid, string.Join(" ", parsed.Errors));
            Assert.Equal("x_joint_1", parsed.Document.Find(source.Nodes[1].Id).JointName);
            Assert.Equal("x_joint", parsed.Document.Find(source.Nodes[2].Id).JointName);
        }

        [Fact]
        public void SwappingNamesWithIdsKeepsOwnersAndRootIdentity()
        {
            LinkTreeDocument source = CreateSession(2).LoadTree();
            string text = LinkTreeOutline.Serialize(source).Replace("part0_link", "temp_link")
                .Replace("part1_link", "part0_link").Replace("temp_link", "part1_link")
                .Replace("base_link", "robot_link");
            LinkTreeOutlineParseResult parsed = LinkTreeOutline.Parse(text, source);
            Assert.True(parsed.IsValid, string.Join(" ", parsed.Errors));
            Assert.Equal("part1_link", parsed.Document.Find(source.Nodes[1].Id).Name);
            Assert.Equal(source.Root.Id, parsed.Document.Root.Id);
        }

        [Fact]
        public void UnknownOrDuplicatedHeadingIdsAreNonDestructive()
        {
            LinkTreeDocument source = CreateSession(1).LoadTree();
            string before = LinkTreeOutline.Serialize(source);
            foreach (string text in new[] {
                before.Replace(source.Nodes[1].Id.ToString(), Guid.NewGuid().ToString()),
                before + "\n## other_link <!-- link-id:" + source.Nodes[1].Id + " -->" })
            {
                Assert.False(LinkTreeOutline.Parse(text, source).IsValid);
                Assert.Equal(before, LinkTreeOutline.Serialize(source));
            }
        }

        [Fact]
        public void ValidationAndCancelSnapshotsDoNotCommitWhileApplyDetachesSavedDraft()
        {
            LinkTreeSession session = CreateSession(1);
            LinkTreeDocument cancelled = session.LoadTree();
            cancelled.AddChild(cancelled.Root.Id);
            session.ValidateTree(cancelled);
            Assert.Equal(0, session.Revision);
            Assert.Equal(2, session.LoadTree().Nodes.Count);
            session.ApplyTree(cancelled);
            LinkNode saved = session.CreateProjection();
            cancelled.Nodes.Clear();
            session.EditTree(null, candidate => candidate.AddChild(candidate.Root.Id));
            Assert.Equal(2, saved.Nodes.Count);
            Assert.Equal(4, session.LoadTree().Nodes.Count);
            Assert.Equal(3, new LinkTreeSession(saved).LoadTree().Nodes.Count);
        }

        private static LinkTreeSession CreateSession(int count)
        {
            LinkNode root = new LinkNode { IsBaseNode = true };
            root.Link.Name = "base_link";
            for (int index = 0; index < count; index++)
            {
                LinkNode child = new LinkNode();
                child.Link.Name = "part" + index + "_link";
                child.Link.Joint.Name = index == 0 ? "configured_joint" : "part" + index + "_joint";
                child.Link.Joint.Type = "continuous";
                child.Link.Inertial.Mass.Value = 2.5;
                child.Link.SWMainComponentPID = new byte[] { 1, 2, 3 };
                root.Nodes.Add(child);
            }
            return new LinkTreeSession(root);
        }

        private static ExportPropertyManager CreateManager(TreeView tree, LinkTreeSession session)
        {
            ExportPropertyManager manager = (ExportPropertyManager)FormatterServices.GetUninitializedObject(typeof(ExportPropertyManager));
            manager.Tree = tree;
            typeof(ExportPropertyManager).GetField("treeSelectionUpdateGuard", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, new TreeSelectionUpdateGuard());
            typeof(ExportPropertyManager).GetField("linkTreeSession", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, session);
            return manager;
        }

        private static TreeSelectionUpdateGuard GetGuard(ExportPropertyManager manager)
        {
            return (TreeSelectionUpdateGuard)typeof(ExportPropertyManager)
                .GetField("treeSelectionUpdateGuard", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
        }

        private static LinkTreeSession CreateMimicSession()
        {
            LinkTreeSession session = CreateSession(2);
            LinkNode root = session.CreateActiveProjection();
            ((LinkNode)root.Nodes[0]).Link.Joint.Mimic.Update("part1_joint", "1.0", "0.0");
            session.CaptureTree(root);
            return session;
        }
    }
}
