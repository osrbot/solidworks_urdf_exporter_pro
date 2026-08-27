using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
        private const double PrincipalAxisLengthScale = 1.15;
        private const int InertiaCurveCount = 6;
        private const int ComMarkerCurveCount = 3;
        internal const int ExpectedCurveCount = InertiaCurveCount + ComMarkerCurveCount;

        private readonly SldWorks swApp;
        private readonly ModelDoc2 model;
        private readonly List<Body2> temporaryBodies = new List<Body2>();

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
                    link,
                    linkCoordinateTransform,
                    out TemporaryBodyDisplayContext displayContext,
                    out string displayContextError))
                {
                    error = displayContextError;
                    failureKind = InertiaPreviewFailureKind.DisplayUnavailable;
                    return false;
                }

                using (displayContext)
                {
                    Matrix<double> linkTransform = MathOps.GetTransformation(
                        displayContext.LinkToDisplayTarget);
                    Matrix<double> inertialTransform = MathOps.GetTransformation(
                        link.Inertial.Origin.GetXYZ(),
                        link.Inertial.Origin.GetRPY());
                    Matrix<double> displayTransform = linkTransform * inertialTransform;
                    Matrix<double> displayRotation = displayTransform.SubMatrix(0, 3, 0, 3);
                    double[] center =
                    {
                        displayTransform[0, 3],
                        displayTransform[1, 3],
                        displayTransform[2, 3]
                    };

                    double[][] axes = new double[3][];
                    for (int i = 0; i < axes.Length; i++)
                    {
                        axes[i] = (displayRotation * ellipsoid.PrincipalAxes.Column(i)).ToArray();
                        Normalize(axes[i]);
                    }

                    Modeler modeler = null;
                    try
                    {
                        modeler = (Modeler)swApp.GetModeler();
                        AddEllipse(modeler, center, ellipsoid.SemiAxes[0], ellipsoid.SemiAxes[1],
                            axes[0], axes[1], DrawingColor.Red, displayContext.DisplayTarget);
                        AddEllipse(modeler, center, ellipsoid.SemiAxes[0], ellipsoid.SemiAxes[2],
                            axes[0], axes[2], DrawingColor.LimeGreen, displayContext.DisplayTarget);
                        AddEllipse(modeler, center, ellipsoid.SemiAxes[1], ellipsoid.SemiAxes[2],
                            axes[1], axes[2], DrawingColor.DodgerBlue, displayContext.DisplayTarget);
                        AddPrincipalAxis(modeler, center, axes[0], ellipsoid.SemiAxes[0],
                            DrawingColor.Red, displayContext.DisplayTarget);
                        AddPrincipalAxis(modeler, center, axes[1], ellipsoid.SemiAxes[1],
                            DrawingColor.LimeGreen, displayContext.DisplayTarget);
                        AddPrincipalAxis(modeler, center, axes[2], ellipsoid.SemiAxes[2],
                            DrawingColor.DodgerBlue, displayContext.DisplayTarget);
                        AddComMarker(modeler, center, ellipsoid.SemiAxes.Max(),
                            displayContext.DisplayTarget);
                    }
                    finally
                    {
                        ReleaseComObject(modeler);
                    }
                }
                model.GraphicsRedraw2();
                if (temporaryBodies.Count == ExpectedCurveCount)
                {
                    return true;
                }

                int displayedCurveCount = temporaryBodies.Count;
                Hide();
                failureKind = InertiaPreviewFailureKind.DisplayUnavailable;
                error = "SolidWorks displayed only " + displayedCurveCount +
                    " of " + ExpectedCurveCount + " inertia and COM preview curves.";
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
            foreach (Body2 body in temporaryBodies)
            {
                try
                {
                    body.Hide(model);
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
            if (model != null)
            {
                model.GraphicsRedraw2();
            }
        }

        public void Dispose()
        {
            Hide();
        }

        private void AddEllipse(
            Modeler modeler,
            double[] center,
            double majorRadius,
            double minorRadius,
            double[] majorAxis,
            double[] minorAxis,
            DrawingColor color,
            object displayTarget)
        {
            object curve = null;
            try
            {
                curve = modeler.CreateEllipse(
                    center,
                    majorRadius,
                    minorRadius,
                    majorAxis,
                    minorAxis);
                AddWireBody(modeler, curve, color, displayTarget);
            }
            finally
            {
                ReleaseComObject(curve);
            }
        }

        private void AddPrincipalAxis(
            Modeler modeler,
            double[] center,
            double[] direction,
            double semiAxis,
            DrawingColor color,
            object displayTarget)
        {
            double halfLength = semiAxis * PrincipalAxisLengthScale;
            AddLine(modeler, center, direction, halfLength, color, displayTarget);
        }

        private void AddComMarker(
            Modeler modeler,
            double[] center,
            double largestSemiAxis,
            object displayTarget)
        {
            double halfLength = Math.Max(0.0025, largestSemiAxis * 0.12);
            AddLine(modeler, center, new[] { 1.0, 0.0, 0.0 }, halfLength,
                DrawingColor.Gold, displayTarget);
            AddLine(modeler, center, new[] { 0.0, 1.0, 0.0 }, halfLength,
                DrawingColor.Gold, displayTarget);
            AddLine(modeler, center, new[] { 0.0, 0.0, 1.0 }, halfLength,
                DrawingColor.Gold, displayTarget);
        }

        private void AddLine(
            Modeler modeler,
            double[] center,
            double[] direction,
            double halfLength,
            DrawingColor color,
            object displayTarget)
        {
            double[] start =
            {
                center[0] - direction[0] * halfLength,
                center[1] - direction[1] * halfLength,
                center[2] - direction[2] * halfLength
            };
            double[] end =
            {
                center[0] + direction[0] * halfLength,
                center[1] + direction[1] * halfLength,
                center[2] + direction[2] * halfLength
            };

            Curve baseCurve = null;
            Curve trimmedCurve = null;
            try
            {
                baseCurve = modeler.CreateLine(center, direction) as Curve;
                if (baseCurve == null)
                {
                    throw new InvalidOperationException(
                        "SolidWorks could not create an inertia principal axis.");
                }
                trimmedCurve = baseCurve.CreateTrimmedCurve2(
                    start[0], start[1], start[2],
                    end[0], end[1], end[2]);
                if (trimmedCurve == null)
                {
                    throw new InvalidOperationException(
                        "SolidWorks could not trim an inertia principal axis.");
                }
                AddWireBody(modeler, trimmedCurve, color, displayTarget);
            }
            finally
            {
                ReleaseComObject(trimmedCurve);
                ReleaseComObject(baseCurve);
            }
        }

        private void AddWireBody(
            Modeler modeler,
            object curve,
            DrawingColor color,
            object displayTarget)
        {
            Body2 body = modeler.CreateWireBody(
                curve,
                (int)swCreateWireBodyOptions_e.swCreateWireBodyByDefault);
            if (body == null)
            {
                throw new InvalidOperationException("SolidWorks could not create the inertia preview curve.");
            }

            try
            {
                int result = body.Display3(
                    displayTarget,
                    ColorTranslator.ToOle(color),
                    (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);
                if (!IsDisplaySuccess(result))
                {
                    throw new InvalidOperationException(
                        "SolidWorks could not display the inertia preview curve. Display3 error code: " +
                        result + ".");
                }
                temporaryBodies.Add(body);
                body = null;
            }
            finally
            {
                ReleaseComObject(body);
            }
        }

        internal static bool IsDisplaySuccess(int result)
        {
            // SolidWorks API: IBody2.Display3 returns 0 on success.
            return result == 0;
        }

        private static void ReleaseComObject(object value)
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

    }
}
