using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;

namespace SW2URDF.Test
{
    public class TestCollisionPreview
    {
        [Fact]
        public void TestBoxDimensionsUseLowerZFaceAndExactExtents()
        {
            ExportHelper.LinkLocalBoundingBox box = CreateBox(-1.0, -2.0, -3.0, 3.0, 4.0, 5.0);

            double[] result = CollisionPreview.BuildBoxDimensions(box);

            Assert.Equal(new[] { 1.0, 1.0, -3.0, 0.0, 0.0, 1.0, 4.0, 6.0, 8.0 }, result);
        }

        [Fact]
        public void TestCylinderDimensionsUseAxisWithClosestRadialDimensions()
        {
            ExportHelper.LinkLocalBoundingBox box = CreateBox(-2.0, -1.0, -1.5, 4.0, 3.0, 1.5);

            double[] result = CollisionPreview.BuildCylinderDimensions(box);

            Assert.Equal(new[] { -2.0, 1.0, 0.0, 1.0, 0.0, 0.0, 2.0, 6.0 }, result);
        }

        [Fact]
        public void TestCylinderDimensionsUseWheelThicknessAsAxis()
        {
            ExportHelper.LinkLocalBoundingBox box =
                CreateBox(-0.02, -0.10, -0.10, 0.02, 0.10, 0.10);

            double[] result = CollisionPreview.BuildCylinderDimensions(box);

            Assert.Equal(new[] { -0.02, 0.0, 0.0, 1.0, 0.0, 0.0, 0.10, 0.04 }, result);
        }

        [Fact]
        public void TestSphereDimensionsUseCenterAndLargestBoxExtent()
        {
            ExportHelper.LinkLocalBoundingBox box = CreateBox(-1.0, -2.0, -3.0, 3.0, 4.0, 5.0);

            double[] result = CollisionPreview.BuildSphereDimensions(box);

            Assert.Equal(new[] { 1.0, 1.0, 1.0, 4.0 }, result);
        }

        [Fact]
        public void TestConvexHullContainsOnlyValidNonDegenerateTriangles()
        {
            ExportHelper.LinkLocalBoundingBox box =
                CreateBox(-1.0, -2.0, -3.0, 3.0, 4.0, 5.0);

            ExportHelper.ConvexHullGeometry geometry =
                ExportHelper.BuildConvexHullGeometry(box);

            Assert.True(geometry.Vertices.Count >= 4);
            Assert.True(geometry.Triangles.Count >= 4);
            Assert.All(geometry.Triangles, triangle =>
            {
                Assert.Equal(3, triangle.Length);
                Assert.All(triangle, index => Assert.InRange(
                    index, 0, geometry.Vertices.Count - 1));
                Assert.True(TriangleAreaSquared(
                    geometry.Vertices[triangle[0]],
                    geometry.Vertices[triangle[1]],
                    geometry.Vertices[triangle[2]]) > 1e-20);
            });
        }

        [Fact]
        public void TestBodyTransformMovesDocumentGeometryIntoDisplayTarget()
        {
            Matrix<double> linkToDisplayTarget = CreateTranslation(2.0, 0.0, 0.0);
            Matrix<double> linkToDocument = CreateTranslation(10.0, 0.0, 0.0);
            Matrix<double> componentToDocument = CreateTranslation(12.0, 3.0, 0.0);

            Matrix<double> result = CollisionPreview.BuildBodyToDisplayTarget(
                linkToDisplayTarget,
                linkToDocument,
                componentToDocument);

            Assert.Equal(4.0, result[0, 3], 12);
            Assert.Equal(3.0, result[1, 3], 12);
            Assert.Equal(0.0, result[2, 3], 12);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(3, false)]
        public void TestDisplay3ReturnCode(int result, bool expected)
        {
            Assert.Equal(expected, CollisionPreview.IsDisplaySuccess(result));
        }

