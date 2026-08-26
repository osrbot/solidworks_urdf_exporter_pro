using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace SW2URDF.Test
{
    public class TestSolidWorksInertiaApiIntegration
    {
        [Fact]
        public void TestApiTensorEigenvaluesMatchApiPrincipalMoments()
        {
            if (!string.Equals(
                System.Environment.GetEnvironmentVariable(
                    "SW2URDF_RUN_SW_INTEGRATION_TESTS"),
                "1",
                StringComparison.Ordinal))
            {
                return;
            }

            SldWorks swApp = null;
            ModelDoc2 model = null;
            MathUtility mathUtility = null;
            MathTransform coordinateSystem = null;
            MassProperty massProperty = null;
            try
            {
                swApp = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
                model = (ModelDoc2)swApp.ActiveDoc;
                Assert.NotNull(model);

                mathUtility = (MathUtility)swApp.GetMathUtility();
                coordinateSystem = (MathTransform)mathUtility.CreateTransform(
                    CreateNonPrincipalCoordinateSystem());
                massProperty = (MassProperty)model.Extension.CreateMassProperty();
                Assert.NotNull(massProperty);

                massProperty.UseSystemUnits = true;
                Assert.True(massProperty.SetCoordinateSystem(coordinateSystem));

                double[] apiMoment = (double[])massProperty.GetMomentOfInertia(0);
                double[] apiPrincipalMoments =
                    (double[])massProperty.PrincipleMomentsOfInertia;
                Assert.Equal(9, apiMoment.Length);
                Assert.Equal(3, apiPrincipalMoments.Length);
                Assert.True(
                    Math.Abs(apiMoment[1]) + Math.Abs(apiMoment[2]) +
                    Math.Abs(apiMoment[5]) > 1e-12,
                    "The integration model must produce non-zero products of inertia.");

                Inertia mappedInertia = new Inertia();
                mappedInertia.SetSolidWorksMomentMatrix(apiMoment);
                AssertPrincipalMomentsMatch(
                    mappedInertia,
                    massProperty.Mass,
                    apiPrincipalMoments);
            }
            finally
            {
                ReleaseComObject(massProperty);
                ReleaseComObject(coordinateSystem);
                ReleaseComObject(mathUtility);
                ReleaseComObject(model);
                ReleaseComObject(swApp);
            }
        }

        private static void AssertPrincipalMomentsMatch(
            Inertia inertia,
            double mass,
            double[] apiPrincipalMoments)
        {
            Assert.True(
                InertiaEllipsoid.TryCreate(
                    mass,
                    inertia,
                    out InertiaEllipsoid ellipsoid,
                    out string error),
                error);

            double[] mappedPrincipalMoments = ellipsoid.PrincipalMoments
                .OrderBy(value => value)
                .ToArray();
            double[] expectedPrincipalMoments = apiPrincipalMoments
                .OrderBy(value => value)
                .ToArray();
            double scale = Math.Max(1e-12, expectedPrincipalMoments.Max());
            for (int i = 0; i < expectedPrincipalMoments.Length; i++)
            {
                Assert.True(
                    Math.Abs(mappedPrincipalMoments[i] - expectedPrincipalMoments[i]) <=
                    scale * 1e-9,
                    string.Format(
                        "Principal moment {0} differs: mapped={1:G17}, api={2:G17}",
                        i,
                        mappedPrincipalMoments[i],
                        expectedPrincipalMoments[i]));
            }
        }

        private static double[] CreateNonPrincipalCoordinateSystem()
        {
            const double angle = 0.73;
            double cosine = Math.Cos(angle);
            double sine = Math.Sin(angle);
            double oneMinusCosine = 1.0 - cosine;
            double norm = Math.Sqrt(14.0);
            double x = 1.0 / norm;
            double y = 2.0 / norm;
            double z = 3.0 / norm;
            return new[]
            {
                oneMinusCosine * x * x + cosine,
                oneMinusCosine * x * y - sine * z,
                oneMinusCosine * x * z + sine * y,
                oneMinusCosine * x * y + sine * z,
                oneMinusCosine * y * y + cosine,
                oneMinusCosine * y * z - sine * x,
                oneMinusCosine * x * z - sine * y,
                oneMinusCosine * y * z + sine * x,
                oneMinusCosine * z * z + cosine,
                0.0, 0.0, 0.0,
                1.0,
                0.0, 0.0, 0.0
            };
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
    }
}
