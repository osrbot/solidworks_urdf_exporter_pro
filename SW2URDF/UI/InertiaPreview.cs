using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using DrawingColor = System.Drawing.Color;

namespace SW2URDF.UI
{
    internal enum InertiaPreviewFailureKind
    {
        None,
        InvalidPhysicalInertia,
        DisplayUnavailable
    }

    internal sealed class InertiaPreview : IDisposable
    {
        internal const int ExpectedBodyCount = 4;
        internal const double PrincipalAxisExtensionFactor = 1.15;

        private readonly SldWorks swApp;
        private readonly ModelDoc2 model;
        private readonly List<Body2> temporaryBodies = new List<Body2>();
        private TemporaryBodyDisplayContext displayContext;

        public InertiaPreview(SldWorks swApp, ModelDoc2 model)
        {
            this.swApp = swApp;
            this.model = model;
        }

        public bool IsVisible
        {
            get { return temporaryBodies.Count > 0; }
        }

        public bool Show(
            Link link,
            MathTransform linkCoordinateTransform,
            out InertiaEllipsoid ellipsoid,
            out string error)
        {
            InertiaPreviewFailureKind failureKind;
            return Show(link, linkCoordinateTransform, out ellipsoid, out error, out failureKind);
        }

        public bool Show(
            Link link,
            MathTransform linkCoordinateTransform,
            out InertiaEllipsoid ellipsoid,
            out string error,
            out InertiaPreviewFailureKind failureKind)
        {
            Hide();
            failureKind = InertiaPreviewFailureKind.None;
            if (link == null || link.Inertial == null || link.Inertial.Mass == null ||
                link.Inertial.Inertia == null)
            {
                ellipsoid = null;
                error = "The link has no complete inertial element.";
                failureKind = InertiaPreviewFailureKind.InvalidPhysicalInertia;
                return false;
            }

            if (!InertiaEllipsoid.TryCreate(
                link.Inertial.Mass.Value,
                link.Inertial.Inertia,
                out ellipsoid,
                out error))
            {
                failureKind = InertiaPreviewFailureKind.InvalidPhysicalInertia;
                return false;
            }
            if (linkCoordinateTransform == null)
            {
                error = "The link coordinate system was not found.";
                failureKind = InertiaPreviewFailureKind.DisplayUnavailable;
                return false;
            }

            try
            {
                if (!TemporaryBodyDisplayContext.TryCreate(
                    swApp,
                    model,
                    linkCoordinateTransform,
                    out TemporaryBodyDisplayContext createdDisplayContext,
                    out string displayContextError))
                {
                    error = displayContextError;
                    failureKind = InertiaPreviewFailureKind.DisplayUnavailable;
                    return false;
                }
                displayContext = createdDisplayContext;

                Matrix<double> linkTransform = MathOps.GetTransformation(
                    displayContext.LinkToDisplayTarget);
                Matrix<double> inertialTransform = MathOps.GetTransformation(
                    link.Inertial.Origin.GetXYZ(),
                    link.Inertial.Origin.GetRPY());
                Matrix<double> principalTransform = BuildPrincipalFrameTransform(
                    ellipsoid.PrincipalAxes);
                Matrix<double> bodyToDisplayTarget = linkTransform *
                    inertialTransform * principalTransform;
                Modeler modeler = null;
                MathUtility mathUtility = null;
                MathTransform bodyTransform = null;
                Body2 body = null;
                try
                {
                    modeler = swApp.GetModeler() as Modeler;
                    mathUtility = swApp.GetMathUtility() as MathUtility;
                    if (modeler == null || mathUtility == null)
                    {
                        throw new InvalidOperationException(ChineseUiText.Translate(
                            "SolidWorks temporary-body services are unavailable.",
                            "SolidWorks 临时实体服务不可用。"));
                    }
                    bodyTransform = mathUtility.CreateTransform(
                        TemporaryBodyDisplayContext.ToSolidWorksTransformData(
                            bodyToDisplayTarget)) as MathTransform;
                    if (bodyTransform == null)
                    {
                        throw new InvalidOperationException(ChineseUiText.Translate(
                            "SolidWorks could not create the inertia preview transform.",
                            "SolidWorks 无法创建惯性预览变换。"));
                    }
                    body = CreateEquivalentBoxBody(
                        modeler,
                        ellipsoid.EquivalentBoxDimensions);
                    Body2 ownedBody = body;
                    body = null;
                    AddBody(ownedBody, bodyTransform, displayContext.DisplayTarget,
                        DrawingColor.DeepSkyBlue);
                    AddPrincipalAxes(
                        modeler,
                        ellipsoid.EquivalentBoxDimensions,
                        bodyTransform,
                        displayContext.DisplayTarget);
                }
                finally
                {
                    ReleaseBody(body);
                    ReleaseComReference(bodyTransform);
                    ReleaseComReference(mathUtility);
                    ReleaseComReference(modeler);
                }
                model.GraphicsRedraw2();
                if (temporaryBodies.Count == ExpectedBodyCount)
                {
                    return true;
                }

                int displayedBodyCount = temporaryBodies.Count;
                Hide();
                failureKind = InertiaPreviewFailureKind.DisplayUnavailable;
                error = ChineseUiText.Translate(
                    "SolidWorks did not display all inertia preview geometry.",
                    "SolidWorks 未能显示全部惯性预览几何。") + " (" +
                    displayedBodyCount + "/" + ExpectedBodyCount + ")";
                return false;
            }
            catch (Exception e)
            {
                Hide();
                error = e.Message;
                failureKind = InertiaPreviewFailureKind.DisplayUnavailable;
                return false;
            }
        }

