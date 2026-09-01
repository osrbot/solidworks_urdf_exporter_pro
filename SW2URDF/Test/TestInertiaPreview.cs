using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
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

        [Theory]
        [InlineData((int)swDocumentTypes_e.swDocPART, "host.SLDASM", true)]
        [InlineData((int)swDocumentTypes_e.swDocASSEMBLY, "host.SLDPRT", false)]
        [InlineData(-1, "host.SLDPRT", true)]
        [InlineData(-1, "host.sldprt", true)]
        [InlineData(-1, "host.SLDASM", false)]
        [InlineData(-1, "", false)]
        public void TestTemporaryBodyHostMustBePartInstance(
            int documentType,
            string path,
            bool expected)
        {
            int? resolvedDocumentType = documentType < 0
                ? (int?)null
                : documentType;

            Assert.Equal(
                expected,
                TemporaryBodyDisplayContext.IsPartDocument(
                    resolvedDocumentType,
                path));
        }

        [Fact]
        public void TestDeepPartHostSelectionRejectsRepeatedDocumentInstances()
        {
            long? selected = TemporaryBodyDisplayContext.SelectUniqueDocumentIdentity(
                new long[] { 10L, 10L, 20L, 30L, 30L });

            Assert.Equal(20L, selected);
            Assert.Null(TemporaryBodyDisplayContext.SelectUniqueDocumentIdentity(
                new long[] { 10L, 10L, 30L, 30L }));
            Assert.Null(TemporaryBodyDisplayContext.SelectUniqueDocumentIdentity(null));
        }

        [Fact]
        public void TestPreviewUsesCuboidAndThreePrincipalAxisBodies()
        {
            Assert.Equal(4, InertiaPreview.ExpectedBodyCount);
        }

        [Fact]
        public void TestEquivalentBoxBodyDimensionsUseCenteredSolidWorksBox()
        {
            double[] result = InertiaPreview.BuildEquivalentBoxBodyDimensions(
                new[] { 2.0, 4.0, 6.0 });

            Assert.Equal(new[]
            {
                0.0, 0.0, -3.0,
                0.0, 0.0, 1.0,
                2.0, 4.0, 6.0
            }, result);
        }

        [Fact]
        public void TestPrincipalAxesCrossCenterAndExtendPastCuboidFaces()
        {
            double[][] result = InertiaPreview.BuildPrincipalAxisLineDimensions(
                new[] { 2.0, 4.0, 6.0 });
            double[][] expected =
            {
                new[] { -1.15, 0.0, 0.0, 1.15, 0.0, 0.0 },
                new[] { 0.0, -2.3, 0.0, 0.0, 2.3, 0.0 },
                new[] { 0.0, 0.0, -3.45, 0.0, 0.0, 3.45 }
            };

            Assert.Equal(3, result.Length);
            for (int axis = 0; axis < expected.Length; axis++)
            {
                for (int coordinate = 0; coordinate < expected[axis].Length; coordinate++)
                {
                    Assert.InRange(
                        Math.Abs(result[axis][coordinate] - expected[axis][coordinate]),
                        0.0,
                        1e-12);
                }
            }
        }

        [Fact]
        public void TestPrincipalAxesAreConvertedToRightHandedRotation()
        {
            Matrix<double> leftHanded = Matrix<double>.Build.DenseOfArray(new[,]
            {
                { 1.0, 0.0, 0.0 },
                { 0.0, 1.0, 0.0 },
                { 0.0, 0.0, -1.0 }
            });

            Matrix<double> result = InertiaPreview.BuildRightHandedPrincipalAxes(leftHanded);

            Assert.Equal(1.0, result.Determinant(), 12);
            Matrix<double> orthogonality = result.TransposeThisAndMultiply(result);
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    Assert.Equal(row == column ? 1.0 : 0.0,
                        orthogonality[row, column], 12);
                }
            }
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

        [Fact]
        public void TestLiveInertiaPreviewUsesVisibleAssemblyDisplayHost()
        {
            if (!String.Equals(
                System.Environment.GetEnvironmentVariable("SW2URDF_RUN_SW_INTEGRATION_TESTS"),
                "1",
                StringComparison.Ordinal))
            {
                return;
            }

            Exception failure = null;
            var staThread = new Thread(() =>
            {
                try
                {
                    RunLiveInertiaPreviewUsesVisibleAssemblyDisplayHost();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private static void RunLiveInertiaPreviewUsesVisibleAssemblyDisplayHost()
        {
            SldWorks swApp = null;
            ModelDoc2 model = null;
            Component2 component = null;
            Component2 displayHost = null;
            MathTransform coordinateTransform = null;
            int? originalVisibility = null;
            int? originalDisplayHostVisibility = null;
            try
            {
                swApp = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
                model = swApp.ActiveDoc as ModelDoc2;
                AssemblyDoc assembly = model as AssemblyDoc;
                Assert.NotNull(assembly);
                object[] topLevelComponents = assembly.GetComponents(true) as object[];
                Component2[] usableComponents = (topLevelComponents ?? new object[0])
                    .Cast<Component2>()
                    .Where(candidate => candidate != null)
                    .Take(2)
                    .ToArray();
                Assert.True(
                    usableComponents.Length >= 2,
                    "The live inertia preview test requires two top-level components.");
                component = usableComponents[0];
                displayHost = usableComponents[1];

                originalVisibility = component.Visible;
                originalDisplayHostVisibility = displayHost.Visible;
                displayHost.Visible = (int)swComponentVisibilityState_e.swComponentVisible;
                component.Visible = (int)swComponentVisibilityState_e.swComponentHidden;
                model.GraphicsRedraw2();
                Assert.True(component.IsHidden(false));
                Assert.False(displayHost.IsHidden(false));

                string coordinateSystemName =
                    System.Environment.GetEnvironmentVariable("SW2URDF_TEST_COORDINATE_SYSTEM");
                if (String.IsNullOrWhiteSpace(coordinateSystemName))
                {
                    coordinateSystemName = "Origin_global";
                }
                ExportHelper exporter = new ExportHelper(swApp);
                ReferenceGeometryEntry frameEntry = exporter.GetRefCoordinateSystems()
                    .FirstOrDefault(entry =>
                        String.Equals(
                            entry.DisplayName,
                            coordinateSystemName,
                            StringComparison.Ordinal) &&
                        String.IsNullOrWhiteSpace(entry.ComponentPath));
                Assert.NotNull(frameEntry);
                coordinateTransform = exporter.GetCoordinateSystemTransform(frameEntry.Reference);
                Assert.NotNull(coordinateTransform);

                Link link = new Link { Name = "inertia_preview_test_link" };
                link.FrameReference = frameEntry.Reference.Clone();
                link.SWComponents.Add(component);
                link.Inertial.Mass.Value = 1.0;
                link.Inertial.Inertia.Ixx = 0.001;
                link.Inertial.Inertia.Iyy = 0.0012;
                link.Inertial.Inertia.Izz = 0.0014;

                Assert.True(
                    TemporaryBodyDisplayContext.TryCreate(
                        swApp,
                        model,
                        link,
                        coordinateTransform,
                        out TemporaryBodyDisplayContext displayContext,
                        out string displayContextError),
                    displayContextError);
                using (displayContext)
                {
                    Component2 displayTarget = displayContext.DisplayTarget as Component2;
                    Assert.NotNull(displayTarget);
                    ModelDoc2 displayTargetModel = displayTarget.GetModelDoc2() as ModelDoc2;
                    Assert.NotNull(displayTargetModel);
                    Assert.Equal(
                        (int)swDocumentTypes_e.swDocPART,
                        displayTargetModel.GetType());
                }

                using (InertiaPreview preview = new InertiaPreview(swApp, model))
                {
                    Assert.True(
                        preview.Show(
                            link,
                            coordinateTransform,
                            out InertiaEllipsoid ellipsoid,
                            out string error),
                        error);
                    Assert.NotNull(ellipsoid);
                    Assert.True(preview.IsVisible);
                    preview.Hide();
                    Assert.False(preview.IsVisible);
                }
            }
            finally
            {
                if (component != null && originalVisibility.HasValue)
                {
                    try
                    {
                        component.Visible = originalVisibility.Value;
                        model?.GraphicsRedraw2();
                    }
                    catch { }
                }
                if (displayHost != null && originalDisplayHostVisibility.HasValue)
                {
                    try
                    {
                        displayHost.Visible = originalDisplayHostVisibility.Value;
                        model?.GraphicsRedraw2();
                    }
                    catch { }
                }
                ReleaseComObject(coordinateTransform);
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.ReleaseComObject(value);
            }
        }
    }
}
