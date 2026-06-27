using SW2URDF.UI;
using SW2URDF.URDF;
using Xunit;

namespace SW2URDF.Test
{
    public class TestInertiaPreview
    {
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
        public void TestTransparentAppearancePreservesOpticalProperties()
        {
            double[] source = { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.0, 0.9 };

            double[] result = InertiaPreview.BuildTransparentAppearance(source);

            Assert.NotSame(source, result);
            Assert.Equal(0.75, result[7]);
            Assert.Equal(source[0], result[0]);
            Assert.Equal(source[8], result[8]);
            Assert.Equal(0.0, source[7]);
        }

        [Fact]
        public void TestTransparentAppearanceProvidesFallback()
        {
            double[] result = InertiaPreview.BuildTransparentAppearance(null);

            Assert.Equal(9, result.Length);
            Assert.Equal(0.75, result[7]);
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
