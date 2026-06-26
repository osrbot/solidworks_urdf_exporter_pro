using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System.Linq;
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
                "link,coordinate_system,quantity,unit,solidworks_expected,urdf_value,absolute_error,relative_error_percent,status,check_type,message",
                csv);
            Assert.Contains(
                "\"base,link\",\"Origin \"\"global\"\"\",mass,kg,1.25,1.5,0.25,20,FAIL,numeric,",
                csv);
        }

        [Fact]
        public void TestPhysicalInertiaDiagnosticsRejectTriangleInequality()
        {
            Link link = new Link();
            link.Name = "thin_bad_link";
            link.Inertial.Mass.Value = 1.0;
            link.Inertial.Inertia.Ixx = 10.0;
            link.Inertial.Inertia.Iyy = 1.0;
            link.Inertial.Inertia.Izz = 1.0;

            var rows = ExportHelper.BuildPhysicalInertiaValidationRows(link);

            Assert.Contains(rows, row =>
                row.Quantity == "inertia.positive_definite" &&
                row.Status == "PASS");
            Assert.Contains(rows, row =>
                row.Quantity == "principal_moments.triangle_inequality" &&
                row.Status == "FAIL");
            Assert.Contains(rows, row =>
                row.Quantity == "ellipsoid.display" &&
                row.CheckType == "display" &&
                row.Status == "WARN" &&
                row.Message.Contains("physical inertia is invalid"));
        }

        [Fact]
        public void TestPhysicalInertiaDiagnosticsWarnForMagnitudeAnomalies()
        {
            Link link = new Link();
            link.Name = "tiny_mass_link";
            link.Inertial.Mass.Value = 1e-12;
            link.Inertial.Inertia.Ixx = 1e-12;
            link.Inertial.Inertia.Iyy = 1e-12;
            link.Inertial.Inertia.Izz = 1e-12;

            var rows = ExportHelper.BuildPhysicalInertiaValidationRows(link);

            ExportHelper.InertialValidationRow massMagnitude =
                rows.First(row => row.Quantity == "mass.magnitude");
            Assert.Equal("magnitude", massMagnitude.CheckType);
            Assert.Equal("WARN", massMagnitude.Status);
            Assert.True(massMagnitude.Passed);
        }

        [Fact]
        public void TestInertialValidationCsvWritesDiagnosticFields()
        {
            ExportHelper.InertialValidationRow row =
                ExportHelper.InertialValidationRow.Diagnostic(
                    "ellipsoid.display",
                    "display",
                    "WARN",
                    "display failed, physical inertia passed");
            ExportHelper.InertialValidationRecord record =
                new ExportHelper.InertialValidationRecord(
                    "base_link",
                    "Origin_global",
                    row);

            string csv = ExportHelper.BuildInertialValidationCsv(new[] { record });

            Assert.Contains(
                "base_link,Origin_global,ellipsoid.display,,,,,,WARN,display,\"display failed, physical inertia passed\"",
                csv);
        }
    }
}
