using Moq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace SW2URDF.Test
{
    public class TestComponentTraversal
    {
        [Fact]
        public void VisibilityUsesOneBatchInsteadOfSelectingEachDescendant()
        {
            var parent = Component("assembly");
            var child = Component("assembly/part-1");
            parent.Setup(component => component.GetChildren()).Returns(new object[] { child.Object });
            var extension = new Mock<ModelDocExtension>();
            extension.Setup(item => item.MultiSelect2(It.IsAny<object>(), false, null))
                .Returns((object values, bool append, object data) => ((object[])values).Length);
            var model = new Mock<ModelDoc2>();
            model.SetupGet(item => item.Extension).Returns(extension.Object);

            CommonSwOperations.SetComponentVisibility(model.Object,
                new List<Component2> { parent.Object, child.Object, parent.Object }, true, component => component);

            extension.Verify(item => item.MultiSelect2(It.Is<object>(values =>
                ((object[])values).Length == 2), false, null), Times.Once);
            model.Verify(item => item.ShowComponent2(), Times.Once);
            parent.Verify(item => item.GetChildren(), Times.Never);
            child.Verify(item => item.GetChildren(), Times.Never);
            parent.Verify(item => item.Select4(It.IsAny<bool>(), It.IsAny<SelectData>(), It.IsAny<bool>()), Times.Never);
            child.Verify(item => item.Select4(It.IsAny<bool>(), It.IsAny<SelectData>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public void EmptyAndSuppressedBatchesNeverActOnAnExistingSelection()
        {
            var model = new Mock<ModelDoc2>(MockBehavior.Strict);
            var component = Component("suppressed");
            component.Setup(item => item.IsSuppressed()).Returns(true);
            CommonSwOperations.SetComponentVisibility(model.Object, new Component2[0], false);
            CommonSwOperations.SetComponentVisibility(model.Object, new[] { component.Object }, true);
        }

        [Fact]
        public void PartialSelectionFailsBeforeVisibilityChangesAndClearsSelection()
        {
            var model = new Mock<ModelDoc2>();
            var extension = new Mock<ModelDocExtension>();
            model.SetupGet(item => item.Extension).Returns(extension.Object);
            extension.Setup(item => item.MultiSelect2(It.IsAny<object>(), false, null)).Returns(0);
            Assert.Throws<InvalidOperationException>(() => CommonSwOperations.SetComponentVisibility(
                model.Object, new[] { Component("part").Object }, false, component => component));
            model.Verify(item => item.HideComponent2(), Times.Never);
            model.Verify(item => item.ClearSelection2(true), Times.Once);
        }

        [Fact]
        public void SnapshotReadFailureDoesNotReturnAnIncompleteExportList()
        {
            var component = Component("assembly");
            component.Setup(item => item.GetChildren()).Throws(new COMException("unavailable children"));
            Assert.Throws<COMException>(() => ExportHelper.CaptureComponentVisibility(new[] { component.Object }));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RestoreStillAttemptsHiddenGroupWhenVisibleGroupFails(bool clearFails)
        {
            var visible = Component("visible");
            var hidden = Component("hidden");
            var model = new Mock<ModelDoc2>();
            var extension = new Mock<ModelDocExtension>();
            model.SetupGet(item => item.Extension).Returns(extension.Object);
            int selections = 0, clears = 0;
            extension.Setup(item => item.MultiSelect2(It.IsAny<object>(), false, null))
                .Returns(() => ++selections == 1 && !clearFails ? 0 : 1);
            model.Setup(item => item.ClearSelection2(true)).Callback(() =>
            {
                if (++clears == 1 && clearFails) throw new COMException("clear selection failure");
            });
            var states = new List<ExportHelper.ComponentVisibilityState>
            {
                new ExportHelper.ComponentVisibilityState(visible.Object, (int)swComponentVisibilityState_e.swComponentVisible),
                new ExportHelper.ComponentVisibilityState(hidden.Object, (int)swComponentVisibilityState_e.swComponentHidden)
            };
            Assert.Throws<AggregateException>(() => ExportHelper.RestoreComponentVisibility(model.Object, states,
                component => component));
            model.Verify(item => item.HideComponent2(), Times.Once);
            hidden.Verify(item => item.GetChildren(), Times.Never);
            Assert.Empty(states);
        }

        [Fact]
        public void SelectionFailureIsNotReplacedByCleanupFailure()
        {
            var model = new Mock<ModelDoc2>();
            var extension = new Mock<ModelDocExtension>();
            model.SetupGet(item => item.Extension).Returns(extension.Object);
            model.Setup(item => item.ClearSelection2(true)).Throws(new COMException("cleanup"));
            Assert.Throws<InvalidOperationException>(() => CommonSwOperations.SetComponentVisibility(
                model.Object, new[] { Component("part").Object }, false, component => component));
        }

        private static Mock<Component2> Component(string name)
        {
            var component = new Mock<Component2>();
            component.SetupGet(item => item.Name2).Returns(name);
            return component;
        }

        [Fact]
        public void FlatSnapshotDoesNotRevisitNestedComponents()
        {
            var root = new Node("assembly");
            var branch = new Node("assembly/subassembly");
            root.Children.Add(branch);
            branch.Children.Add(new Node("assembly/subassembly/part-1"));
            var flat = new[] { root, branch, branch.Children[0], root };
            int visited = 0;

            var result = CommonSwOperations.ExpandDistinctComponents(flat, node => node.Path,
                node => { visited++; return node.Children; });

            Assert.Equal(3, visited);
            Assert.Equal(flat.Take(3), result);
        }

        [Fact]
        public void ComponentInstancePathKeepsRepeatedPartsDistinct()
        {
            var first = new Node("assembly/subassembly-1/part-1");
            var second = new Node("assembly/subassembly-2/part-1");
            var result = CommonSwOperations.ExpandDistinctComponents(new[] { first, second },
                node => node.Path, node => node.Children);
            Assert.Equal(new[] { first, second }, result);
        }

        [Fact]
        public void DeepOverlappingRootsAndCyclesRemainLinear()
        {
            var nodes = Enumerable.Range(0, 10000).Select(index => new Node(index.ToString())).ToArray();
            for (int index = 1; index < nodes.Length; index++)
                nodes[index - 1].Children.Add(nodes[index]);
            nodes[nodes.Length - 1].Children.Add(nodes[0]);
            int visited = 0;
            var result = CommonSwOperations.ExpandDistinctComponents(nodes, node => node.Path,
                node => { visited++; return node.Children; });
            Assert.Equal(nodes.Length, visited);
            Assert.Equal(nodes, result);
        }

        [Fact]
        public void EmptyAndNullComponentsHaveNoVisibilityTargets()
        {
            Assert.Empty(CommonSwOperations.ExpandDistinctComponents<Node>(null,
                node => node.Path, node => node.Children));
            Assert.Empty(CommonSwOperations.ExpandDistinctComponents(new Node[] { null },
                node => node.Path, node => node.Children));
        }

        private sealed class Node
        {
            internal Node(string path) { Path = path; }
            internal string Path { get; private set; }
            internal List<Node> Children { get; } = new List<Node>();
        }
    }
}
