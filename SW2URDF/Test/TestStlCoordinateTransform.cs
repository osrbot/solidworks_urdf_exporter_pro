using MathNet.Numerics.LinearAlgebra;
using SW2URDF.URDFExport;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SW2URDF.Test
{
    public class TestStlCoordinateTransform
    {
        [Fact]
        public void BinaryStlVerticesAreTransformedIntoLinkFrameAtomically()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-stl-transform-" + Guid.NewGuid().ToString("N") + ".stl");
            try
            {
                WriteSingleTriangle(
                    path,
                    new[] { 10.0, 20.0, 30.0 },
                    new[] { 11.0, 20.0, 30.0 },
                    new[] { 10.0, 21.0, 30.0 },
                    73);
                Matrix<double> rootToLink = Matrix<double>.Build.DenseIdentity(4);
                rootToLink[0, 3] = -10.0;
                rootToLink[1, 3] = -20.0;
                rootToLink[2, 3] = -30.0;

                Assert.True(ExportHelper.TransformBinaryStl(path, rootToLink));

                using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
                {
                    Assert.All(reader.ReadBytes(80), value => Assert.Equal(0, value));
                    Assert.Equal((uint)1, reader.ReadUInt32());
                    AssertVector(reader, new[] { 0.0, 0.0, 1.0 });
                    AssertVector(reader, new[] { 0.0, 0.0, 0.0 });
                    AssertVector(reader, new[] { 1.0, 0.0, 0.0 });
                    AssertVector(reader, new[] { 0.0, 1.0, 0.0 });
                    Assert.Equal((ushort)73, reader.ReadUInt16());
                    Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
                }
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void FailedStlTransformLeavesOriginalFileUntouched()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-invalid-stl-" + Guid.NewGuid().ToString("N") + ".stl");
            try
            {
                byte[] original = new byte[] { 1, 2, 3, 4, 5 };
                File.WriteAllBytes(path, original);

                Assert.False(ExportHelper.TransformBinaryStl(
                    path,
                    Matrix<double>.Build.DenseIdentity(4)));
                Assert.True(original.SequenceEqual(File.ReadAllBytes(path)));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static void WriteSingleTriangle(
            string path,
            double[] p0,
            double[] p1,
            double[] p2,
            ushort attribute)
        {
            using (BinaryWriter writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(Enumerable.Repeat((byte)0x7f, 80).ToArray());
                writer.Write((uint)1);
                WriteVector(writer, new[] { 0.0, 0.0, 1.0 });
                WriteVector(writer, p0);
                WriteVector(writer, p1);
                WriteVector(writer, p2);
                writer.Write(attribute);
            }
        }

        private static void WriteVector(BinaryWriter writer, double[] value)
        {
            writer.Write((float)value[0]);
            writer.Write((float)value[1]);
            writer.Write((float)value[2]);
        }

        private static void AssertVector(BinaryReader reader, double[] expected)
        {
            Assert.Equal(expected[0], reader.ReadSingle(), 6);
            Assert.Equal(expected[1], reader.ReadSingle(), 6);
            Assert.Equal(expected[2], reader.ReadSingle(), 6);
        }
    }
}
