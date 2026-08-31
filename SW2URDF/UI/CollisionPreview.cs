using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using DrawingColor = System.Drawing.Color;

namespace SW2URDF.UI
{
    internal sealed class CollisionPreview : IDisposable
    {
        private readonly SldWorks swApp;
        private readonly ModelDoc2 model;
        private readonly ExportHelper exporter;
        private readonly List<Body2> temporaryBodies = new List<Body2>();

        public CollisionPreview(SldWorks swApp, ModelDoc2 model, ExportHelper exporter)
        {
            this.swApp = swApp;
            this.model = model;
            this.exporter = exporter;
        }

        public bool IsVisible { get { return temporaryBodies.Count > 0; } }

        internal int TemporaryBodyCount { get { return temporaryBodies.Count; } }

        internal bool TryGetDisplayedBounds(out double[] bounds)
        {
            bounds = null;
            foreach (Body2 body in temporaryBodies)
            {
                double[] bodyBounds = null;
                try
                {
                    bodyBounds = body.GetBodyBox() as double[];
                }
                catch (COMException) { }
                if (bodyBounds == null || bodyBounds.Length < 6)
                {
                    continue;
                }
                if (bounds == null)
                {
                    bounds = (double[])bodyBounds.Clone();
                    continue;
                }
                bounds[0] = Math.Min(bounds[0], bodyBounds[0]);
                bounds[1] = Math.Min(bounds[1], bodyBounds[1]);
                bounds[2] = Math.Min(bounds[2], bodyBounds[2]);
                bounds[3] = Math.Max(bounds[3], bodyBounds[3]);
                bounds[4] = Math.Max(bounds[4], bodyBounds[4]);
                bounds[5] = Math.Max(bounds[5], bodyBounds[5]);
            }
            return bounds != null &&
                bounds[3] > bounds[0] &&
                bounds[4] > bounds[1] &&
                bounds[5] > bounds[2];
        }

