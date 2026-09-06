using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using SW2URDF.UI.LinkTreeCanvas;
using Xunit;

namespace SW2URDF.Test
{
    public class TestCanvasReviewRegressions
    {
        private const string CycleError = "Link \u6811\u4e2d\u5b58\u5728\u5faa\u73af\u5173\u7cfb\u3002";
        private const string IdError = "Link \u8282\u70b9\u6807\u8bc6\u4e0d\u80fd\u91cd\u590d\u3002";

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void MultiSelectionNeverCommitsPlaceholderFields(bool paste, bool loseFocus)
        {
            RunSta(() =>
            {
                CanvasHost host = new CanvasHost { Source = Chain(3) };
                LinkTreeCanvasWindow window = new LinkTreeCanvasWindow(host);
                try
                {
                    Invoke(window, "SelectNode", host.Source.Nodes[1].Id, false);
                    Invoke(window, "SelectNode", host.Source.Nodes[2].Id, true);
                    if (paste)
                    {
                        Invoke(window, "CopySelected");
                        Invoke(window, "PasteCopied");
                        Assert.Equal(5, Document(window).Nodes.Count);
                        Assert.Equal(2, Document(window).Nodes.Count(node => node.CopySourceId.HasValue));
                    }
                    LinkTreeDocument expected = Document(window).Clone();
                    TextBox linkName = (TextBox)window.FindName("LinkNameTextBox");
                    TextBox jointName = (TextBox)window.FindName("JointNameTextBox");
                    ComboBox jointType = (ComboBox)window.FindName("JointTypeComboBox");
                    Assert.False(linkName.IsEnabled);
                    Assert.False(jointName.IsEnabled);
                    Assert.False(jointType.IsEnabled);
                    Assert.Equal(string.Empty, jointName.Text);

                    if (loseFocus)
                    {
                        linkName.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                        jointName.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                        // Disabled controls can still receive programmatic selection events.
                        jointType.SelectedIndex = 0;
                        AssertFieldsEqual(expected, Document(window));
                    }

                    ApplyUntilHost(window);
                    Assert.Equal(1, host.ApplyCount);
                    AssertFieldsEqual(expected, host.Applied);
                    AssertFieldsEqual(expected, Document(window));
                }
                finally { window.Close(); }
            });
        }

