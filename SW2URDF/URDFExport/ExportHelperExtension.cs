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
using System.Runtime.InteropServices;
using System.Text;

namespace SW2URDF.URDFExport
{
    public partial class ExportHelper
    {
        private string referenceSketchName;
        public string ExportErrorWhy { get; private set; }

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
            ApplyMassPropertyToLink(
                Link,
                ReadDocumentFrameMassProperty(swModel, null));

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

        internal static void LocalizeVisualAndCollision(
            Link link,
            Matrix<double> globalTransform)
        {
            LocalizeVisualAndCollisionWithInverse(link, globalTransform.Inverse());
        }

        private static void LocalizeVisualAndCollisionWithInverse(
            Link link,
            Matrix<double> globalTransformInverse)
        {
            Matrix<double> linkVisualTransform = MathOps.GetTransformation(
                link.Visual.Origin.GetXYZ(),
                link.Visual.Origin.GetRPY());
            Matrix<double> localVisualTransform = globalTransformInverse * linkVisualTransform;

            Matrix<double> linkCollisionTransform = MathOps.GetTransformation(
                link.Collision.Origin.GetXYZ(),
                link.Collision.Origin.GetRPY());
            Matrix<double> localCollisionTransform =
                globalTransformInverse * linkCollisionTransform;

            link.Collision.Origin.SetXYZ(MathOps.GetXYZ(localCollisionTransform));
            link.Collision.Origin.SetRPY(MathOps.GetRPY(localCollisionTransform));
            link.Visual.Origin.SetXYZ(MathOps.GetXYZ(localVisualTransform));
            link.Visual.Origin.SetRPY(MathOps.GetRPY(localVisualTransform));
        }

        // The one used by the Assembly Exporter
        public bool CreateRobotFromTreeView(LinkNode baseNode)
        {
            ExportErrorWhy = "";
            URDFRobot = new Robot();

            string jointTypeError = FindJointTypeError(baseNode, ComputeJointKinematics);
            if (!string.IsNullOrWhiteSpace(jointTypeError))
            {
                ExportErrorWhy = jointTypeError;
                logger.Warn(ExportErrorWhy);
                return false;
            }

            bool progressStarted = false;
            try
            {
                progressStarted = true;
                progressBar.Start(0, CommonSwOperations.GetCount(baseNode.Nodes) + 1,
                    ChineseUiText.Translate("Building links", "\u6b63\u5728\u6784\u5efa Link"));
                int count = 0;
                Link baseLink = CreateLink(baseNode, ref count);
                if (baseLink == null || !string.IsNullOrWhiteSpace(ExportErrorWhy))
                {
                    logger.Warn(ExportErrorWhy);
                    return false;
                }
                URDFRobot.SetBaseLink(baseLink);
                baseNode.Link = baseLink;

                jointTypeError = FindJointTypeError(baseNode, false);
                if (!string.IsNullOrWhiteSpace(jointTypeError))
                {
                    ExportErrorWhy = jointTypeError;
                    logger.Warn(ExportErrorWhy);
                    return false;
                }

                string computationError = FindJointComputationError(baseNode);
                if (!string.IsNullOrWhiteSpace(computationError))
                {
                    ExportErrorWhy = computationError;
                    logger.Warn(ExportErrorWhy);
                    return false;
                }

                string jointDataError = FindJointDataError(baseNode);
                if (!string.IsNullOrWhiteSpace(jointDataError))
                {
                    ExportErrorWhy = jointDataError;
                    logger.Warn(ExportErrorWhy);
                    return false;
                }

                return true;
            }
            finally
            {
                if (progressStarted)
                {
                    try
                    {
                        progressBar.End();
                    }
                    catch (Exception e)
                    {
                        logger.Error("Ending the SolidWorks link-building progress bar failed", e);
                    }
                }
            }
        }

        private static string FindJointTypeError(LinkNode node, bool allowAutomaticType)
        {
            if (!node.IsBaseNode)
            {
                string jointType = node.Link.Joint.Type;
                if (Joint.IsAutomaticType(jointType))
                {
                    if (!allowAutomaticType)
                    {
                        return "Joint '" + node.Link.Joint.Name +
                            "' still uses automatic type detection. Enable joint kinematics " +
                            "computation before exporting.";
                    }
                }
                else if (!Joint.AvailableTypes.Contains(jointType))
                {
                    return "Joint '" + node.Link.Joint.Name + "' has unsupported type '" +
                        jointType + "'.";
                }
            }

            foreach (LinkNode child in node.Nodes)
            {
                string error = FindJointTypeError(child, allowAutomaticType);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    return error;
                }
            }
            return string.Empty;
        }

