using System;
using OSURDF.Core.Model;

namespace OSURDF.Core.Validation
{
    internal static class InertiaTensorMath
    {
        public static bool TryGetPrincipalMoments(
            InertiaTensorDocument tensor,
            out double[] principalMoments)
        {
            principalMoments = null;
            if (tensor == null ||
                !IsFinite(tensor.Ixx) || !IsFinite(tensor.Ixy) || !IsFinite(tensor.Ixz) ||
                !IsFinite(tensor.Iyy) || !IsFinite(tensor.Iyz) || !IsFinite(tensor.Izz))
            {
                return false;
            }

            double offDiagonalSquared =
                tensor.Ixy * tensor.Ixy +
                tensor.Ixz * tensor.Ixz +
                tensor.Iyz * tensor.Iyz;
            if (offDiagonalSquared == 0.0)
            {
                principalMoments = new[] { tensor.Ixx, tensor.Iyy, tensor.Izz };
                Array.Sort(principalMoments);
                return true;
            }

            // Closed-form eigenvalues for a real symmetric 3x3 matrix. The clamp protects
            // acos from round-off when the determinant is infinitesimally outside [-2, 2].
            double mean = (tensor.Ixx + tensor.Iyy + tensor.Izz) / 3.0;
            double centeredSquared =
                (tensor.Ixx - mean) * (tensor.Ixx - mean) +
                (tensor.Iyy - mean) * (tensor.Iyy - mean) +
                (tensor.Izz - mean) * (tensor.Izz - mean) +
                2.0 * offDiagonalSquared;
            double scale = Math.Sqrt(centeredSquared / 6.0);
            if (scale == 0.0)
            {
                principalMoments = new[] { mean, mean, mean };
                return true;
            }

            double bxx = (tensor.Ixx - mean) / scale;
            double byy = (tensor.Iyy - mean) / scale;
            double bzz = (tensor.Izz - mean) / scale;
            double bxy = tensor.Ixy / scale;
            double bxz = tensor.Ixz / scale;
            double byz = tensor.Iyz / scale;
            double determinant =
                bxx * byy * bzz + 2.0 * bxy * bxz * byz -
                bxx * byz * byz - byy * bxz * bxz - bzz * bxy * bxy;
            double halfDeterminant = determinant / 2.0;
            if (halfDeterminant < -1.0) halfDeterminant = -1.0;
            if (halfDeterminant > 1.0) halfDeterminant = 1.0;

            double angle = Math.Acos(halfDeterminant) / 3.0;
            double largest = mean + 2.0 * scale * Math.Cos(angle);
            double smallest = mean + 2.0 * scale * Math.Cos(angle + 2.0 * Math.PI / 3.0);
            double middle = 3.0 * mean - largest - smallest;
            principalMoments = new[] { smallest, middle, largest };
            Array.Sort(principalMoments);
            return IsFinite(principalMoments[0]) &&
                IsFinite(principalMoments[1]) &&
                IsFinite(principalMoments[2]);
        }

        public static bool SatisfiesTriangleInequality(double[] principalMoments)
        {
            if (principalMoments == null || principalMoments.Length != 3 ||
                !IsFinite(principalMoments[0]) ||
                !IsFinite(principalMoments[1]) ||
                !IsFinite(principalMoments[2]))
            {
                return false;
            }
            double largestMagnitude = Math.Max(
                Math.Abs(principalMoments[0]),
                Math.Max(Math.Abs(principalMoments[1]), Math.Abs(principalMoments[2])));
            if (largestMagnitude == 0.0)
            {
                return true;
            }

            // Compare normalized moments so the tolerance is relative to the tensor's own
            // scale. A fixed kg*m^2 epsilon can hide invalid inertia on very small robots and
            // overflow when auditing unusually large synthetic inputs.
            const double relativeTolerance = 1e-9;
            double first = principalMoments[0] / largestMagnitude;
            double second = principalMoments[1] / largestMagnitude;
            double third = principalMoments[2] / largestMagnitude;
            return first + second >= third - relativeTolerance &&
                first + third >= second - relativeTolerance &&
                second + third >= first - relativeTolerance;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
