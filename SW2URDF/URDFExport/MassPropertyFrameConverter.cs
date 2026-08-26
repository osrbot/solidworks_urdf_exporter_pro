using MathNet.Numerics.LinearAlgebra;
using System;

namespace SW2URDF.URDFExport
{
    internal sealed class MassPropertySnapshot
    {
        public MassPropertySnapshot(double mass, double[] centerOfMass, double[] moment)
        {
            if (centerOfMass == null || centerOfMass.Length != 3)
            {
                throw new ArgumentException(
                    "A center of mass must contain exactly three values.",
                    "centerOfMass");
            }
            if (moment == null || moment.Length != 9)
            {
                throw new ArgumentException(
                    "An inertia tensor must contain exactly nine values.",
                    "moment");
            }

            Mass = mass;
            CenterOfMass = (double[])centerOfMass.Clone();
            Moment = (double[])moment.Clone();
        }

        public double Mass { get; private set; }
        public double[] CenterOfMass { get; private set; }
        public double[] Moment { get; private set; }
    }

    internal static class MassPropertyFrameConverter
    {
        public static MassPropertySnapshot Convert(
            MassPropertySnapshot source,
            Matrix<double> sourceFrameToDocument,
            Matrix<double> targetFrameToDocument)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            ValidateTransform(sourceFrameToDocument, "sourceFrameToDocument");
            ValidateTransform(targetFrameToDocument, "targetFrameToDocument");

            Matrix<double> sourceToTarget =
                targetFrameToDocument.Inverse() * sourceFrameToDocument;
            double[] centerOfMass = TransformPoint(
                sourceToTarget,
                source.CenterOfMass);
            double[] moment = RotateTensor(sourceToTarget, source.Moment);
            return new MassPropertySnapshot(source.Mass, centerOfMass, moment);
        }

        private static double[] TransformPoint(Matrix<double> transform, double[] point)
        {
            return new[]
            {
                transform[0, 0] * point[0] + transform[0, 1] * point[1] +
                    transform[0, 2] * point[2] + transform[0, 3],
                transform[1, 0] * point[0] + transform[1, 1] * point[1] +
                    transform[1, 2] * point[2] + transform[1, 3],
                transform[2, 0] * point[0] + transform[2, 1] * point[1] +
                    transform[2, 2] * point[2] + transform[2, 3]
            };
        }

        private static double[] RotateTensor(
            Matrix<double> sourceToTarget,
            double[] sourceMoment)
        {
            double[,] rotated = new double[3, 3];
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    double value = 0.0;
                    for (int left = 0; left < 3; left++)
                    {
                        for (int right = 0; right < 3; right++)
                        {
                            value += sourceToTarget[row, left] *
                                sourceMoment[left * 3 + right] *
                                sourceToTarget[column, right];
                        }
                    }
                    rotated[row, column] = value;
                }
            }

            // Remove insignificant asymmetry introduced by floating-point multiplication.
            for (int row = 0; row < 3; row++)
            {
                for (int column = row + 1; column < 3; column++)
                {
                    double average = (rotated[row, column] + rotated[column, row]) / 2.0;
                    rotated[row, column] = average;
                    rotated[column, row] = average;
                }
            }

            return new[]
            {
                rotated[0, 0], rotated[0, 1], rotated[0, 2],
                rotated[1, 0], rotated[1, 1], rotated[1, 2],
                rotated[2, 0], rotated[2, 1], rotated[2, 2]
            };
        }

        private static void ValidateTransform(Matrix<double> transform, string parameterName)
        {
            if (transform == null || transform.RowCount != 4 || transform.ColumnCount != 4)
            {
                throw new ArgumentException(
                    "A frame transform must be a 4x4 matrix.",
                    parameterName);
            }
        }
    }
}
