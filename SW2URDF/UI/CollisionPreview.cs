using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
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

        public bool Show(Link link, CollisionMeshStrategy strategy,
            MathTransform linkCoordinateTransform, out string status, out string error)
        {
            Hide();
            error = null;

            if (strategy == CollisionMeshStrategy.ConvexHull)
            {
                status = ChineseUiText.Translate(
                    "Live preview is unavailable for convex hull collision geometry.",
                    "凸包碰撞几何暂不支持实时预览。");
                return false;
            }
            if (IsMeshStrategy(strategy))
            {
                status = ChineseUiText.Translate(
                    "Live preview is unavailable for mesh collision strategies.",
                    "网格碰撞策略暂不支持实时预览。");
                return false;
            }
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
                                "Box collision wireframe preview shown.",
                                "已显示盒体碰撞线框预览。");
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
                                "Sphere collision preview shown as three wire circles.",
                                "已用三个线框圆显示球体碰撞预览。");
                            break;
                        case CollisionMeshStrategy.ComponentBoxes:
                            displayedBodyCount = ShowComponentBoxes(modeler, link,
                                displayContext.LinkToDisplayTarget,
                                displayContext.DisplayTarget);
                            status = ChineseUiText.Translate(
                                "Component box wireframe preview shown (" +
                                    displayedBodyCount.ToString(CultureInfo.InvariantCulture) + ").",
                                "已显示组件盒体线框预览（" +
                                    displayedBodyCount.ToString(CultureInfo.InvariantCulture) + " 个）。");
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

        internal static double[][] BuildBoxEdgeDimensions(ExportHelper.LinkLocalBoundingBox box)
        {
            RequireUsableBox(box);
            double[][] corners =
            {
                new[] { box.MinX, box.MinY, box.MinZ },
                new[] { box.MaxX, box.MinY, box.MinZ },
                new[] { box.MaxX, box.MaxY, box.MinZ },
                new[] { box.MinX, box.MaxY, box.MinZ },
                new[] { box.MinX, box.MinY, box.MaxZ },
                new[] { box.MaxX, box.MinY, box.MaxZ },
                new[] { box.MaxX, box.MaxY, box.MaxZ },
                new[] { box.MinX, box.MaxY, box.MaxZ }
            };
            int[][] edgeIndices =
            {
                new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 },
                new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 4 },
                new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 }
            };
            double[][] edges = new double[edgeIndices.Length][];
            for (int index = 0; index < edges.Length; index++)
            {
                edges[index] = BuildLineDimensions(
                    corners[edgeIndices[index][0]], corners[edgeIndices[index][1]]);
            }
            return edges;
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

        internal static double[][] BuildSphereCircleDimensions(ExportHelper.LinkLocalBoundingBox box)
        {
            RequireUsableBox(box);
            double[] center = box.Center;
            double radius = Math.Max(box.Width, Math.Max(box.Depth, box.Height)) / 2.0;
            return new[]
            {
                BuildCircleDimensions(center, radius, new[] { 1.0, 0.0, 0.0 }, new[] { 0.0, 1.0, 0.0 }),
                BuildCircleDimensions(center, radius, new[] { 1.0, 0.0, 0.0 }, new[] { 0.0, 0.0, 1.0 }),
                BuildCircleDimensions(center, radius, new[] { 0.0, 1.0, 0.0 }, new[] { 0.0, 0.0, 1.0 })
            };
        }

        internal static bool IsDisplaySuccess(int result) { return result == 0; }

        private int ShowBox(Modeler modeler, Link link, MathTransform transform,
            object displayTarget)
        {
            double[][] edges = BuildBoxEdgeDimensions(exporter.CreateLinkLocalBoundingBox(link));
            foreach (double[] edge in edges)
            {
                AddLine(modeler, edge, transform, displayTarget);
            }
            return edges.Length;
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
            double[][] circles = BuildSphereCircleDimensions(exporter.CreateLinkLocalBoundingBox(link));
            foreach (double[] circle in circles)
            {
                AddCircle(modeler, circle, transform, displayTarget);
            }
            return circles.Length;
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
                foreach (double[] edge in BuildBoxEdgeDimensions(box))
                {
                    AddLine(modeler, edge, transform, displayTarget);
                }
            }
            return boxes.Count;
        }

        private void AddLine(Modeler modeler, double[] dimensions, MathTransform transform,
            object displayTarget)
        {
            Curve sourceCurve = null;
            Curve trimmedCurve = null;
            Body2 body = null;
            try
            {
                double[] start = { dimensions[0], dimensions[1], dimensions[2] };
                double[] direction =
                {
                    dimensions[3] - dimensions[0],
                    dimensions[4] - dimensions[1],
                    dimensions[5] - dimensions[2]
                };
                sourceCurve = modeler.CreateLine(start, direction) as Curve;
                if (sourceCurve == null)
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks could not create a collision preview line.",
                        "SolidWorks 无法创建碰撞预览线。"));
                }
                trimmedCurve = sourceCurve.CreateTrimmedCurve2(
                    dimensions[0], dimensions[1], dimensions[2],
                    dimensions[3], dimensions[4], dimensions[5]);
                if (trimmedCurve == null)
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks could not trim a collision preview line.",
                        "SolidWorks 无法裁剪碰撞预览线。"));
                }
                body = modeler.CreateWireBody(trimmedCurve,
                    (int)swCreateWireBodyOptions_e.swCreateWireBodyByDefault);
                Body2 ownedBody = body;
                body = null;
                AddBody(ownedBody, transform, DrawingColor.OrangeRed, displayTarget);
            }
            finally
            {
                ReleaseComObject(body);
                ReleaseComObject(trimmedCurve);
                ReleaseComObject(sourceCurve);
            }
        }

        private void AddCircle(Modeler modeler, double[] dimensions, MathTransform transform,
            object displayTarget)
        {
            object curve = null;
            Body2 body = null;
            try
            {
                curve = modeler.CreateEllipse(
                    new[] { dimensions[0], dimensions[1], dimensions[2] },
                    dimensions[3], dimensions[4],
                    new[] { dimensions[5], dimensions[6], dimensions[7] },
                    new[] { dimensions[8], dimensions[9], dimensions[10] });
                if (curve == null)
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks could not create a sphere preview circle.",
                        "SolidWorks 无法创建球体预览圆。"));
                }
                body = modeler.CreateWireBody(curve,
                    (int)swCreateWireBodyOptions_e.swCreateWireBodyByDefault);
                Body2 ownedBody = body;
                body = null;
                AddBody(ownedBody, transform, DrawingColor.OrangeRed, displayTarget);
            }
            finally
            {
                ReleaseComObject(body);
                ReleaseComObject(curve);
            }
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

        private static double[] BuildCircleDimensions(
            double[] center, double radius, double[] majorAxis, double[] minorAxis)
        {
            return new[]
            {
                center[0], center[1], center[2], radius, radius,
                majorAxis[0], majorAxis[1], majorAxis[2],
                minorAxis[0], minorAxis[1], minorAxis[2]
            };
        }

        private static double[] BuildLineDimensions(double[] start, double[] end)
        {
            return new[] { start[0], start[1], start[2], end[0], end[1], end[2] };
        }

        private static bool IsMeshStrategy(CollisionMeshStrategy strategy)
        {
            return strategy == CollisionMeshStrategy.VisualMesh ||
                strategy == CollisionMeshStrategy.SimplifiedMesh ||
                strategy == CollisionMeshStrategy.AccurateMesh;
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
