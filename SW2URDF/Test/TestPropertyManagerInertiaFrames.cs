using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using MathNet.Numerics.LinearAlgebra;
using Moq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using Xunit;

namespace SW2URDF.Test
{
    public class TestPropertyManagerInertiaFrames
    {
        private static readonly CadFeatureReference OldFrame = Frame(1);
        private static readonly CadFeatureReference NewFrame = Frame(2);
        private static readonly Matrix<double> Identity = Matrix<double>.Build.DenseIdentity(4);
        private static readonly Matrix<double> MovedFrame = MathOps.GetTransformation(
            new[] { 1.0, 2.0, 3.0 }, new[] { 0.0, 0.0, Math.PI / 2 });

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void RootAndChildComboAndSaveDeferAndReexpressManualValues(bool root, bool callback)
        {
            using (var page = new DraftPage(root))
            {
                var before = InertialEditingPolicy.Copy(page.Link.Inertial);
                if (callback) page.Select();
                else page.Save();
                Assert.Equal(NewFrame, page.Link.FrameReference);
                Assert.True(page.Link.InertialEditing.FrameChangePending);
                Assert.Equal(OldFrame, page.Link.InertialEditing.InertialFrameReference);
                AssertInertial(before, page.Link.Inertial);
                Assert.Null(page.Manager.Exporter);
                Assert.Null(page.Manager.ActiveSWModel);

                Resolve(page.Link);
                AssertPhysical(before, page.Link.Inertial);
                Assert.False(page.Link.InertialEditing.FrameChangePending);
                Assert.True(page.Link.InertialEditing.OriginEdited);
                Assert.True(page.Link.InertialEditing.TensorEdited);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RepeatedSavesDoNotTransformTwiceOrChangeEditingOwnership(bool root)
        {
            using (var page = new DraftPage(root))
            {
                page.Select();
                for (int i = 0; i < 5; i++) page.Save();
                Resolve(page.Link);
                var once = InertialEditingPolicy.Copy(page.Link.Inertial);
                for (int i = 0; i < 5; i++)
                {
                    page.Save();
                    InertialEditingPolicy.ResolvePendingFrameChange(page.Link, _ =>
                    {
                        throw new Exception("An unchanged frame must not call CAD.");
                    });
                    InertialEditingPolicy.ApplyEdits(page.Link, InertialEditingPolicy.Copy(page.Link.Inertial));
                }
                AssertInertial(once, page.Link.Inertial);
                Assert.True(page.Link.InertialEditing.MassEdited);
                Assert.True(page.Link.InertialEditing.OriginEdited);
                Assert.True(page.Link.InertialEditing.TensorEdited);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MissingOldFramePreservesValuesAndPendingDraftForRetry(bool root)
        {
            using (var page = new DraftPage(root))
            {
                page.Select();
                var before = InertialEditingPolicy.Copy(page.Link.Inertial);
                var state = page.Link.InertialEditing;
                var source = InertialEditingPolicy.Copy(state.Source);
                Assert.Throws<InvalidOperationException>(() =>
                    InertialEditingPolicy.ResolvePendingFrameChange(page.Link, _ => null));
                Assert.Same(state, page.Link.InertialEditing);
                AssertInertial(before, page.Link.Inertial);
                AssertInertial(source, state.Source);
                Assert.Equal(NewFrame, page.Link.FrameReference);
                Assert.Equal(OldFrame, state.InertialFrameReference);
                Assert.True(state.FrameChangePending);
                Assert.Throws<InvalidOperationException>(() =>
                    InertialEditingPolicy.ApplySource(page.Link, source, false));
                Resolve(page.Link);
                AssertPhysical(before, page.Link.Inertial);
            }
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void PartialEditsKeepTheirOwnershipAndUseTransformedStableSource(bool originEdited, bool tensorEdited)
        {
            var link = EditedLink(originEdited, tensorEdited);
            var before = InertialEditingPolicy.Copy(link.Inertial);
            var source = InertialEditingPolicy.Copy(link.InertialEditing.Source);
            InertialEditingPolicy.QueueFrameChange(link, NewFrame);
            Resolve(link);
            AssertPhysical(before, link.Inertial);
            AssertPhysical(source, link.InertialEditing.Source);
            Assert.Equal(originEdited, link.InertialEditing.OriginEdited);
            Assert.Equal(tensorEdited, link.InertialEditing.TensorEdited);
            var resolved = InertialEditingPolicy.Copy(link.Inertial);
            InertialEditingPolicy.ApplySource(link, InertialEditingPolicy.Copy(link.InertialEditing.Source), false);
            AssertInertial(resolved, link.Inertial);
        }

        [Fact]
        public void MultipleDraftSelectionsAndSerializationRetainOriginalFrame()
        {
            var link = EditedLink();
            var before = InertialEditingPolicy.Copy(link.Inertial);
            InertialEditingPolicy.QueueFrameChange(link, Frame(3));
            InertialEditingPolicy.QueueFrameChange(link, CadFeatureReference.Automatic(ReferenceGeometryKind.CoordinateSystem));
            InertialEditingPolicy.QueueFrameChange(link, NewFrame);
            var clone = link.Clone();
            Assert.NotSame(link.InertialEditing.InertialFrameReference, clone.InertialEditing.InertialFrameReference);
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractSerializer(typeof(Link));
                serializer.WriteObject(stream, clone);
                stream.Position = 0;
                var restored = (Link)serializer.ReadObject(stream);
                Assert.Equal(OldFrame, restored.InertialEditing.InertialFrameReference);
                Resolve(restored);
                AssertPhysical(before, restored.Inertial);
                Assert.True(link.InertialEditing.FrameChangePending);
                AssertInertial(before, link.Inertial);
            }
        }

        [Fact]
        public void ReturningToOriginalFrameCancelsPendingTransformWithoutCad()
        {
            var link = EditedLink();
            var before = InertialEditingPolicy.Copy(link.Inertial);
            InertialEditingPolicy.QueueFrameChange(link, NewFrame);
            InertialEditingPolicy.QueueFrameChange(link, OldFrame);
            InertialEditingPolicy.ResolvePendingFrameChange(link, _ => { throw new Exception("No CAD expected."); });
            Assert.False(link.InertialEditing.FrameChangePending);
            Assert.Null(link.InertialEditing.InertialFrameReference);
            AssertInertial(before, link.Inertial);
        }

        [Fact]
        public void LegacyValuesArePreservedButEmptyLinksStillAcceptTheirFirstSource()
        {
            var legacy = EditedLink();
            legacy.InertialEditing = null;
            var before = InertialEditingPolicy.Copy(legacy.Inertial);
            InertialEditingPolicy.QueueFrameChange(legacy, NewFrame);
            Resolve(legacy);
            AssertPhysical(before, legacy.Inertial);
            Assert.True(legacy.InertialEditing.LegacyValuesPreserved);

            var empty = new Link();
            InertialEditingPolicy.QueueFrameChange(empty, NewFrame);
            Assert.Null(empty.InertialEditing);
            InertialEditingPolicy.ApplySource(empty, before, false);
            Assert.False(empty.InertialEditing.TensorEdited);
            AssertInertial(before, empty.Inertial);
        }

        [Fact]
        public void FailedNewFrameResolutionDoesNotPartiallyTransformSourceOrEdits()
        {
            var link = EditedLink();
            var before = InertialEditingPolicy.Copy(link.Inertial);
            InertialEditingPolicy.QueueFrameChange(link, NewFrame);
            var state = link.InertialEditing;
            Assert.Throws<InvalidOperationException>(() =>
                InertialEditingPolicy.ResolvePendingFrameChange(link, reference =>
                    reference.Equals(OldFrame) ? Identity : null));
            Assert.Same(state, link.InertialEditing);
            AssertInertial(before, link.Inertial);
            Resolve(link);
            AssertPhysical(before, link.Inertial);
        }

        [Fact]
        public void UnknownOriginalFrameCannotBeReplacedWithIdentity()
        {
            var link = EditedLink();
            link.FrameReference = null;
            var before = InertialEditingPolicy.Copy(link.Inertial);
            InertialEditingPolicy.QueueFrameChange(link, NewFrame);
            Assert.Throws<InvalidOperationException>(() =>
                InertialEditingPolicy.ResolvePendingFrameChange(link, _ => MovedFrame));
            AssertInertial(before, link.Inertial);
            Assert.True(link.InertialEditing.FrameChangePending);
            Assert.Null(link.InertialEditing.InertialFrameReference);
        }

        [Fact]
        public void InvalidTransformPreservesDetachedSourceAndEdits()
        {
            var link = EditedLink();
            var before = InertialEditingPolicy.Copy(link.Inertial);
            InertialEditingPolicy.QueueFrameChange(link, NewFrame);
            var state = link.InertialEditing;
            var source = InertialEditingPolicy.Copy(state.Source);
            Assert.ThrowsAny<Exception>(() =>
                InertialEditingPolicy.ResolvePendingFrameChange(link, reference =>
                    reference.Equals(OldFrame) ? Identity : Matrix<double>.Build.DenseIdentity(3)));
            Assert.Same(state, link.InertialEditing);
            AssertInertial(before, link.Inertial);
            AssertInertial(source, state.Source);
            Resolve(link);
            AssertPhysical(before, link.Inertial);
        }

        private static CadFeatureReference Frame(byte id)
        {
            return CadFeatureReference.ExplicitRoot(ReferenceGeometryKind.CoordinateSystem, new[] { id });
        }

        private static Link EditedLink(bool originEdited = true, bool tensorEdited = true)
        {
            var link = TestInertialEditing.SourceLink();
            link.FrameReference = OldFrame.Clone();
            var edits = InertialEditingPolicy.Copy(link.Inertial);
            edits.Mass.Value = 4;
            if (originEdited)
            {
                edits.Origin.SetXYZ(new[] { .4, .5, .6 });
                edits.Origin.SetRPY(new[] { .15, -.25, .4 });
            }
            if (tensorEdited) edits.Inertia.Ixx = .21;
            InertialEditingPolicy.ApplyEdits(link, edits);
            return link;
        }

        private static void Resolve(Link link)
        {
            InertialEditingPolicy.ResolvePendingFrameChange(link, reference =>
                reference.Equals(OldFrame) ? Identity : reference.Equals(NewFrame) ? MovedFrame : null);
        }

        private static void AssertPhysical(Inertial before, Inertial after)
        {
            var oldPose = MathOps.GetTransformation(before.Origin.GetXYZ(), before.Origin.GetRPY());
            var newPose = MovedFrame * MathOps.GetTransformation(after.Origin.GetXYZ(), after.Origin.GetRPY());
            AssertArray(MathOps.GetXYZ(oldPose), MathOps.GetXYZ(newPose));
            var oldRotation = oldPose.SubMatrix(0, 3, 0, 3);
            var newRotation = newPose.SubMatrix(0, 3, 0, 3);
            var oldTensor = Matrix<double>.Build.DenseOfRowMajor(3, 3, before.Inertia.GetMoment());
            var newTensor = Matrix<double>.Build.DenseOfRowMajor(3, 3, after.Inertia.GetMoment());
            AssertArray((oldRotation * oldTensor * oldRotation.Transpose()).ToRowMajorArray(),
                (newRotation * newTensor * newRotation.Transpose()).ToRowMajorArray());
            Assert.Equal(before.Mass.Value, after.Mass.Value);
        }

        private static void AssertInertial(Inertial expected, Inertial actual)
        {
            Assert.Equal(expected.Mass.Value, actual.Mass.Value);
            AssertArray(expected.Origin.GetXYZ(), actual.Origin.GetXYZ());
            AssertArray(expected.Origin.GetRPY(), actual.Origin.GetRPY());
            AssertArray(expected.Inertia.GetMoment(), actual.Inertia.GetMoment());
        }

        private static void AssertArray(double[] expected, double[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i], 10);
        }

        private sealed class DraftPage : IDisposable
        {
            internal readonly ExportPropertyManager Manager;
            private readonly LinkNode node;
            private readonly TreeView tree = new TreeView();
            internal Link Link { get { return node.Link; } }

            internal DraftPage(bool root)
            {
                Manager = (ExportPropertyManager)FormatterServices.GetUninitializedObject(typeof(ExportPropertyManager));
                Set("treeSelectionUpdateGuard", new TreeSelectionUpdateGuard());
                node = new LinkNode { IsBaseNode = root, Link = EditedLink() };
                if (root) tree.Nodes.Add(node);
                else
                {
                    var parent = new LinkNode { IsBaseNode = true };
                    tree.Nodes.Add(parent);
                    parent.Nodes.Add(node);
                }
                tree.SelectedNode = node;
                Manager.Tree = tree;
                Manager.previouslySelectedNode = node;
                Set("pmGlobalFrameReferences", new List<CadFeatureReference> { NewFrame });
                Set("pmLinkFrameReferences", new List<CadFeatureReference> { NewFrame });
                Set("pmAxisReferences", new List<CadFeatureReference>());
                var name = new Mock<PropertyManagerPageTextbox>();
                name.SetupGet(x => x.Text).Returns("link");
                Set("PMTextBoxLinkName", name.Object);
                Set("PMTextBoxJointName", name.Object);
                var combo = new Mock<PropertyManagerPageCombobox>();
                combo.SetupGet(x => x.CurrentSelection).Returns((short)0);
                combo.Setup(x => x.get_ItemText(-1)).Returns("fixed");
                Set("PMComboBoxGlobalCoordsys", combo.Object);
                Set("PMComboBoxCoordSys", combo.Object);
                Set("PMComboBoxAxes", combo.Object);
                Set("PMComboBoxJointType", combo.Object);
            }

            internal void Select()
            {
                ((IPropertyManagerPage2Handler9)Manager).OnComboboxSelectionChanged(node.IsBaseNode ? 24 : 19, 0);
            }
            internal void Save()
            {
                typeof(ExportPropertyManager).GetMethod("SaveActiveNodeFields", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(Manager, new object[] { node });
            }
            private void Set(string name, object value)
            {
                typeof(ExportPropertyManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(Manager, value);
            }
            public void Dispose() { tree.Dispose(); }
        }
    }
}
