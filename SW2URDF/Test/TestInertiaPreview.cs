using MathNet.Numerics.LinearAlgebra;
using SW2URDF.UI;
using SW2URDF.URDF;
using Xunit;

namespace SW2URDF.Test
{
    public class TestInertiaPreview
    {
        [Fact]
        public void TestAssemblyDisplayTransformConvertsLinkFrameToComponentFrame()
        {
            Matrix<double> linkToDocument = Matrix<double>.Build.DenseIdentity(4);
            linkToDocument[0, 3] = 13.0;
            linkToDocument[1, 3] = 2.0;
            Matrix<double> componentToDocument = Matrix<double>.Build.DenseIdentity(4);
            componentToDocument[0, 3] = 10.0;

            Matrix<double> result = TemporaryBodyDisplayContext.BuildLinkToDisplayTarget(
                linkToDocument,
                componentToDocument);

            Assert.Equal(3.0, result[0, 3], 12);
            Assert.Equal(2.0, result[1, 3], 12);
            Assert.Equal(0.0, result[2, 3], 12);
        }

        [Fact]
        public void TestSolidWorksTransformArrayUsesColumnMajorRotationAndTranslationSlots()
        {
            Matrix<double> transform = Matrix<double>.Build.DenseIdentity(4);
            transform[0, 0] = 0.0;
            transform[1, 0] = 1.0;
            transform[0, 1] = -1.0;
            transform[1, 1] = 0.0;
            transform[0, 3] = 4.0;
            transform[1, 3] = 5.0;
            transform[2, 3] = 6.0;

            double[] result = TemporaryBodyDisplayContext.ToSolidWorksTransformData(transform);

            Assert.Equal(new[]
            {
                0.0, 1.0, 0.0,
                -1.0, 0.0, 0.0,
                0.0, 0.0, 1.0,
                4.0, 5.0, 6.0,
                1.0, 0.0, 0.0, 0.0
            }, result);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(3, false)]
        public void TestDisplay3ReturnCode(int result, bool expected)
        {
            Assert.Equal(expected, InertiaPreview.IsDisplaySuccess(result));
        }

        [Fact]
        public void TestPreviewIncludesInertiaCurvesAndComMarker()
        {
            Assert.Equal(9, InertiaPreview.ExpectedCurveCount);
        }

        [Fact]
        public void TestShowClassifiesInvalidPhysicalInertia()
        {
            Link link = new Link();
            link.Inertial.Mass.Value = 1.0;
            link.Inertial.Inertia.Ixx = 10.0;
            link.Inertial.Inertia.Iyy = 1.0;
            link.Inertial.Inertia.Izz = 1.0;

            using (InertiaPreview preview = new InertiaPreview(null, null))
            {
                bool success = preview.Show(
                    link,
                    null,
                    out _,
                    out string error,
                    out InertiaPreviewFailureKind failureKind);

                Assert.False(success);
                Assert.Equal(InertiaPreviewFailureKind.InvalidPhysicalInertia, failureKind);
                Assert.Contains("triangle inequality", error);
            }
        }

        [Fact]
        public void TestShowClassifiesMissingCoordinateSystemAsDisplayUnavailable()
        {
            Link link = new Link();
            link.Inertial.Mass.Value = 1.0;
            link.Inertial.Inertia.Ixx = 1.0;
            link.Inertial.Inertia.Iyy = 1.0;
            link.Inertial.Inertia.Izz = 1.0;

            using (InertiaPreview preview = new InertiaPreview(null, null))
            {
                bool success = preview.Show(
                    link,
                    null,
                    out _,
                    out string error,
                    out InertiaPreviewFailureKind failureKind);

                Assert.False(success);
                Assert.Equal(InertiaPreviewFailureKind.DisplayUnavailable, failureKind);
                Assert.Contains("coordinate system", error);
            }
        }
    }
}
