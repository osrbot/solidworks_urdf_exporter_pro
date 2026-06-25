using SW2URDF.URDFExport;
using System;
using Xunit;

namespace SW2URDF.Test
{
    public class TestInertiaEllipsoid
    {
        [Fact]
        public void TestUniformBoxTensorRecoversBoxEquivalentSemiAxes()
        {
            const double mass = 6.0;
            const double halfX = 0.5;
            const double halfY = 1.0;
            const double halfZ = 1.5;
            double ixx = mass * (halfY * halfY + halfZ * halfZ) / 5.0;
            double iyy = mass * (halfX * halfX + halfZ * halfZ) / 5.0;
            double izz = mass * (halfX * halfX + halfY * halfY) / 5.0;

            bool success = InertiaEllipsoid.TryCreate(
                mass,
                new[]
                {
                    ixx, 0.0, 0.0,
                    0.0, iyy, 0.0,
                    0.0, 0.0, izz
                },
                out InertiaEllipsoid ellipsoid,
                out string error);

            Assert.True(success, error);
            Array.Sort(ellipsoid.SemiAxes);
            Assert.Equal(halfX, ellipsoid.SemiAxes[0], 10);
            Assert.Equal(halfY, ellipsoid.SemiAxes[1], 10);
            Assert.Equal(halfZ, ellipsoid.SemiAxes[2], 10);
        }

        [Fact]
        public void TestRotatedTensorReturnsPositiveEquivalentSemiAxes()
        {
            bool success = InertiaEllipsoid.TryCreate(
                2.0,
                new[]
                {
                    0.18, 0.02, 0.0,
                    0.02, 0.22, 0.0,
                    0.0, 0.0, 0.24
                },
                out InertiaEllipsoid ellipsoid,
                out string error);

            Assert.True(success, error);
            Assert.All(ellipsoid.SemiAxes, value => Assert.True(value > 0.0));
            Assert.Equal(3, ellipsoid.PrincipalAxes.RowCount);
            Assert.Equal(3, ellipsoid.PrincipalAxes.ColumnCount);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void TestRejectsNonPositiveMass(double mass)
        {
            Assert.False(InertiaEllipsoid.TryCreate(
                mass,
                new[]
                {
                    1.0, 0.0, 0.0,
                    0.0, 1.0, 0.0,
                    0.0, 0.0, 1.0
                },
                out _,
                out _));
        }

        [Fact]
        public void TestRejectsTensorThatViolatesTriangleInequality()
        {
            Assert.False(InertiaEllipsoid.TryCreate(
                1.0,
                new[]
                {
                    10.0, 0.0, 0.0,
                    0.0, 1.0, 0.0,
                    0.0, 0.0, 1.0
                },
                out _,
                out string error));
            Assert.Contains("triangle inequality", error);
        }
    }
}
