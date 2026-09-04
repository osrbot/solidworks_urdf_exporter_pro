using System;
using System.Linq;
using SW2URDF.UI.LinkTreeCanvas;
using Xunit;

namespace SW2URDF.Test
{
    public class TestLinkTreeDocumentTransactions
    {
        [Fact]
        public void ChildCountGrowsSevenToEightWithoutIncompleteNameCollision()
        {
            LinkTreeDocument source = CreateDocument();
            source.SetChildCount(source.Root.Id, 7);
            LinkTreeDocument candidate = source.Clone();
            candidate.SetChildCount(candidate.Root.Id, 8);
            Assert.Equal(8, source.Nodes.Count);
            Assert.Equal(9, candidate.Nodes.Count);
            Assert.Equal(9, candidate.Nodes.Select(node => node.Name).Distinct().Count());
            Assert.Equal(8, candidate.Nodes.Skip(1).Select(node => node.JointName).Distinct().Count());
            Assert.All(candidate.Nodes.Skip(3), node => Assert.Equal(string.Empty, node.JointType));
            Assert.Empty(candidate.ValidateDraft());
            Assert.NotEmpty(candidate.Validate());
        }

        [Fact]
        public void MissingNamesHaveStableIdentityButFailExportValidation()
        {
            LinkTreeDocument document = CreateDocument();
            Guid[] ids = document.Nodes.Select(node => node.Id).ToArray();
            foreach (LinkTreeNode node in document.Nodes.Skip(1))
            {
                node.Name = "";
                node.JointName = "";
                node.JointType = "";
            }
            Assert.Empty(document.ValidateDraft());
            Assert.NotEmpty(document.Validate());
            Assert.Equal(ids, document.Clone().Nodes.Select(node => node.Id));
            LinkTreeOutlineParseResult parsed = LinkTreeOutline.Parse(LinkTreeOutline.Serialize(document), document);
            Assert.True(parsed.IsValid, string.Join(" ", parsed.Errors));
            Assert.Equal(ids, parsed.Document.Nodes.Select(node => node.Id));
        }

        [Fact]
        public void InvalidMovesAndRootDeletionDoNotChangeDocument()
        {
            LinkTreeDocument document = CreateDocument();
            LinkTreeNode descendant = document.AddChild(document.Nodes[1].Id);
            string before = LinkTreeOutline.Serialize(document);
            Assert.Throws<InvalidOperationException>(() => document.Reparent(document.Root.Id, descendant.Id));
            Assert.Throws<InvalidOperationException>(() => document.Reparent(document.Nodes[1].Id, descendant.Id));
            Assert.Throws<InvalidOperationException>(() => document.Reparent(descendant.Id, descendant.Id));
            Assert.Throws<InvalidOperationException>(() => document.DeleteBranch(document.Root.Id));
            Assert.Equal(before, LinkTreeOutline.Serialize(document));
        }

        [Fact]
        public void CyclicDescendantSearchTerminatesWithError()
        {
            LinkTreeDocument document = CreateDocument();
            document.Nodes[1].ParentId = document.Nodes[2].Id;
            document.Nodes[2].ParentId = document.Nodes[1].Id;
            Assert.Throws<InvalidOperationException>(() => document.IsDescendant(document.Nodes[1].Id, document.Root.Id));
            Assert.NotEmpty(document.ValidateDraft());
        }

        [Fact]
        public void RenamePlusAddRetainsExplicitIdentityAndJointConfiguration()
        {
            LinkTreeDocument document = CreateDocument();
            string before = LinkTreeOutline.Serialize(document);
            LinkTreeOutlineParseResult parsed = LinkTreeOutline.Parse(
                before.Replace("a_link", "renamed_link") + "\n## extra_link", document);
            Assert.True(parsed.IsValid, string.Join(" ", parsed.Errors));
            Assert.Equal(document.Nodes[1].Id, parsed.Document.Nodes.Single(node => node.Name == "renamed_link").Id);
            Assert.Equal("continuous", parsed.Document.Find(document.Nodes[1].Id).JointType);
            Assert.Equal(before, LinkTreeOutline.Serialize(document));
        }