        [Fact]
        public void TestLiveCollisionStrategiesPreserveComponentAppearance()
        {
            if (!String.Equals(
                System.Environment.GetEnvironmentVariable("SW2URDF_RUN_SW_INTEGRATION_TESTS"),
                "1",
                StringComparison.Ordinal))
            {
                return;
            }

            Exception failure = null;
            Thread staThread = new Thread(() =>
            {
                try
                {
                    RunLiveCollisionStrategiesPreserveComponentAppearance();
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

        private static void RunLiveCollisionStrategiesPreserveComponentAppearance()
        {

            SldWorks swApp = null;
            ModelDoc2 model = null;
            Component2 component = null;
            Component2 displayHost = null;
            MathTransform coordinateTransform = null;
            int? originalVisibility = null;
            int? originalDisplayHostVisibility = null;
            bool ownsApplication = false;
            bool openedTestDocument = false;
            try
            {
                try
                {
                    swApp = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
                }
                catch (COMException)
                {
                    swApp = (SldWorks)Activator.CreateInstance(
                        Type.GetTypeFromProgID("SldWorks.Application"));
                    swApp.Visible = true;
                    ownsApplication = true;
                }
                model = swApp.ActiveDoc as ModelDoc2;
                if (!(model is AssemblyDoc))
                {
                    int openErrors = 0;
                    int openWarnings = 0;
                    string assemblyPath = Path.Combine(
                        SW2URDFTest.GetModelDirectory(SW2URDFTest.ModelName4Wheeler),
                        "4_WHEELER.SLDASM");
                    model = swApp.OpenDoc6(
                        assemblyPath,
                        (int)swDocumentTypes_e.swDocASSEMBLY,
                        (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                        String.Empty,
                        ref openErrors,
                        ref openWarnings) as ModelDoc2;
                    Assert.True(
                        model != null,
                        "SolidWorks could not open the collision preview test assembly. " +
                        "errors=" + openErrors + ", warnings=" + openWarnings +
                        ", path=" + assemblyPath);
                    openedTestDocument = true;
                }
                AssemblyDoc assembly = model as AssemblyDoc;
                Assert.NotNull(assembly);
                object[] topLevelComponents = assembly.GetComponents(true) as object[];
                Component2[] usableComponents = (topLevelComponents ?? new object[0])
                    .Cast<Component2>()
                    .Where(candidate =>
                    {
                        try
                        {
                            double[] box = candidate.GetBox(false, false);
                            return box != null && box.Length >= 6;
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .Take(2)
                    .ToArray();
                Assert.True(
                    usableComponents.Length >= 2,
                    "The live collision preview test requires two top-level components with usable geometry.");
                component = usableComponents[0];
                displayHost = usableComponents[1];
                originalVisibility = component.Visible;
                originalDisplayHostVisibility = displayHost.Visible;
                component.Visible = (int)swComponentVisibilityState_e.swComponentHidden;
                displayHost.Visible = (int)swComponentVisibilityState_e.swComponentVisible;
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
                coordinateTransform = exporter.GetCoordinateSystemTransform(coordinateSystemName);
                Assert.NotNull(coordinateTransform);
                Link link = new Link { Name = "preview_test_link" };
                link.Joint.CoordinateSystemName = coordinateSystemName;
                link.SWComponents.Add(component);
                using (TemporaryBodyDisplayContext context =
                    CreateDisplayContext(swApp, model, link, coordinateTransform))
                {
                    Component2 displayTarget = context.DisplayTarget as Component2;
                    Assert.NotNull(displayTarget);
                    Assert.False(displayTarget.IsHidden(false));
                    ModelDoc2 displayTargetModel = displayTarget.GetModelDoc2() as ModelDoc2;
                    Assert.NotNull(displayTargetModel);
                    Assert.Equal(
                        (int)swDocumentTypes_e.swDocPART,
                        displayTargetModel.GetType());
                }
                double[] before = CloneAppearance(component.MaterialPropertyValues);
                ExportHelper.LinkLocalBoundingBox previewBounds =
                    exporter.CreateLinkLocalBoundingBox(link);
                Assert.True(previewBounds.IsUsable);

                using (CollisionPreview preview =
                    new CollisionPreview(swApp, model, exporter))
                {
                    foreach (CollisionMeshStrategy strategy in new[]
                    {
                        CollisionMeshStrategy.BoxPrimitive,
                        CollisionMeshStrategy.CylinderPrimitive,
                        CollisionMeshStrategy.SpherePrimitive,
                        CollisionMeshStrategy.ComponentBoxes,
                        CollisionMeshStrategy.ConvexHull,
                        CollisionMeshStrategy.VisualMesh,
                        CollisionMeshStrategy.AccurateMesh,
                        CollisionMeshStrategy.SimplifiedMesh
                    })
                    {
                        Assert.True(preview.Show(
                            link,
                            strategy,
                            coordinateTransform,
                            out string status,
                            out string error),
                            strategy + ": " + (error ?? status));
                        Assert.True(preview.IsVisible);
                        Assert.True(preview.TemporaryBodyCount > 0);
                        Assert.True(
                            preview.TryGetDisplayedBounds(out double[] displayedBounds),
                            strategy + " did not create bounded temporary geometry.");
                        Assert.True(displayedBounds.All(value =>
                            !Double.IsNaN(value) && !Double.IsInfinity(value)));
                        Assert.True(component.IsHidden(false));
                        if (strategy == CollisionMeshStrategy.SimplifiedMesh)
                        {
                            Assert.True(
                                status.IndexOf("coarse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                status.Contains("粗化"));
                        }
                        preview.Hide();
                        Assert.False(preview.IsVisible);
                    }
                }

                Assert.Equal(before, CloneAppearance(component.MaterialPropertyValues));

                var visibilitySnapshot = ExportHelper.CaptureComponentVisibility(
                    new[] { component, displayHost });
                component.Visible = (int)swComponentVisibilityState_e.swComponentVisible;
                displayHost.Visible = (int)swComponentVisibilityState_e.swComponentHidden;
                ExportHelper.RestoreComponentVisibility(model, visibilitySnapshot);
                Assert.Equal(
                    (int)swComponentVisibilityState_e.swComponentHidden,
                    component.Visible);
                Assert.Equal(
                    (int)swComponentVisibilityState_e.swComponentVisible,
                    displayHost.Visible);
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
                if (openedTestDocument && swApp != null)
                {
                    try { swApp.CloseDoc(model == null ? String.Empty : model.GetTitle()); }
                    catch { }
                }
                if (ownsApplication && swApp != null)
                {
                    try { swApp.ExitApp(); }
                    catch { }
                    ReleaseComObject(swApp);
                }
            }
        }

        private static TemporaryBodyDisplayContext CreateDisplayContext(
            SldWorks swApp,
            ModelDoc2 model,
            Link link,
            MathTransform coordinateTransform)
        {
            Assert.True(
                TemporaryBodyDisplayContext.TryCreate(
                    swApp,
                    model,
                    link,
                    coordinateTransform,
                    out TemporaryBodyDisplayContext context,
                    out string error),
                error);
            return context;
        }

        private static double[] CloneAppearance(object values)
        {
            double[] appearance = values as double[];
            return appearance == null ? null : (double[])appearance.Clone();
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.ReleaseComObject(value);
            }
        }

        private static double TriangleAreaSquared(double[] first, double[] second, double[] third)
        {
            double abX = second[0] - first[0];
            double abY = second[1] - first[1];
            double abZ = second[2] - first[2];
            double acX = third[0] - first[0];
            double acY = third[1] - first[1];
            double acZ = third[2] - first[2];
            double crossX = abY * acZ - abZ * acY;
            double crossY = abZ * acX - abX * acZ;
            double crossZ = abX * acY - abY * acX;
            return crossX * crossX + crossY * crossY + crossZ * crossZ;
        }

        private static ExportHelper.LinkLocalBoundingBox CreateBox(
            double minX,
            double minY,
            double minZ,
            double maxX,
            double maxY,
            double maxZ)
        {
            ExportHelper.LinkLocalBoundingBox box = new ExportHelper.LinkLocalBoundingBox();
            box.Include(minX, minY, minZ);
            box.Include(maxX, maxY, maxZ);
            return box;
        }

        private static Matrix<double> CreateTranslation(double x, double y, double z)
        {
            Matrix<double> matrix = Matrix<double>.Build.DenseIdentity(4);
            matrix[0, 3] = x;
            matrix[1, 3] = y;
            matrix[2, 3] = z;
            return matrix;
        }
    }
}
