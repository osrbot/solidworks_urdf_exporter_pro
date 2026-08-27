using SolidWorks.Interop.sldworks;
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
        public void TestBoxWireframeContainsTwelveNonZeroEdges()
        {
            ExportHelper.LinkLocalBoundingBox box = CreateBox(-1.0, -2.0, -3.0, 3.0, 4.0, 5.0);

            double[][] edges = CollisionPreview.BuildBoxEdgeDimensions(box);

            Assert.Equal(12, edges.Length);
            Assert.All(edges, edge =>
            {
                Assert.Equal(6, edge.Length);
                double lengthSquared =
                    Math.Pow(edge[3] - edge[0], 2.0) +
                    Math.Pow(edge[4] - edge[1], 2.0) +
                    Math.Pow(edge[5] - edge[2], 2.0);
                Assert.True(lengthSquared > 0.0);
            });
            Assert.Equal(12, edges.Select(NormalizeEdge).Distinct().Count());
        }

        [Fact]
        public void TestCylinderDimensionsMatchExporterLongestAxisRule()
        {
            ExportHelper.LinkLocalBoundingBox box = CreateBox(-2.0, -1.0, -1.5, 4.0, 3.0, 1.5);

            double[] result = CollisionPreview.BuildCylinderDimensions(box);

            Assert.Equal(new[] { -2.0, 1.0, 0.0, 1.0, 0.0, 0.0, 2.0, 6.0 }, result);
        }

        [Fact]
        public void TestCylinderWireframeUsesTwoCirclesAndFourAxialLines()
        {
            ExportHelper.LinkLocalBoundingBox box = CreateBox(-2.0, -1.0, -1.5, 4.0, 3.0, 1.5);

            double[][] circles = CollisionPreview.BuildCylinderCircleDimensions(box);
            double[][] lines = CollisionPreview.BuildCylinderLineDimensions(box);

            Assert.Equal(2, circles.Length);
            Assert.Equal(4, lines.Length);
            Assert.Equal(-2.0, circles[0][0], 12);
            Assert.Equal(4.0, circles[1][0], 12);
            Assert.All(lines, line =>
            {
                Assert.Equal(6.0, line[3] - line[0], 12);
                Assert.Equal(0.0, line[4] - line[1], 12);
                Assert.Equal(0.0, line[5] - line[2], 12);
            });
        }

        [Fact]
        public void TestSphereUsesThreeOrthogonalGreatCircles()
        {
            ExportHelper.LinkLocalBoundingBox box = CreateBox(-1.0, -2.0, -3.0, 3.0, 4.0, 5.0);

            double[][] result = CollisionPreview.BuildSphereCircleDimensions(box);

            Assert.Equal(3, result.Length);
            Assert.Equal(new[] { 1.0, 1.0, 1.0, 4.0, 4.0, 1.0, 0.0, 0.0, 0.0, 1.0, 0.0 }, result[0]);
            Assert.Equal(new[] { 1.0, 1.0, 1.0, 4.0, 4.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0 }, result[1]);
            Assert.Equal(new[] { 1.0, 1.0, 1.0, 4.0, 4.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0 }, result[2]);
        }

        [Theory]
        [InlineData(CollisionMeshStrategy.VisualMesh)]
        [InlineData(CollisionMeshStrategy.SimplifiedMesh)]
        [InlineData(CollisionMeshStrategy.AccurateMesh)]
        public void TestMeshStrategiesReturnExplicitStatusWithoutOverlay(
            CollisionMeshStrategy strategy)
        {
            using (CollisionPreview preview = new CollisionPreview(null, null, null))
            {
                bool shown = preview.Show(null, strategy, null, out string status, out string error);

                Assert.False(shown);
                Assert.False(preview.IsVisible);
                Assert.True(
                    status.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    status.Contains("预览"));
                Assert.Null(error);
            }
        }

        [Fact]
        public void TestConvexHullExplicitlyReportsLivePreviewUnavailable()
        {
            using (CollisionPreview preview = new CollisionPreview(null, null, null))
            {
                bool shown = preview.Show(
                    null,
                    CollisionMeshStrategy.ConvexHull,
                    null,
                    out string status,
                    out string error);

                Assert.False(shown);
                Assert.False(preview.IsVisible);
                Assert.True(
                    status.IndexOf("convex hull", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    status.Contains("凸包"));
                Assert.True(
                    status.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    status.Contains("不支持"));
                Assert.Null(error);
            }
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
        public void TestLiveBoxPreviewPreservesComponentAppearance()
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
                    RunLiveBoxPreviewPreservesComponentAppearance();
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

        private static void RunLiveBoxPreviewPreservesComponentAppearance()
        {

            SldWorks swApp = null;
            ModelDoc2 model = null;
            Component2 component = null;
            MathTransform coordinateTransform = null;
            try
            {
                swApp = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
                model = swApp.ActiveDoc as ModelDoc2;
                AssemblyDoc assembly = model as AssemblyDoc;
                Assert.NotNull(assembly);
                object[] components = assembly.GetComponents(false) as object[];
                Assert.NotNull(components);
                object[] topLevelComponents = assembly.GetComponents(true) as object[];
                component = (topLevelComponents ?? new object[0])
                    .Cast<Component2>()
                    .FirstOrDefault(candidate =>
                    {
                        try
                        {
                            double[] box = candidate.GetBox(false, false);
                            return candidate.IsHidden(false) && box != null && box.Length >= 6;
                        }
                        catch
                        {
                            return false;
                        }
                    });
                component = component ?? components.Cast<Component2>().FirstOrDefault(candidate =>
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
                });
                Assert.NotNull(component);

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
                        CollisionMeshStrategy.SpherePrimitive
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
                        preview.Hide();
                        Assert.False(preview.IsVisible);
                    }
                }

                Assert.Equal(before, CloneAppearance(component.MaterialPropertyValues));
            }
            finally
            {
                ReleaseComObject(coordinateTransform);
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

        private static string NormalizeEdge(double[] edge)
        {
            string start = String.Join(",", edge.Take(3).Select(FormatCoordinate));
            string end = String.Join(",", edge.Skip(3).Take(3).Select(FormatCoordinate));
            return String.CompareOrdinal(start, end) <= 0
                ? start + "|" + end
                : end + "|" + start;
        }

        private static string FormatCoordinate(double value)
        {
            return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
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
    }
}