        public bool Show(Link link, CollisionMeshStrategy strategy,
            MathTransform linkCoordinateTransform, out string status, out string error)
        {
            Hide();
            error = null;

            if (link == null || linkCoordinateTransform == null ||
                swApp == null || model == null || exporter == null)
            {
                status = ChineseUiText.Translate(
                    "Collision preview is unavailable.", "碰撞预览不可用。");
                error = ChineseUiText.Translate(
                    "The link, link coordinate transform, model, or exporter is missing.",
                    "缺少 Link、Link 坐标变换、模型或导出器。");
                return false;
            }

            Modeler modeler = null;
            try
            {
                if (!TemporaryBodyDisplayContext.TryCreate(
                    swApp,
                    model,
                    link,
                    linkCoordinateTransform,
                    out TemporaryBodyDisplayContext displayContext,
                    out string displayContextError))
                {
                    status = ChineseUiText.Translate(
                        "Collision preview is unavailable.", "碰撞预览不可用。");
                    error = displayContextError;
                    return false;
                }

                modeler = swApp.GetModeler() as Modeler;
                if (modeler == null)
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks modeler is unavailable.", "SolidWorks 建模器不可用。"));
                }

                int displayedBodyCount;
                using (displayContext)
                {
                    switch (strategy)
                    {
                        case CollisionMeshStrategy.Primitive:
                        case CollisionMeshStrategy.BoxPrimitive:
                            displayedBodyCount = ShowBox(modeler, link,
                                displayContext.LinkToDisplayTarget,
                                displayContext.DisplayTarget);
                            status = ChineseUiText.Translate(
                                "Solid box collision preview shown.",
                                "已显示实体盒体碰撞预览。");
                            break;
                        case CollisionMeshStrategy.CylinderPrimitive:
                            displayedBodyCount = ShowCylinder(modeler, link,
                                displayContext.LinkToDisplayTarget,
                                displayContext.DisplayTarget);
                            status = ChineseUiText.Translate(
                                "Solid cylinder collision preview shown.",
                                "已显示实体圆柱碰撞预览。");
                            break;
                        case CollisionMeshStrategy.SpherePrimitive:
                            displayedBodyCount = ShowSphere(modeler, link,
                                displayContext.LinkToDisplayTarget,
                                displayContext.DisplayTarget);
                            status = ChineseUiText.Translate(
                                "Spherical collision body preview shown.",
                                "已显示球形碰撞体预览。");
                            break;
                        case CollisionMeshStrategy.ComponentBoxes:
                            displayedBodyCount = ShowComponentBoxes(modeler, link,
                                displayContext.LinkToDisplayTarget,
                                displayContext.DisplayTarget);
                            status = ChineseUiText.Translate(
                                "Solid component-box collision preview shown (" +
                                    displayedBodyCount.ToString(CultureInfo.InvariantCulture) + ").",
                                "已显示实体组件盒体碰撞预览（" +
                                    displayedBodyCount.ToString(CultureInfo.InvariantCulture) + " 个）。");
                            break;
                        case CollisionMeshStrategy.ConvexHull:
                            displayedBodyCount = ShowConvexHull(modeler, link,
                                displayContext.LinkToDisplayTarget,
                                displayContext.DisplayTarget);
                            status = ChineseUiText.Translate(
                                "Faceted convex hull collision preview shown.",
                                "已显示分面凸包碰撞体预览。");
                            break;
                        case CollisionMeshStrategy.VisualMesh:
                            displayedBodyCount = ShowComponentBodies(
                                link,
                                linkCoordinateTransform,
                                displayContext.LinkToDisplayTarget,
                                displayContext.DisplayTarget);
                            status = ChineseUiText.Translate(
                                "Visual-mesh collision geometry preview shown from SolidWorks bodies.",
                                "已使用 SolidWorks 实体显示复用可视网格的碰撞几何预览。");
                            break;
                        case CollisionMeshStrategy.AccurateMesh:
                            displayedBodyCount = ShowComponentBodies(
                                link,
                                linkCoordinateTransform,
                                displayContext.LinkToDisplayTarget,
                                displayContext.DisplayTarget);
                            status = ChineseUiText.Translate(
                                "Accurate collision geometry preview shown from SolidWorks bodies.",
                                "已使用 SolidWorks 实体显示精确碰撞几何预览。");
                            break;
                        case CollisionMeshStrategy.SimplifiedMesh:
                            displayedBodyCount = ShowComponentBodies(
                                link,
                                linkCoordinateTransform,
                                displayContext.LinkToDisplayTarget,
                                displayContext.DisplayTarget);
                            status = ChineseUiText.Translate(
                                "CAD shape preview shown. The final coarse-tessellation STL uses the selected tolerance and may have fewer facets.",
                                "已显示 CAD 外形预览；最终粗化三角化 STL 使用所选公差，分面数量可能更少。");
                            break;
                        default:
                            status = ChineseUiText.Translate(
                                "Live preview is unavailable for this collision strategy.",
                                "此碰撞策略暂不支持实时预览。");
                            error = status;
                            return false;
                    }
                }

                model.GraphicsRedraw2();
                return displayedBodyCount > 0;
            }
            catch (Exception exception)
            {
                Hide();
                status = ChineseUiText.Translate(
                    "Collision preview could not be displayed.", "无法显示碰撞预览。");
                error = exception.Message;
                return false;
            }
            finally
            {
                ReleaseComReference(modeler);
            }
        }

        public void Hide()
        {
            foreach (Body2 body in temporaryBodies)
            {
                try { body.Hide(model); }
                catch { }
                finally { ReleaseComObject(body); }
            }
            temporaryBodies.Clear();
            if (model != null)
            {
                try { model.GraphicsRedraw2(); }
                catch { }
            }
        }

        public void Dispose() { Hide(); }

        internal static double[] BuildBoxDimensions(ExportHelper.LinkLocalBoundingBox box)
        {
            RequireUsableBox(box);
            double[] center = box.Center;
            return new[]
            {
                center[0], center[1], box.MinZ, 0.0, 0.0, 1.0,
                box.Width, box.Depth, box.Height
            };
        }

        internal static double[] BuildCylinderDimensions(ExportHelper.LinkLocalBoundingBox box)
        {
            RequireUsableBox(box);
            int axis = box.CylinderAxisIndex;
            int uAxis = (axis + 1) % 3;
            int vAxis = (axis + 2) % 3;
            double[] center = box.Center;
            double[] dimensions = new double[8];
            dimensions[0] = center[0];
            dimensions[1] = center[1];
            dimensions[2] = center[2];
            dimensions[axis] -= box.GetDimension(axis) / 2.0;
            dimensions[3 + axis] = 1.0;
            dimensions[6] = Math.Max(box.GetDimension(uAxis), box.GetDimension(vAxis)) / 2.0;
            dimensions[7] = box.GetDimension(axis);
            return dimensions;
        }