        [Theory]
        [InlineData("# base_link\n## renamed_link\n## b_link\n## extra_link")]
        [InlineData("# base_link\n## x_link\n## y_link")]
        [InlineData("# base_link\n## renamed_link\n## b_link")]
        public void UnmarkedAmbiguousChangesAreRejected(string text)
        {
            LinkTreeDocument document = CreateDocument();
            string before = LinkTreeOutline.Serialize(document);
            LinkTreeOutlineParseResult parsed = LinkTreeOutline.Parse(text, document);
            Assert.False(parsed.IsValid);
            Assert.Contains("link-id", string.Join(" ", parsed.Errors));
            Assert.Equal(before, LinkTreeOutline.Serialize(document));
        }

        [Fact]
        public void RenamedCustomJointIsReservedByStableOwner()
        {
            LinkTreeDocument document = CreateDocument();
            document.Nodes[2].JointName = "x_joint";
            string text = LinkTreeOutline.Serialize(document).Replace("a_link", "x_link").Replace("b_link", "y_link");
            LinkTreeOutlineParseResult parsed = LinkTreeOutline.Parse(text, document);
            Assert.True(parsed.IsValid, string.Join(" ", parsed.Errors));
            Assert.Equal("x_joint_1", parsed.Document.Find(document.Nodes[1].Id).JointName);
            Assert.Equal("x_joint", parsed.Document.Find(document.Nodes[2].Id).JointName);
        }

        [Fact]
        public void PlainExistingNamesCanMoveAndRootCanRename()
        {
            LinkTreeDocument document = CreateDocument();
            LinkTreeOutlineParseResult parsed = LinkTreeOutline.Parse("# robot_link\n## b_link\n### a_link", document);
            Assert.True(parsed.IsValid, string.Join(" ", parsed.Errors));
            Assert.Equal(document.Root.Id, parsed.Document.Root.Id);
            Assert.Equal(document.Nodes[2].Id, parsed.Document.Find(document.Nodes[1].Id).ParentId);
        }

        [Fact]
        public void DuplicateOrUnknownIdentityAndRootReplacementAreRejected()
        {
            LinkTreeDocument document = CreateDocument();
            string text = LinkTreeOutline.Serialize(document);
            Assert.False(LinkTreeOutline.Parse(text.Replace(document.Nodes[1].Id.ToString(), Guid.NewGuid().ToString()), document).IsValid);
            Assert.False(LinkTreeOutline.Parse(text.Replace(document.Nodes[1].Id.ToString(), document.Nodes[2].Id.ToString()), document).IsValid);
            string swapped = text.Replace(document.Root.Id.ToString(), "ROOT_ID")
                .Replace(document.Nodes[1].Id.ToString(), document.Root.Id.ToString())
                .Replace("ROOT_ID", document.Nodes[1].Id.ToString());
            Assert.False(LinkTreeOutline.Parse(swapped, document).IsValid);
            Assert.False(LinkTreeOutline.Parse("# a_link\n## b_link", document).IsValid);
        }

        private static LinkTreeDocument CreateDocument()
        {
            LinkTreeDocument document = new LinkTreeDocument();
            LinkTreeNode root = LinkTreeDocument.NewNode("base_link", null, 0, 0);
            document.Nodes.Add(root);
            LinkTreeNode first = LinkTreeDocument.NewNode("a_link", root.Id, 300, 0);
            first.JointType = "continuous";
            document.Nodes.Add(first);
            LinkTreeNode second = LinkTreeDocument.NewNode("b_link", root.Id, 300, 118);
            second.JointType = "fixed";
            document.Nodes.Add(second);
            return document;
        }
    }
}
