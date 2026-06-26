using SW2URDF.URDF;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public class TestInertiaConvention
    {
        [Fact]
        public void TestSolidWorksMomentProductsAreConvertedToUrdfConvention()
        {
            double[] solidWorksMoment =
            {
                1.0, 0.2, -0.3,
                0.2, 2.0, 0.4,
                -0.3, 0.4, 3.0
            };

            double[] urdfMoment =
                ExportHelper.ConvertSolidWorksMomentToUrdfConvention(solidWorksMoment);

            Assert.Equal(1.0, urdfMoment[0], 10);
            Assert.Equal(-0.2, urdfMoment[1], 10);
            Assert.Equal(0.3, urdfMoment[2], 10);
            Assert.Equal(2.0, urdfMoment[3], 10);
            Assert.Equal(-0.4, urdfMoment[4], 10);
            Assert.Equal(3.0, urdfMoment[5], 10);
        }

        [Fact]
        public void TestInertiaSetMomentMatrixMatchesValidationConvention()
        {
            double[] solidWorksMoment =
            {
                0.011, -0.002, 0.003,
                -0.002, 0.022, -0.004,
                0.003, -0.004, 0.033
            };

            double[] expected =
                ExportHelper.ConvertSolidWorksMomentToUrdfConvention(solidWorksMoment);
            Inertia inertia = new Inertia();

            inertia.SetMomentMatrix(solidWorksMoment);

            Assert.Equal(expected[0], inertia.Ixx, 10);
            Assert.Equal(expected[1], inertia.Ixy, 10);
            Assert.Equal(expected[2], inertia.Ixz, 10);
            Assert.Equal(expected[3], inertia.Iyy, 10);
            Assert.Equal(expected[4], inertia.Iyz, 10);
            Assert.Equal(expected[5], inertia.Izz, 10);
        }

        [Fact]
        public void TestInertiaValidationRejectsWrongOffDiagonalSign()
        {
            ExportHelper.InertialValidationRow row =
                new ExportHelper.InertialValidationRow(
                    "ixy",
                    "kg*m^2",
                    2.0e-4,
                    -2.0e-4);

            Assert.False(row.Passed);
        }

        [Fact]
        public void TestInertialValidationCsvEscapesFieldsAndKeepsErrors()
        {
            ExportHelper.InertialValidationRow row =
                new ExportHelper.InertialValidationRow(
                    "mass",
                    "kg",
                    1.25,
                    1.5);
            ExportHelper.InertialValidationRecord record =
                new ExportHelper.InertialValidationRecord(
                    "base,link",
                    "Origin \"global\"",
                    row);

            string csv = ExportHelper.BuildInertialValidationCsv(new[] { record });

            Assert.Contains(
                "link,coordinate_system,quantity,unit,solidworks_expected,urdf_value,absolute_error,relative_error_percent,status",
                csv);
            Assert.Contains(
                "\"base,link\",\"Origin \"\"global\"\"\",mass,kg,1.25,1.5,0.25,20,FAIL",
                csv);
        }
    }
}