        internal static double[] BuildSphereDimensions(ExportHelper.LinkLocalBoundingBox box)
        {
            RequireUsableBox(box);
            double[] center = box.Center;
            double radius = Math.Max(box.Width, Math.Max(box.Depth, box.Height)) / 2.0;
            return new[] { center[0], center[1], center[2], radius };
        }

        internal static Matrix<double> BuildBodyToDisplayTarget(
            Matrix<double> linkToDisplayTarget,
            Matrix<double> linkToDocument,
            Matrix<double> componentToDocument)
        {
            if (linkToDisplayTarget == null || linkToDocument == null ||
                componentToDocument == null)
            {
                throw new ArgumentNullException("A collision preview transform is missing.");
            }
            return linkToDisplayTarget * linkToDocument.Inverse() * componentToDocument;
        }

        internal static bool IsDisplaySuccess(int result) { return result == 0; }

        private int ShowBox(Modeler modeler, Link link, MathTransform transform,
            object displayTarget)
        {
            Body2 body = null;
            try
            {
                body = CreateBoxBody(modeler, exporter.CreateLinkLocalBoundingBox(link));
                Body2 ownedBody = body;
                body = null;
                AddBody(ownedBody, transform, DrawingColor.OrangeRed, displayTarget);
                return 1;
            }
            finally
            {
                ReleaseComObject(body);
            }
        }

