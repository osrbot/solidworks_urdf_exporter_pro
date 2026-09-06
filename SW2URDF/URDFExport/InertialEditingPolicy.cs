using System;
using System.Linq;
using SW2URDF.URDF;
using MathNet.Numerics.LinearAlgebra;
using SW2URDF.Utilities;

namespace SW2URDF.URDFExport
{
    internal static class InertialEditingPolicy
    {
        internal static Inertial Copy(Inertial value)
        {
            var copy = new Inertial();
            copy.SetElement(value);
            return copy;
        }

        internal static InertialEditingState EnsureSource(Link link)
        {
            if (link.InertialEditing == null || link.InertialEditing.Source == null)
                link.InertialEditing = new InertialEditingState
                {
                    Source = Copy(link.Inertial), MassEdited = true, OriginEdited = true,
                    TensorEdited = true, CalibrationDisabled = true, LegacyValuesPreserved = true
                };
            return link.InertialEditing;
        }

        internal static bool CanCalibrate(Link link)
        {
            var state = EnsureSource(link);
            return !state.TensorEdited && !state.SourceHasInertiaOverride &&
                IsPositiveFinite(state.Source.Mass.Value);
        }

        internal static bool IsPositiveFinite(double value)
        {
            return value > 0 && !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        // Always resolve from the stable source, never multiply an already calibrated tensor.
        internal static Inertial Resolve(InertialEditingState state, Inertial edits, Inertial source)
        {
            var result = Copy(source);
            if (state.MassEdited) result.Mass.Value = edits.Mass.Value;
            if (state.OriginEdited) result.Origin.SetElement(edits.Origin);
            if (state.TensorEdited) result.Inertia.SetElement(edits.Inertia);
            else if (state.MassEdited && !state.CalibrationDisabled &&
                !state.SourceHasInertiaOverride && IsPositiveFinite(source.Mass.Value) &&
                IsPositiveFinite(result.Mass.Value))
            {
                double factor = result.Mass.Value / source.Mass.Value;
                result.Inertia.SetUrdfMomentMatrix(source.Inertia.GetMoment().Select(x => x * factor).ToArray());
            }
            else if (state.MassEdited && !IsPositiveFinite(result.Mass.Value))
            {
                // Preserve the currently displayed tensor while a mass edit is incomplete.
                // Otherwise correcting the mass would misclassify that display as a manual tensor edit.
                result.Inertia.SetElement(edits.Inertia);
            }
            return result;
        }

        internal static void ApplyEdits(Link link, Inertial edited)
        {
            var state = EnsureSource(link);
            var previous = link.Inertial;
            if (!Same(previous.Mass.Value, edited.Mass.Value))
                state.MassEdited = !Same(state.Source.Mass.Value, edited.Mass.Value);
            if (!Same(previous.Origin.GetXYZ(), edited.Origin.GetXYZ()) ||
                !Same(previous.Origin.GetRPY(), edited.Origin.GetRPY()))
                state.OriginEdited = true;
            if (!Same(previous.Inertia.GetMoment(), edited.Inertia.GetMoment()))
                state.TensorEdited = true;
            link.Inertial.SetElement(Resolve(state, edited, state.Source));
        }

        internal static void ApplySource(Link link, Inertial source, bool explicitInertia)
        {
            var state = link.InertialEditing;
            if (state != null && state.FrameChangePending)
                throw new InvalidOperationException("Resolve the pending Link frame change before applying a new inertial source.");
            if (state == null || state.Source == null)
                state = HasExistingMass(link) ? EnsureSource(link) : new InertialEditingState();
            state.Source = Copy(source);
            state.SourceIsSolidWorks = true;
            state.SourceHasInertiaOverride = explicitInertia;
            link.InertialEditing = state;
            link.Inertial.SetElement(Resolve(state, link.Inertial, source));
        }

        private static bool HasExistingMass(Link link)
        {
            try { return IsPositiveFinite(link.Inertial.Mass.Value); }
            catch (NullReferenceException) { return false; }
        }

        internal static void SetCalibration(Link link, bool enabled)
        {
            var state = EnsureSource(link);
            state.CalibrationDisabled = !enabled;
            link.Inertial.SetElement(Resolve(state, link.Inertial, state.Source));
        }

        internal static void ApplyExplicitValues(Link link, Inertial values)
        {
            var state = EnsureSource(link);
            state.MassEdited = state.OriginEdited = state.TensorEdited = true;
            link.Inertial.SetElement(values);
        }

        internal static void ReexpressEdits(Link link, Matrix<double> oldFrame, Matrix<double> newFrame)
        {
            var state = link.InertialEditing;
            if (state == null || (!state.OriginEdited && !state.TensorEdited)) return;
            Matrix<double> delta = newFrame.Inverse() * oldFrame;
            Matrix<double> oldOrigin = MathOps.GetTransformation(link.Inertial.Origin.GetXYZ(),
                link.Inertial.Origin.GetRPY());
            if (state.OriginEdited)
            {
                Matrix<double> origin = delta * oldOrigin;
                link.Inertial.Origin.SetXYZ(MathOps.GetXYZ(origin));
                if (!state.TensorEdited)
                {
                    // The derived tensor also rotates with the fresh SW Link frame.
                    // Express the user's orientation adjustment in that same new basis.
                    var rotation = delta.SubMatrix(0, 3, 0, 3);
                    origin.SetSubMatrix(0, 0, rotation * oldOrigin.SubMatrix(0, 3, 0, 3) * rotation.Transpose());
                }
                link.Inertial.Origin.SetRPY(MathOps.GetRPY(origin));
            }
            else if (state.TensorEdited)
            {
                // The fresh SW COM frame has zero RPY; rotate the manual tensor into it.
                var rotation = (delta * oldOrigin).SubMatrix(0, 3, 0, 3);
                var tensor = Matrix<double>.Build.DenseOfRowMajor(3, 3, link.Inertial.Inertia.GetMoment());
                link.Inertial.Inertia.SetUrdfMomentMatrix((rotation * tensor * rotation.Transpose()).ToRowMajorArray());
            }
        }

        // Draft events retain the frame owning the values, even across multiple selections/saves.
        internal static void QueueFrameChange(Link link, CadFeatureReference frameReference)
        {
            if (link == null) throw new ArgumentNullException("link");
            if (frameReference == null || !frameReference.IsValidFor(ReferenceGeometryKind.CoordinateSystem, false))
                throw new ArgumentException("A Link coordinate-system reference must be selected.", "frameReference");
            if (frameReference.Equals(link.FrameReference)) return;

            var state = link.InertialEditing;
            if ((state != null && state.Source != null) || HasExistingMass(link))
            {
                state = EnsureSource(link);
                if (!state.FrameChangePending)
                    state.InertialFrameReference = link.FrameReference == null ? null : link.FrameReference.Clone();
                state.FrameChangePending = !frameReference.Equals(state.InertialFrameReference);
                if (!state.FrameChangePending) state.InertialFrameReference = null;
            }
            link.FrameReference = frameReference.Clone();
        }

        internal static void ResolvePendingFrameChange(Link link,
            Func<CadFeatureReference, Matrix<double>> resolveFrame)
        {
            var state = link.InertialEditing;
            if (state == null || !state.FrameChangePending) return;

            Matrix<double> oldFrame = state.InertialFrameReference == null
                ? null : resolveFrame(state.InertialFrameReference);
            if (oldFrame == null)
                throw new InvalidOperationException("The previous Link frame could not be resolved; inertia edits cannot be transformed safely.");
            Matrix<double> newFrame = resolveFrame(link.FrameReference);
            if (newFrame == null)
                throw new InvalidOperationException("The selected Link frame could not be resolved; inertia edits cannot be transformed safely.");

            // Work on detached values: resolution or matrix failures must leave the draft retryable.
            var candidate = new Link { InertialEditing = state.Clone() };
            candidate.Inertial.SetElement(link.Inertial);
            ReexpressEdits(candidate, oldFrame, newFrame);
            var source = candidate.InertialEditing.Source;
            var sourcePose = newFrame.Inverse() * oldFrame *
                MathOps.GetTransformation(source.Origin.GetXYZ(), source.Origin.GetRPY());
            var rotation = sourcePose.SubMatrix(0, 3, 0, 3);
            var tensor = Matrix<double>.Build.DenseOfRowMajor(3, 3, source.Inertia.GetMoment());
            source.Origin.SetXYZ(MathOps.GetXYZ(sourcePose));
            source.Origin.SetRPY(new double[3]);
            source.Inertia.SetUrdfMomentMatrix((rotation * tensor * rotation.Transpose()).ToRowMajorArray());
            candidate.Inertial.SetElement(Resolve(candidate.InertialEditing, candidate.Inertial, source));
            candidate.InertialEditing.FrameChangePending = false;
            candidate.InertialEditing.InertialFrameReference = null;
            link.Inertial.SetElement(candidate.Inertial);
            link.InertialEditing = candidate.InertialEditing;
        }

        internal static void Reset(Link link)
        {
            var state = EnsureSource(link);
            state.MassEdited = state.OriginEdited = state.TensorEdited = false;
            state.CalibrationDisabled = false;
            link.Inertial.SetElement(state.Source);
        }

        internal static bool Same(double left, double right)
        {
            return left.Equals(right) || Math.Abs(left - right) <=
                1e-14 * Math.Max(Math.Abs(left), Math.Abs(right));
        }

        internal static bool Same(double[] left, double[] right)
        {
            return left.Length == right.Length && left.Zip(right, Same).All(x => x);
        }
    }
}
