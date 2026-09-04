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
        private const int Visible = (int)swComponentVisibilityState_e.swComponentVisible;
        private const int Hidden = (int)swComponentVisibilityState_e.swComponentHidden;

        [Fact]
        public void VisibilityUsesOneBatchInsteadOfSelectingEachDescendant()
        {
            var parent = Component("assembly");
            var child = Component("assembly/part-1");
            parent.Setup(component => component.GetChildren()).Returns(new object[] { child.Object });
            var batch = new VisibilityBatch();

            CommonSwOperations.SetComponentVisibility(batch.Model.Object,
                new List<Component2> { parent.Object, child.Object, parent.Object }, true, component => component);

            Assert.Equal(new[] { parent.Object, child.Object }, Assert.Single(batch.Selections));
            batch.Model.Verify(item => item.ShowComponent2(), Times.Once);
            batch.Model.Verify(item => item.ClearSelection2(true), Times.Once);
            Assert.Equal(Visible, parent.Object.Visible);
            Assert.Equal(Visible, child.Object.Visible);
            parent.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            child.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
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
            component.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
        }

        [Fact]
        public void AlreadyMatching253ComponentsSelectOnlyTwoHiddenParents()
        {
            var unchanged = Enumerable.Range(0, 253).Select(index => Component("part-" + index, Visible)).ToArray();
            var first = Component("parent-1");
            var second = Component("parent-2");
            var batch = new VisibilityBatch();
            var targets = unchanged.Select(component => component.Object)
                .Concat(new[] { first.Object, second.Object }).ToArray();

            CommonSwOperations.SetComponentVisibility(batch.Model.Object, targets, true, component => component);

            Assert.Equal(new[] { first.Object, second.Object }, Assert.Single(batch.Selections));
            Assert.All(targets, component => Assert.Equal(Visible, component.Visible));
            foreach (var component in unchanged.Concat(new[] { first, second }))
            {
                component.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
                component.Verify(item => item.GetChildren(), Times.Never);
            }
            batch.Model.Verify(item => item.ShowComponent2(), Times.Once);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EntirelyMatchingBatchNeverSelectsOrWrites(bool visible)
        {
            var component = Component("unchanged", visible ? Visible : Hidden);
            var model = new Mock<ModelDoc2>(MockBehavior.Strict);
            int preparations = 0;

            CommonSwOperations.SetComponentVisibility(model.Object, new[] { component.Object }, visible,
                item => { preparations++; return item; });

            Assert.Equal(0, preparations);
            component.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void PartialOrFailedMultiSelectFallsBackForAll253WithoutBulkAction(bool visible, bool selectionThrows)
        {
            int requested = visible ? Visible : Hidden;
            var components = Enumerable.Range(0, 253)
                .Select(index => Component("assembly/part-" + index, visible ? Hidden : Visible)).ToArray();
            var batch = new VisibilityBatch { SelectionCount = count => 11 };
            if (selectionThrows)
                batch.Extension.Setup(item => item.MultiSelect2(It.IsAny<object>(), false, null))
                    .Throws(new COMException("selection failed"));

            CommonSwOperations.SetComponentVisibility(batch.Model.Object,
                components.Select(component => component.Object), visible, component => component);

            foreach (var component in components)
            {
                Assert.Equal(requested, component.Object.Visible);
                component.VerifySet(item => item.Visible = requested, Times.Once);
                component.Verify(item => item.Select4(It.IsAny<bool>(), It.IsAny<SelectData>(), It.IsAny<bool>()), Times.Never);
            }
            batch.Model.Verify(item => item.ShowComponent2(), Times.Never);
            batch.Model.Verify(item => item.HideComponent2(), Times.Never);
            batch.Model.Verify(item => item.ClearSelection2(true), Times.Once);
        }

        [Theory]
        [InlineData(false, 253, 0)]
        [InlineData(true, 253, 0)]
        [InlineData(false, 253, 11)]
        [InlineData(true, 253, 11)]
        [InlineData(false, 254, 0)]
        [InlineData(true, 254, 0)]
        public void BatchCountsDoNotProveCompletionAndFallbackOnlyWritesMismatches(
            bool visible, int reportedCount, int changedByBatch)
        {
            int requested = visible ? Visible : Hidden;
            var components = Enumerable.Range(0, 253)
                .Select(index => Component("assembly/part-" + index, visible ? Hidden : Visible)).ToArray();
            var batch = new VisibilityBatch
            {
                SelectionCount = count => reportedCount,
                BatchUpdateCount = changedByBatch
            };

            CommonSwOperations.SetComponentVisibility(batch.Model.Object,
                components.Select(component => component.Object), visible, component => component);

            Assert.Equal(253, Assert.Single(batch.Selections).Length);
            for (int index = 0; index < components.Length; index++)
            {
                Assert.Equal(requested, components[index].Object.Visible);
                components[index].VerifySet(item => item.Visible = requested,
                    index < changedByBatch ? Times.Never() : Times.Once());
            }
            batch.Model.Verify(item => item.ClearSelection2(true), Times.Once);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BulkExceptionStillRepairsOnlyMismatchesAfterClearingSelection(bool visible)
        {
            int requested = visible ? Visible : Hidden;
            int initial = visible ? Hidden : Visible;
            var changed = Component("assembly/changed-by-bulk", initial);
            var pending = Component("assembly/pending", initial);
            var batch = new VisibilityBatch();
            bool cleared = false;
            Action bulk = () =>
            {
                changed.SetupProperty(item => item.Visible, requested);
                throw new COMException("bulk failed after one component");
            };
            if (visible) batch.Model.Setup(item => item.ShowComponent2()).Callback(bulk);
            else batch.Model.Setup(item => item.HideComponent2()).Callback(bulk);
            batch.Model.Setup(item => item.ClearSelection2(true)).Callback(() => cleared = true);
            pending.SetupSet(item => item.Visible = It.IsAny<int>()).Callback<int>(value =>
            {
                Assert.True(cleared, "Fallback must not write while a bulk selection is active.");
                pending.SetupGet(item => item.Visible).Returns(value);
            });

            CommonSwOperations.SetComponentVisibility(batch.Model.Object,
                new[] { changed.Object, pending.Object }, visible, component => component);

            Assert.Equal(requested, changed.Object.Visible);
            Assert.Equal(requested, pending.Object.Visible);
            changed.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            pending.VerifySet(item => item.Visible = requested, Times.Once);
            batch.Model.Verify(item => item.ClearSelection2(true), Times.Once);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CleanupFailureStillAllowsEveryFallbackWriteBeforeItIsReported(bool visible)
        {
            int requested = visible ? Visible : Hidden;
            var components = new[]
            {
                Component("assembly/first", visible ? Hidden : Visible),
                Component("assembly/last", visible ? Hidden : Visible)
            };
            var batch = new VisibilityBatch { SelectionCount = count => 0 };
            bool cleanupAttempted = false;
            batch.Model.Setup(item => item.ClearSelection2(true)).Callback(() =>
            {
                cleanupAttempted = true;
                throw new COMException("cleanup failed before fallback");
            });
            foreach (var component in components)
                component.SetupSet(item => item.Visible = It.IsAny<int>()).Callback<int>(value =>
                {
                    Assert.True(cleanupAttempted);
                    component.SetupGet(item => item.Visible).Returns(value);
                });

            Exception failure = Record.Exception(() => CommonSwOperations.SetComponentVisibility(batch.Model.Object,
                components.Select(component => component.Object), visible, component => component));

            Assert.NotNull(failure);
            Assert.Contains("cleanup failed before fallback", failure.ToString());
            foreach (var component in components)
            {
                Assert.Equal(requested, component.Object.Visible);
                component.VerifySet(item => item.Visible = requested, Times.Once);
            }
            batch.Model.Verify(item => item.ShowComponent2(), Times.Never);
            batch.Model.Verify(item => item.HideComponent2(), Times.Never);
        }

        [Fact]
        public void VerificationIncludesOriginallyMatchingTargetsChangedByBulkSideEffects()
        {
            var parent = Component("assembly");
            var child = Component("assembly/child", Visible);
            var batch = new VisibilityBatch();
            batch.Model.Setup(item => item.ShowComponent2()).Callback(() =>
            {
                parent.SetupProperty(item => item.Visible, Visible);
                child.SetupProperty(item => item.Visible, Hidden);
            });

            CommonSwOperations.SetComponentVisibility(batch.Model.Object,
                new[] { parent.Object, child.Object }, true, component => component);

            Assert.Equal(new[] { parent.Object }, Assert.Single(batch.Selections));
            Assert.Equal(Visible, parent.Object.Visible);
            Assert.Equal(Visible, child.Object.Visible);
            parent.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            child.VerifySet(item => item.Visible = Visible, Times.Once);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void DirectSetterFailureOrNoOpIsNamedAndDoesNotPreventRemainingTargets(bool visible, bool throws)
        {
            int requested = visible ? Visible : Hidden;
            int initial = visible ? Hidden : Visible;
            var before = Component("assembly/before", initial);
            var failed = Component("assembly/failed-instance", initial);
            var after = Component("assembly/after", initial);
            var batch = new VisibilityBatch { SelectionCount = count => 0 };
            if (throws)
                failed.SetupSet(item => item.Visible = It.IsAny<int>()).Throws(new COMException("setter failed"));
            else
                failed.SetupSet(item => item.Visible = It.IsAny<int>()).Callback(() => { });

            Exception failure = Record.Exception(() => CommonSwOperations.SetComponentVisibility(batch.Model.Object,
                new[] { before.Object, failed.Object, after.Object }, visible, component => component));

            Assert.NotNull(failure);
            Assert.Contains("assembly/failed-instance", failure.ToString());
            Assert.Equal(initial, failed.Object.Visible);
            Assert.Equal(requested, before.Object.Visible);
            Assert.Equal(requested, after.Object.Visible);
            after.VerifySet(item => item.Visible = requested, Times.Once);
            failed.VerifySet(item => item.Visible = requested, Times.Once);
            batch.Model.Verify(item => item.ShowComponent2(), Times.Never);
            batch.Model.Verify(item => item.HideComponent2(), Times.Never);
            batch.Model.Verify(item => item.ClearSelection2(true), Times.Once);
        }

        [Fact]
        public void MultipleFailedReadbacksAreAllReportedAfterBestEffortWrites()
        {
            var first = Component("assembly/first-failed");
            var second = Component("assembly/second-failed");
            var last = Component("assembly/last-good");
            first.SetupSet(item => item.Visible = It.IsAny<int>()).Callback(() => { });
            second.SetupSet(item => item.Visible = It.IsAny<int>()).Callback(() => { });
            var batch = new VisibilityBatch { BatchUpdateCount = 0 };

            Exception failure = Record.Exception(() => CommonSwOperations.SetComponentVisibility(batch.Model.Object,
                new[] { first.Object, second.Object, last.Object }, true, component => component));

            Assert.NotNull(failure);
            Assert.Contains(first.Object.Name2, failure.ToString());
            Assert.Contains(second.Object.Name2, failure.ToString());
            Assert.Equal(Visible, last.Object.Visible);
            batch.Model.Verify(item => item.ClearSelection2(true), Times.Once);
        }

        [Fact]
        public void FlatScopeDeduplicatesInstancesExcludesSuppressedAndDoesNotExpandChildren()
        {
            var first = Component("assembly/sub-1/part-1");
            var alias = Component("ASSEMBLY/SUB-1/PART-1");
            var repeated = Component("assembly/sub-2/part-1");
            var suppressed = Component("assembly/suppressed");
            var outsideScope = Component("assembly/sub-1/part-1/child");
            first.Setup(item => item.GetChildren()).Returns(new object[] { outsideScope.Object });
            suppressed.Setup(item => item.IsSuppressed()).Returns(true);
            var batch = new VisibilityBatch { SelectionCount = count => 0 };

            CommonSwOperations.SetComponentVisibility(batch.Model.Object,
                new[] { first.Object, null, first.Object, alias.Object, suppressed.Object, repeated.Object },
                true, component => component);

            Assert.Equal(new[] { first.Object, repeated.Object }, Assert.Single(batch.Selections));
            Assert.Equal(Visible, first.Object.Visible);
            Assert.Equal(Visible, repeated.Object.Visible);
            first.VerifySet(item => item.Visible = Visible, Times.Once);
            repeated.VerifySet(item => item.Visible = Visible, Times.Once);
            foreach (var excluded in new[] { alias, suppressed, outsideScope })
            {
                Assert.Equal(Hidden, excluded.Object.Visible);
                excluded.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            }
            first.Verify(item => item.GetChildren(), Times.Never);
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
            var hidden = Component("hidden", Visible);
            var batch = new VisibilityBatch();
            int clears = 0;
            if (!clearFails)
            {
                batch.Model.Setup(item => item.ShowComponent2()).Callback(() => { });
                visible.SetupSet(item => item.Visible = It.IsAny<int>()).Throws(new COMException("visible setter failed"));
            }
            else
                batch.Model.Setup(item => item.ClearSelection2(true)).Callback(() =>
                {
                    if (++clears == 1) throw new COMException("clear selection failure");
                });
            var states = new List<ExportHelper.ComponentVisibilityState>
            {
                new ExportHelper.ComponentVisibilityState(visible.Object, (int)swComponentVisibilityState_e.swComponentVisible),
                new ExportHelper.ComponentVisibilityState(hidden.Object, (int)swComponentVisibilityState_e.swComponentHidden)
            };
            var originalStates = states.ToArray();
            Assert.Throws<AggregateException>(() => ExportHelper.RestoreComponentVisibility(batch.Model.Object, states,
                component => component));
            batch.Model.Verify(item => item.HideComponent2(), Times.Once);
            Assert.Equal(Hidden, hidden.Object.Visible);
            Assert.Equal(clearFails ? Visible : Hidden, visible.Object.Visible);
            hidden.Verify(item => item.GetChildren(), Times.Never);
            Assert.Equal(originalStates, states.ToArray());
        }

        [Fact]
        public void SetterFailureIsNotReplacedByCleanupFailure()
        {
            var failed = Component("failed-part", Visible);
            failed.SetupSet(item => item.Visible = It.IsAny<int>()).Throws(new COMException("setter failure"));
            var batch = new VisibilityBatch { SelectionCount = count => 0 };
            batch.Model.Setup(item => item.ClearSelection2(true)).Throws(new COMException("cleanup"));

            Exception failure = Record.Exception(() => CommonSwOperations.SetComponentVisibility(
                batch.Model.Object, new[] { failed.Object }, false, component => component));

            Assert.NotNull(failure);
            Assert.Contains("failed-part", failure.ToString());
            Assert.Contains("setter failure", failure.ToString());
            batch.Model.Verify(item => item.ClearSelection2(true), Times.Once);
        }

        [Fact]
        public void SuccessfulVisibilityChangeDoesNotSwallowCleanupFailure()
        {
            var component = Component("part");
            var batch = new VisibilityBatch();
            batch.Model.Setup(item => item.ClearSelection2(true)).Throws(new COMException("cleanup failed"));

            Exception failure = Record.Exception(() => CommonSwOperations.SetComponentVisibility(
                batch.Model.Object, new[] { component.Object }, true, item => item));

            Assert.NotNull(failure);
            Assert.Contains("cleanup failed", failure.ToString());
            Assert.Equal(Visible, component.Object.Visible);
        }

        [Fact]
        public void SnapshotRestorationReinstatesOriginallyHiddenAndVisibleStates()
        {
            var visible = Component("visible", Visible);
            var hidden = Component("hidden", Hidden);
            var states = ExportHelper.CaptureComponentVisibility(new[] { visible.Object, hidden.Object });
            visible.Object.Visible = Hidden;
            hidden.Object.Visible = Visible;
            var batch = new VisibilityBatch();

            ExportHelper.RestoreComponentVisibility(batch.Model.Object, states, component => component);

            Assert.Equal(Visible, visible.Object.Visible);
            Assert.Equal(Hidden, hidden.Object.Visible);
            Assert.Equal(2, batch.Selections.Count);
            batch.Model.Verify(item => item.ShowComponent2(), Times.Once);
            batch.Model.Verify(item => item.HideComponent2(), Times.Once);
            Assert.Empty(states);
        }

        [Fact]
        public void SnapshotPreservesLocalVisibleEvenWhenHiddenParentMakesChildEffectivelyHidden()
        {
            var parent = Component("assembly", Hidden);
            var child = Component("assembly/child", Visible);
            parent.Setup(item => item.GetChildren()).Returns(new object[] { child.Object });
            parent.Setup(item => item.IsHidden(It.IsAny<bool>())).Returns(true);
            child.Setup(item => item.IsHidden(It.IsAny<bool>())).Returns(true);
            var states = ExportHelper.CaptureComponentVisibility(new[] { parent.Object, child.Object });

            Assert.Equal(2, states.Count);
            Assert.Equal(Hidden, states.Single(state => state.Component == parent.Object).Visibility);
            Assert.Equal(Visible, states.Single(state => state.Component == child.Object).Visibility);
            parent.Object.Visible = Visible;
            child.Object.Visible = Hidden;
            var batch = new VisibilityBatch();

            ExportHelper.RestoreComponentVisibility(batch.Model.Object, states, component => component);

            Assert.Equal(Hidden, parent.Object.Visible);
            Assert.Equal(Visible, child.Object.Visible);
            parent.Verify(item => item.IsHidden(It.IsAny<bool>()), Times.Never);
            child.Verify(item => item.IsHidden(It.IsAny<bool>()), Times.Never);
            Assert.Empty(states);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedRestorationRetainsFullSnapshotUntilSuccessfulRetry(bool clearFails)
        {
            var visible = Component("originally-visible");
            var hidden = Component("originally-hidden", Visible);
            var batch = new VisibilityBatch();
            var states = new List<ExportHelper.ComponentVisibilityState>
            {
                new ExportHelper.ComponentVisibilityState(visible.Object, Visible),
                new ExportHelper.ComponentVisibilityState(hidden.Object, Hidden)
            };
            var originalStates = states.ToArray();
            if (clearFails)
            {
                int clears = 0;
                batch.Model.Setup(item => item.ClearSelection2(true)).Callback(() =>
                {
                    if (++clears == 1) throw new COMException("first cleanup failed");
                });
            }
            else
            {
                batch.Model.Setup(item => item.ShowComponent2()).Callback(() => { });
                visible.SetupSet(item => item.Visible = It.IsAny<int>()).Callback(() => { });
            }

            Exception failure = Record.Exception(() => ExportHelper.RestoreComponentVisibility(
                batch.Model.Object, states, component => component));

            Assert.NotNull(failure);
            Assert.Contains(clearFails ? "first cleanup failed" : "originally-visible", failure.ToString());
            Assert.Equal(Hidden, hidden.Object.Visible);
            Assert.Equal(originalStates, states.ToArray());
            Assert.Equal(2, batch.Selections.Count);
            batch.Model.Verify(item => item.HideComponent2(), Times.Once);
            batch.Model.Verify(item => item.ClearSelection2(true), Times.AtLeast(2));

            // Remove the injected fault, leaving already restored targets at their actual states.
            visible.SetupProperty(item => item.Visible, visible.Object.Visible);
            batch.Model.Setup(item => item.ClearSelection2(true)).Callback(() => { });
            hidden.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            ExportHelper.RestoreComponentVisibility(batch.Model.Object, states, component => component);

            Assert.Equal(Visible, visible.Object.Visible);
            Assert.Equal(Hidden, hidden.Object.Visible);
            Assert.Empty(states);
            batch.Model.Verify(item => item.HideComponent2(), Times.Once);
        }

        [Fact]
        public void RestorationCannotSucceedIfHiddenGroupChangesPreviouslyRestoredVisibleChild()
        {
            var parent = Component("assembly", Visible);
            var child = Component("assembly/visible-child", Hidden);
            var states = new List<ExportHelper.ComponentVisibilityState>
            {
                new ExportHelper.ComponentVisibilityState(parent.Object, Hidden),
                new ExportHelper.ComponentVisibilityState(child.Object, Visible)
            };
            var originalStates = states.ToArray();
            var batch = new VisibilityBatch();
            batch.Model.Setup(item => item.HideComponent2()).Callback(() =>
            {
                parent.SetupProperty(item => item.Visible, Hidden);
                child.SetupProperty(item => item.Visible, Hidden);
            });

            Exception failure = Record.Exception(() => ExportHelper.RestoreComponentVisibility(
                batch.Model.Object, states, component => component));

            if (failure == null)
            {
                Assert.Equal(Hidden, parent.Object.Visible);
                Assert.Equal(Visible, child.Object.Visible);
                Assert.Empty(states);
            }
            else
            {
                Assert.Contains("assembly/visible-child", failure.ToString());
                Assert.Equal(originalStates, states.ToArray());
            }
            batch.Model.Verify(item => item.ClearSelection2(true), Times.AtLeast(2));
        }

        [Fact]
        public void IsolatedNestedChildMakesAncestorsVisibleAndOtherLinkSiblingHiddenThenRestoresSnapshot()
        {
            var parent = Component("assembly", Hidden);
            var branch = Component("assembly/branch", Hidden);
            var owned = Component("assembly/branch/owned", Visible);
            var sibling = Component("assembly/branch/other-link", Visible);
            var unrelated = Component("unrelated-link", Visible);
            parent.Setup(item => item.GetParent()).Returns((Component2)null);
            branch.Setup(item => item.GetParent()).Returns(parent.Object);
            owned.Setup(item => item.GetParent()).Returns(branch.Object);
            sibling.Setup(item => item.GetParent()).Returns(branch.Object);
            parent.Setup(item => item.GetChildren()).Returns(new object[] { branch.Object });
            branch.Setup(item => item.GetChildren()).Returns(new object[] { owned.Object, sibling.Object });
            var states = VisibilitySnapshot(parent, branch, owned, sibling, unrelated);
            var originalStates = states.ToArray();

            var plan = ExportHelper.CreateIsolatedVisibilityPlan(states, new[] { owned.Object });

            AssertIsolationPlan(states, plan, parent.Object, branch.Object, owned.Object);
            Assert.Equal(originalStates, states.ToArray());
            Assert.Equal(new[] { Hidden, Hidden, Visible, Visible, Visible }, states.Select(state => state.Visibility));
            parent.Verify(item => item.GetParent(), Times.Once);
            var batch = new VisibilityBatch();
            ExportHelper.RestoreComponentVisibility(batch.Model.Object, plan, component => component);
            Assert.Equal(Visible, parent.Object.Visible);
            Assert.Equal(Visible, branch.Object.Visible);
            Assert.Equal(Visible, owned.Object.Visible);
            Assert.Equal(Hidden, sibling.Object.Visible);
            Assert.Equal(Hidden, unrelated.Object.Visible);
            Assert.Equal(5, states.Count);

            ExportHelper.RestoreComponentVisibility(batch.Model.Object, states, component => component);

            Assert.Equal(Hidden, parent.Object.Visible);
            Assert.Equal(Hidden, branch.Object.Visible);
            Assert.Equal(Visible, owned.Object.Visible);
            Assert.Equal(Visible, sibling.Object.Visible);
            Assert.Equal(Visible, unrelated.Object.Visible);
            Assert.Empty(states);
        }

        [Fact]
        public void IsolatedRequestedParentExpandsItsDescendantsButNotAncestorsOtherLinkChildren()
        {
            var ancestor = Component("assembly", Hidden);
            var ownedParent = Component("assembly/owned-parent", Hidden);
            var child = Component("assembly/owned-parent/child", Hidden);
            var grandchild = Component("assembly/owned-parent/child/grandchild", Hidden);
            var otherLink = Component("assembly/other-link", Visible);
            ownedParent.Setup(item => item.GetParent()).Returns(ancestor.Object);
            child.Setup(item => item.GetParent()).Returns(ownedParent.Object);
            grandchild.Setup(item => item.GetParent()).Returns(child.Object);
            otherLink.Setup(item => item.GetParent()).Returns(ancestor.Object);
            ancestor.Setup(item => item.GetChildren()).Returns(new object[] { ownedParent.Object, otherLink.Object });
            ownedParent.Setup(item => item.GetChildren()).Returns(new object[] { child.Object });
            child.Setup(item => item.GetChildren()).Returns(new object[] { grandchild.Object });
            var states = VisibilitySnapshot(ancestor, ownedParent, child, grandchild, otherLink);

            var plan = ExportHelper.CreateIsolatedVisibilityPlan(states, new[] { ownedParent.Object });

            AssertIsolationPlan(states, plan, ancestor.Object, ownedParent.Object, child.Object, grandchild.Object);
            ancestor.Verify(item => item.GetChildren(), Times.Never);
            Assert.Equal(Visible, otherLink.Object.Visible);
            Assert.All(new[] { ancestor, ownedParent, child, grandchild, otherLink },
                component => component.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never));
        }

        [Fact]
        public void IsolatedPlanKeepsRepeatedPartInstancePathsDistinctAndDeduplicatesRequests()
        {
            var firstParent = Component("assembly/sub-1");
            var secondParent = Component("assembly/sub-2", Visible);
            var firstPart = Component("assembly/sub-1/part-1");
            var secondPart = Component("assembly/sub-2/part-1", Visible);
            firstPart.Setup(item => item.GetParent()).Returns(firstParent.Object);
            secondPart.Setup(item => item.GetParent()).Returns(secondParent.Object);
            firstParent.Setup(item => item.GetChildren()).Returns(new object[] { firstPart.Object });
            secondParent.Setup(item => item.GetChildren()).Returns(new object[] { secondPart.Object });
            var states = VisibilitySnapshot(firstParent, firstPart, secondParent, secondPart);

            var plan = ExportHelper.CreateIsolatedVisibilityPlan(states, new[] { firstPart.Object, firstPart.Object });

            AssertIsolationPlan(states, plan, firstParent.Object, firstPart.Object);
            Assert.Equal(Hidden, plan.Single(state => state.Component == secondPart.Object).Visibility);
            Assert.Equal(Hidden, plan.Single(state => state.Component == secondParent.Object).Visibility);
            firstPart.Verify(item => item.GetChildren(), Times.Once);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void IsolatedPlanRejectsOwnedComponentMissingFromAssemblySnapshot(bool missingDescendant)
        {
            var known = Component("assembly/known", Visible);
            var missing = Component("assembly/missing-owned", Hidden);
            if (missingDescendant)
            {
                known.Setup(item => item.GetChildren()).Returns(new object[] { missing.Object });
                missing.Setup(item => item.GetParent()).Returns(known.Object);
            }
            var states = VisibilitySnapshot(known);
            var originalStates = states.ToArray();

            var failure = Assert.Throws<InvalidOperationException>(() => ExportHelper.CreateIsolatedVisibilityPlan(
                states, new[] { missingDescendant ? known.Object : missing.Object }));

            Assert.Contains(missing.Object.Name2, failure.ToString());
            Assert.Equal(originalStates, states.ToArray());
            Assert.Equal(Visible, known.Object.Visible);
            Assert.Equal(Hidden, missing.Object.Visible);
            known.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            missing.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void IsolatedPlanRejectsSelfAndMultiComponentParentCycles(bool selfCycle)
        {
            var owned = Component("assembly/owned", Visible);
            var parent = Component("assembly/parent", Hidden);
            int parentReads = 0;
            Action guard = () =>
            {
                if (++parentReads > 6)
                    throw new InvalidOperationException("Test guard: parent traversal did not terminate.");
            };
            owned.Setup(item => item.GetParent()).Callback(guard).Returns(selfCycle ? owned.Object : parent.Object);
            parent.Setup(item => item.GetParent()).Callback(guard).Returns(owned.Object);
            var states = VisibilitySnapshot(owned, parent);
            var originalStates = states.ToArray();

            var failure = Assert.Throws<InvalidOperationException>(() => ExportHelper.CreateIsolatedVisibilityPlan(
                states, new[] { owned.Object }));

            Assert.Contains("cycl", failure.ToString().ToLowerInvariant());
            Assert.Contains(owned.Object.Name2, failure.ToString());
            Assert.True(parentReads <= 6);
            Assert.Equal(originalStates, states.ToArray());
            owned.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            parent.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
        }

        [Fact]
        public void IsolatedPlanRejectsNonNullParentMissingFromAssemblySnapshot()
        {
            var owned = Component("assembly/owned", Visible);
            var missingParent = Component("assembly", Hidden);
            owned.Setup(item => item.GetParent()).Returns(missingParent.Object);
            var states = VisibilitySnapshot(owned);
            var originalStates = states.ToArray();

            var failure = Assert.Throws<InvalidOperationException>(() => ExportHelper.CreateIsolatedVisibilityPlan(
                states, new[] { owned.Object }));

            Assert.Contains(missingParent.Object.Name2, failure.ToString());
            Assert.Equal(originalStates, states.ToArray());
            owned.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            missingParent.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void IsolatedPlanRejectsMixedExplicitRootsContainingSuppressedComponent(bool suppressedFirst)
        {
            var valid = Component("valid-owned", Visible);
            var suppressed = Component("suppressed-owned", Hidden);
            suppressed.Setup(item => item.IsSuppressed()).Returns(true);
            var states = VisibilitySnapshot(valid, suppressed);
            var originalStates = states.ToArray();
            var requested = suppressedFirst
                ? new[] { suppressed.Object, valid.Object }
                : new[] { valid.Object, suppressed.Object };

            var failure = Assert.Throws<InvalidOperationException>(() =>
                ExportHelper.CreateIsolatedVisibilityPlan(states, requested));

            Assert.Contains(suppressed.Object.Name2, failure.ToString());
            Assert.Contains("suppressed", failure.ToString().ToLowerInvariant());
            Assert.Equal(originalStates, states.ToArray());
            Assert.Equal(Visible, valid.Object.Visible);
            Assert.Equal(Hidden, suppressed.Object.Visible);
            valid.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            suppressed.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            suppressed.Verify(item => item.SetSuppression2(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void IsolatedPlanRejectsSuppressedRequiredAncestorWithoutUnsuppressingIt()
        {
            var parent = Component("assembly/suppressed-parent", Hidden);
            var owned = Component("assembly/suppressed-parent/owned", Visible);
            owned.Setup(item => item.GetParent()).Returns(parent.Object);
            parent.Setup(item => item.IsSuppressed()).Returns(true);
            var states = VisibilitySnapshot(parent, owned);
            var originalStates = states.ToArray();

            var failure = Assert.Throws<InvalidOperationException>(() =>
                ExportHelper.CreateIsolatedVisibilityPlan(states, new[] { owned.Object }));

            Assert.Contains(parent.Object.Name2, failure.ToString());
            Assert.Contains("suppressed", failure.ToString().ToLowerInvariant());
            Assert.Equal(originalStates, states.ToArray());
            parent.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            owned.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            parent.Verify(item => item.SetSuppression2(It.IsAny<int>()), Times.Never);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SelectedSubassemblyLeavesSuppressedDescendantLocalStateUnchanged(bool initiallyVisible)
        {
            var parent = Component("assembly/selected", Hidden);
            var child = Component("assembly/selected/active-child", Hidden);
            var suppressed = Component("assembly/selected/suppressed-child", initiallyVisible ? Visible : Hidden);
            parent.Setup(item => item.GetChildren()).Returns(new object[] { child.Object, suppressed.Object });
            child.Setup(item => item.GetParent()).Returns(parent.Object);
            suppressed.Setup(item => item.GetParent()).Returns(parent.Object);
            suppressed.Setup(item => item.IsSuppressed()).Returns(true);
            var states = VisibilitySnapshot(parent, child, suppressed);

            var plan = ExportHelper.CreateIsolatedVisibilityPlan(states, new[] { parent.Object });

            Assert.Equal(Visible, plan.Single(state => state.Component == parent.Object).Visibility);
            Assert.Equal(Visible, plan.Single(state => state.Component == child.Object).Visibility);
            var batch = new VisibilityBatch();
            ExportHelper.RestoreComponentVisibility(batch.Model.Object, plan, component => component);
            Assert.Equal(Visible, parent.Object.Visible);
            Assert.Equal(Visible, child.Object.Visible);
            Assert.Equal(initiallyVisible ? Visible : Hidden, suppressed.Object.Visible);
            Assert.DoesNotContain(suppressed.Object, batch.Selections.SelectMany(items => items));
            ExportHelper.RestoreComponentVisibility(batch.Model.Object, states, component => component);

            Assert.Equal(Hidden, parent.Object.Visible);
            Assert.Equal(Hidden, child.Object.Visible);
            Assert.Equal(initiallyVisible ? Visible : Hidden, suppressed.Object.Visible);
            suppressed.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            suppressed.Verify(item => item.SetSuppression2(It.IsAny<int>()), Times.Never);
            Assert.Empty(states);
        }

        [Fact]
        public void FinalCleanupFailureBlocksUnchangedRestorationAndPreservesSnapshotForRetry()
        {
            var visible = Component("visible", Visible);
            var hidden = Component("hidden", Hidden);
            var states = VisibilitySnapshot(visible, hidden);
            var originalStates = states.ToArray();
            var batch = new VisibilityBatch();
            batch.Model.Setup(item => item.ClearSelection2(true)).Throws(new COMException("final cleanup failed"));

            Exception failure = Record.Exception(() => ExportHelper.RestoreComponentVisibility(
                batch.Model.Object, states, component => component));

            Assert.NotNull(failure);
            Assert.Contains("final cleanup failed", failure.ToString());
            Assert.Equal(originalStates, states.ToArray());
            Assert.Empty(batch.Selections);
            visible.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            hidden.VerifySet(item => item.Visible = It.IsAny<int>(), Times.Never);
            batch.Model.Verify(item => item.ClearSelection2(true), Times.Once);
            batch.Model.Setup(item => item.ClearSelection2(true)).Callback(() => { });

            ExportHelper.RestoreComponentVisibility(batch.Model.Object, states, component => component);

            Assert.Empty(states);
            Assert.Equal(Visible, visible.Object.Visible);
            Assert.Equal(Hidden, hidden.Object.Visible);
            batch.Model.Verify(item => item.ClearSelection2(true), Times.Exactly(2));
        }

        private static List<ExportHelper.ComponentVisibilityState> VisibilitySnapshot(params Mock<Component2>[] components)
        {
            return components.Select(component => new ExportHelper.ComponentVisibilityState(
                component.Object, component.Object.Visible)).ToList();
        }

        private static void AssertIsolationPlan(IList<ExportHelper.ComponentVisibilityState> original,
            IList<ExportHelper.ComponentVisibilityState> plan, params Component2[] visible)
        {
            Assert.NotSame(original, plan);
            Assert.Equal(original.Count, plan.Count);
            Assert.Equal(original.Count, plan.Select(state => state.Component.Name2)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var state in original)
            {
                var desired = Assert.Single(plan.Where(item => item.Component == state.Component));
                Assert.Equal(visible.Contains(state.Component) ? Visible : Hidden, desired.Visibility);
            }
        }

        [Fact]
        public void FinalRestoreRepairsChildBeforeHiddenParentWhenChildSetterUnhidesParent()
        {
            var parent = Component("assembly", Hidden);
            var child = Component("assembly/visible-child", Hidden);
            var sibling = Component("assembly/hidden-sibling", Hidden);
            int childVisibility = Hidden;
            child.SetupGet(item => item.Visible).Returns(() => childVisibility);
            child.SetupSet(item => item.Visible = It.IsAny<int>()).Callback<int>(value =>
            {
                childVisibility = value;
                if (value == Visible)
                {
                    parent.Object.Visible = Visible;
                    sibling.Object.Visible = Hidden;
                }
            });
            var states = new List<ExportHelper.ComponentVisibilityState>
            {
                new ExportHelper.ComponentVisibilityState(parent.Object, Hidden),
                new ExportHelper.ComponentVisibilityState(child.Object, Visible),
                new ExportHelper.ComponentVisibilityState(sibling.Object, Hidden)
            };
            var batch = new VisibilityBatch();
            batch.Model.Setup(item => item.ShowComponent2()).Callback(() => { });
            batch.Model.Setup(item => item.HideComponent2()).Callback(() =>
            {
                parent.SetupProperty(item => item.Visible, Hidden);
                childVisibility = Hidden;
            });

            ExportHelper.RestoreComponentVisibility(batch.Model.Object, states, component => component);

            Assert.Equal(Hidden, parent.Object.Visible);
            Assert.Equal(Visible, child.Object.Visible);
            Assert.Equal(Hidden, sibling.Object.Visible);
            Assert.Empty(states);
            child.VerifySet(item => item.Visible = Visible, Times.Exactly(2));
            batch.Model.Verify(item => item.ShowComponent2(), Times.Once);
            batch.Model.Verify(item => item.HideComponent2(), Times.Once);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FinalRepairRecoversTransientSetterFailureButNeverDiscardsCleanupFailure(bool cleanupFails)
        {
            var transient = Component("assembly/transient", Hidden);
            var hidden = Component("assembly/originally-hidden", Visible);
            int setterAttempts = 0;
            transient.SetupSet(item => item.Visible = It.IsAny<int>()).Callback<int>(value =>
            {
                if (++setterAttempts > 1)
                    transient.SetupGet(item => item.Visible).Returns(value);
            });
            var states = new List<ExportHelper.ComponentVisibilityState>
            {
                new ExportHelper.ComponentVisibilityState(transient.Object, Visible),
                new ExportHelper.ComponentVisibilityState(hidden.Object, Hidden)
            };
            var originalStates = states.ToArray();
            var batch = new VisibilityBatch { BatchUpdateCount = 0 };
            int cleanupAttempts = 0;
            var cleanupException = new COMException("transient group selection cleanup failed");
            batch.Model.Setup(item => item.ClearSelection2(true)).Callback(() =>
            {
                if (++cleanupAttempts == 1 && cleanupFails)
                    throw cleanupException;
            });

            Exception failure = Record.Exception(() => ExportHelper.RestoreComponentVisibility(
                batch.Model.Object, states, component => component));

            Assert.Equal(2, setterAttempts);
            Assert.Equal(Visible, transient.Object.Visible);
            Assert.Equal(Hidden, hidden.Object.Visible);
            batch.Model.Verify(item => item.ShowComponent2(), Times.Once);
            batch.Model.Verify(item => item.HideComponent2(), Times.Once);
            Assert.Equal(3, cleanupAttempts);
            if (cleanupFails)
            {
                var aggregate = Assert.IsType<AggregateException>(failure);
                Assert.Contains("ERROR COMPONENT_VISIBILITY:", aggregate.Message);
                var visibilityFailure = Assert.Single(aggregate.Flatten().InnerExceptions
                    .OfType<CommonSwOperations.ComponentVisibilityException>());
                Assert.True(visibilityFailure.SelectionCleanupFailed);
                var groupFailures = Assert.IsType<AggregateException>(visibilityFailure.InnerException)
                    .Flatten().InnerExceptions;
                var recordedCleanup = Assert.Single(groupFailures.OfType<COMException>());
                Assert.Same(cleanupException, recordedCleanup);
                Assert.Equal("transient group selection cleanup failed", recordedCleanup.Message);
                Assert.Equal(originalStates, states.ToArray());
            }
            else
            {
                Assert.Null(failure);
                Assert.Empty(states);
            }
        }

        private static Mock<Component2> Component(string name, int visibility = Hidden)
        {
            var component = new Mock<Component2>();
            component.SetupGet(item => item.Name2).Returns(name);
            component.SetupProperty(item => item.Visible, visibility);
            return component;
        }

        private sealed class VisibilityBatch
        {
            internal Mock<ModelDoc2> Model { get; } = new Mock<ModelDoc2>();
            internal Mock<ModelDocExtension> Extension { get; } = new Mock<ModelDocExtension>();
            internal List<Component2[]> Selections { get; } = new List<Component2[]>();
            internal Func<int, int> SelectionCount { get; set; } = count => count;
            internal int BatchUpdateCount { get; set; } = Int32.MaxValue;
            private Component2[] selected = new Component2[0];

            internal VisibilityBatch()
            {
                Model.SetupGet(item => item.Extension).Returns(Extension.Object);
                Extension.Setup(item => item.MultiSelect2(It.IsAny<object>(), false, null))
                    .Returns((object values, bool append, object data) =>
                    {
                        selected = ((object[])values).Cast<Component2>().ToArray();
                        Selections.Add(selected);
                        return SelectionCount(selected.Length);
                    });
                Model.Setup(item => item.ShowComponent2()).Callback(() => ApplyBatch(Visible));
                Model.Setup(item => item.HideComponent2()).Callback(() => ApplyBatch(Hidden));
                Model.Setup(item => item.ClearSelection2(true)).Callback(() => selected = new Component2[0]);
            }

            private void ApplyBatch(int visibility)
            {
                // Model the COM bulk effect without recording a direct component setter call.
                foreach (Component2 component in selected.Take(BatchUpdateCount))
                    Mock.Get(component).SetupProperty(item => item.Visible, visibility);
            }
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