        [Theory]
        [InlineData(false, "custom_joint")]
        [InlineData(true, "custom_joint")]
        [InlineData(true, "")]
        public void SingleSelectionStillCommitsOnlyTheSelectedJoint(bool loseFocus, string value)
        {
            RunSta(() =>
            {
                CanvasHost host = new CanvasHost { Source = Chain(3) };
                LinkTreeCanvasWindow window = new LinkTreeCanvasWindow(host);
                try
                {
                    LinkTreeDocument expected = host.Source.Clone();
                    expected.Nodes[1].JointName = value;
                    Invoke(window, "SelectNode", expected.Nodes[1].Id, false);
                    TextBox field = (TextBox)window.FindName("JointNameTextBox");
                    field.Text = value;
                    if (loseFocus)
                    {
                        field.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                        AssertFieldsEqual(expected, Document(window));
                    }
                    ApplyUntilHost(window);
                    AssertFieldsEqual(expected, host.Applied);
                }
                finally { window.Close(); }
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CycleAndDanglingDiagnosticsKeepTheirOrder(bool draft)
        {
            LinkTreeDocument document = Chain(5);
            document.Nodes[1].ParentId = document.Nodes[2].Id;
            document.Nodes[3].ParentId = Guid.NewGuid();
            string dangling = document.Nodes[3].Name + " \u7684\u7236 Link \u4e0d\u5b58\u5728\u3002";
            Assert.Equal(new[] { dangling, CycleError }, Validate(document, draft).ToArray());

            document.Nodes[1].ParentId = document.Nodes[0].Id;
            Assert.Equal(new[] { dangling }, Validate(document, draft).ToArray());
            document.Nodes[3].ParentId = document.Nodes[2].Id;
            Assert.Empty(Validate(document, draft));
            document.Nodes[4].ParentId = document.Nodes[4].Id;
            Assert.Equal(new[] { CycleError }, Validate(document, draft).ToArray());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void InvalidIdsReturnOnlyTheExistingIdentityDiagnostic(bool draft)
        {
            LinkTreeDocument document = Chain(3);
            document.Nodes[1].Id = document.Nodes[0].Id;
            Assert.Equal(new[] { IdError }, Validate(document, draft).ToArray());
            document.Nodes[1].Id = Guid.Empty;
            Assert.Equal(new[] { IdError }, Validate(document, draft).ToArray());
            document.Nodes[1] = null;
            Assert.Equal(new[] { IdError }, Validate(document, draft).ToArray());
        }

        [Fact]
        public void DraftStillAllowsIncompleteFieldsButRejectsConflictsAndUnsupportedTypes()
        {
            LinkTreeDocument document = Chain(3);
            foreach (LinkTreeNode node in document.Nodes.Skip(1))
            {
                node.Name = "";
                node.JointName = "";
                node.JointType = "";
            }
            Assert.Empty(document.ValidateDraft());
            Assert.NotEmpty(document.Validate());
            document.Nodes[1].Name = "invalid name";
            Assert.Empty(document.ValidateDraft());
            document.Nodes[2].Name = "INVALID NAME";
            document.Nodes[1].JointName = "shared_joint";
            document.Nodes[2].JointName = "SHARED_JOINT";
            document.Nodes[2].JointType = "unsupported";
            Assert.Equal(3, document.ValidateDraft().Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DeepChainValidationIsIterativeAndDoesNotCacheMutableNodes(bool reverse)
        {
            LinkTreeDocument document = Chain(12000);
            LinkTreeNode root = document.Nodes[0];
            LinkTreeNode firstChild = document.Nodes[1];
            LinkTreeNode leaf = document.Nodes.Last();
            if (reverse) document.Nodes.Reverse();
            Assert.Empty(document.Validate());
            Assert.Empty(document.ValidateDraft());
            firstChild.ParentId = leaf.Id;
            Assert.Equal(new[] { CycleError }, document.Validate().ToArray());
            Assert.Equal(new[] { CycleError }, document.ValidateDraft().ToArray());
            firstChild.ParentId = root.Id;
            Assert.Empty(document.ValidateDraft());
        }

        [Theory]
        [InlineData(128, false)]
        [InlineData(128, true)]
        [InlineData(1024, false)]
        [InlineData(1024, true)]
        [InlineData(8192, false)]
        [InlineData(8192, true)]
        public void CycleTraversalLooksUpEachParentEdgeOnlyOnce(int count, bool reverse)
        {
            LinkTreeDocument document = Chain(count);
            LinkTreeNode firstChild = document.Nodes[1];
            LinkTreeNode leaf = document.Nodes.Last();
            if (reverse) document.Nodes.Reverse();
            CountingIndex index = new CountingIndex(document.Nodes);
            Assert.False(LinkTreeDocument.HasParentCycle(index));
            Assert.Equal(count - 1, index.ParentLookups);

            firstChild.ParentId = leaf.Id;
            index.ParentLookups = 0;
            Assert.True(LinkTreeDocument.HasParentCycle(index));
            Assert.Equal(count - 1, index.ParentLookups);

            firstChild.ParentId = Guid.NewGuid();
            index.ParentLookups = 0;
            Assert.False(LinkTreeDocument.HasParentCycle(index));
            Assert.Equal(count - 1, index.ParentLookups);
        }

        private static IList<string> Validate(LinkTreeDocument document, bool draft)
        {
            return draft ? document.ValidateDraft() : document.Validate();
        }

        private static LinkTreeDocument Chain(int count)
        {
            LinkTreeDocument document = new LinkTreeDocument();
            Guid? parentId = null;
            for (int index = 0; index < count; index++)
            {
                LinkTreeNode node = LinkTreeDocument.NewNode("link_" + index, parentId, index * 200, 0);
                node.JointType = "fixed";
                document.Nodes.Add(node);
                parentId = node.Id;
            }
            return document;
        }

        private sealed class CountingIndex : Dictionary<Guid, LinkTreeNode>, IDictionary<Guid, LinkTreeNode>
        {
            internal int ParentLookups;

            internal CountingIndex(IEnumerable<LinkTreeNode> nodes)
                : base(nodes.ToDictionary(node => node.Id)) { }

            bool IDictionary<Guid, LinkTreeNode>.TryGetValue(Guid id, out LinkTreeNode node)
            {
                ParentLookups++;
                return TryGetValue(id, out node);
            }
        }

        private static void AssertFieldsEqual(LinkTreeDocument expected, LinkTreeDocument actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Nodes.Count, actual.Nodes.Count);
            for (int index = 0; index < expected.Nodes.Count; index++)
            {
                LinkTreeNode left = expected.Nodes[index];
                LinkTreeNode right = actual.Nodes[index];
                Assert.Equal(left.Id, right.Id);
                Assert.Equal(left.ParentId, right.ParentId);
                Assert.Equal(left.Name, right.Name);
                Assert.Equal(left.JointName, right.JointName);
                Assert.Equal(left.JointType, right.JointType);
            }
        }

        private static LinkTreeDocument Document(LinkTreeCanvasWindow window)
        {
            return (LinkTreeDocument)typeof(LinkTreeCanvasWindow).GetField("document",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
        }

        private static void Invoke(LinkTreeCanvasWindow window, string method, params object[] args)
        {
            typeof(LinkTreeCanvasWindow).GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, args);
        }

        private static void ApplyUntilHost(LinkTreeCanvasWindow window)
        {
            // Stop after the real Apply entry point reaches the host, before modal-only DialogResult.
            TargetInvocationException error = Assert.Throws<TargetInvocationException>(() =>
                Invoke(window, "ApplyClick", null, new RoutedEventArgs()));
            Assert.IsType<AppliedException>(error.InnerException);
        }

        private sealed class AppliedException : Exception { }

        private sealed class CanvasHost : ILinkTreeCanvasHost
        {
            internal LinkTreeDocument Source;
            internal LinkTreeDocument Applied;
            internal int ApplyCount;
            public LinkTreeDocument LoadTree() { return Source.Clone(); }
            public void ApplyTree(LinkTreeDocument document)
            {
                Applied = document.Clone();
                ApplyCount++;
                throw new AppliedException();
            }
        }

        private static void RunSta(Action action)
        {
            Exception failure = null;
            Thread thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception exception) { failure = exception; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
