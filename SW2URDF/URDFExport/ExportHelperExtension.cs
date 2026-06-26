/*
Copyright (c) 2015 Stephen Brawner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace SW2URDF.URDFExport
{
    public partial class ExportHelper
    {
        private string referenceSketchName;
        private string ExportErrorWhy;

        #region SW to Robot and link methods

        //Used right now only by the Part Exporter, but this starts the building of the robot
        public void CreateRobotFromActiveModel()
        {
            URDFRobot = new Robot();
            URDFRobot.Name = ActiveSWModel.GetTitle();

            Configuration swConfig = ActiveSWModel.ConfigurationManager.ActiveConfiguration;
            foreach (string state in swConfig.GetDisplayStates())
            {
                if (state.Equals("URDF Export"))
                {
                    swConfig.ApplyDisplayState("URDF Export");
                }
            }

            //Each Robot contains a single base link, build this link
            Link baseLink = CreateBaseLinkFromActiveModel();
            URDFRobot.SetBaseLink(baseLink);
        }

        // This method now only works for the part exporter
        private Link CreateBaseLinkFromActiveModel()
        {
            // If the model is a part
            if (ActiveSWModel.GetType() == (int)swDocumentTypes_e.swDocPART)
            {
                return CreateLinkFromPartModel(ActiveSWModel);
            }
            return null;
        }

        // This creates a Link from a Part ModelDoc. It basically just extracts the material
        // properties and saves them to the appropriate fields.
        private static Link CreateLinkFromPartModel(ModelDoc2 swModel)
        {
            Link Link = new Link(null);
            Link.Name = swModel.GetTitle();

            Link.isFixedFrame = false;

            //Get link properties from SolidWorks part
            IMassProperty swMass = swModel.Extension.CreateMassProperty();
            Link.Inertial.Mass.Value = swMass.Mass;

            // returned as double with values [Lxx, Lxy, Lxz, Lyx, Lyy, Lyz, Lzx, Lzy, Lzz]
            double[] moment = swMass.GetMomentOfInertia(
                (int)swMassPropertyMoment_e.swMassPropertyMomentAboutCenterOfMass);
            Link.Inertial.Inertia.SetMomentMatrix(moment);

            double[] centerOfMass = swMass.CenterOfMass;
            Link.Inertial.Origin.SetXYZ(centerOfMass);
            Link.Inertial.Origin.SetRPY(new double[3] { 0, 0, 0 });

            // Will this ever not be zeros?
            Link.Visual.Origin.SetXYZ(new double[3] { 0, 0, 0 });
            Link.Visual.Origin.SetRPY(new double[3] { 0, 0, 0 });
            Link.Collision.Origin.SetXYZ(new double[3] { 0, 0, 0 });
            Link.Collision.Origin.SetRPY(new double[3] { 0, 0, 0 });

            // [ R, G, B, Ambient, Diffuse, Specular, Shininess, Transparency, Emission ]
            double[] values = swModel.MaterialPropertyValues;
            Link.Visual.Material.Color.Red = values[0];
            Link.Visual.Material.Color.Green = values[1];
            Link.Visual.Material.Color.Blue = values[2];
            Link.Visual.Material.Color.Alpha = 1.0 - values[7];
            Link.Visual.Material.Name = "material_" + Link.Name;

            return Link;
        }

        //This is only used by the Part Exporter, but it localizes the link to the Origin_global
        // coordinate system
        private static void LocalizeLink(Link Link, Matrix<double> GlobalTransform)
        {
            Matrix<double> GlobalTransformInverse = GlobalTransform.Inverse();
            Matrix<double> linkCoMTransform = MathOps.GetTranslation(Link.Inertial.Origin.GetXYZ());
            Matrix<double> localLinkCoMTransform = GlobalTransformInverse * linkCoMTransform;

            Matrix<double> linkVisualTransform =
                MathOps.GetTransformation(Link.Visual.Origin.GetXYZ(), Link.Visual.Origin.GetRPY());
            Matrix<double> localVisualTransform = GlobalTransformInverse * linkVisualTransform;

            Matrix<double> linkCollisionTransform =
                MathOps.GetTransformation(Link.Collision.Origin.GetXYZ(), Link.Collision.Origin.GetRPY());
            Matrix<double> localCollisionTransform =
                GlobalTransformInverse * linkCollisionTransform;

            // The linear array in Link.Inertial.Inertia.Moment is in row major order, but this
            // matrix constructor uses column major order. It's a rotation matrix, so this
            // shouldn't matter. If it does, just transpose linkGlobalMomentInertia. These three
            // matrices are 3x3 as opposed to the 4x4 transformation matrices above.
            // You're welcome for the confusion.
            Matrix<double> linkGlobalMomentInertia =
                new DenseMatrix(3, 3, Link.Inertial.Inertia.GetMoment());
            Matrix<double> GlobalRotMat =
                GlobalTransform.SubMatrix(0, 3, 0, 3);
            Matrix<double> linkLocalMomentInertia =
                GlobalRotMat * linkGlobalMomentInertia * GlobalRotMat.Transpose();

            Link.Inertial.Origin.SetXYZ(MathOps.GetXYZ(localLinkCoMTransform));
            Link.Inertial.Origin.SetRPY(new double[] { 0, 0, 0 });

            // Wait are you saying that even though the matrix was trasposed from column major
            // order, you are writing it in row-major order here. Yes, yes I am.
            double[] moment = linkLocalMomentInertia.ToRowMajorArray();
            Link.Inertial.Inertia.SetMomentMatrix(moment);

            Link.Collision.Origin.SetXYZ(MathOps.GetXYZ(localCollisionTransform));
            Link.Collision.Origin.SetRPY(MathOps.GetRPY(localCollisionTransform));

            Link.Visual.Origin.SetXYZ(MathOps.GetXYZ(localVisualTransform));
            Link.Visual.Origin.SetRPY(MathOps.GetRPY(localVisualTransform));
        }

        // The one used by the Assembly Exporter
        public bool CreateRobotFromTreeView(LinkNode baseNode)
        {
            ExportErrorWhy = "";
            URDFRobot = new Robot();

            progressBar.Start(0, CommonSwOperations.GetCount(baseNode.Nodes) + 1,
                ChineseUiText.Translate("Building links", "\u6b63\u5728\u6784\u5efa Link"));
            int count = 0;

            progressBar.UpdateProgress(count);
            progressBar.UpdateTitle(ChineseUiText.Translate(
                "Building link: " + baseNode.Name,
                "\u6b63\u5728\u6784\u5efa Link: " + baseNode.Name));
            
            Link baseLink = CreateLink(baseNode, 1);
            if (baseLink == null || !string.IsNullOrWhiteSpace(ExportErrorWhy))
            {
                MessageBox.Show(ExportErrorWhy);
                logger.Warn(ExportErrorWhy);
                progressBar.End();
                return false;
            }
            URDFRobot.SetBaseLink(baseLink);
            baseNode.Link = baseLink;

            progressBar.End();
            return true;
        }

        private Link CreateBaseLinkFromComponents(LinkNode node)
        {
            // Build the link from the partdoc
            Link link = CreateLinkFromComponents(null, node);
            if (node.Link.Joint.CoordinateSystemName == "Automatically Generate")
            {
                CreateBaseRefOrigin(true);
                node.Link.Joint.CoordinateSystemName = "Origin_global";
                link.Joint.CoordinateSystemName = node.Link.Joint.CoordinateSystemName;
            }
            else
            {
                link.Joint.CoordinateSystemName = node.Link.Joint.CoordinateSystemName;
            }
            return link;
        }

        //Method which builds an entire link and iterates through.
        private Link CreateLink(LinkNode node, int count)
        {
            progressBar.UpdateTitle(ChineseUiText.Translate(
                "Building link: " + node.Name,
                "\u6b63\u5728\u6784\u5efa Link: " + node.Name));
            progressBar.UpdateProgress(count);
            Link link;
            if (node.IsBaseNode)
            {
                link = CreateBaseLinkFromComponents(node);
                URDFRobot.SetBaseLink(link);
            }
            else
            {
                LinkNode parentNode = (LinkNode)node.Parent;
                link = CreateLinkFromComponents(parentNode.Link, node);
            }
            node.Link = link;
            if (!string.IsNullOrWhiteSpace(ExportErrorWhy))
            {
                return null;
            }

            // Reset list of children, don't worry the links that were saved are still attached to the child nodes
            link.Children.Clear();
            foreach (LinkNode child in node.Nodes)
            {
                Link childLink = CreateLink(child, count + 1);

                if (!string.IsNullOrWhiteSpace(ExportErrorWhy))
                {
                    return null;
                }
                else
                {
                    link.Children.Add(childLink);
                }
            }
            return link;
        }

        private void ComputeInertialProperties(Link link)
        {
            // Get the SolidWorks MathTransform that corresponds to the child coordinate system
            MathTransform jointTransform = GetCoordinateSystemTransform(link.Joint.CoordinateSystemName);
            if (jointTransform == null)
            {
                throw new Exception("Cannot compute mass properties because coordinate system " +
                    link.Joint.CoordinateSystemName + " was not found");
            }
            List<Body2> bodies = GetBodies(link.SWComponents);

            logger.Info("Computing inertial properties for link " + link.Name +
                " from " + bodies.Count + " solid bodies in coordinate system " +
                link.Joint.CoordinateSystemName);
            MassProperty swMass = CreateMassProperty(bodies, jointTransform);

            link.Inertial.Mass.Value = swMass.Mass;
            link.Inertial.Origin.SetXYZ(swMass.CenterOfMass);
            link.Inertial.Origin.SetRPY(new double[3] { 0, 0, 0 });

            double[] moment = (double[])swMass.GetMomentOfInertia(
                (int)swMassPropertyMoment_e.swMassPropertyMomentAboutCenterOfMass);
            link.Inertial.Inertia.SetMomentMatrix(moment);

            if (!InertiaEllipsoid.TryCreate(
                link.Inertial.Mass.Value,
                link.Inertial.Inertia,
                out InertiaEllipsoid ellipsoid,
                out string error))
            {
                throw new Exception("Computed inertia for link " + link.Name +
                    " is not physically valid: " + error);
            }
            logger.Info(string.Format(CultureInfo.InvariantCulture,
                "Computed inertia for link {0}: mass={1:G9} kg, COM=({2:G9}, {3:G9}, {4:G9}) m, " +
                "equivalent ellipsoid semi-axes=({5:G9}, {6:G9}, {7:G9}) m",
                link.Name,
                link.Inertial.Mass.Value,
                link.Inertial.Origin.GetXYZ()[0],
                link.Inertial.Origin.GetXYZ()[1],
                link.Inertial.Origin.GetXYZ()[2],
                ellipsoid.SemiAxes[0],
                ellipsoid.SemiAxes[1],
                ellipsoid.SemiAxes[2]));
        }

        private List<InertialValidationRecord> LogInertialValidation(Link link, string csvFileName)
        {
            List<InertialValidationRecord> records = new List<InertialValidationRecord>();
            logger.Info("Validating URDF inertial values against SolidWorks mass properties");
            LogLinkInertialValidation(link, records);
            WriteInertialValidationCsv(csvFileName, records);
            logger.Info("Wrote inertial validation CSV with " + records.Count + " rows to " + csvFileName);
            return records;
        }

        private void LogLinkInertialValidation(Link link, List<InertialValidationRecord> records)
        {
            if (link == null)
            {
                return;
            }

            try
            {
                LogSingleLinkInertialValidation(link, records);
            }
            catch (Exception e)
            {
                logger.Warn("Could not validate inertial values for link " + link.Name, e);
            }

            foreach (Link child in link.Children)
            {
                LogLinkInertialValidation(child, records);
            }
        }

        private void LogSingleLinkInertialValidation(Link link, List<InertialValidationRecord> records)
        {
            if (link.SWComponents == null || link.SWComponents.Count == 0)
            {
                logger.Warn("Skipping inertial validation for link " + link.Name +
                    " because it has no SolidWorks components");
                return;
            }

            MathTransform jointTransform = GetCoordinateSystemTransform(link.Joint.CoordinateSystemName);
            if (jointTransform == null)
            {
                logger.Warn("Skipping inertial validation for link " + link.Name +
                    " because coordinate system " + link.Joint.CoordinateSystemName + " was not found");
                return;
            }

            List<Body2> bodies = GetBodies(link.SWComponents);
            MassProperty swMass = CreateMassProperty(bodies, jointTransform);
            double[] swCenterOfMass = swMass.CenterOfMass;
            double[] swMoment = (double[])swMass.GetMomentOfInertia(
                (int)swMassPropertyMoment_e.swMassPropertyMomentAboutCenterOfMass);
            double[] expectedMoment = ConvertSolidWorksMomentToUrdfConvention(swMoment);

            double[] urdfOrigin = link.Inertial.Origin.GetXYZ();
            double[] urdfMoment = new double[]
            {
                link.Inertial.Inertia.Ixx,
                link.Inertial.Inertia.Ixy,
                link.Inertial.Inertia.Ixz,
                link.Inertial.Inertia.Iyy,
                link.Inertial.Inertia.Iyz,
                link.Inertial.Inertia.Izz
            };

            List<InertialValidationRow> rows = new List<InertialValidationRow>
            {
                new InertialValidationRow("mass", "kg", swMass.Mass, link.Inertial.Mass.Value),
                new InertialValidationRow("origin.x", "m", swCenterOfMass[0], urdfOrigin[0]),
                new InertialValidationRow("origin.y", "m", swCenterOfMass[1], urdfOrigin[1]),
                new InertialValidationRow("origin.z", "m", swCenterOfMass[2], urdfOrigin[2]),
                new InertialValidationRow("ixx", "kg*m^2", expectedMoment[0], urdfMoment[0]),
                new InertialValidationRow("ixy", "kg*m^2", expectedMoment[1], urdfMoment[1]),
                new InertialValidationRow("ixz", "kg*m^2", expectedMoment[2], urdfMoment[2]),
                new InertialValidationRow("iyy", "kg*m^2", expectedMoment[3], urdfMoment[3]),
                new InertialValidationRow("iyz", "kg*m^2", expectedMoment[4], urdfMoment[4]),
                new InertialValidationRow("izz", "kg*m^2", expectedMoment[5], urdfMoment[5])
            };

            foreach (InertialValidationRow row in rows)
            {
                records.Add(new InertialValidationRecord(
                    link.Name,
                    link.Joint.CoordinateSystemName,
                    row));
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Inertial validation for link '" + link.Name + "'");
            builder.AppendLine("Coordinate system: " + link.Joint.CoordinateSystemName);
            builder.AppendLine("SolidWorks source: MassProperty with selected coordinate system, COM inertia; " +
                "off-diagonal products converted to URDF sign convention.");
            builder.AppendLine("Units: mass kg, origin m, inertia kg*m^2. SolidWorks UI equivalent: origin m*1000, inertia kg*m^2*1e6.");
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-12} {1,-8} {2,18} {3,18} {4,18} {5,14} {6,8}",
                "quantity", "unit", "sw_expected", "urdf_value", "abs_error", "rel_error_%", "status"));

            double maxAbsError = 0;
            double maxRelativeErrorPercent = 0;
            bool validationPassed = true;
            foreach (InertialValidationRow row in rows)
            {
                maxAbsError = Math.Max(maxAbsError, Math.Abs(row.AbsoluteError));
                double? relativeErrorPercent = row.RelativeErrorPercent;
                if (relativeErrorPercent.HasValue)
                {
                    maxRelativeErrorPercent = Math.Max(maxRelativeErrorPercent, relativeErrorPercent.Value);
                }
                validationPassed &= row.Passed;

                builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-12} {1,-8} {2,18} {3,18} {4,18} {5,14} {6,8}",
                    row.Quantity,
                    row.Unit,
                    FormatValidationNumber(row.SolidWorksExpected),
                    FormatValidationNumber(row.UrdfValue),
                    FormatValidationNumber(row.AbsoluteError),
                    FormatValidationPercent(relativeErrorPercent),
                    row.Passed ? "PASS" : "FAIL"));
            }

            builder.AppendLine("Max abs error: " + FormatValidationNumber(maxAbsError) +
                "; max relative error: " + FormatValidationNumber(maxRelativeErrorPercent) + "%");
            builder.AppendLine("Validation result: " + (validationPassed ? "PASS" : "FAIL"));
            if (validationPassed)
            {
                logger.Info(builder.ToString());
            }
            else
            {
                logger.Warn(builder.ToString());
            }
        }

        private static void WriteInertialValidationCsv(
            string csvFileName,
            IEnumerable<InertialValidationRecord> records)
        {
            string directory = Path.GetDirectoryName(csvFileName);
            if (!String.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                csvFileName,
                BuildInertialValidationCsv(records),
                new UTF8Encoding(false));
        }

        internal static string BuildInertialValidationCsv(IEnumerable<InertialValidationRecord> records)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(
                "link,coordinate_system,quantity,unit,solidworks_expected,urdf_value,absolute_error,relative_error_percent,status");
            foreach (InertialValidationRecord record in records)
            {
                InertialValidationRow row = record.Row;
                builder.AppendLine(String.Join(",", new[]
                {
                    CsvField(record.LinkName),
                    CsvField(record.CoordinateSystemName),
                    CsvField(row.Quantity),
                    CsvField(row.Unit),
                    FormatCsvNumber(row.SolidWorksExpected),
                    FormatCsvNumber(row.UrdfValue),
                    FormatCsvNumber(row.AbsoluteError),
                    FormatCsvPercent(row.RelativeErrorPercent),
                    row.Passed ? "PASS" : "FAIL"
                }));
            }

            return builder.ToString();
        }

        private static string FormatCsvNumber(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static string FormatCsvPercent(double? value)
        {
            return value.HasValue ? FormatCsvNumber(value.Value) : "";
        }

        private static string CsvField(string value)
        {
            if (value == null)
            {
                return "";
            }

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        internal static double[] ConvertSolidWorksMomentToUrdfConvention(double[] solidWorksMoment)
        {
            return new double[]
            {
                solidWorksMoment[0],
                -solidWorksMoment[1],
                -solidWorksMoment[2],
                solidWorksMoment[4],
                -solidWorksMoment[5],
                solidWorksMoment[8]
            };
        }

        private static string FormatValidationNumber(double value)
        {
            return value.ToString("0.#########E+0", CultureInfo.InvariantCulture);
        }

        private static string FormatValidationPercent(double? value)
        {
            if (!value.HasValue)
            {
                return "n/a";
            }

            return FormatValidationNumber(value.Value);
        }

        internal class InertialValidationRecord
        {
            public InertialValidationRecord(
                string linkName,
                string coordinateSystemName,
                InertialValidationRow row)
            {
                LinkName = linkName;
                CoordinateSystemName = coordinateSystemName;
                Row = row;
            }

            public string LinkName { get; private set; }

            public string CoordinateSystemName { get; private set; }

            public InertialValidationRow Row { get; private set; }
        }

        internal class MeshExportRecord
        {
            public MeshExportRecord(
                string linkName,
                string collisionStrategy,
                string collisionEffectiveStrategy,
                string collisionGeometryType,
                string collisionNotes,
                string meshFormat,
                string visualUri,
                string collisionUri,
                string visualWindowsPath,
                string collisionWindowsPath,
                bool visualExists,
                bool collisionExists,
                long? visualBytes,
                long? collisionBytes,
                uint? visualTriangles,
                uint? collisionTriangles,
                StlExportStats stlStats = null)
            {
                LinkName = linkName;
                CollisionStrategy = collisionStrategy;
                CollisionEffectiveStrategy = collisionEffectiveStrategy;
                CollisionGeometryType = collisionGeometryType;
                CollisionNotes = collisionNotes;
                MeshFormat = meshFormat;
                VisualUri = visualUri;
                CollisionUri = collisionUri;
                VisualWindowsPath = visualWindowsPath;
                CollisionWindowsPath = collisionWindowsPath;
                VisualExists = visualExists;
                CollisionExists = collisionExists;
                VisualBytes = visualBytes;
                CollisionBytes = collisionBytes;
                VisualTriangles = visualTriangles;
                CollisionTriangles = collisionTriangles;
                StlStats = stlStats ?? StlExportStats.NotExported();
            }

            public string LinkName { get; private set; }

            public string CollisionStrategy { get; private set; }

            public string CollisionEffectiveStrategy { get; private set; }

            public string CollisionGeometryType { get; private set; }

            public string CollisionNotes { get; private set; }

            public string MeshFormat { get; private set; }

            public string VisualUri { get; private set; }

            public string CollisionUri { get; private set; }

            public string VisualWindowsPath { get; private set; }

            public string CollisionWindowsPath { get; private set; }

            public bool VisualExists { get; private set; }

            public bool CollisionExists { get; private set; }

            public long? VisualBytes { get; private set; }

            public long? CollisionBytes { get; private set; }

            public uint? VisualTriangles { get; private set; }

            public uint? CollisionTriangles { get; private set; }

            public StlExportStats StlStats { get; private set; }
        }

        internal class StlExportStats
        {
            public StlExportStats()
            {
                QualityLabel = "";
            }

            public string QualityLabel { get; set; }

            public double? ReductionRatio { get; set; }

            public bool? CustomSettings { get; set; }

            public double? Deviation { get; set; }

            public double? AngleTolerance { get; set; }

            public int? BaselineEstimatedTriangles { get; set; }

            public long? BaselineEstimatedBytes { get; set; }

            public int? EstimatedTriangles { get; set; }

            public long? EstimatedBytes { get; set; }

            public uint? ActualTriangles { get; set; }

            public long? ActualBytes { get; set; }

            public double? EstimateErrorPercent { get; set; }

            public double? EstimatedReductionPercent { get; set; }

            public double? ActualReductionPercent { get; set; }

            public static StlExportStats FromSettings(StlMeshSettings settings)
            {
                return new StlExportStats
                {
                    QualityLabel = settings == null ? "" : settings.QualityLabel,
                    ReductionRatio = settings == null ? (double?)null : settings.ReductionRatio,
                    CustomSettings = settings == null ? (bool?)null : settings.UseCustom,
                    Deviation = settings == null ? (double?)null : settings.Deviation,
                    AngleTolerance = settings == null ? (double?)null : settings.AngleTolerance
                };
            }

            public static StlExportStats NotExported()
            {
                return new StlExportStats();
            }
        }

        internal class CollisionMeshExportResult
        {
            public CollisionMeshExportResult(
                CollisionMeshStrategy requestedStrategy,
                CollisionMeshStrategy effectiveStrategy,
                string geometryType,
                string notes)
            {
                RequestedStrategy = requestedStrategy;
                EffectiveStrategy = effectiveStrategy;
                GeometryType = geometryType;
                Notes = notes;
            }

            public CollisionMeshStrategy RequestedStrategy { get; private set; }

            public CollisionMeshStrategy EffectiveStrategy { get; private set; }

            public string GeometryType { get; private set; }

            public string Notes { get; private set; }

            public static CollisionMeshExportResult NotExported(CollisionMeshStrategy requestedStrategy)
            {
                return new CollisionMeshExportResult(
                    requestedStrategy,
                    requestedStrategy,
                    "not_exported",
                    "mesh_export_disabled");
            }
        }

        internal class InertialValidationRow
        {
            private const double RelativeErrorFloor = 1e-30;
            private const double RoundedUrdfRelativeTolerance = 5e-5;

            public InertialValidationRow(string quantity, string unit, double solidWorksExpected, double urdfValue)
            {
                Quantity = quantity;
                Unit = unit;
                SolidWorksExpected = solidWorksExpected;
                UrdfValue = urdfValue;
            }

            public string Quantity { get; private set; }

            public string Unit { get; private set; }

            public double SolidWorksExpected { get; private set; }

            public double UrdfValue { get; private set; }

            public double AbsoluteError
            {
                get { return UrdfValue - SolidWorksExpected; }
            }

            public double? RelativeErrorPercent
            {
                get
                {
                    if (Math.Abs(SolidWorksExpected) < RelativeErrorFloor)
                    {
                        if (Math.Abs(AbsoluteError) < RelativeErrorFloor)
                        {
                            return 0;
                        }

                        return null;
                    }

                    return Math.Abs(AbsoluteError) / Math.Abs(SolidWorksExpected) * 100.0;
                }
            }

            public bool Passed
            {
                get
                {
                    double absoluteTolerance;
                    if (Quantity == "mass")
                    {
                        absoluteTolerance = Math.Max(1e-9,
                            Math.Abs(SolidWorksExpected) * RoundedUrdfRelativeTolerance);
                    }
                    else if (Quantity.StartsWith("origin.", StringComparison.Ordinal))
                    {
                        absoluteTolerance = Math.Max(5e-6,
                            Math.Abs(SolidWorksExpected) * RoundedUrdfRelativeTolerance);
                    }
                    else
                    {
                        absoluteTolerance = Math.Max(1e-12,
                            Math.Abs(SolidWorksExpected) * 1e-4);
                    }
                    return Math.Abs(AbsoluteError) <= absoluteTolerance;
                }
            }
        }

        private MassProperty CreateMassProperty(List<Body2> bodies, MathTransform coordinateSystemTransform)
        {
            if (bodies.Count == 0)
            {
                throw new Exception("Cannot compute mass properties because no solid bodies were found");
            }

            MassProperty swMass = ActiveSWModel.Extension.CreateMassProperty();
            bool addedBodies = swMass.AddBodies(bodies.ToArray());
            if (!addedBodies)
            {
                throw new Exception("Failed to add bodies to swMass");
            }
            bool coordinateSystemSet = swMass.SetCoordinateSystem(coordinateSystemTransform);
            if (!coordinateSystemSet)
            {
                throw new Exception("SolidWorks rejected the requested mass-property coordinate system");
            }

            return swMass;
        }

        private static void ComputeVisualCollisionProperties(Link link)
        {
            link.Visual.Origin.SetXYZ(new double[3] { 0, 0, 0 });
            link.Visual.Origin.SetRPY(new double[3] { 0, 0, 0 });
            link.Collision.Origin.SetXYZ(new double[3] { 0, 0, 0 });
            link.Collision.Origin.SetRPY(new double[3] { 0, 0, 0 });

            if (link.SWComponents.Count == 0)
            {
                return;
            }

            ModelDoc2 mainCompdoc = link.SWComponents[0].GetModelDoc2();

            // [ R, G, B, Ambient, Diffuse, Specular, Shininess, Transparency, Emission ]
            double[] values = mainCompdoc.MaterialPropertyValues;
            link.Visual.Material.Color.Red = values[0];
            link.Visual.Material.Color.Green = values[1];
            link.Visual.Material.Color.Blue = values[2];
            link.Visual.Material.Color.Alpha = 1.0 - values[7];
        }

        //Method which builds a single link
        private Link CreateLinkFromComponents(Link parent, LinkNode node)
        {
            ApplyCollisionStrategyPrefix(node.Link);

            if (node.Link.SWComponents.Count > 0)
            {
                List<Component2> components = node.Link.SWComponents;
                node.Link.SWMainComponent = components[0];
            }

            if (parent != null && ComputeJointKinematics)
            {
                logger.Info("Creating joint " + node.Link.Name);
                bool error = CreateJoint(parent, node.Link);
                if (error)
                {
                    logger.Warn(
                        string.Format("Creating joint from parent {0} to child {1} failed", 
                            parent.Name, node.Link.Name));
                }
            }

            if (ComputeInertialValues)
            {
                ComputeInertialProperties(node.Link);
            }

            if (ComputeVisualCollision)
            {
                ComputeVisualCollisionProperties(node.Link);
            }

            return node.Link;
        }

        private List<Body2> GetBodies(List<Component2> components)
        {
            List<Body2> bodies = new List<Body2>();
            foreach (Component2 comp in components)
            {
                // Retrieving the Body2 bodies of the component. Also need to recur through the assembly tree
                object[] componentBodies =
                    (object[])comp.GetBodies3((int)swBodyType_e.swSolidBody, out _);
                if (componentBodies != null)
                {
                    foreach (Body2 obj in componentBodies)
                    {
                        bodies.Add(obj);
                    }
                }
                object[] children = comp.GetChildren();
                if (children != null)
                {
                    List<Component2> childComponents = new List<Component2>();
                    foreach (Component2 child in children)
                    {
                        childComponents.Add(child);
                    }
                    bodies.AddRange(GetBodies(childComponents));
                }
            }
            return bodies;
        }

        #endregion SW to Robot and link methods

        #region Joint methods

        //Base method for constructing a joint from a parent link and child link.
        private bool CreateJoint(Link parent, Link child)
        {
            CheckRefGeometryExists(child);

            string coordSysName = child.Joint.CoordinateSystemName;
            string axisName = child.Joint.AxisName;
            string jointType = child.Joint.Type;

            child.Joint.Parent.Name = parent.Name;
            child.Joint.Child.Name = child.Name;
            if (child.isFixedFrame)
            {
                axisName = "";
                jointType = "fixed";
                child.Joint.Type = jointType;
            }
            else if (coordSysName == "Automatically Generate" ||
                axisName == "Automatically Generate" || jointType == "Automatically Detect")
            {
                // We have to estimate the joint if the user specifies automatic for either the
                // reference coordinate system, the reference axis or the joint type.
                EstimateGlobalJointFromComponents(parent, child);
                bool autoGenerateError = (
                    child.Joint.Origin.X == 0.0 && child.Joint.Origin.Y == 0.0 && child.Joint.Origin.Z == 0.0 &&
                    child.Joint.Origin.Roll == 0.0 && child.Joint.Origin.Pitch == 0.0 && child.Joint.Origin.Yaw == 0.0);

                if (autoGenerateError)
                {
                    ExportErrorWhy = string.Format("Inferring the joint geometry failed for the joint {0} " +
                        "from link {1} to {2} failed. Check that the mates have not fully defined the " +
                        "components in link {1} and that there is exactly one degree of freedom.",
                        child.Joint.Name, child.Name, parent.Name);
                    return false;
                }
            }

            if (coordSysName == "Automatically Generate")
            {
                child.Joint.CoordinateSystemName = "Origin_" + child.Joint.Name;
                ActiveSWModel.ClearSelection2(true);
                int i = 2;
                while (ActiveSWModel.Extension.SelectByID2(
                    child.Joint.CoordinateSystemName, "COORDSYS", 0, 0, 0, false, 0, null, 0))
                {
                    ActiveSWModel.ClearSelection2(true);
                    child.Joint.CoordinateSystemName =
                        "Origin_" + child.Joint.Name + i.ToString();
                    i++;
                }

                CreateRefOrigin(child.Joint);
            }

            if (axisName == "Automatically Generate")
            {
                child.Joint.AxisName = "Axis_" + child.Joint.Name;
                ActiveSWModel.ClearSelection2(true);
                int i = 2;
                while (ActiveSWModel.Extension.SelectByID2(
                    child.Joint.AxisName, "AXIS", 0, 0, 0, false, 0, null, 0))
                {
                    ActiveSWModel.ClearSelection2(true);
                    child.Joint.AxisName = "Axis_" + child.Joint.Name + i.ToString();
                    i++;
                }
                if (child.Joint.Type != "fixed")
                {
                    CreateRefAxis(child.Joint);
                }
            }

            EstimateGlobalJointFromRefGeometry(child);

            coordSysName = parent.Joint.CoordinateSystemName;

            LocalizeJoint(child.Joint, coordSysName);
            return true;
        }

        // Creates a Reference Coordinate System in the SolidWorks Model to symbolize the joint location
        private void CreateRefOrigin(Joint Joint)
        {
            CreateRefOrigin(Joint.Origin, Joint.CoordinateSystemName);
        }

        // Creates a Reference Coordinate System in the SolidWorks Model to symbolize the joint location
        private void CreateRefOrigin(Origin Origin, string CoordinateSystemName)
        {
            // Adds the sketch segments and point to the 3D sketch. The sketchEnties are the actual
            // items created (and their locations)
            object[] sketchEntities = AddSketchGeometry(Origin);

            SketchPoint OriginPoint = (SketchPoint)sketchEntities[0];
            SketchSegment xaxis = (SketchSegment)sketchEntities[1];
            SketchSegment yaxis = (SketchSegment)sketchEntities[2];

            double originX = (double)sketchEntities[3]; //OriginPoint X
            double originY = (double)sketchEntities[4];
            double originZ = (double)sketchEntities[5];

            double xAxisX = (double)sketchEntities[6];
            double xAxisY = (double)sketchEntities[7];
            double xAxisZ = (double)sketchEntities[8];

            double yAxisX = (double)sketchEntities[9];
            double yAxisY = (double)sketchEntities[10];
            double yAxisZ = (double)sketchEntities[11];

            ActiveSWModel.ClearSelection2(true);
            SelectionMgr selectionManager = ActiveSWModel.SelectionManager;
            SelectData data = selectionManager.CreateSelectData();

            // First select the origin
            bool SelectedOrigin = false;
            bool SelectedXAxis = false;
            bool SelectedYAxis = false;
            if (OriginPoint != null)
            {
                data.Mark = 1;
                SelectedOrigin = OriginPoint.Select4(true, data);
            }
            if (!SelectedOrigin)
            {
                ActiveSWModel.Extension.SelectByID2(
                    "", "EXTSKETCHPOINT", originX, originY, originZ, true, 1, null, 0);
            }

            // Second, select the xaxis
            if (xaxis != null)
            {
                data.Mark = 2;
                SelectedXAxis = xaxis.Select4(true, data);
            }
            if (!SelectedXAxis)
            {
               ActiveSWModel.Extension.SelectByID2
                 ("", "EXTSKETCHPOINT", xAxisX, xAxisY, xAxisZ, true, 2, null, 0);
            }

            // Third, select the yaxis
            if (yaxis != null)
            {
                data.Mark = 4;
                SelectedYAxis = yaxis.Select4(true, data);
            }
            if (!SelectedYAxis)
            {
                ActiveSWModel.Extension.SelectByID2(
                    "", "EXTSKETCHPOINT", yAxisX, yAxisY, yAxisZ, true, 4, null, 0);
            }

            //From the selected items, insert a coordinate system.
            Feature coordinates =
                ActiveSWModel.FeatureManager.InsertCoordinateSystem(false, false, false);
            if (coordinates != null)
            {
                coordinates.Name = CoordinateSystemName;
            }
        }

        //Creates the Origin_global coordinate system
        private void CreateBaseRefOrigin(bool zIsUp)
        {
            if (!ActiveSWModel.Extension.SelectByID2(
                    "Origin_global", "COORDSYS", 0, 0, 0, false, 0, null, 0))
            {
                Joint Joint = new Joint();
                if (zIsUp)
                {
                    Joint.Origin.SetRPY(new double[] { -Math.PI / 2, 0, 0 });
                }
                else
                {
                    Joint.Origin.SetRPY(new double[] { 0, 0, 0 });
                }
                Joint.Origin.SetXYZ(new double[] { 0, 0, 0 });
                Joint.CoordinateSystemName = "Origin_global";
                if (referenceSketchName == null)
                {
                    referenceSketchName = Setup3DSketch();
                }
                CreateRefOrigin(Joint);
            }
        }

        // Creates a Reference Axis to be used to calculate the joint axis
        private void CreateRefAxis(Joint Joint)
        {
            //Adds sketch segment
            SketchSegment rotaxis = AddSketchGeometry(Joint.Axis, Joint.Origin, Joint.CoordinateSystemName);
            if (rotaxis != null)
            {
                //Use special method to create the axis
                Feature featAxis = InsertAxis(rotaxis);
                if (featAxis != null)
                {
                    featAxis.Name = Joint.AxisName;
                }
            }
        }

        // Takes a links joint and calculates the local transform from the global transforms of
        // the parent and child. It also converts the axis to local values
        private void LocalizeJoint(Joint Joint, string parentCoordsysName)
        {
            MathTransform parentTransform = GetCoordinateSystemTransform(parentCoordsysName);
            
            Matrix<double> ParentJointGlobalTransform =
                MathOps.GetTransformation(parentTransform);
            MathTransform coordsysTransform =
                GetCoordinateSystemTransform(Joint.CoordinateSystemName);
           
            //Transform from global origin to child joint
            Matrix<double> ChildJointGlobalTransform =
                MathOps.GetTransformation(coordsysTransform);
            Matrix<double> ChildJointOrigin =
                ParentJointGlobalTransform.Inverse() * ChildJointGlobalTransform;
            
            //Localize the axis to the Link's coordinate system.
            Joint.Axis.SetXYZ(LocalizeAxis(Joint.Axis.GetXYZ(), Joint.CoordinateSystemName));

            // Get the array values and threshold them so small values are set to 0.
            Joint.Origin.SetXYZ(MathOps.GetXYZ(ChildJointOrigin));
            Joint.Origin.SetXYZ(MathOps.Threshold(Joint.Origin.GetXYZ(), 0.00001));
            Joint.Origin.SetRPY(MathOps.GetRPY(ChildJointOrigin));
            Joint.Origin.SetRPY(MathOps.Threshold(Joint.Origin.GetRPY(), 0.00001));
        }

        // Funny method I created that inserts a RefAxis and then finds the reference to it.
        private Feature InsertAxis(SketchSegment axis)
        {
            //First select the axis
            SelectData data = ActiveSWModel.SelectionManager.CreateSelectData();
            axis.Select4(false, data);

            //Get the features before the axis is created
            object[] featuresBefore, featuresAfter;
            featuresBefore = ActiveSWModel.FeatureManager.GetFeatures(true);
            
            //Create the axis
            ActiveSWModel.InsertAxis2(true);

            //Get the features after the axis is created
            featuresAfter = ActiveSWModel.FeatureManager.GetFeatures(true);
            
            // If it was created, try to find it
            if (featuresBefore.Length < featuresAfter.Length)
            {
                //It was probably added at the end (hence .Reverse())
                foreach (Feature feat in featuresAfter.Reverse())
                {
                    //If the feature in featuresAfter is not in features before, its gotta be the
                    // axis we inserted
                    if (!featuresBefore.Contains(feat))
                    {
                        return feat;
                    }
                }
            }
            return null;
        }

        // Inserts a sketch into the main assembly and name it
        private string Setup3DSketch()
        {
            bool sketchExists =
                ActiveSWModel.Extension.SelectByID2(
                    "URDF Reference", "SKETCH", 0, 0, 0, false, 0, null, 0);
            ActiveSWModel.SketchManager.Insert3DSketch(true);
            ActiveSWModel.SketchManager.CreatePoint(0, 0, 0);
            IFeature sketch = (IFeature)ActiveSWModel.SketchManager.ActiveSketch;
            ActiveSWModel.SketchManager.Insert3DSketch(true);
            if (!sketchExists)
            {
                sketch.Name = "URDF Reference";
            }
            return sketch.Name;
        }

        // Adds lines and a point to create the entities for a reference coordinates
        private object[] AddSketchGeometry(Origin Origin)
        {
            //Find if the sketch exists first
            if (ActiveSWModel.SketchManager.ActiveSketch == null)
            {
                bool sketchExists =
                    ActiveSWModel.Extension.SelectByID2(
                        referenceSketchName, "SKETCH", 0, 0, 0, false, 0, null, 0);
                if (!sketchExists)
                {
                    throw new Exception("Reference sketch " + referenceSketchName + " does not exist");
                }
                ActiveSWModel.SketchManager.Insert3DSketch(true);
            }

            //Calculate the lines that need to be drawn
            Matrix<double> transform = MathOps.GetRotation(Origin.GetRPY());
            Matrix<double> Axes = 0.01 * DenseMatrix.CreateIdentity(4);
            Matrix<double> tA = transform * Axes;

            // origin at X, Y, Z
            SketchPoint OriginPoint = ActiveSWModel.SketchManager.CreatePoint(Origin.X,
                                                                      Origin.Y,
                                                                      Origin.Z);

            // xAxis is a 1cm line from the origin in the direction of the xaxis of the coordinate system
            SketchSegment XAxis = ActiveSWModel.SketchManager.CreateLine(Origin.X,
                                                                         Origin.Y,
                                                                         Origin.Z,
                                                                         Origin.X + tA[0, 0],
                                                                         Origin.Y + tA[1, 0],
                                                                         Origin.Z + tA[2, 0]);
            XAxis.ConstructionGeometry = true;

            //yAxis is a 1cm line from the origin in the direction of the yaxis of the coordinate system
            SketchSegment YAxis = ActiveSWModel.SketchManager.CreateLine(Origin.X,
                                                                         Origin.Y,
                                                                         Origin.Z,
                                                                         Origin.X + tA[0, 1],
                                                                         Origin.Y + tA[1, 1],
                                                                         Origin.Z + tA[2, 1]);
            YAxis.ConstructionGeometry = true;

            //Close the sketch
            if (ActiveSWModel.SketchManager.ActiveSketch != null)
            {
                ActiveSWModel.SketchManager.Insert3DSketch(true);
            }
            // Return an array of objects representing the sketch items that were just inserted,
            // as well as the actual locations of those objecs (aids selection).
            return new object[] { OriginPoint, XAxis, YAxis,
                Origin.X, Origin.Y, Origin.Z,
                Origin.X + tA[0, 0], Origin.Y + tA[1, 0], Origin.Z + tA[2, 0],
                Origin.X + tA[0, 1], Origin.Y + tA[1, 1], Origin.Z + tA[2, 1] };
        }

        //Inserts a sketch segment for use when creating a Reference Axis
        private SketchSegment AddSketchGeometry(Axis axis, Origin origin, string coordSysName)
        {
            if (ActiveSWModel.SketchManager.ActiveSketch == null)
            {
                ActiveSWModel.Extension.SelectByID2(
                    referenceSketchName, "SKETCH", 0, 0, 0, false, 0, null, 0);
                ActiveSWModel.SketchManager.Insert3DSketch(true);
            }

            bool flip = CheckReverseAxis(axis, coordSysName);
            double sign = (flip) ? -1.0 : 1.0;

            //Insert sketch segment 0.1m long centered on the origin.
            SketchSegment rotAxis = ActiveSWModel.SketchManager.CreateLine(
                origin.X + sign * 0.05 * axis.X,
                origin.Y + sign * 0.05 * axis.Y,
                origin.Z + sign * 0.05 * axis.Z,
                origin.X - sign * 0.05 * axis.X,
                origin.Y - sign * 0.05 * axis.Y,
                origin.Z - sign * 0.05 * axis.Z);
            if (rotAxis == null)
            {
                return null;
            }
            rotAxis.ConstructionGeometry = true;
            rotAxis.Width = 2;

            //Close sketch
            if (ActiveSWModel.SketchManager.ActiveSketch != null)
            {
                ActiveSWModel.SketchManager.Insert3DSketch(true);
            }
            return rotAxis;
        }

        // Checks if an axis to be created should be flipped, so as to favor positive directions of rotation
        // This prefers that the first non-zero value be positive
        private bool CheckReverseAxis(Axis axis, string coordSysName)
        {
            //axis is a double[] {x, y, z}
            double[] transformedAxis = LocalizeAxis(axis.GetXYZ(), coordSysName);

            // If x is negative, flip
            if (transformedAxis[0] < 0)
            {
                return true;
            }
            // Else if x is 0 and y is negative, flip
            else if (Math.Abs(transformedAxis[0]) < 0.00001 && transformedAxis[1] < 0)
            {
                return true;
            }
            // Else if x and y are 0 and z is negative, flip
            else if (Math.Abs(transformedAxis[0]) < 0.00001 &&
                     Math.Abs(transformedAxis[1]) < 0.00001 &&
                     transformedAxis[2] < 0)
            {
                return true;
            }
            return false;
        }

        //Calculates the free degree of freedom (if exists), and then determines the location of the joint,
        // the axis of rotation/translation, and the type of joint
        public Boolean EstimateGlobalJointFromComponents(Link parent, Link child)
        {
            //Create the ref objects
            int degreesOfFreedom;

            // Fix parent components so that only the actual degree of freedom can be detected.
            List<Component2> fixedComponents = FixComponents(parent);

            // Surpress Limit Mates to properly find degrees of freedom. They don't work with the API call
            List<Mate2> limitMates = SuppressLimitMates(child.SWMainComponent);
            Boolean success = false;
            if (child.SWMainComponent != null)
            {
                // The wonderful undocumented API call I found to get the degrees of freedom in a joint.
                // https://forum.solidworks.com/thread/57414
                int remainingDOFs =
                    child.SWMainComponent.GetRemainingDOFs(
                        out int R1Status, out MathPoint RPoint1, out int R1DirStatus, out MathVector RDir1,
                        out int R2Status, out MathPoint RPoint2, out int R2DirStatus, out MathVector RDir2,
                        out int L1Status, out MathVector LDir1,
                        out int L2Status, out MathVector LDir2);
                if (RPoint1 != null)
                {
                    logger.Info("R1: " + R1Status + ", " + RPoint1 + ", " + R1DirStatus + ", " + RDir1.ArrayData);
                }
                else
                {
                    logger.Info("R1: " + R1Status + ", " + R1DirStatus);
                }

                if (RPoint2 != null)
                {
                    logger.Info("R2: " + R2Status + ", " + RPoint2 + ", " + R2DirStatus + ", " + RDir2.ArrayData);
                }
                else
                {
                    logger.Info("R2: " + R2Status + ", " + R2DirStatus);
                }
                if (LDir1 != null)
                {
                    logger.Info("L1: " + L1Status + ", " + LDir1.ArrayData);
                }
                else
                {
                    logger.Info("L1: " + L1Status);
                }
                if (LDir2 != null)
                {
                    logger.Info("L2: " + ", " + LDir2.ArrayData);
                }
                else
                {
                    logger.Info("L2: " + L2Status);
                }

                degreesOfFreedom = remainingDOFs;

                // Convert the gotten degrees of freedom to a joint type, origin and axis
                child.Joint.Type = "fixed";
                child.Joint.Origin.SetXYZ(MathOps.GetXYZ(child.SWMainComponent.Transform2));
                child.Joint.Origin.SetRPY(MathOps.GetRPY(child.SWMainComponent.Transform2));

                if (degreesOfFreedom == 0 && (R1Status + L1Status > 0))
                {
                    success = true;
                    if (R1Status == 1)
                    {
                        child.Joint.Type = "continuous";
                        child.Joint.Axis.SetXYZ(RDir1.ArrayData);
                        child.Joint.Origin.SetXYZ(RPoint1.ArrayData);
                        child.Joint.Origin.SetRPY(MathOps.GetRPY(child.SWMainComponent.Transform2));
                        MoveOrigin(parent, child);
                    }
                    else if (L1Status == 1)
                    {
                        child.Joint.Type = "prismatic";
                        child.Joint.Axis.SetXYZ(LDir1.ArrayData);
                        child.Joint.Origin.SetXYZ(MathOps.GetXYZ(child.SWMainComponent.Transform2));
                        child.Joint.Origin.SetRPY(MathOps.GetRPY(child.SWMainComponent.Transform2));
                        MoveOrigin(parent, child);
                    }
                }
                child.Joint.Origin.SetXYZ(MathOps.Threshold(child.Joint.Origin.GetXYZ(), 0.00001));
                child.Joint.Origin.SetRPY(MathOps.Threshold(child.Joint.Origin.GetRPY(), 0.00001));
                UnsuppressLimitMates(limitMates);
                if (limitMates.Count > 0 && ComputeJointLimits)
                {
                    AddLimits(child.Joint, limitMates, parent.SWMainComponent, child.SWMainComponent);
                }
            }

            UnFixComponents(fixedComponents);
            return success;
        }

        //This now needs to be able to get the component, and it's associated coordinate system name.
        //Then it needs to transform to the top level assembly (sounds like fun).
        private void EstimateGlobalJointFromRefGeometry(Link child)
        {
            MathTransform GlobalCoordsysTransform =
                GetCoordinateSystemTransform(child.Joint.CoordinateSystemName);
            if (GlobalCoordsysTransform == null)
            {
                logger.Warn(
                    string.Format("Joint transform for coordinate system {0} could not be computed for joint {1}", 
                        child.Joint.CoordinateSystemName, child.Joint.Name));
                return;
            }
            child.Joint.Origin.SetXYZ(MathOps.GetXYZ(GlobalCoordsysTransform));
            child.Joint.Origin.SetRPY(MathOps.GetRPY(GlobalCoordsysTransform));
            if (child.Joint.Type != "fixed")
            {
                EstimateAxis(child.Joint);
            }
        }

        // Method to get the SolidWorks MathTransform from a coordinate system. This method can account for
        // coordinate systems that are embedded in subcomponents, and apply the correct transformation to return
        // it to a global transform. It assumes that the coordinate system name is formatted like:
        // "Coordinate System 1 <assy/subassy/comp>" where the full Component2.Name2 is between the <>
        internal MathTransform GetCoordinateSystemTransform(string CoordinateSystemName)
        {
            ModelDoc2 ComponentModel = ActiveSWModel;
            MathTransform ComponentTransform = default;
            if (CoordinateSystemName == null)
            {
                throw new Exception("Coordinate system string is null");
            }
            if (CoordinateSystemName.Contains("<") && CoordinateSystemName.Contains(">"))
            {
                string componentStr = "";
                int indexFirst = CoordinateSystemName.IndexOf('<');
                int indexLast = CoordinateSystemName.IndexOf('>', indexFirst);
                if (indexLast > indexFirst)
                {
                    componentStr =
                        CoordinateSystemName.Substring(indexFirst + 1, indexLast - indexFirst - 1);
                    string CoordinateSystemNameUnTrimmed = CoordinateSystemName.Substring(0, indexFirst);
                    CoordinateSystemName = CoordinateSystemNameUnTrimmed.Trim();
                }
                AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
                object[] components = assy.GetComponents(false);
                foreach (Component2 comp in components)
                {
                    if (comp.Name2 == componentStr)
                    {
                        ComponentModel = comp.GetModelDoc2();
                        ComponentTransform = comp.Transform2;
                    }
                }
            }
            MathTransform LocalCoordsysTransform =
                ComponentModel.Extension.GetCoordinateSystemTransformByName(CoordinateSystemName);
            MathTransform GlobalCoordsysTransform = (ComponentTransform == null) ?
                LocalCoordsysTransform : LocalCoordsysTransform.Multiply(ComponentTransform);
            return GlobalCoordsysTransform;
        }

        private void MoveOrigin(Link parent, Link nonLocalizedChild)
        {
            double xMax = Double.MinValue;
            double yMax = Double.MinValue;
            double zMax = Double.MinValue;
            double xMin = Double.MaxValue;
            double yMin = Double.MaxValue;
            double zMin = Double.MaxValue;
            double[] points;

            foreach (Component2 comp in nonLocalizedChild.SWComponents)
            {
                // Returns box as [ XCorner1, YCorner1, ZCorner1, XCorner2, YCorner2, ZCorner2 ]
                points = comp.GetBox(false, false);
                xMax = MathOps.Max(points[0], points[3], xMax);
                yMax = MathOps.Max(points[1], points[4], yMax);
                zMax = MathOps.Max(points[2], points[5], zMax);
                xMin = MathOps.Min(points[0], points[3], xMin);
                yMin = MathOps.Min(points[1], points[4], yMin);
                zMin = MathOps.Min(points[2], points[5], zMin);
            }
            string coordsys = parent.Joint.CoordinateSystemName;
            MathTransform parentTransform = GetCoordinateSystemTransform(coordsys);

            double[] xyzParent = MathOps.GetXYZ(parentTransform);
            double[] xyzJointAxis = nonLocalizedChild.Joint.Axis.GetXYZ();
            double[] xyzOrigin = nonLocalizedChild.Joint.Origin.GetXYZ();
            double[] idealOrigin =
                MathOps.ClosestPointOnLineToPoint(xyzParent, xyzJointAxis, xyzOrigin);

            nonLocalizedChild.Joint.Origin.SetXYZ(
                MathOps.ClosestPointOnLineWithinBox(xMin, xMax, yMin, yMax, zMin, zMax,
                    nonLocalizedChild.Joint.Axis.GetXYZ(), idealOrigin));
        }

        // Calculates the axis from a Reference Axis in the model
        private void EstimateAxis(Joint Joint)
        {
            Joint.Axis.SetXYZ(EstimateAxis(Joint.AxisName));
        }

        //This doesn't seem to get the right values for the estimatedAxis. Check the actual values
        public double[] EstimateAxis(string axisName)
        {
            //Select the axis
            ActiveSWModel.ClearSelection2(true);

            return GetRefAxis(axisName);
        }

        private double[] GetRefAxis(string axisStr)
        {
            ModelDoc2 ComponentModel = ActiveSWModel;
            string axisName = axisStr;
            MathTransform ComponentTransform = default;

            if (axisStr.Contains("<") && axisStr.Contains(">"))
            {
                string componentStr = "";
                int indexFirst = axisStr.IndexOf('<');
                int indexLast = axisStr.IndexOf('>', indexFirst);
                if (indexLast > indexFirst)
                {
                    componentStr = axisStr.Substring(indexFirst + 1, indexLast - indexFirst - 1);
                    string CoordinateSystemNameUnTrimmed = axisStr.Substring(0, indexFirst);
                    axisName = CoordinateSystemNameUnTrimmed.Trim();
                }
                AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
                object[] components = assy.GetComponents(false);
                foreach (Component2 comp in components)
                {
                    if (comp.Name2 == componentStr)
                    {
                        ComponentModel = comp.GetModelDoc2();
                        ComponentTransform = comp.Transform2;
                    }
                }
            }
            //Calculate!
            double[] axisParams;
            double[] axisVector = new double[3];

            bool selected =
                ComponentModel.Extension.SelectByID2(axisName, "AXIS", 0, 0, 0, false, 0, null, 0);
            if (selected)
            {
                Feature feat = ComponentModel.SelectionManager.GetSelectedObject6(1, 0);
                RefAxis axis = (RefAxis)feat.GetSpecificFeature2();

                // GetRefAxisParams returns {startX, startY, startZ, endX, endY, endZ}
                axisParams = axis.GetRefAxisParams();
                axisVector[0] = axisParams[0] - axisParams[3];
                axisVector[1] = axisParams[1] - axisParams[4];
                axisVector[2] = axisParams[2] - axisParams[5];

                // Normalize and cleanup
                axisVector = MathOps.PNorm(axisVector, 2);

                // Transform to proper coordinates
                axisVector = GlobalAxis(axisVector, ComponentTransform);
            }

            return axisVector;
        }

        //This is called whenever the pull down menu is changed and the axis needs to be
        // recalculated in reference to the coordinate system
        public double[] LocalizeAxis(double[] Axis, string coordsys)
        {
            MathTransform coordsysTransform = GetCoordinateSystemTransform(coordsys);
            return LocalizeAxis(Axis, coordsysTransform);
        }

        // This is called by the above method and the getRefAxis method
        private static double[] LocalizeAxis(double[] Axis, MathTransform coordsysTransform)
        {
            if (coordsysTransform != null)
            {
                Vector<double> vec = new DenseVector(new double[] { Axis[0], Axis[1], Axis[2], 0 });
                Matrix<double> transform = MathOps.GetTransformation(coordsysTransform);
                vec = transform.Inverse() * vec;
                Axis[0] = vec[0]; Axis[1] = vec[1]; Axis[2] = vec[2];
            }
            return MathOps.Threshold(Axis, 0.00001);
        }

        private static double[] GlobalAxis(double[] axis, Matrix<double> transform)
        {
            double[] transformedAxis = new double[axis.Length];
            if (transform != null)
            {
                Vector<double> transformedVector = new DenseVector(new double[] { axis[0], axis[1], axis[2], 0 });
                transformedVector = transform * transformedVector;
                transformedAxis[0] = transformedVector[0];
                transformedAxis[1] = transformedVector[1];
                transformedAxis[2] = transformedVector[2];
            }
            return MathOps.Threshold(transformedAxis, 0.00001);
        }

        private static double[] GlobalAxis(double[] axis, MathTransform coordsysTransform)
        {
            if (coordsysTransform != null)
            {
                Matrix<double> transform = MathOps.GetTransformation(coordsysTransform);
                return GlobalAxis(axis, transform);
            }
            return axis;
        }

        // Creates a list of all the features of this type.
        private Dictionary<string, List<Feature>> GetFeaturesOfType(string featureName, bool topLevelOnly)
        {
            Dictionary<string, List<Feature>> features = new Dictionary<string, List<Feature>>();
            GetFeaturesOfType(ActiveSWModel, featureName, topLevelOnly, "", features);
            return features;
        }

        private void GetFeaturesOfType(ModelDoc2 modelDoc, string featureName,
            bool topLevelOnly, string keyName, Dictionary<string, List<Feature>> features)
        {
            string fileName = (string.IsNullOrWhiteSpace(keyName)) ? modelDoc.GetTitle() : keyName;
            logger.Info("Retrieving features of type [" + featureName + "] from " + fileName);

            features[keyName] = new List<Feature>();

            object[] featureObjects = modelDoc.FeatureManager.GetFeatures(false);
            if (featureObjects == null)
            {
                logger.Info("No features found in " + modelDoc.GetTitle());
                return;
            }

            logger.Info("Found " + featureObjects.Length + " in " + fileName);
            foreach (Feature feat in featureObjects)
            {
                if (feat.GetTypeName2() == featureName)
                {
                    features[keyName].Add(feat);
                }
            }

            logger.Info("Found " + features[keyName].Count + " features of type [" + featureName + "] in " + fileName);
            if (!topLevelOnly && modelDoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                logger.Info("Proceeding through assembly components");
                AssemblyDoc assyDoc = (AssemblyDoc)modelDoc;

                // Get top level components in an assembly. If the user wants to use a reference
                // coordinate system or axis not located in the top level assembly, then it will
                // need to be in a top level component. This will probably be ok because most
                // users keep their reference geometry in the top level assembly as it is.
                object[] components = assyDoc.GetComponents(true);

                // If there are no components in an assembly, this object will be null.
                if (components != null)
                {
                    logger.Info(components.Length + " components to check");
                    foreach (Component2 comp in components)
                    {
                        ModelDoc2 doc = comp.GetModelDoc2();
                        if (doc != null)
                        {
                            //We already have all the components in an assembly, we don't want
                            // to recur as we go through them. (topLevelOnly = true)
                            GetFeaturesOfType(doc, featureName, true, comp.Name2, features);
                        }
                    }
                }
            }
        }

        private static Dictionary<string, string> GetComponentRefGeoNames(string StringToParse)
        {
            string RefGeoName = StringToParse;
            string ComponentName = "";
            if (StringToParse.Contains("<") && StringToParse.Contains(">"))
            {
                int indexFirst = StringToParse.IndexOf('<');
                int indexLast = StringToParse.IndexOf('>', indexFirst);
                if (indexLast > indexFirst)
                {
                    ComponentName = StringToParse.Substring(indexFirst + 1, indexLast - indexFirst - 1);
                    string RefGeoNameUnTrimmed = StringToParse.Substring(0, indexFirst);
                    RefGeoName = RefGeoNameUnTrimmed.Trim();
                }
            }

            Dictionary<string, string> dict = new Dictionary<string, string>
            {
                ["geo"] = RefGeoName,
                ["component"] = ComponentName
            };
            return dict;
        }

        private List<string> FindRefGeoNames(string FeatureName)
        {
            Dictionary<string, List<Feature>> features = GetFeaturesOfType(FeatureName, false);
            List<string> featureNames = new List<string>();
            foreach (string key in features.Keys)
            {
                foreach (Feature feat in features[key])
                {
                    if (String.IsNullOrWhiteSpace(key))
                    {
                        featureNames.Add(feat.Name);
                    }
                    else
                    {
                        featureNames.Add(feat.Name + " <" + key + ">");
                    }
                }
            }
            return featureNames;
        }

        public void UpdateReferenceGeometries()
        {
            List<string> coordinateSystemNames = FindRefGeoNames("CoordSys");
            List<string> axesNames = FindRefGeoNames("RefAxis");

            ReferenceCoordinateSystemNames.Clear();
            ReferenceCoordinateSystemNames.AddRange(coordinateSystemNames);

            ReferenceAxesNames.Clear();
            ReferenceAxesNames.AddRange(axesNames);
        }

        public List<string> GetRefCoordinateSystems()
        {
            return new List<string>(ReferenceCoordinateSystemNames);
        }

        public List<string> GetRefAxes()
        {
            return new List<string>(ReferenceAxesNames);
        }

        //This method adds in the limits from a limit mate, to make a joint a revolute joint.
        // It really needs to checked for correctness.
        private static void AddLimits(Joint Joint, List<Mate2> limitMates,
            Component2 parentComponent, Component2 childComponent)
        {
            logger.Info("Parent SW Component: " + parentComponent.Name2);
            logger.Info("Child SW Component: " + childComponent.Name2);
            // The number of limit Mates should only be one. But for completeness, I cycle through
            // every found limit mate.
            foreach (Mate2 swMate in limitMates)
            {
                logger.Info("Determining limit mate eligibility ");
                List<Component2> entities = new List<Component2>();
                for (int i = 0; i < swMate.GetMateEntityCount(); i++)
                {
                    MateEntity2 entity = swMate.MateEntity(i);
                    
                    // Check if entity.ReferenceComponent is null and skip if so
                    if (entity.ReferenceComponent == null)
                    {
                        logger.Warn("Mate entity has no reference component");
                        continue;
                    }
                    
                    entities.Add(entity.ReferenceComponent);
                    logger.Info("Adding component entity: " + entity.ReferenceComponent.Name2);

                    Component2 parent = entity.ReferenceComponent.GetParent();
                    while (parent != null)
                    {
                        logger.Info("Adding component entity: " + parent.Name2);
                        entities.Add(parent);
                        parent = parent.GetParent();
                    }
                }

                if (entities.Contains(parentComponent) && entities.Contains(childComponent))
                {
                    // [TODO] This assumes the limit mate limits the right degree of freedom,
                    // it really should check that assumption
                    if ((Joint.Type == "continuous" && swMate.Type ==
                            (int)swMateType_e.swMateANGLE) ||
                        (Joint.Type == "prismatic" && swMate.Type ==
                            (int)swMateType_e.swMateDISTANCE))
                    {
                        // Unclear if flipped is the right thing we want to be checking here.
                        // From a sample size of 1, in SolidWorks it appears that an aligned and
                        // anti-aligned mates are NOT flipped...
                        if (!swMate.Flipped)
                        {
                            // Reverse mate directions, for some reason
                            Joint.Limit.Upper = -swMate.MinimumVariation;
                            Joint.Limit.Lower = -swMate.MaximumVariation;
                        }
                        else
                        {
                            // Lucky me that no conversion is necessary
                            Joint.Limit.Upper = swMate.MaximumVariation;
                            Joint.Limit.Lower = swMate.MinimumVariation;
                        }
                        if (Joint.Type == "continuous")
                        {
                            Joint.Type = "revolute";
                        }
                    }
                }
            }
        }

        // Suppresses limit mates to make it easier to find the free degree of freedom in a joint
        private static List<Mate2> SuppressLimitMates(IComponent2 component)
        {
            List<Mate2> limitMates = new List<Mate2>();

            object[] objs = component.GetMates();

            //limit mates aren't always present
            if (objs != null)
            {
                foreach (object obj in objs)
                {
                    if (obj is Mate2 swMate)
                    {
                        if (swMate.MinimumVariation != swMate.MaximumVariation)
                        {
                            limitMates.Add(swMate);
                        }
                    }
                }
            }

            foreach (Mate2 swMate in limitMates)
            {
                Feature feat = (Feature)swMate;
                feat.Select(false);
                feat.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
            }

            return limitMates;
        }

        // Unsuppresses limit mates that were suppressed before
        private static void UnsuppressLimitMates(List<Mate2> limitMates)
        {
            foreach (Mate2 swMate in limitMates)
            {
                Feature feat = (Feature)swMate;
                feat.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
            }
        }

        //Unfixes components that were fixed to find the free degree of freedom
        private void UnFixComponents(List<Component2> components)
        {
            foreach (Component2 comp in components)
            {
                logger.Info("Unfixing component " + comp.GetID());
            }

            CommonSwOperations.SelectComponents(ActiveSWModel, components, true);
            AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
            assy.UnfixComponent();
        }

        //Verifies that the reference geometry still exists. This can happen if the reference
        // geometry was deleted but the configuration was kept
        private void CheckRefGeometryExists(Link link)
        {
            if (!CheckRefCoordsysExists(link.Joint.CoordinateSystemName))
            {
                link.Joint.CoordinateSystemName = "Automatically Generate";
            }
            if (!CheckRefAxisExists(link.Joint.AxisName))
            {
                link.Joint.AxisName = "Automatically Generate";
            }
        }

        private bool CheckRefCoordsysExists(string OriginName)
        {
            return ReferenceCoordinateSystemNames.Contains(OriginName);
        }

        private bool CheckRefAxisExists(string AxisName)
        {
            return ReferenceAxesNames.Contains(AxisName);
        }

        private List<Component2> GetParentAncestorComponents(Link node)
        {
            List<Component2> components = new List<Component2>(node.SWComponents);
            if (node.Parent != null)
            {
                components.AddRange(GetParentAncestorComponents(node.Parent));
            }
            return components;
        }

        //Used to fix components to estimate the degree of freedom.
        private List<Component2> FixComponents(Link parent)
        {
            logger.Info("Fixing components for " + parent.Name);
            List<Component2> componentsToFix = GetParentAncestorComponents(parent);
            List<Component2> componentsToUnfix = new List<Component2>();
            foreach (Component2 comp in componentsToFix)
            {
                logger.Info("Fixing " + comp.GetID());
                if (!comp.IsFixed())
                {
                    componentsToUnfix.Add(comp);
                }
                else
                {
                    logger.Info("Component " + comp.GetID() + " is already fixed");
                }
            }
            CommonSwOperations.SelectComponents(ActiveSWModel, componentsToFix, true);
            AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
            assy.FixComponent();
            return componentsToUnfix;
        }

        #endregion Joint methods
    }

    public enum MeshExportFormat
    {
        STL,
        THREEDXML
    }
}
