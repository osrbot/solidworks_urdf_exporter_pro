using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using SW2URDF.URDF;
using System;

namespace SW2URDF.URDFExport
{
    internal sealed class InertiaEllipsoid
    {
        public double[] PrincipalMoments { get; private set; }

        public Matrix<double> PrincipalAxes { get; private set; }

        public double[] SemiAxes { get; private set; }

        public double[] EquivalentBoxDimensions { get; private set; }

        public static bool TryCreate(
            double mass,
            Inertia inertia,
            out InertiaEllipsoid ellipsoid,
            out string error)
        {
            return TryCreate(mass, inertia.GetMoment(), out ellipsoid, out error);
        }

        internal static bool TryCreate(
            double mass,
            double[] moment,
            out InertiaEllipsoid ellipsoid,
            out string error)
        {
            ellipsoid = null;
            error = "";
            if (!IsFinitePositive(mass))
            {
                error = "Mass must be greater than zero.";
                return false;
            }
            if (moment == null || moment.Length != 9)
            {
                error = "The inertia tensor must contain nine matrix values.";
                return false;
            }
            foreach (double value in moment)
            {
                if (Double.IsNaN(value) || Double.IsInfinity(value))
                {
                    error = "The inertia tensor contains a non-finite value.";
                    return false;
                }
            }

            Matrix<double> tensor = DenseMatrix.OfArray(new[,]
            {
                { moment[0], moment[1], moment[2] },
                { moment[3], moment[4], moment[5] },
                { moment[6], moment[7], moment[8] }
            });
            double symmetryTolerance = Math.Max(1.0, tensor.L2Norm()) * 1e-10;
            if (Math.Abs(tensor[0, 1] - tensor[1, 0]) > symmetryTolerance ||
                Math.Abs(tensor[0, 2] - tensor[2, 0]) > symmetryTolerance ||
                Math.Abs(tensor[1, 2] - tensor[2, 1]) > symmetryTolerance)
            {
                error = "The inertia tensor is not symmetric.";
                return false;
            }

            var decomposition = tensor.Evd(Symmetricity.Symmetric);
            double[] principalMoments = new double[3];
            for (int i = 0; i < principalMoments.Length; i++)
            {
                if (Math.Abs(decomposition.EigenValues[i].Imaginary) > symmetryTolerance)
                {
                    error = "The inertia tensor produced complex principal moments.";
                    return false;
                }
                principalMoments[i] = decomposition.EigenValues[i].Real;
                if (!IsFinitePositive(principalMoments[i]))
                {
                    error = "All principal moments must be greater than zero.";
                    return false;
                }
            }

            double radiusScale = 5.0 / (2.0 * mass);
            double[] squaredSemiAxes =
            {
                radiusScale * (principalMoments[1] + principalMoments[2] - principalMoments[0]),
                radiusScale * (principalMoments[0] + principalMoments[2] - principalMoments[1]),
                radiusScale * (principalMoments[0] + principalMoments[1] - principalMoments[2])
            };
            double physicalTolerance = Math.Max(principalMoments[0],
                Math.Max(principalMoments[1], principalMoments[2])) * radiusScale * 1e-9;
            double[] semiAxes = new double[3];
            double[] equivalentBoxDimensions = new double[3];
            double equivalentBoxScale = Math.Sqrt(12.0 / 5.0);
            for (int i = 0; i < squaredSemiAxes.Length; i++)
            {
                if (squaredSemiAxes[i] <= physicalTolerance)
                {
                    error = "The principal moments violate the rigid-body triangle inequality.";
                    return false;
                }
                semiAxes[i] = Math.Sqrt(squaredSemiAxes[i]);
                equivalentBoxDimensions[i] = semiAxes[i] * equivalentBoxScale;
            }

            ellipsoid = new InertiaEllipsoid
            {
                PrincipalMoments = principalMoments,
                PrincipalAxes = decomposition.EigenVectors,
                SemiAxes = semiAxes,
                EquivalentBoxDimensions = equivalentBoxDimensions
            };
            return true;
        }

        private static bool IsFinitePositive(double value)
        {
            return value > 0.0 && !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
    }
}