        public void Hide()
        {
            ModelDoc2 hideTarget = displayContext == null
                ? model
                : displayContext.HideTarget;
            foreach (Body2 body in temporaryBodies)
            {
                try
                {
                    body.Hide(hideTarget);
                }
                catch
                {
                    // SolidWorks can already have discarded a temporary body after a rebuild.
                }
                finally
                {
                    if (Marshal.IsComObject(body))
                    {
                        Marshal.FinalReleaseComObject(body);
                    }
                }
            }
            temporaryBodies.Clear();
            if (displayContext != null)
            {
                displayContext.Dispose();
                displayContext = null;
            }
            if (model != null)
            {
                model.GraphicsRedraw2();
            }
        }

        public void Dispose()
        {
            Hide();
        }

        internal static double[] BuildEquivalentBoxBodyDimensions(double[] dimensions)
        {
            if (dimensions == null || dimensions.Length != 3 ||
                !IsFinitePositive(dimensions[0]) ||
                !IsFinitePositive(dimensions[1]) ||
                !IsFinitePositive(dimensions[2]))
            {
                throw new ArgumentException(
                    "Equivalent inertia cuboid dimensions must contain three positive values.",
                    nameof(dimensions));
            }
            return new[]
            {
                0.0, 0.0, -dimensions[2] / 2.0,
                0.0, 0.0, 1.0,
                dimensions[0], dimensions[1], dimensions[2]
            };
        }

        internal static Matrix<double> BuildRightHandedPrincipalAxes(
            Matrix<double> principalAxes)
        {
            if (principalAxes == null || principalAxes.RowCount != 3 ||
                principalAxes.ColumnCount != 3)
            {
                throw new ArgumentException("Principal axes must be a 3x3 matrix.",
                    nameof(principalAxes));
            }

            double[] x = principalAxes.Column(0).ToArray();
            double[] y = principalAxes.Column(1).ToArray();
            Normalize(x);
            double projection = Dot(x, y);
            for (int i = 0; i < 3; i++)
            {
                y[i] -= projection * x[i];
            }
            Normalize(y);
            double[] z = Cross(x, y);
            Normalize(z);

            Matrix<double> result = Matrix<double>.Build.Dense(3, 3);
            for (int row = 0; row < 3; row++)
            {
                result[row, 0] = x[row];
                result[row, 1] = y[row];
                result[row, 2] = z[row];
            }
            return result;
        }