        private int ShowCylinder(Modeler modeler, Link link, MathTransform transform,
            object displayTarget)
        {
            Body2 body = null;
            try
            {
                double[] dimensions = BuildCylinderDimensions(
                    exporter.CreateLinkLocalBoundingBox(link));
                body = modeler.CreateBodyFromCyl(dimensions) as Body2;
                if (body == null)
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks could not create the solid cylinder collision preview.",
                        "SolidWorks 无法创建实体圆柱碰撞预览。"));
                }

                Body2 ownedBody = body;
                body = null;
                AddBody(ownedBody, transform, DrawingColor.OrangeRed, displayTarget);
                return 1;
            }
            finally
            {
                ReleaseComObject(body);
            }
        }

        private int ShowSphere(Modeler modeler, Link link, MathTransform transform,
            object displayTarget)
        {
            Surface surface = null;
            Body2 body = null;
            try
            {
                double[] dimensions = BuildSphereDimensions(
                    exporter.CreateLinkLocalBoundingBox(link));
                surface = modeler.CreateSphericalSurface2(
                    new[] { dimensions[0], dimensions[1], dimensions[2] },
                    new[] { 0.0, 0.0, 1.0 },
                    new[] { 1.0, 0.0, 0.0 },
                    dimensions[3]) as Surface;
                if (surface == null)
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks could not create the spherical collision surface.",
                        "SolidWorks 无法创建球形碰撞曲面。"));
                }

                body = surface.CreateTrimmedSheet5(
                    new Curve[] { null },
                    true,
                    0.00001) as Body2;
                if (body == null)
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks could not create the spherical collision body.",
                        "SolidWorks 无法创建球形碰撞体。"));
                }

                Body2 ownedBody = body;
                body = null;
                AddBody(ownedBody, transform, DrawingColor.OrangeRed, displayTarget);
                return 1;
            }
            finally
            {
                ReleaseComObject(body);
                ReleaseComObject(surface);
            }
        }

        private int ShowComponentBoxes(Modeler modeler, Link link, MathTransform transform,
            object displayTarget)
        {
            IList<ExportHelper.LinkLocalBoundingBox> boxes =
                exporter.CreateComponentLocalBoundingBoxes(link);
            if (boxes == null || boxes.Count == 0)
            {
                throw new InvalidOperationException(ChineseUiText.Translate(
                    "No usable component bounding boxes were found.",
                    "未找到可用的组件包围盒。"));
            }
            foreach (ExportHelper.LinkLocalBoundingBox box in boxes)
            {
                Body2 body = null;
                try
                {
                    body = CreateBoxBody(modeler, box);
                    Body2 ownedBody = body;
                    body = null;
                    AddBody(ownedBody, transform, DrawingColor.OrangeRed, displayTarget);
                }
                finally
                {
                    ReleaseComObject(body);
                }
            }
            return boxes.Count;
        }

        private static Body2 CreateBoxBody(
            Modeler modeler,
            ExportHelper.LinkLocalBoundingBox box)
        {
            double[] dimensions = BuildBoxDimensions(box);
            Body2 body;
            try
            {
                body = modeler.CreateBodyFromBox3(dimensions);
            }
            catch (COMException exception) when (
                exception.ErrorCode == unchecked((int)0x8002000D))
            {
                body = modeler.ICreateBodyFromBox2(ref dimensions[0]);
                if (body == null)
                {
                    body = modeler.CreateBodyFromBox(dimensions) as Body2;
                }
            }
            if (body == null)
            {
                throw new InvalidOperationException(ChineseUiText.Translate(
                    "SolidWorks could not create the solid box collision preview.",
                    "SolidWorks 无法创建实体盒体碰撞预览。"));
            }
            return body;
        }

        private int ShowConvexHull(Modeler modeler, Link link, MathTransform transform,
            object displayTarget)
        {
            ExportHelper.ConvexHullGeometry geometry = ExportHelper.BuildConvexHullGeometry(
                exporter.CreateLinkLocalBoundingBox(link));
            if (geometry.Triangles == null || geometry.Triangles.Count == 0)
            {
                throw new InvalidOperationException(ChineseUiText.Translate(
                    "No usable convex hull faces were found.",
                    "未找到可用的凸包面。"));
            }

            List<Body2> triangleSheets = new List<Body2>();
            List<Body2> sewnBodies = new List<Body2>();
            try
            {
                foreach (int[] triangle in geometry.Triangles)
                {
                    Body2 sheet = CreateTriangleSheet(modeler, geometry.Vertices, triangle);
                    if (sheet != null)
                    {
                        triangleSheets.Add(sheet);
                    }
                }
                if (triangleSheets.Count == 0)
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks could not create the convex hull faces.",
                        "SolidWorks 无法创建凸包面。"));
                }

                sewnBodies.AddRange(SewTriangleSheets(modeler, triangleSheets));
                IList<Body2> bodiesToDisplay = sewnBodies.Count > 0
                    ? (IList<Body2>)sewnBodies
                    : triangleSheets;
                int displayedBodyCount = bodiesToDisplay.Count;
                while (bodiesToDisplay.Count > 0)
                {
                    Body2 previewBody = bodiesToDisplay[0];
                    bodiesToDisplay.RemoveAt(0);
                    AddBody(previewBody, transform, DrawingColor.OrangeRed, displayTarget);
                }
                return displayedBodyCount;
            }
            finally
            {
                ReleaseComObjects(sewnBodies);
                ReleaseComObjects(triangleSheets);
            }
        }

        private static Body2 CreateTriangleSheet(
            Modeler modeler,
            IList<double[]> vertices,
            int[] triangle)
        {
            if (vertices == null || triangle == null || triangle.Length < 3 ||
                triangle[0] < 0 || triangle[1] < 0 || triangle[2] < 0 ||
                triangle[0] >= vertices.Count ||
                triangle[1] >= vertices.Count ||
                triangle[2] >= vertices.Count)
            {
                return null;
            }
            double[] first = vertices[triangle[0]];
            double[] second = vertices[triangle[1]];
            double[] third = vertices[triangle[2]];
            double[] firstEdge = Subtract(second, first);
            double[] secondEdge = Subtract(third, first);
            double[] normal = Normalize(Cross(firstEdge, secondEdge));
            double[] reference = Normalize(firstEdge);
            if (normal == null || reference == null)
            {
                return null;
            }

            Surface surface = null;
            List<Curve> sourceCurves = new List<Curve>();
            List<Curve> trimmedCurves = new List<Curve>();
            Body2 body = null;
            try
            {
                surface = modeler.CreatePlanarSurface2(first, normal, reference) as Surface;
                if (surface == null)
                {
                    return null;
                }
                double[][] points = { first, second, third, first };
                for (int index = 0; index < 3; index++)
                {
                    double[] direction = Subtract(points[index + 1], points[index]);
                    Curve source = modeler.CreateLine(points[index], direction) as Curve;
                    if (source == null)
                    {
                        return null;
                    }
                    sourceCurves.Add(source);
                    Curve trimmed = source.CreateTrimmedCurve2(
                        points[index][0], points[index][1], points[index][2],
                        points[index + 1][0], points[index + 1][1], points[index + 1][2]);
                    if (trimmed == null)
                    {
                        return null;
                    }
                    trimmedCurves.Add(trimmed);
                }
                body = surface.CreateTrimmedSheet5(trimmedCurves.ToArray(), true, 0.00001)
                    as Body2;
                Body2 result = body;
                body = null;
                return result;
            }
            finally
            {
                ReleaseComObject(body);
                ReleaseComObjects(trimmedCurves);
                ReleaseComObjects(sourceCurves);
                ReleaseComObject(surface);
            }
        }

        private static IList<Body2> SewTriangleSheets(Modeler modeler, IList<Body2> sheets)
        {
            List<Body2> bodies = new List<Body2>();
            int error = 0;
            object result = null;
            try
            {
                result = modeler.CreateBodiesFromSheets2(
                    new List<Body2>(sheets).ToArray(),
                    (int)swSheetSewingOption_e.swSewToSolidOrSheets,
                    0.000001,
                    ref error);
                Body2 singleBody = result as Body2;
                if (singleBody != null)
                {
                    bodies.Add(singleBody);
                    result = null;
                    return bodies;
                }
                object[] resultArray = result as object[];
                if (resultArray != null)
                {
                    foreach (object candidate in resultArray)
                    {
                        Body2 body = candidate as Body2;
                        if (body != null)
                        {
                            bodies.Add(body);
                        }
                    }
                    result = null;
                }
                return bodies;
            }
            catch (COMException)
            {
                ReleaseComObjects(bodies);
                bodies.Clear();
                return bodies;
            }
            finally
            {
                ReleaseComObject(result);
            }
        }

        private static double[] Subtract(double[] left, double[] right)
        {
            return new[]
            {
                left[0] - right[0],
                left[1] - right[1],
                left[2] - right[2]
            };
        }

        private static double[] Cross(double[] left, double[] right)
        {
            return new[]
            {
                left[1] * right[2] - left[2] * right[1],
                left[2] * right[0] - left[0] * right[2],
                left[0] * right[1] - left[1] * right[0]
            };
        }

        private static double[] Normalize(double[] vector)
        {
            double length = Math.Sqrt(
                vector[0] * vector[0] +
                vector[1] * vector[1] +
                vector[2] * vector[2]);
            if (Double.IsNaN(length) || Double.IsInfinity(length) || length <= 1e-12)
            {
                return null;
            }
            return new[] { vector[0] / length, vector[1] / length, vector[2] / length };
        }

        private int ShowComponentBodies(
            Link link,
            MathTransform linkToDocument,
            MathTransform linkToDisplayTarget,
            object displayTarget)
        {
            if (link.SWComponents == null || link.SWComponents.Count == 0)
            {
                throw new InvalidOperationException(ChineseUiText.Translate(
                    "The Link has no SolidWorks components to preview.",
                    "该 Link 没有可供预览的 SolidWorks 组件。"));
            }

            Matrix<double> linkToDocumentMatrix = MathOps.GetTransformation(linkToDocument);
            Matrix<double> linkToDisplayTargetMatrix =
                MathOps.GetTransformation(linkToDisplayTarget);
            MathUtility mathUtility = null;
            try
            {
                mathUtility = swApp.GetMathUtility() as MathUtility;
                if (mathUtility == null)
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks MathUtility is unavailable.",
                        "SolidWorks MathUtility 不可用。"));
                }

                HashSet<string> visitedComponents = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                int displayedBodyCount = 0;
                foreach (Component2 component in link.SWComponents)
                {
                    displayedBodyCount += ShowComponentBodiesRecursive(
                        component,
                        linkToDocumentMatrix,
                        linkToDisplayTargetMatrix,
                        displayTarget,
                        mathUtility,
                        visitedComponents);
                }
                if (displayedBodyCount == 0)
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "No usable SolidWorks bodies were found for this Link.",
                        "未找到该 Link 可用的 SolidWorks 实体。"));
                }
                return displayedBodyCount;
            }
            finally
            {
                ReleaseComReference(mathUtility);
            }
        }

        private int ShowComponentBodiesRecursive(
            Component2 component,
            Matrix<double> linkToDocument,
            Matrix<double> linkToDisplayTarget,
            object displayTarget,
            MathUtility mathUtility,
            ISet<string> visitedComponents)
        {
            if (component == null ||
                !visitedComponents.Add(GetComponentIdentity(component)))
            {
                return 0;
            }

            Matrix<double> componentToDocument = Matrix<double>.Build.DenseIdentity(4);
            MathTransform componentTransform = null;
            MathTransform bodyToDisplayTarget = null;
            int displayedBodyCount = 0;
            try
            {
                componentTransform =
                    ReferenceGeometryResolver.GetComponentToRootTransform(component);
                if (componentTransform != null)
                {
                    componentToDocument = MathOps.GetTransformation(componentTransform);
                }
                Matrix<double> bodyTransformMatrix = BuildBodyToDisplayTarget(
                    linkToDisplayTarget,
                    linkToDocument,
                    componentToDocument);
                bodyToDisplayTarget = mathUtility.CreateTransform(
                    TemporaryBodyDisplayContext.ToSolidWorksTransformData(bodyTransformMatrix))
                    as MathTransform;
                if (bodyToDisplayTarget == null)
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks could not create the collision body preview transform.",
                        "SolidWorks 无法创建碰撞实体预览变换。"));
                }

                object[] bodies = null;
                try
                {
                    object bodyInfo;
                    bodies = component.GetBodies3(
                        (int)swBodyType_e.swSolidBody,
                        out bodyInfo) as object[];
                }
                catch (COMException)
                {
                    bodies = null;
                }
                foreach (object bodyObject in bodies ?? new object[0])
                {
                    Body2 sourceBody = bodyObject as Body2;
                    if (sourceBody == null)
                    {
                        continue;
                    }
                    try
                    {
                        Body2 copiedBody = sourceBody.Copy() as Body2;
                        if (copiedBody == null)
                        {
                            continue;
                        }
                        Body2 ownedBody = copiedBody;
                        copiedBody = null;
                        AddBody(ownedBody, bodyToDisplayTarget,
                            DrawingColor.OrangeRed, displayTarget);
                        displayedBodyCount++;
                    }
                    finally
                    {
                        ReleaseComReference(sourceBody);
                    }
                }
            }
            finally
            {
                ReleaseComReference(bodyToDisplayTarget);
                ReleaseComReference(componentTransform);
            }

            object[] children = null;
            try
            {
                children = component.GetChildren() as object[];
            }
            catch (COMException) { }
            foreach (object childObject in children ?? new object[0])
            {
                displayedBodyCount += ShowComponentBodiesRecursive(
                    childObject as Component2,
                    linkToDocument,
                    linkToDisplayTarget,
                    displayTarget,
                    mathUtility,
                    visitedComponents);
            }
            return displayedBodyCount;
        }

        private void AddBody(Body2 body, MathTransform transform, DrawingColor color,
            object displayTarget)
        {
            if (body == null)
            {
                throw new InvalidOperationException(ChineseUiText.Translate(
                    "SolidWorks could not create the temporary collision body.",
                    "SolidWorks 无法创建临时碰撞体。"));
            }
            bool displayed = false;
            try
            {
                if (!body.ApplyTransform(transform))
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks could not transform the temporary collision body.",
                        "SolidWorks 无法变换临时碰撞体。"));
                }
                int result = body.Display3(displayTarget, ColorTranslator.ToOle(color),
                    (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);
                if (!IsDisplaySuccess(result))
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks could not display the temporary collision body. Display3 error code: ",
                        "SolidWorks 无法显示临时碰撞体。Display3 错误码：") +
                        result.ToString(CultureInfo.InvariantCulture) + ".");
                }
                displayed = true;
                temporaryBodies.Add(body);
                body = null;
            }
            catch (COMException exception)
            {
                throw new InvalidOperationException(ChineseUiText.Translate(
                    "SolidWorks collision preview API call failed: ",
                    "SolidWorks 碰撞预览 API 调用失败：") + exception.Message, exception);
            }
            finally
            {
                if (displayed && body != null)
                {
                    try { body.Hide(model); }
                    catch { }
                }
                ReleaseComObject(body);
            }
        }

        private static string GetComponentIdentity(Component2 component)
        {
            try
            {
                string name = component.Name2;
                if (!String.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            catch (COMException) { }
            return component.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }

        private static void RequireUsableBox(ExportHelper.LinkLocalBoundingBox box)
        {
            if (box == null || !box.IsUsable)
            {
                throw new InvalidOperationException(ChineseUiText.Translate(
                    "No usable link-local bounding box was found.",
                    "未找到可用的 Link 局部包围盒。"));
            }
        }

        private static void ReleaseComObject(object value)
        {
            try
            {
                if (value != null && Marshal.IsComObject(value))
                {
                    Marshal.FinalReleaseComObject(value);
                }
            }
            catch (InvalidComObjectException) { }
            catch (COMException) { }
        }

        private static void ReleaseComObjects<T>(IEnumerable<T> values)
        {
            if (values == null)
            {
                return;
            }
            foreach (T value in values)
            {
                ReleaseComObject(value);
            }
        }

        private static void ReleaseComReference(object value)
        {
            try
            {
                if (value != null && Marshal.IsComObject(value))
                {
                    Marshal.ReleaseComObject(value);
                }
            }
            catch (InvalidComObjectException) { }
            catch (COMException) { }
        }
    }
}