        private static string FindJointDataError(LinkNode node)
        {
            if (!node.IsBaseNode)
            {
                Joint joint = node.Link.Joint;
                if (Joint.RequiresAxis(joint.Type) && !joint.Axis.HasValidDirection())
                {
                    return "Joint '" + joint.Name +
                        "' requires a finite, nonzero axis direction.";
                }
                if (!joint.AreRequiredFieldsSatisfied())
                {
                    return "Joint '" + joint.Name +
                        "' is missing one or more required URDF values.";
                }
            }

            foreach (LinkNode child in node.Nodes)
            {
                string error = FindJointDataError(child);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    return error;
                }
            }
            return string.Empty;
        }

        private static string FindJointComputationError(LinkNode node)
        {
            if (!node.IsBaseNode)
            {
                if (node.Link.JointKinematicsDirty)
                {
                    return "Joint '" + node.Link.Joint.Name +
                        "' kinematics could not be recomputed. Check its components and reference geometry.";
                }
                if (node.Link.JointLimitsDirty)
                {
                    return "Joint '" + node.Link.Joint.Name +
                        "' limits could not be recomputed. Add a compatible SolidWorks limit mate " +
                        "or enter valid limits manually before exporting.";
                }
            }

            foreach (LinkNode child in node.Nodes)
            {
                string error = FindJointComputationError(child);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    return error;
                }
            }
            return string.Empty;
        }

        private Link CreateBaseLinkFromComponents(LinkNode node)
        {
            if (node.Link.Joint.CoordinateSystemName == "Automatically Generate")
            {
                CreateBaseRefOrigin(true);
                node.Link.Joint.CoordinateSystemName = "Origin_global";
            }

            string configuredGlobalFrame = node.Link.Joint.CoordinateSystemName;
            string resolvedGlobalFrame = LinkTreeGlobalFramePolicy.Resolve(
                node,
                ReferenceCoordinateSystemNames);
            if (!string.Equals(
                configuredGlobalFrame,
                resolvedGlobalFrame,
                StringComparison.Ordinal))
            {
                logger.Warn("The root coordinate system had been overwritten by child Joint frame " +
                    configuredGlobalFrame + "; restored Origin_global.");
                node.Link.Joint.CoordinateSystemName = resolvedGlobalFrame;
            }

            assemblyGlobalCoordinateSystemName = node.Link.Joint.CoordinateSystemName;
            Link link = CreateLinkFromComponents(null, node);
            link.Joint.CoordinateSystemName = assemblyGlobalCoordinateSystemName;
            return link;
        }

        //Method which builds an entire link and iterates through.
        private Link CreateLink(LinkNode node, ref int count)
        {
            progressBar.UpdateTitle(ChineseUiText.Translate(
                "Building link: " + node.Name,
                "\u6b63\u5728\u6784\u5efa Link: " + node.Name));
            progressBar.UpdateProgress(count);
            count++;
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
                Link childLink = CreateLink(child, ref count);

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

        internal void ComputeInertialProperties(Link link)
        {
            MathTransform linkTransform = GetCoordinateSystemTransform(
                link.Joint.CoordinateSystemName);
            if (linkTransform == null)
            {
                throw new Exception("Cannot compute mass properties because coordinate system " +
                    link.Joint.CoordinateSystemName + " was not found");
            }
            List<Body2> bodies = GetBodies(link.SWComponents);

            logger.Info("Computing inertial properties for link " + link.Name +
                " from " + bodies.Count + " solid bodies in the document frame, then " +
                "explicitly transforming COM and tensor to Link coordinate system " +
                link.Joint.CoordinateSystemName);
            MassPropertySnapshot massProperty = ReadLinkLocalMassProperty(
                bodies,
                linkTransform);

            ApplyMassPropertyToLink(link, massProperty);

            if (!InertiaEllipsoid.TryCreate(
                link.Inertial.Mass.Value,
                link.Inertial.Inertia,
                out InertiaEllipsoid ellipsoid,
                out string error))
            {
                double[] urdfMoment = link.Inertial.Inertia.GetMoment();
                throw new Exception(string.Format(
                    CultureInfo.InvariantCulture,
                    "Computed inertia for link {0} is not physically valid: {1} " +
                    "mass={2:G17} kg, tensor=[{3:G17}, {4:G17}, {5:G17}; " +
                    "{6:G17}, {7:G17}, {8:G17}; {9:G17}, {10:G17}, {11:G17}] kg*m^2",
                    link.Name,
                    error,
                    link.Inertial.Mass.Value,
                    urdfMoment[0],
                    urdfMoment[1],
                    urdfMoment[2],
                    urdfMoment[3],
                    urdfMoment[4],
                    urdfMoment[5],
                    urdfMoment[6],
                    urdfMoment[7],
                    urdfMoment[8]));
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

        internal static void EnsureNoBlockingInertialFailures(
            IEnumerable<InertialValidationRecord> records)
        {
            if (records == null)
            {
                throw new ArgumentNullException("records");
            }

            string[] failures = records
                .Where(record => record != null &&
                    record.Row != null &&
                    !record.Row.Passed)
                .Select(record => String.Format(
                    CultureInfo.InvariantCulture,
                    "{0} ({1})",
                    record.LinkName,
                    record.Row.Quantity))
                .Where(value => !String.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (failures.Length == 0)
            {
                return;
            }

            throw new InvalidOperationException(ChineseUiText.Translate(
                "Export stopped because inertial validation failed: " +
                String.Join(", ", failures) +
                ". Check the selected components, Link coordinate systems, and validation CSV.",
                "\u5bfc\u51fa\u5df2\u505c\u6b62\uff1a\u60ef\u6027\u6821\u9a8c\u5931\u8d25\uff1a" +
                String.Join(", ", failures) +
                "\u3002\u8bf7\u68c0\u67e5\u7ec4\u4ef6\u9009\u62e9\u3001Link \u5750\u6807\u7cfb\u548c\u6821\u9a8c CSV\u3002"));
        }

        internal void RecomputeLinkCoordinateSystem(
            LinkNode node,
            string coordinateSystemName)
        {
            if (node == null || node.Link == null)
            {
                throw new ArgumentNullException("node");
            }

            if (string.IsNullOrWhiteSpace(coordinateSystemName))
            {
                throw new ArgumentException(
                    "A Link coordinate system must be selected.",
                    "coordinateSystemName");
            }

            MathTransform selectedFrame = GetCoordinateSystemTransform(
                coordinateSystemName);
            if (selectedFrame == null)
            {
                throw new Exception("Cannot use Link coordinate system " +
                    coordinateSystemName + " because it was not found");
            }

            string previousGlobalCoordinateSystemName =
                assemblyGlobalCoordinateSystemName;
            string previousExportErrorWhy = ExportErrorWhy;
            List<Tuple<Link, Link>> snapshots = new List<Tuple<Link, Link>>
            {
                Tuple.Create(node.Link, SnapshotLinkValues(node.Link))
            };
            snapshots.AddRange(
                node.Nodes
                    .Cast<LinkNode>()
                    .Select(child => Tuple.Create(
                        child.Link,
                        SnapshotLinkValues(child.Link))));

            try
            {
                node.Link.Joint.CoordinateSystemName = coordinateSystemName;
                LinkNode parentNode = node.Parent as LinkNode;
                if (parentNode == null)
                {
                    assemblyGlobalCoordinateSystemName = coordinateSystemName;
                }
                else if (!CreateJoint(parentNode.Link, node.Link))
                {
                    throw new Exception("Could not recompute Joint " + node.Link.Joint.Name +
                        " after changing Link coordinate system");
                }

                ComputeInertialProperties(node.Link);

                foreach (LinkNode childNode in node.Nodes)
                {
                    if (!CreateJoint(node.Link, childNode.Link))
                    {
                        throw new Exception("Could not recompute child Joint " +
                            childNode.Link.Joint.Name + " after changing Link coordinate system");
                    }
                }
            }
            catch
            {
                assemblyGlobalCoordinateSystemName =
                    previousGlobalCoordinateSystemName;
                ExportErrorWhy = previousExportErrorWhy;
                foreach (Tuple<Link, Link> snapshot in snapshots)
                {
                    snapshot.Item1.SetElement(snapshot.Item2);
                }
                throw;
            }
        }

        private static Link SnapshotLinkValues(Link source)
        {
            Link snapshot = new Link();
            snapshot.SetElement(source);
            snapshot.SetSWComponents(source);
            return snapshot;
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
                records.Add(new InertialValidationRecord(
                    link.Name,
                    GetValidationCoordinateSystemName(link),
                    InertialValidationRow.Diagnostic(
                        "validation.completed",
                        "internal",
                        "FAIL",
                        e.Message)));
            }

            foreach (Link child in link.Children)
            {
                LogLinkInertialValidation(child, records);
            }
        }

        private void LogSingleLinkInertialValidation(Link link, List<InertialValidationRecord> records)
        {
            string coordinateSystemName = GetValidationCoordinateSystemName(link);
            List<InertialValidationRow> rows = new List<InertialValidationRow>();

            if (link.SWComponents == null || link.SWComponents.Count == 0)
            {
                logger.Warn("Skipping SolidWorks numeric inertial comparison for link " + link.Name +
                    " because it has no SolidWorks components");
            }
            else
            {
                MathTransform jointTransform = GetCoordinateSystemTransform(coordinateSystemName);
                if (jointTransform == null)
                {
                    logger.Warn("Skipping SolidWorks numeric inertial comparison for link " + link.Name +
                        " because coordinate system " + coordinateSystemName + " was not found");
                }
                else
                {
                    rows.AddRange(BuildSolidWorksInertiaComparisonRows(link, jointTransform));
                }
            }

            rows.AddRange(BuildPhysicalInertiaValidationRows(link));
            rows.Add(BuildCenterOfMassBoundsValidationRow(link));

            foreach (InertialValidationRow row in rows)
            {
                records.Add(new InertialValidationRecord(
                    link.Name,
                    coordinateSystemName,
                    row));
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Inertial validation for link '" + link.Name + "'");
            builder.AppendLine("Coordinate system: " + coordinateSystemName);
            builder.AppendLine("SolidWorks source: MassProperty calculated in the document frame, then COM " +
                "and the COM inertia tensor explicitly transformed to the selected Link coordinate system.");
            builder.AppendLine("Units: mass kg, origin m, inertia kg*m^2. SolidWorks UI equivalent: origin m*1000, inertia kg*m^2*1e6.");
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-35} {1,-10} {2,-8} {3,18} {4,18} {5,18} {6,14} {7,8} {8}",
                "quantity", "check", "unit", "sw_expected", "urdf_value", "abs_error", "rel_error_%", "status", "message"));

            double maxAbsError = 0;
            double maxRelativeErrorPercent = 0;
            bool validationPassed = true;
            bool validationHasWarning = false;
            foreach (InertialValidationRow row in rows)
            {
                if (row.HasNumericComparison)
                {
                    maxAbsError = Math.Max(maxAbsError, Math.Abs(row.AbsoluteError));
                }
                double? relativeErrorPercent = row.RelativeErrorPercent;
                if (relativeErrorPercent.HasValue)
                {
                    maxRelativeErrorPercent = Math.Max(maxRelativeErrorPercent, relativeErrorPercent.Value);
                }
                validationPassed &= row.Passed;
                validationHasWarning |= row.IsWarning;

                builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-35} {1,-10} {2,-8} {3,18} {4,18} {5,18} {6,14} {7,8} {8}",
                    row.Quantity,
                    row.CheckType,
                    row.Unit,
                    FormatValidationNumber(row.SolidWorksExpected),
                    FormatValidationNumber(row.UrdfValue),
                    FormatValidationNumber(row.AbsoluteError),
                    FormatValidationPercent(relativeErrorPercent),
                    row.Status,
                    row.Message));
            }

            builder.AppendLine("Max abs error: " + FormatValidationNumber(maxAbsError) +
                "; max relative error: " + FormatValidationNumber(maxRelativeErrorPercent) + "%");
            builder.AppendLine("Validation result: " +
                (validationPassed ? validationHasWarning ? "WARN" : "PASS" : "FAIL"));
            if (validationPassed && !validationHasWarning)
            {
                logger.Info(builder.ToString());
            }
            else
            {
                logger.Warn(builder.ToString());
            }
        }

        private static string GetValidationCoordinateSystemName(Link link)
        {
            if (link == null || link.Joint == null)
            {
                return "";
            }

            return link.Joint.CoordinateSystemName ?? "";
        }

        private List<InertialValidationRow> BuildSolidWorksInertiaComparisonRows(
            Link link,
            MathTransform jointTransform)
        {
            List<Body2> bodies = GetBodies(link.SWComponents);
            MassPropertySnapshot massProperty = ReadLinkLocalMassProperty(
                bodies,
                jointTransform);
            double[] expectedMoment = ConvertSolidWorksMomentToUrdfConvention(
                massProperty.Moment);

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

            return new List<InertialValidationRow>
            {
                new InertialValidationRow("mass", "kg", massProperty.Mass, link.Inertial.Mass.Value),
                new InertialValidationRow("origin.x", "m", massProperty.CenterOfMass[0], urdfOrigin[0]),
                new InertialValidationRow("origin.y", "m", massProperty.CenterOfMass[1], urdfOrigin[1]),
                new InertialValidationRow("origin.z", "m", massProperty.CenterOfMass[2], urdfOrigin[2]),
                new InertialValidationRow("ixx", "kg*m^2", expectedMoment[0], urdfMoment[0]),
                new InertialValidationRow("ixy", "kg*m^2", expectedMoment[1], urdfMoment[1]),
                new InertialValidationRow("ixz", "kg*m^2", expectedMoment[2], urdfMoment[2]),
                new InertialValidationRow("iyy", "kg*m^2", expectedMoment[3], urdfMoment[3]),
                new InertialValidationRow("iyz", "kg*m^2", expectedMoment[4], urdfMoment[4]),
                new InertialValidationRow("izz", "kg*m^2", expectedMoment[5], urdfMoment[5])
            };
        }

        private InertialValidationRow BuildCenterOfMassBoundsValidationRow(Link link)
        {
            LinkLocalBoundingBox box = CreateLinkLocalBoundingBox(link);
            if (!box.IsUsable)
            {
                return InertialValidationRow.Diagnostic(
                    "origin.within_selected_geometry_bounds",
                    "geometry",
                    "WARN",
                    "Selected component bounds could not be calculated.");
            }

            double[] center = link.Inertial.Origin.GetXYZ();
            const double tolerance = 1e-6;
            bool inside = center[0] >= box.MinX - tolerance &&
                center[0] <= box.MaxX + tolerance &&
                center[1] >= box.MinY - tolerance &&
                center[1] <= box.MaxY + tolerance &&
                center[2] >= box.MinZ - tolerance &&
                center[2] <= box.MaxZ + tolerance;
            return InertialValidationRow.Diagnostic(
                "origin.within_selected_geometry_bounds",
                "geometry",
                inside ? "PASS" : "FAIL",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "COM=({0:G9},{1:G9},{2:G9}); bounds=[{3:G9},{4:G9}]x[{5:G9},{6:G9}]x[{7:G9},{8:G9}] m.",
                    center[0], center[1], center[2],
                    box.MinX, box.MaxX, box.MinY, box.MaxY, box.MinZ, box.MaxZ));
        }

        internal static List<InertialValidationRow> BuildPhysicalInertiaValidationRows(Link link)
        {
            List<InertialValidationRow> rows = new List<InertialValidationRow>();
            if (link == null || link.Inertial == null)
            {
                rows.Add(InertialValidationRow.Diagnostic(
                    "inertial.exists",
                    "physical",
                    "FAIL",
                    "The link has no inertial element."));
                return rows;
            }

            double mass = link.Inertial.Mass == null ? Double.NaN : link.Inertial.Mass.Value;
            bool massPositive = IsFinite(mass) && mass > 0.0;
            rows.Add(InertialValidationRow.Diagnostic(
                "mass.positive",
                "physical",
                massPositive ? "PASS" : "FAIL",
                massPositive
                    ? "Mass is positive and finite."
                    : "Mass must be positive and finite."));
            rows.Add(BuildMassMagnitudeDiagnostic(mass));

            double[] origin = link.Inertial.Origin == null ? null : link.Inertial.Origin.GetXYZ();
            bool originFinite = origin != null && origin.Length == 3 && origin.All(IsFinite);
            rows.Add(InertialValidationRow.Diagnostic(
                "origin.finite",
                "physical",
                originFinite ? "PASS" : "FAIL",
                originFinite
                    ? "COM origin is finite: " + FormatDiagnosticVector(origin)
                    : "COM origin must contain three finite values."));
            rows.Add(BuildOriginMagnitudeDiagnostic(origin));

            double[] moment = link.Inertial.Inertia == null ? null : link.Inertial.Inertia.GetMoment();
            double[] principalMoments;
            string principalError;
            bool principalAvailable = TryGetPrincipalMoments(moment, out principalMoments, out principalError);
            bool positiveDefinite = principalAvailable && principalMoments.All(value => IsFinite(value) && value > 0.0);
            rows.Add(InertialValidationRow.Diagnostic(
                "inertia.positive_definite",
                "physical",
                positiveDefinite ? "PASS" : "FAIL",
                positiveDefinite
                    ? "Principal moments are positive: " + FormatDiagnosticVector(principalMoments)
                    : principalError));

            bool triangleValid = positiveDefinite && PrincipalMomentsSatisfyTriangleInequality(principalMoments);
            rows.Add(InertialValidationRow.Diagnostic(
                "principal_moments.triangle_inequality",
                "physical",
                triangleValid ? "PASS" : "FAIL",
                triangleValid
                    ? "Principal moments satisfy Ix + Iy >= Iz, Ix + Iz >= Iy, Iy + Iz >= Ix."
                    : "Principal moments violate the rigid-body triangle inequality: " +
                      FormatDiagnosticVector(principalMoments)));
            rows.Add(BuildPrincipalMagnitudeDiagnostic(principalMoments, positiveDefinite));
            rows.Add(BuildEllipsoidDisplayDiagnostic(mass, moment, massPositive && positiveDefinite && triangleValid));

            return rows;
        }

        private static InertialValidationRow BuildMassMagnitudeDiagnostic(double mass)
        {
            if (!IsFinite(mass) || mass <= 0.0)
            {
                return InertialValidationRow.Diagnostic(
                    "mass.magnitude",
                    "magnitude",
                    "WARN",
                    "Mass magnitude was not checked because mass is not positive and finite.");
            }

            if (mass < 1e-9 || mass > 1e6)
            {
                return InertialValidationRow.Diagnostic(
                    "mass.magnitude",
                    "magnitude",
                    "WARN",
                    "Mass is outside the expected robotics export range [1e-9, 1e6] kg: " +
                    FormatDiagnosticNumber(mass));
            }

            return InertialValidationRow.Diagnostic(
                "mass.magnitude",
                "magnitude",
                "PASS",
                "Mass magnitude is within the expected robotics export range.");
        }

        private static InertialValidationRow BuildOriginMagnitudeDiagnostic(double[] origin)
        {
            if (origin == null || origin.Length != 3 || !origin.All(IsFinite))
            {
                return InertialValidationRow.Diagnostic(
                    "origin.magnitude",
                    "magnitude",
                    "WARN",
                    "COM magnitude was not checked because origin is invalid.");
            }

            double maxAbs = origin.Select(Math.Abs).Max();
            if (maxAbs > 1000.0)
            {
                return InertialValidationRow.Diagnostic(
                    "origin.magnitude",
                    "magnitude",
                    "WARN",
                    "COM origin is larger than 1000 m from the link frame: " +
                    FormatDiagnosticVector(origin));
            }

            return InertialValidationRow.Diagnostic(
                "origin.magnitude",
                "magnitude",
                "PASS",
                "COM origin magnitude is within the expected range.");
        }

        private static InertialValidationRow BuildPrincipalMagnitudeDiagnostic(
            double[] principalMoments,
            bool positiveDefinite)
        {
            if (!positiveDefinite || principalMoments == null || principalMoments.Length != 3)
            {
                return InertialValidationRow.Diagnostic(
                    "principal_moments.magnitude",
                    "magnitude",
                    "WARN",
                    "Principal moment magnitude was not checked because the inertia tensor is not positive definite.");
            }

            double min = principalMoments.Min();
            double max = principalMoments.Max();
            if (min < 1e-18 || max > 1e6)
            {
                return InertialValidationRow.Diagnostic(
                    "principal_moments.magnitude",
                    "magnitude",
                    "WARN",
                    "Principal moments are outside the expected range [1e-18, 1e6] kg*m^2: " +
                    FormatDiagnosticVector(principalMoments));
            }
            if (max / min > 1e12)
            {
                return InertialValidationRow.Diagnostic(
                    "principal_moments.magnitude",
                    "magnitude",
                    "WARN",
                    "Principal moment ratio is larger than 1e12: " +
                    FormatDiagnosticVector(principalMoments));
            }

            return InertialValidationRow.Diagnostic(
                "principal_moments.magnitude",
                "magnitude",
                "PASS",
                "Principal moment magnitudes are within the expected range.");
        }

        private static InertialValidationRow BuildEllipsoidDisplayDiagnostic(
            double mass,
            double[] moment,
            bool physicalInertiaValid)
        {
            InertiaEllipsoid ellipsoid;
            string error;
            if (InertiaEllipsoid.TryCreate(mass, moment, out ellipsoid, out error))
            {
                return InertialValidationRow.Diagnostic(
                    "ellipsoid.display",
                    "display",
                    "PASS",
                    "Inertia ellipsoid can be displayed.");
            }

            return InertialValidationRow.Diagnostic(
                "ellipsoid.display",
                "display",
                "WARN",
                physicalInertiaValid
                    ? "Ellipsoid display failed although physical checks passed: " + error
                    : "Ellipsoid display is blocked because physical inertia is invalid: " + error);
        }

        private static bool TryGetPrincipalMoments(
            double[] moment,
            out double[] principalMoments,
            out string error)
        {
            principalMoments = new double[0];
            error = "";
            if (moment == null || moment.Length != 9)
            {
                error = "The inertia tensor must contain nine matrix values.";
                return false;
            }
            if (moment.Any(value => !IsFinite(value)))
            {
                error = "The inertia tensor contains a non-finite value.";
                return false;
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
            principalMoments = new double[3];
            for (int i = 0; i < principalMoments.Length; i++)
            {
                if (Math.Abs(decomposition.EigenValues[i].Imaginary) > symmetryTolerance)
                {
                    error = "The inertia tensor produced complex principal moments.";
                    return false;
                }
                principalMoments[i] = decomposition.EigenValues[i].Real;
            }

            Array.Sort(principalMoments);
            error = "Principal moments are not all positive: " + FormatDiagnosticVector(principalMoments);
            return true;
        }

        private static bool PrincipalMomentsSatisfyTriangleInequality(double[] principalMoments)
        {
            if (principalMoments == null || principalMoments.Length != 3)
            {
                return false;
            }

            double[] sorted = (double[])principalMoments.Clone();
            Array.Sort(sorted);
            double tolerance = Math.Max(sorted[2], 1e-18) * 1e-9;
            return sorted[2] <= sorted[0] + sorted[1] + tolerance;
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static string FormatDiagnosticVector(double[] values)
        {
            if (values == null || values.Length == 0)
            {
                return "n/a";
            }

            return String.Join("/", values.Select(FormatDiagnosticNumber));
        }

        private static string FormatDiagnosticNumber(double value)
        {
            return IsFinite(value) ? value.ToString("0.######E+0", CultureInfo.InvariantCulture) : "n/a";
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
                "link,coordinate_system,quantity,unit,solidworks_expected,urdf_value,absolute_error,relative_error_percent,status,check_type,message");
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
                    row.Status,
                    CsvField(row.CheckType),
                    CsvField(row.Message)
                }));
            }

            return builder.ToString();
        }

        private static string FormatCsvNumber(double value)
        {
            if (!IsFinite(value))
            {
                return "";
            }

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
            if (solidWorksMoment == null || solidWorksMoment.Length != 9)
            {
                throw new ArgumentException(
                    "A SolidWorks inertia tensor must contain exactly nine values.",
                    "solidWorksMoment");
            }

            return new double[]
            {
                solidWorksMoment[0],
                solidWorksMoment[1],
                solidWorksMoment[2],
                solidWorksMoment[4],
                solidWorksMoment[5],
                solidWorksMoment[8]
            };
        }

        private static string FormatValidationNumber(double value)
        {
            if (!IsFinite(value))
            {
                return "n/a";
            }

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
                StlExportStats stlStats = null,
                string collisionUrdfReference = null)
            {
                LinkName = linkName;
                CollisionStrategy = collisionStrategy;
                CollisionEffectiveStrategy = collisionEffectiveStrategy;
                CollisionGeometryType = collisionGeometryType;
                CollisionNotes = collisionNotes;
                MeshFormat = meshFormat;
                VisualUri = visualUri;
                CollisionUri = collisionUri;
                CollisionUrdfReference = String.IsNullOrWhiteSpace(collisionUrdfReference)
                    ? collisionUri
                    : collisionUrdfReference;
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

            public string CollisionUrdfReference { get; private set; }

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
            private readonly string manualStatus;

            public InertialValidationRow(string quantity, string unit, double solidWorksExpected, double urdfValue)
                : this(quantity, unit, solidWorksExpected, urdfValue, "numeric", null, "")
            {
            }

            private InertialValidationRow(
                string quantity,
                string unit,
                double solidWorksExpected,
                double urdfValue,
                string checkType,
                string manualStatus,
                string message)
            {
                Quantity = quantity;
                Unit = unit;
                SolidWorksExpected = solidWorksExpected;
                UrdfValue = urdfValue;
                CheckType = String.IsNullOrWhiteSpace(checkType) ? "numeric" : checkType;
                this.manualStatus = NormalizeStatus(manualStatus);
                Message = message ?? "";
            }

            public static InertialValidationRow Diagnostic(
                string quantity,
                string checkType,
                string status,
                string message)
            {
                return new InertialValidationRow(
                    quantity,
                    "",
                    Double.NaN,
                    Double.NaN,
                    checkType,
                    status,
                    message);
            }

            public string Quantity { get; private set; }

            public string Unit { get; private set; }

            public string CheckType { get; private set; }

            public string Message { get; private set; }

            public double SolidWorksExpected { get; private set; }

            public double UrdfValue { get; private set; }

            public bool HasNumericComparison
            {
                get
                {
                    return String.Equals(CheckType, "numeric", StringComparison.Ordinal) &&
                        IsFinite(SolidWorksExpected) &&
                        IsFinite(UrdfValue);
                }
            }

            public double AbsoluteError
            {
                get { return HasNumericComparison ? UrdfValue - SolidWorksExpected : Double.NaN; }
            }

            public double? RelativeErrorPercent
            {
                get
                {
                    if (!HasNumericComparison)
                    {
                        return null;
                    }

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

            public string Status
            {
                get
                {
                    if (!String.IsNullOrWhiteSpace(manualStatus))
                    {
                        return manualStatus;
                    }

                    return NumericPassed ? "PASS" : "FAIL";
                }
            }

            public bool Passed
            {
                get { return !String.Equals(Status, "FAIL", StringComparison.Ordinal); }
            }

            public bool IsWarning
            {
                get { return String.Equals(Status, "WARN", StringComparison.Ordinal); }
            }

            private bool NumericPassed
            {
                get
                {
                    if (!HasNumericComparison)
                    {
                        return false;
                    }

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

            private static string NormalizeStatus(string status)
            {
                if (String.IsNullOrWhiteSpace(status))
                {
                    return null;
                }

                string normalized = status.Trim().ToUpperInvariant();
                if (normalized == "PASS" || normalized == "WARN" || normalized == "FAIL")
                {
                    return normalized;
                }

                throw new ArgumentException("Unsupported inertial validation status: " + status);
            }
        }

        private MassPropertySnapshot ReadLinkLocalMassProperty(
            IList<Body2> bodies,
            MathTransform linkFrameToDocument)
        {
            MassPropertySnapshot documentSnapshot = ReadDocumentFrameMassProperty(
                ActiveSWModel,
                bodies);
            return MassPropertyFrameConverter.Convert(
                documentSnapshot,
                Matrix<double>.Build.DenseIdentity(4),
                MathOps.GetTransformation(linkFrameToDocument));
        }

        private static MassProperty CreateDocumentFrameMassProperty(
            ModelDoc2 model,
            IList<Body2> bodies)
        {
            MassProperty swMass = CreateSystemUnitMassProperty(model);
            if (bodies != null)
            {
                if (bodies.Count == 0)
                {
                    throw new Exception(
                        "Cannot compute mass properties because no solid bodies were found");
                }
                DispatchWrapper[] dispatchBodies = bodies
                    .Select(body => new DispatchWrapper(body))
                    .ToArray();
                if (!swMass.AddBodies(dispatchBodies))
                {
                    throw new Exception("Failed to add bodies to the mass-property object");
                }
            }

            return swMass;
        }

        private static MassPropertySnapshot ReadDocumentFrameMassProperty(
            ModelDoc2 model,
            IList<Body2> bodies)
        {
            // SW2023 invalidates one cached result depending on whether CenterOfMass or
            // GetMomentOfInertia is read first. Keep those reads on separate COM objects.
            // SolidWorks owns their COM lifetime; explicitly releasing either RCW can terminate
            // the host after several Link queries.
            MassProperty centerMassProperty = CreateDocumentFrameMassProperty(model, bodies);
            double[] centerOfMass = (double[])centerMassProperty.CenterOfMass;
            double mass = centerMassProperty.Mass;

            MassProperty inertiaMassProperty = CreateDocumentFrameMassProperty(model, bodies);
            double[] moment = (double[])inertiaMassProperty.GetMomentOfInertia(
                (int)swMassPropertyMoment_e.swMassPropertyMomentAboutCenterOfMass);

            return new MassPropertySnapshot(mass, centerOfMass, moment);
        }

        private static MassProperty CreateSystemUnitMassProperty(ModelDoc2 model)
        {
            if (model == null)
            {
                throw new ArgumentNullException("model");
            }

            MassProperty swMass = model.Extension.CreateMassProperty();
            if (swMass == null)
            {
                throw new Exception("SolidWorks could not create a mass-property object");
            }

            // Make the API contract explicit instead of depending on its default value.
            // URDF requires meters, kilograms, and kg*m^2.
            swMass.UseSystemUnits = true;
            return swMass;
        }

        private static void ApplyMassPropertyToLink(
            Link link,
            MassPropertySnapshot massProperty)
        {
            if (link == null)
            {
                throw new ArgumentNullException("link");
            }

            link.Inertial.Mass.Value = massProperty.Mass;
            link.Inertial.Origin.SetXYZ(massProperty.CenterOfMass);
            link.Inertial.Origin.SetRPY(new double[] { 0, 0, 0 });
            link.Inertial.Inertia.SetSolidWorksMomentMatrix(massProperty.Moment);
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
                bool success = CreateJoint(parent, node.Link);
                ApplyJointComputationResult(node.Link, success);
                if (!success)
                {
                    logger.Warn(
                        string.Format("Creating joint from parent {0} to child {1} failed", 
                            parent.Name, node.Link.Name));
                }
            }
            else if (parent != null && ComputeJointLimits)
            {
                node.Link.JointLimitsDirty =
                    !ComputeJointLimitsFromComponents(parent, node.Link);
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

        internal static void ApplyJointComputationResult(Link link, bool success)
        {
            if (link == null)
            {
                throw new ArgumentNullException("link");
            }

            link.JointKinematicsDirty = !success;
            if (!success)
            {
                link.JointLimitsDirty = true;
            }
        }

        private List<Body2> GetBodies(List<Component2> components)
        {
            List<Body2> bodies = new List<Body2>();
            HashSet<string> visitedComponents =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Component2 comp in components)
            {
                AddComponentBodies(comp, visitedComponents, bodies);
            }
            return bodies;
        }

        private void AddComponentBodies(
            Component2 component,
            ISet<string> visitedComponents,
            ICollection<Body2> bodies)
        {
            if (component == null)
            {
                return;
            }

            string componentPath = component.Name2;
            if (string.IsNullOrWhiteSpace(componentPath))
            {
                componentPath = "<component-id:" + component.GetID() + ">";
            }
            if (!visitedComponents.Add(componentPath))
            {
                logger.Warn("Skipping duplicate mass-property component: " + componentPath);
                return;
            }

            object[] componentBodies =
                (object[])component.GetBodies3((int)swBodyType_e.swSolidBody, out _);
            if (componentBodies != null)
            {
                foreach (Body2 body in componentBodies)
                {
                    if (body != null)
                    {
                        bodies.Add(body);
                    }
                }
            }

            object[] children = component.GetChildren();
            if (children == null)
            {
                return;
            }
            foreach (Component2 child in children)
            {
                AddComponentBodies(child, visitedComponents, bodies);
            }
        }

        #endregion SW to Robot and link methods

        #region Joint methods

        //Base method for constructing a joint from a parent link and child link.
        private bool CreateJoint(Link parent, Link child)
        {
            CheckRefGeometryExists(child);

            child.Joint.Parent.Name = parent.Name;
            child.Joint.Child.Name = child.Name;
            string jointType = child.isFixedFrame
                ? "fixed"
                : JointConfigurationPolicy.Normalize(child.Joint.Type);
            JointConfigurationPolicy.Apply(child.Joint, jointType);

            string coordSysName = child.Joint.CoordinateSystemName;
            string axisName = child.Joint.AxisName;
            if (child.isFixedFrame)
            {
                axisName = "";
                child.Joint.AxisName = "";
            }
            else if (coordSysName == "Automatically Generate" ||
                (JointConfigurationPolicy.RequiresMotionAxis(jointType) &&
                 axisName == "Automatically Generate") ||
                jointType == Joint.AutomaticallyDetectType)
            {
                // We have to estimate the joint if the user specifies automatic for either the
                // reference coordinate system, the reference axis or the joint type.
                if (!EstimateGlobalJointFromComponents(parent, child))
                {
                    ExportErrorWhy = string.Format("Inferring the joint geometry failed for the joint {0} " +
                        "from link {1} to {2} failed. Check that the mates have not fully defined the " +
                        "components in link {1} and that there is exactly one degree of freedom.",
                        child.Joint.Name, child.Name, parent.Name);
                    return false;
                }
                JointConfigurationPolicy.Apply(child.Joint, child.Joint.Type);
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

            if (axisName == "Automatically Generate" &&
                JointConfigurationPolicy.RequiresMotionAxis(child.Joint.Type))
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
                CreateRefAxis(child.Joint);
            }
            else if (!JointConfigurationPolicy.RequiresMotionAxis(child.Joint.Type))
            {
                child.Joint.AxisName = string.Empty;
            }

            if (!EstimateGlobalJointFromRefGeometry(child))
            {
                return false;
            }

            coordSysName = parent.Joint.CoordinateSystemName;

            if (!LocalizeJoint(child.Joint, coordSysName))
            {
                return false;
            }
            if (ComputeJointLimits)
            {
                child.JointLimitsDirty = !ComputeJointLimitsFromComponents(parent, child);
            }
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
        private bool LocalizeJoint(Joint Joint, string parentCoordsysName)
        {
            MathTransform parentTransform = GetCoordinateSystemTransform(parentCoordsysName);
            if (parentTransform == null)
            {
                logger.Warn("Parent coordinate system could not be resolved: " + parentCoordsysName);
                return false;
            }
            
            Matrix<double> ParentJointGlobalTransform =
                MathOps.GetTransformation(parentTransform);
            MathTransform coordsysTransform =
                GetCoordinateSystemTransform(Joint.CoordinateSystemName);
            if (coordsysTransform == null)
            {
                logger.Warn("Joint coordinate system could not be resolved: " +
                    Joint.CoordinateSystemName);
                return false;
            }
           
            //Transform from global origin to child joint
            Matrix<double> ChildJointGlobalTransform =
                MathOps.GetTransformation(coordsysTransform);
            Matrix<double> ChildJointOrigin =
                ParentJointGlobalTransform.Inverse() * ChildJointGlobalTransform;
            
            if (JointConfigurationPolicy.RequiresMotionAxis(Joint.Type))
            {
                if (!Joint.Axis.HasValidDirection())
                {
                    logger.Warn("Joint axis is missing or invalid for joint " + Joint.Name);
                    return false;
                }
                Joint.Axis.SetXYZ(LocalizeAxis(Joint.Axis.GetXYZ(), coordsysTransform));
                if (!Joint.Axis.HasValidDirection())
                {
                    logger.Warn("Localized joint axis is invalid for joint " + Joint.Name);
                    return false;
                }
            }

            // Get the array values and threshold them so small values are set to 0.
            Joint.Origin.SetXYZ(MathOps.GetXYZ(ChildJointOrigin));
            Joint.Origin.SetXYZ(MathOps.Threshold(Joint.Origin.GetXYZ(), 0.00001));
            Joint.Origin.SetRPY(MathOps.GetRPY(ChildJointOrigin));
            Joint.Origin.SetRPY(MathOps.Threshold(Joint.Origin.GetRPY(), 0.00001));
            return true;
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
            if (child.SWMainComponent == null || child.SWMainComponent.Transform2 == null)
            {
                return false;
            }

            string configuredType = JointConfigurationPolicy.Normalize(child.Joint.Type);
            if (configuredType == "fixed" || configuredType == "floating")
            {
                JointConfigurationPolicy.Apply(child.Joint, configuredType);
                child.Joint.Origin.SetXYZ(MathOps.Threshold(
                    MathOps.GetXYZ(child.SWMainComponent.Transform2),
                    0.00001));
                child.Joint.Origin.SetRPY(MathOps.Threshold(
                    MathOps.GetRPY(child.SWMainComponent.Transform2),
                    0.00001));
                return true;
            }

            //Create the ref objects
            List<Component2> fixedComponents = new List<Component2>();
            List<LimitMateSuppressionState> limitMates =
                new List<LimitMateSuppressionState>();
            Boolean success = false;
            Exception operationFailure = null;
            try
            {
                // Fix parent components so that only the actual degree of freedom can be detected.
                fixedComponents = FixComponents(parent);

                // Suppress limit mates to properly find degrees of freedom. They don't work with the API call.
                if (child.SWMainComponent != null)
                {
                    limitMates = SuppressLimitMates(child.SWMainComponent);
                }

                // The wonderful undocumented API call I found to get the degrees of freedom in a joint.
                // https://forum.solidworks.com/thread/57414
                int apiResult =
                    child.SWMainComponent.GetRemainingDOFs(
                        out int R1Status, out MathPoint RPoint1, out int R1DirStatus, out MathVector RDir1,
                        out int R2Status, out MathPoint RPoint2, out int R2DirStatus, out MathVector RDir2,
                        out int L1Status, out MathVector LDir1,
                        out int L2Status, out MathVector LDir2);
                if (RPoint1 != null && RDir1 != null)
                {
                    logger.Info("R1: " + R1Status + ", " + RPoint1 + ", " + R1DirStatus + ", " + RDir1.ArrayData);
                }
                else
                {
                    logger.Info("R1: " + R1Status + ", " + R1DirStatus);
                }

                if (RPoint2 != null && RDir2 != null)
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
                    logger.Info("L2: " + L2Status + ", " + LDir2.ArrayData);
                }
                else
                {
                    logger.Info("L2: " + L2Status);
                }

                string inferredType = null;
                if (!JointConfigurationPolicy.TryClassifyDetectedType(
                        apiResult,
                        R1Status,
                        R2Status,
                        L1Status,
                        L2Status,
                        out inferredType) ||
                    !JointConfigurationPolicy.IsDetectedTypeCompatible(
                        configuredType,
                        inferredType))
                {
                    logger.Warn(string.Format(
                        "Joint DOF inference rejected for {0}: result={1}, R1={2}, R2={3}, L1={4}, L2={5}, configured={6}.",
                        child.Joint.Name,
                        apiResult,
                        R1Status,
                        R2Status,
                        L1Status,
                        L2Status,
                        configuredType));
                    return false;
                }

                JointConfigurationPolicy.Apply(child.Joint,
                    JointConfigurationPolicy.ResolveDetectedType(configuredType, inferredType));
                child.Joint.Origin.SetXYZ(MathOps.GetXYZ(child.SWMainComponent.Transform2));
                child.Joint.Origin.SetRPY(MathOps.GetRPY(child.SWMainComponent.Transform2));

                if (!JointConfigurationPolicy.RequiresMotionAxis(child.Joint.Type))
                {
                    success = true;
                }
                else if (inferredType == "continuous" && R1DirStatus == 1 &&
                    RDir1 != null && RPoint1 != null)
                {
                    child.Joint.Axis.SetXYZ(RDir1.ArrayData);
                    child.Joint.Origin.SetXYZ(RPoint1.ArrayData);
                    child.Joint.Origin.SetRPY(MathOps.GetRPY(child.SWMainComponent.Transform2));
                    success = MoveOrigin(parent, child);
                }
                else if (inferredType == "prismatic" && LDir1 != null)
                {
                    child.Joint.Axis.SetXYZ(LDir1.ArrayData);
                    child.Joint.Origin.SetXYZ(MathOps.GetXYZ(child.SWMainComponent.Transform2));
                    child.Joint.Origin.SetRPY(MathOps.GetRPY(child.SWMainComponent.Transform2));
                    success = MoveOrigin(parent, child);
                }
                child.Joint.Origin.SetXYZ(MathOps.Threshold(child.Joint.Origin.GetXYZ(), 0.00001));
                child.Joint.Origin.SetRPY(MathOps.Threshold(child.Joint.Origin.GetRPY(), 0.00001));
                return success;
            }
            catch (Exception exception)
            {
                operationFailure = exception;
                throw;
            }
            finally
            {
                Exception cleanupFailure = RestoreJointInferenceEnvironment(
                    limitMates,
                    fixedComponents);
                if (cleanupFailure != null)
                {
                    if (operationFailure != null)
                    {
                        logger.Error(
                            "Restoring the SolidWorks assembly after joint inference also failed.",
                            cleanupFailure);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "SolidWorks assembly state could not be restored after joint inference.",
                            cleanupFailure);
                    }
                }
            }
        }

        //This now needs to be able to get the component, and it's associated coordinate system name.
        //Then it needs to transform to the top level assembly (sounds like fun).
        private bool EstimateGlobalJointFromRefGeometry(Link child)
        {
            MathTransform GlobalCoordsysTransform =
                GetCoordinateSystemTransform(child.Joint.CoordinateSystemName);
            if (GlobalCoordsysTransform == null)
            {
                logger.Warn(
                    string.Format("Joint transform for coordinate system {0} could not be computed for joint {1}", 
                        child.Joint.CoordinateSystemName, child.Joint.Name));
                return false;
            }
            child.Joint.Origin.SetXYZ(MathOps.GetXYZ(GlobalCoordsysTransform));
            child.Joint.Origin.SetRPY(MathOps.GetRPY(GlobalCoordsysTransform));
            if (JointConfigurationPolicy.RequiresMotionAxis(child.Joint.Type))
            {
                EstimateAxis(child.Joint);
                if (!child.Joint.Axis.HasValidDirection())
                {
                    logger.Warn(
                        string.Format("Reference axis {0} could not be resolved for joint {1}",
                            child.Joint.AxisName, child.Joint.Name));
                    return false;
                }
            }
            return true;
        }

        // Method to get the SolidWorks MathTransform from a coordinate system. This method can account for
        // coordinate systems that are embedded in subcomponents, and apply the correct transformation to return
        // it to a global transform. It assumes that the coordinate system name is formatted like:
        // "Coordinate System 1 <assy/subassy/comp>" where the full Component2.Name2 is between the <>
        internal MathTransform GetCoordinateSystemTransform(string CoordinateSystemName)
        {
            ModelDoc2 ComponentModel = ActiveSWModel;
            MathTransform ComponentTransform = default;
            if (string.IsNullOrWhiteSpace(CoordinateSystemName))
            {
                return null;
            }
            bool hasComponentQualifier =
                CoordinateSystemName.Contains("<") || CoordinateSystemName.Contains(">");
            if (hasComponentQualifier)
            {
                int indexFirst = CoordinateSystemName.IndexOf('<');
                int indexLast = CoordinateSystemName.IndexOf('>', indexFirst);
                if (indexFirst < 0 || indexLast <= indexFirst)
                {
                    return null;
                }
                string componentStr =
                    CoordinateSystemName.Substring(indexFirst + 1, indexLast - indexFirst - 1);
                string CoordinateSystemNameUnTrimmed = CoordinateSystemName.Substring(0, indexFirst);
                CoordinateSystemName = CoordinateSystemNameUnTrimmed.Trim();
                AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
                object[] components = assy.GetComponents(false);
                bool componentFound = false;
                if (components == null)
                {
                    return null;
                }
                foreach (Component2 comp in components)
                {
                    if (comp.Name2 == componentStr)
                    {
                        ComponentModel = comp.GetModelDoc2();
                        ComponentTransform = comp.Transform2;
                        componentFound = true;
                        break;
                    }
                }
                if (!componentFound)
                {
                    return null;
                }
            }
            if (ComponentModel == null || ComponentModel.Extension == null)
            {
                return null;
            }
            MathTransform LocalCoordsysTransform =
                ComponentModel.Extension.GetCoordinateSystemTransformByName(CoordinateSystemName);
            if (LocalCoordsysTransform == null)
            {
                return null;
            }
            MathTransform GlobalCoordsysTransform = (ComponentTransform == null) ?
                LocalCoordsysTransform : LocalCoordsysTransform.Multiply(ComponentTransform);
            return GlobalCoordsysTransform;
        }

        private bool MoveOrigin(Link parent, Link nonLocalizedChild)
        {
            if (nonLocalizedChild.SWComponents == null ||
                nonLocalizedChild.SWComponents.Count == 0 ||
                !nonLocalizedChild.Joint.Axis.HasValidDirection())
            {
                return false;
            }
            double xMax = Double.MinValue;
            double yMax = Double.MinValue;
            double zMax = Double.MinValue;
            double xMin = Double.MaxValue;
            double yMin = Double.MaxValue;
            double zMin = Double.MaxValue;
            double[] points;

            foreach (Component2 comp in nonLocalizedChild.SWComponents)
            {
                if (comp == null)
                {
                    return false;
                }
                // Returns box as [ XCorner1, YCorner1, ZCorner1, XCorner2, YCorner2, ZCorner2 ]
                points = comp.GetBox(false, false);
                if (points == null || points.Length < 6)
                {
                    return false;
                }
                xMax = MathOps.Max(points[0], points[3], xMax);
                yMax = MathOps.Max(points[1], points[4], yMax);
                zMax = MathOps.Max(points[2], points[5], zMax);
                xMin = MathOps.Min(points[0], points[3], xMin);
                yMin = MathOps.Min(points[1], points[4], yMin);
                zMin = MathOps.Min(points[2], points[5], zMin);
            }
            string coordsys = parent.Joint.CoordinateSystemName;
            MathTransform parentTransform = GetCoordinateSystemTransform(coordsys);
            if (parentTransform == null)
            {
                return false;
            }

            double[] xyzParent = MathOps.GetXYZ(parentTransform);
            double[] xyzJointAxis = nonLocalizedChild.Joint.Axis.GetXYZ();
            double[] xyzOrigin = nonLocalizedChild.Joint.Origin.GetXYZ();
            double[] idealOrigin =
                MathOps.ClosestPointOnLineToPoint(xyzParent, xyzJointAxis, xyzOrigin);

            nonLocalizedChild.Joint.Origin.SetXYZ(
                MathOps.ClosestPointOnLineWithinBox(xMin, xMax, yMin, yMax, zMin, zMax,
                    nonLocalizedChild.Joint.Axis.GetXYZ(), idealOrigin));
            return true;
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
            double[] axisVector = new double[3];
            if (string.IsNullOrWhiteSpace(axisStr))
            {
                return axisVector;
            }

            ModelDoc2 ComponentModel = ActiveSWModel;
            string axisName = axisStr;
            MathTransform ComponentTransform = default;

            bool hasComponentQualifier = axisStr.Contains("<") || axisStr.Contains(">");
            if (hasComponentQualifier)
            {
                int indexFirst = axisStr.IndexOf('<');
                int indexLast = axisStr.IndexOf('>', indexFirst);
                if (indexFirst < 0 || indexLast <= indexFirst)
                {
                    return axisVector;
                }
                string componentStr =
                    axisStr.Substring(indexFirst + 1, indexLast - indexFirst - 1);
                string CoordinateSystemNameUnTrimmed = axisStr.Substring(0, indexFirst);
                axisName = CoordinateSystemNameUnTrimmed.Trim();
                AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
                object[] components = assy.GetComponents(false);
                bool componentFound = false;
                if (components == null)
                {
                    return axisVector;
                }
                foreach (Component2 comp in components)
                {
                    if (comp.Name2 == componentStr)
                    {
                        ComponentModel = comp.GetModelDoc2();
                        ComponentTransform = comp.Transform2;
                        componentFound = true;
                        break;
                    }
                }
                if (!componentFound)
                {
                    return axisVector;
                }
            }
            //Calculate!
            if (ComponentModel == null || ComponentModel.Extension == null ||
                ComponentModel.SelectionManager == null)
            {
                return axisVector;
            }

            bool selected =
                ComponentModel.Extension.SelectByID2(axisName, "AXIS", 0, 0, 0, false, 0, null, 0);
            if (selected)
            {
                Feature feat = ComponentModel.SelectionManager.GetSelectedObject6(1, 0) as Feature;
                RefAxis axis = feat == null ? null : feat.GetSpecificFeature2() as RefAxis;
                if (axis == null)
                {
                    return axisVector;
                }

                // GetRefAxisParams returns {startX, startY, startZ, endX, endY, endZ}
                double[] axisParams = axis.GetRefAxisParams();
                if (axisParams == null || axisParams.Length < 6)
                {
                    return axisVector;
                }
                axisVector[0] = axisParams[0] - axisParams[3];
                axisVector[1] = axisParams[1] - axisParams[4];
                axisVector[2] = axisParams[2] - axisParams[5];
                if (!Axis.IsValidDirection(axisVector))
                {
                    return new double[3];
                }

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
            double[] transformedAxis = (double[])axis.Clone();
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
            foreach (object featureObject in featureObjects)
            {
                Feature feat = featureObject as Feature;
                if (feat == null)
                {
                    logger.Warn("Skipping a SolidWorks feature entry that does not expose IFeature in " +
                        fileName + ".");
                    continue;
                }

                try
                {
                    if (feat.GetTypeName2() == featureName)
                    {
                        features[keyName].Add(feat);
                    }
                }
                catch (COMException exception)
                {
                    logger.Warn("Skipping an unavailable SolidWorks feature entry in " + fileName +
                        ": " + exception.Message);
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
                    foreach (object componentObject in components)
                    {
                        Component2 comp = componentObject as Component2;
                        if (comp == null)
                        {
                            logger.Warn("Skipping an assembly component entry that does not expose IComponent2 in " +
                                fileName + ".");
                            continue;
                        }

                        ModelDoc2 doc;
                        try
                        {
                            doc = comp.GetModelDoc2();
                        }
                        catch (COMException exception)
                        {
                            logger.Warn("Skipping an unavailable assembly component in " + fileName +
                                ": " + exception.Message);
                            continue;
                        }
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

        private bool ComputeJointLimitsFromComponents(Link parent, Link child)
        {
            string jointType = JointConfigurationPolicy.Normalize(child.Joint.Type);
            if (jointType == "fixed" || jointType == "floating" || jointType == "planar")
            {
                JointConfigurationPolicy.Apply(child.Joint, jointType);
                return true;
            }
            if (jointType != "continuous" && jointType != "revolute" &&
                jointType != "prismatic")
            {
                return false;
            }

            JointConfigurationPolicy.PrepareLimitRecomputation(child.Joint);
            if (parent.SWMainComponent == null || child.SWMainComponent == null)
            {
                return jointType == "continuous";
            }

            List<LimitMateSuppressionState> limitMates =
                SuppressLimitMates(child.SWMainComponent);
            Exception operationFailure = null;
            try
            {
                bool applied = AddLimits(
                    child.Joint,
                    limitMates.Select(state => state.Mate).ToList(),
                    parent.SWMainComponent,
                    child.SWMainComponent);
                return applied || jointType == "continuous";
            }
            catch (Exception exception)
            {
                operationFailure = exception;
                throw;
            }
            finally
            {
                Exception cleanupFailure = RestoreJointInferenceEnvironment(
                    limitMates,
                    null);
                if (cleanupFailure != null)
                {
                    if (operationFailure != null)
                    {
                        logger.Error(
                            "Restoring SolidWorks limit mates after limit computation also failed.",
                            cleanupFailure);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "SolidWorks limit mates could not be restored after limit computation.",
                            cleanupFailure);
                    }
                }
            }
        }

        // Adds position bounds from an eligible SolidWorks limit mate.
        private static bool AddLimits(Joint joint, List<Mate2> limitMates,
            Component2 parentComponent, Component2 childComponent)
        {
            logger.Info("Parent SW Component: " + parentComponent.Name2);
            logger.Info("Child SW Component: " + childComponent.Name2);
            List<Mate2> eligibleMates = new List<Mate2>();
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

                if (entities.Any(component =>
                        CommonSwOperations.ComReferencesEqual(component, parentComponent)) &&
                    entities.Any(component =>
                        CommonSwOperations.ComReferencesEqual(component, childComponent)))
                {
                    // [TODO] This assumes the limit mate limits the right degree of freedom,
                    // it really should check that assumption
                    if (((joint.Type == "continuous" || joint.Type == "revolute") &&
                            swMate.Type == (int)swMateType_e.swMateANGLE) ||
                        (joint.Type == "prismatic" && swMate.Type ==
                            (int)swMateType_e.swMateDISTANCE))
                    {
                        eligibleMates.Add(swMate);
                    }
                }
            }

            if (eligibleMates.Count == 0)
            {
                return false;
            }
            if (eligibleMates.Count > 1)
            {
                throw new InvalidOperationException(
                    "Multiple SolidWorks limit mates match the same URDF joint. " +
                    "Select a single limit mate before exporting.");
            }

            Mate2 selectedMate = eligibleMates[0];
            double lower;
            double upper;
            // SolidWorks reports an unflipped mate in the opposite direction to URDF.
            if (!selectedMate.Flipped)
            {
                upper = -selectedMate.MinimumVariation;
                lower = -selectedMate.MaximumVariation;
            }
            else
            {
                upper = selectedMate.MaximumVariation;
                lower = selectedMate.MinimumVariation;
            }
            if (double.IsNaN(lower) || double.IsInfinity(lower) ||
                double.IsNaN(upper) || double.IsInfinity(upper) || lower > upper)
            {
                throw new InvalidOperationException(
                    "The selected SolidWorks limit mate has invalid bounds.");
            }

            if (joint.Type == "continuous")
            {
                JointConfigurationPolicy.Apply(joint, "revolute");
            }
            joint.Limit.Lower = lower;
            joint.Limit.Upper = upper;
            return true;
        }

        // Suppresses limit mates to make it easier to find the free degree of freedom in a joint
        private sealed class LimitMateSuppressionState
        {
            public LimitMateSuppressionState(Mate2 mate, Feature feature)
            {
                Mate = mate;
                Feature = feature;
            }

            public Mate2 Mate { get; private set; }
            public Feature Feature { get; private set; }
        }

        private static List<LimitMateSuppressionState> SuppressLimitMates(
            IComponent2 component)
        {
            List<LimitMateSuppressionState> suppressedMates =
                new List<LimitMateSuppressionState>();

            if (component == null)
            {
                return suppressedMates;
            }

            object[] objs = component.GetMates();
            try
            {
                if (objs == null)
                {
                    return suppressedMates;
                }

                foreach (object obj in objs)
                {
                    Mate2 swMate = obj as Mate2;
                    if (swMate == null ||
                        swMate.MinimumVariation == swMate.MaximumVariation)
                    {
                        continue;
                    }

                    Feature feature = (Feature)swMate;
                    if (ReadSuppressionState(feature.IsSuppressed2(
                        (int)swInConfigurationOpts_e.swThisConfiguration,
                        null)))
                    {
                        continue;
                    }

                    LimitMateSuppressionState state =
                        new LimitMateSuppressionState(swMate, feature);
                    suppressedMates.Add(state);
                    feature.Select(false);
                    if (!feature.SetSuppression2(
                        (int)swFeatureSuppressionAction_e.swSuppressFeature,
                        (int)swInConfigurationOpts_e.swThisConfiguration,
                        null))
                    {
                        throw new InvalidOperationException(
                            "SolidWorks refused to suppress an active limit mate.");
                    }
                }
            }
            catch
            {
                try
                {
                    RestoreLimitMates(suppressedMates);
                }
                catch (Exception restoreException)
                {
                    logger.Error(
                        "Restoring partially suppressed SolidWorks limit mates failed.",
                        restoreException);
                }
                throw;
            }

            return suppressedMates;
        }

        internal static bool ReadSuppressionState(object value)
        {
            if (value is bool)
            {
                return (bool)value;
            }

            Array values = value as Array;
            if (values != null && values.Length > 0 && values.GetValue(0) is bool)
            {
                return (bool)values.GetValue(0);
            }

            throw new InvalidOperationException(
                "SolidWorks returned an unreadable feature suppression state.");
        }

        // Restores only mates that this operation changed from active to suppressed.
        private static void RestoreLimitMates(
            List<LimitMateSuppressionState> limitMates)
        {
            Exception firstFailure = null;
            foreach (LimitMateSuppressionState state in limitMates)
            {
                try
                {
                    if (!state.Feature.SetSuppression2(
                        (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                        (int)swInConfigurationOpts_e.swThisConfiguration,
                        null))
                    {
                        throw new InvalidOperationException(
                            "SolidWorks refused to restore a limit mate.");
                    }
                }
                catch (Exception exception)
                {
                    logger.Error("Restoring a SolidWorks limit mate failed.", exception);
                    if (firstFailure == null)
                    {
                        firstFailure = exception;
                    }
                }
            }

            if (firstFailure != null)
            {
                throw new InvalidOperationException(
                    "One or more SolidWorks limit mates could not be restored.",
                    firstFailure);
            }
        }

        private Exception RestoreJointInferenceEnvironment(
            List<LimitMateSuppressionState> limitMates,
            List<Component2> fixedComponents)
        {
            List<Exception> failures = new List<Exception>();
            try
            {
                RestoreLimitMates(limitMates ?? new List<LimitMateSuppressionState>());
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                UnFixComponents(fixedComponents);
            }
            catch (Exception exception)
            {
                logger.Error("Restoring fixed SolidWorks components failed.", exception);
                failures.Add(exception);
            }

            if (failures.Count == 0)
            {
                return null;
            }
            if (failures.Count == 1)
            {
                return failures[0];
            }
            return new AggregateException(
                "Multiple SolidWorks assembly cleanup operations failed.",
                failures);
        }

        //Unfixes components that were fixed to find the free degree of freedom
        private void UnFixComponents(List<Component2> components)
        {
            if (components == null || components.Count == 0)
            {
                return;
            }
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
            string jointType = link.isFixedFrame
                ? "fixed"
                : JointConfigurationPolicy.Normalize(link.Joint.Type);
            if (Joint.IsAutomaticType(jointType) ||
                JointConfigurationPolicy.RequiresMotionAxis(jointType))
            {
                if (!CheckRefAxisExists(link.Joint.AxisName))
                {
                    link.Joint.AxisName = "Automatically Generate";
                }
            }
            else
            {
                link.Joint.AxisName = string.Empty;
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
            try
            {
                assy.FixComponent();
            }
            catch (Exception exception)
            {
                try
                {
                    UnFixComponents(componentsToUnfix);
                }
                catch (Exception restoreException)
                {
                    logger.Error(
                        "Restoring components after a failed fix operation also failed.",
                        restoreException);
                }
                throw new InvalidOperationException(
                    "SolidWorks could not fix the parent components for joint inference.",
                    exception);
            }
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
