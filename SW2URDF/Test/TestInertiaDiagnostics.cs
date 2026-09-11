using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra.Double;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public class TestInertiaDiagnostics
    {
        [Fact]
        public void OptionalProbeFailureDoesNotInterruptScopeAndNativeCallFailureStillPropagates()
        {
            var lines = new List<string>();
            var failure = new InvalidOperationException("probe failure");
            using (InertiaDiagnostics.Begin("test", lines.Add))
            {
                InertiaDiagnostics.Observe("principal", () => { throw failure; });
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                    InertiaDiagnostics.Call<int>("required", () => { throw failure; })));
            }
            Assert.Contains(lines, x => x.Contains("principal unavailable"));
            Assert.Contains(lines, x => x.Contains("failed required"));
            Assert.EndsWith("end scope", lines.Last());
            Assert.False(InertiaDiagnostics.Enabled);
        }

        [Fact]
        public void FailedSinkNeverBlocksRestorationAndNestedScopeRestoresParent()
        {
            using (InertiaDiagnostics.Begin("parent", text => { throw new Exception(); }))
            {
                using (InertiaDiagnostics.Begin("child", text => { })) { }
                Assert.True(InertiaDiagnostics.Enabled);
                Assert.Equal(42, InertiaDiagnostics.Call("restore", () => 42));
            }
            Assert.False(InertiaDiagnostics.Enabled);
        }

        [Fact]
        public void SnapshotReportsOriginalSignedTensorWithoutRepair()
        {
            var lines = new List<string>();
            var tensor = new[] { 1.0, 0.2, -0.3, 0.2, 2.0, 0.4, -0.3, 0.4, 2.5 };
            var source = new MassPropertySnapshot(3, new double[3], tensor);
            using (InertiaDiagnostics.Begin("test", lines.Add)) InertiaDiagnostics.Snapshot("A", source);
            Assert.Equal(tensor, source.Moment);
            Assert.Contains(lines, x => x.Contains("A.trace=5.5"));
            Assert.Contains(lines, x => x.Contains("A.eigenvalues="));
        }

        [Fact]
        public void EditedAndLegacyInertiaAreNotMislabelledAsComputedApiData()
        {
            var link = new Link { InertialEditing = new InertialEditingState { TensorEdited = true } };
            Assert.Equal("Edited or preserved inertia", InertiaDiagnostics.InvalidSourceLabel(link));
            link.InertialEditing.TensorEdited = false;
            link.InertialEditing.LegacyValuesPreserved = true;
            Assert.Equal("Edited or preserved inertia", InertiaDiagnostics.InvalidSourceLabel(link));
            link.InertialEditing.LegacyValuesPreserved = false;
            link.InertialEditing.MassEdited = true;
            Assert.Equal("Inertia after mass editing", InertiaDiagnostics.InvalidSourceLabel(link));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
        public void RejectsTransformsThatCanCorruptInertia(int kind)
        {
            var frame = DenseMatrix.CreateIdentity(4);
            if (kind == 0) frame[0, 0] = 2;
            if (kind == 1) frame[0, 1] = .1;
            if (kind == 2) frame[0, 3] = double.NaN;
            if (kind == 3) frame[3, 0] = .1;
            var source = new MassPropertySnapshot(1, new double[3], new[] {1.0,0,0,0,2,0,0,0,3});
            Assert.Throws<ArgumentException>(() => MassPropertyFrameConverter.Convert(source, DenseMatrix.CreateIdentity(4), frame));
        }
    }
}
