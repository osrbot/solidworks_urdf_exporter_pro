using SW2URDF.UI;
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
    }
}