        internal static Matrix<double> BuildPrincipalFrameTransform(
            Matrix<double> principalAxes)
        {
            Matrix<double> rotation = BuildRightHandedPrincipalAxes(principalAxes);
            Matrix<double> transform = Matrix<double>.Build.DenseIdentity(4);
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    transform[row, column] = rotation[row, column];
                }
            }
            return transform;
        }

        internal static double[][] BuildPrincipalAxisLineDimensions(double[] dimensions)
        {
            if (dimensions == null || dimensions.Length != 3 ||
                !IsFinitePositive(dimensions[0]) ||
                !IsFinitePositive(dimensions[1]) ||
                !IsFinitePositive(dimensions[2]))
            {
                throw new ArgumentException(
                    "Principal-axis dimensions must contain three positive values.",
                    nameof(dimensions));
            }

            double x = dimensions[0] * PrincipalAxisExtensionFactor / 2.0;
            double y = dimensions[1] * PrincipalAxisExtensionFactor / 2.0;
            double z = dimensions[2] * PrincipalAxisExtensionFactor / 2.0;
            return new[]
            {
                new[] { -x, 0.0, 0.0, x, 0.0, 0.0 },
                new[] { 0.0, -y, 0.0, 0.0, y, 0.0 },
                new[] { 0.0, 0.0, -z, 0.0, 0.0, z }
            };
        }

        private static Body2 CreateEquivalentBoxBody(
            Modeler modeler,
            double[] equivalentBoxDimensions)
        {
            double[] bodyDimensions = BuildEquivalentBoxBodyDimensions(
                equivalentBoxDimensions);
            try
            {
                return modeler.CreateBodyFromBox3(bodyDimensions);
            }
            catch (COMException exception) when (
                exception.ErrorCode == unchecked((int)0x8002000D))
            {
                // SolidWorks 2023 can reject the object SAFEARRAY as locked.
                Body2 body = modeler.ICreateBodyFromBox2(ref bodyDimensions[0]);
                if (body != null)
                {
                    return body;
                }
                return modeler.CreateBodyFromBox(bodyDimensions) as Body2;
            }
        }

        private void AddPrincipalAxes(
            Modeler modeler,
            double[] dimensions,
            MathTransform transform,
            object displayTarget)
        {
            double[][] axes = BuildPrincipalAxisLineDimensions(dimensions);
            DrawingColor[] colors =
            {
                DrawingColor.Red,
                DrawingColor.LimeGreen,
                DrawingColor.Blue
            };
            for (int index = 0; index < axes.Length; index++)
            {
                AddPrincipalAxis(modeler, axes[index], transform, displayTarget, colors[index]);
            }
        }

        private void AddPrincipalAxis(
            Modeler modeler,
            double[] dimensions,
            MathTransform transform,
            object displayTarget,
            DrawingColor color)
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
                        "SolidWorks could not create an inertia principal-axis line.",
                        "SolidWorks 无法创建惯性主轴线。"));
                }
                trimmedCurve = sourceCurve.CreateTrimmedCurve2(
                    dimensions[0], dimensions[1], dimensions[2],
                    dimensions[3], dimensions[4], dimensions[5]);
                if (trimmedCurve == null)
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks could not trim an inertia principal-axis line.",
                        "SolidWorks 无法裁剪惯性主轴线。"));
                }
                body = modeler.CreateWireBody(
                    trimmedCurve,
                    (int)swCreateWireBodyOptions_e.swCreateWireBodyByDefault);
                Body2 ownedBody = body;
                body = null;
                AddBody(ownedBody, transform, displayTarget, color);
            }
            finally
            {
                ReleaseBody(body);
                ReleaseComReference(trimmedCurve);
                ReleaseComReference(sourceCurve);
            }
        }

        private void AddBody(
            Body2 body,
            MathTransform transform,
            object displayTarget,
            DrawingColor color)
        {
            if (body == null)
            {
                throw new InvalidOperationException(ChineseUiText.Translate(
                    "SolidWorks could not create the inertia preview geometry.",
                    "SolidWorks 无法创建惯性预览几何。"));
            }

            try
            {
                if (!body.ApplyTransform(transform))
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks could not transform the inertia preview geometry.",
                        "SolidWorks 无法变换惯性预览几何。"));
                }
                int result = body.Display3(
                    displayTarget,
                    ColorTranslator.ToOle(color),
                    (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);
                if (!IsDisplaySuccess(result))
                {
                    throw new InvalidOperationException(ChineseUiText.Translate(
                        "SolidWorks could not display the inertia preview geometry. Display3 error code: ",
                        "SolidWorks 无法显示惯性预览几何。Display3 错误码：") +
                        result + ".");
                }
                temporaryBodies.Add(body);
                body = null;
            }
            finally
            {
                if (body != null && displayContext != null)
                {
                    try { body.Hide(displayContext.HideTarget); }
                    catch { }
                }
                ReleaseBody(body);
            }
        }

        internal static bool IsDisplaySuccess(int result)
        {
            // SolidWorks API: IBody2.Display3 returns 0 on success.
            return result == 0;
        }

        private static void ReleaseBody(object value)
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

        private static void Normalize(double[] vector)
        {
            double length = Math.Sqrt(
                vector[0] * vector[0] +
                vector[1] * vector[1] +
                vector[2] * vector[2]);
            if (length <= 0.0)
            {
                throw new InvalidOperationException("An inertia principal axis has zero length.");
            }
            vector[0] /= length;
            vector[1] /= length;
            vector[2] /= length;
        }

        private static double Dot(double[] left, double[] right)
        {
            return left[0] * right[0] + left[1] * right[1] + left[2] * right[2];
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

        private static bool IsFinitePositive(double value)
        {
            return value > 0.0 && !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

    }
}
