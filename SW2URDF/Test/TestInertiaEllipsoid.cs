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
        public void TestUniformBoxTensorRecoversEquivalentBoxDimensions()
        {
            const double mass = 6.0;
            double[] dimensions = { 1.0, 2.0, 3.0 };
            double ixx = mass * (dimensions[1] * dimensions[1] +
                dimensions[2] * dimensions[2]) / 12.0;
            double iyy = mass * (dimensions[0] * dimensions[0] +
                dimensions[2] * dimensions[2]) / 12.0;
            double izz = mass * (dimensions[0] * dimensions[0] +
                dimensions[1] * dimensions[1]) / 12.0;

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
            Array.Sort(ellipsoid.EquivalentBoxDimensions);
            Assert.Equal(dimensions[0], ellipsoid.EquivalentBoxDimensions[0], 10);
            Assert.Equal(dimensions[1], ellipsoid.EquivalentBoxDimensions[1], 10);
            Assert.Equal(dimensions[2], ellipsoid.EquivalentBoxDimensions[2], 10);
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

        [Fact]
        public void TestThinWheelTensorFromSolidWorksLogStaysDisplayable()
        {
            bool success = InertiaEllipsoid.TryCreate(
                0.836349965,
                new[]
                {
                    3.998552042E-4, -1.130620548E-8, 1.764469125E-7,
                    -1.130620548E-8, 3.998137025E-4, 2.945171384E-7,
                    1.764469125E-7, 2.945171384E-7, 4.809812796E-8
                },
                out InertiaEllipsoid ellipsoid,
                out string error);

            Assert.True(success, error);
            Assert.All(ellipsoid.SemiAxes, value => Assert.True(value > 0.0));
            Array.Sort(ellipsoid.SemiAxes);
            Assert.InRange(ellipsoid.SemiAxes[0], 4.0E-5, 6.0E-5);
            Assert.InRange(ellipsoid.SemiAxes[1], 4.0E-4, 7.0E-4);
            Assert.InRange(ellipsoid.SemiAxes[2], 4.0E-2, 6.0E-2);
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
