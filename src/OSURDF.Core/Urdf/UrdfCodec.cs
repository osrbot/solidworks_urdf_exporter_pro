using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using OSURDF.Core.Model;

namespace OSURDF.Core.Urdf
{
    public static class UrdfCodec
    {
        public static RobotDocument Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A URDF path is required.", nameof(path));
            }

            string fullPath = Path.GetFullPath(path);
            XDocument document;
            try
            {
                XmlReaderSettings readerSettings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 64L * 1024L * 1024L
                };
                using (XmlReader reader = XmlReader.Create(fullPath, readerSettings))
                {
                    document = XDocument.Load(reader, LoadOptions.SetLineInfo);
                }
            }
            catch (Exception exception) when (
                exception is IOException || exception is XmlException || exception is UnauthorizedAccessException)
            {
                throw new InvalidDataException("URDF could not be read: " + fullPath, exception);
            }
            if (document.Root == null || document.Root.Name.LocalName != "robot")
            {
                throw new InvalidDataException("URDF root element must be <robot>.");
            }

            RobotDocument robot = new RobotDocument
            {
                Name = Attribute(document.Root, "name") ?? string.Empty,
                Metadata = new RobotMetadata
                {
                    Generator = "OSURDF URDF importer",
                    GeneratorVersion = "2",
                    Commit = "unknown",
                    SourceFormat = "urdf",
                    SourceDigest = Sha256File(fullPath)
                }
            };
            IDictionary<string, MaterialDocument> globalMaterials = ReadGlobalMaterials(document.Root);
            foreach (XElement element in document.Root.Elements("link"))
            {
                robot.Links.Add(ReadLink(element, globalMaterials));
            }
            foreach (XElement element in document.Root.Elements("joint"))
            {
                robot.Joints.Add(ReadJoint(element));
            }
            return robot;
        }

        public static void Write(string path, RobotDocument robot, bool portableAssetPaths = true)
        {
            if (robot == null)
            {
                throw new ArgumentNullException(nameof(robot));
            }
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("URDF output path has no parent directory.");
            }
            Directory.CreateDirectory(directory);

            if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("URDF output must not be a symbolic link or reparse point: " + fullPath);
            }

            string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            Exception writeFailure = null;
            try
            {
                WriteDocument(temporaryPath, robot, portableAssetPaths);
                Read(temporaryPath);
                PublishVerifiedFile(temporaryPath, fullPath);
            }
            catch (Exception exception)
            {
                writeFailure = exception;
                throw;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (Exception cleanupFailure) when (
                        writeFailure != null &&
                        (cleanupFailure is IOException || cleanupFailure is UnauthorizedAccessException))
                    {
                        writeFailure.Data["urdfTemporaryCleanup"] = cleanupFailure.Message;
                        writeFailure.Data["urdfTemporaryPath"] = temporaryPath;
                    }
                }
            }
        }

        private static void WriteDocument(string fullPath, RobotDocument robot, bool portableAssetPaths)
        {

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = false
            };
            using (XmlWriter writer = XmlWriter.Create(fullPath, settings))
            {
                writer.WriteStartDocument();
                writer.WriteComment(" Generated by OSURDF v2. Model license is defined by the model owner, not by the exporter. ");
                writer.WriteStartElement("robot");
                writer.WriteAttributeString("name", robot.Name);

                foreach (LinkDocument link in robot.Links)
                {
                    WriteLink(writer, link, portableAssetPaths);
                }
                foreach (JointDocument joint in robot.Joints)
                {
                    WriteJoint(writer, joint);
                }
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
        }

        private static IDictionary<string, MaterialDocument> ReadGlobalMaterials(XElement root)
        {
            Dictionary<string, MaterialDocument> materials = new Dictionary<string, MaterialDocument>(StringComparer.Ordinal);
            foreach (XElement element in root.Elements("material"))
            {
                MaterialDocument material = ReadMaterialDefinition(element);
                if (string.IsNullOrWhiteSpace(material.Name))
                {
                    throw new InvalidDataException("A top-level URDF material requires a name.");
                }
                if (materials.ContainsKey(material.Name))
                {
                    throw new InvalidDataException("Duplicate top-level URDF material name: " + material.Name);
                }
                materials.Add(material.Name, material);
            }
            return materials;
        }

        private static LinkDocument ReadLink(XElement element, IDictionary<string, MaterialDocument> globalMaterials)
        {
            string name = Attribute(element, "name") ?? string.Empty;
            LinkDocument link = new LinkDocument
            {
                Id = StableId.Create("link", name),
                Name = name,
                Source = SourceProvenance.ImportedUrdf()
            };
            XElement inertial = element.Element("inertial");
            if (inertial != null)
            {
                link.Inertial = new InertialDocument
                {
                    Origin = ReadPose(inertial.Element("origin")),
                    Mass = ReadDouble(inertial.Element("mass"), "value") ?? 0.0,
                    Inertia = ReadInertia(inertial.Element("inertia"))
                };
            }
            int visualIndex = 0;
            foreach (XElement visual in element.Elements("visual"))
            {
                link.Visuals.Add(new VisualDocument
                {
                    Name = Attribute(visual, "name") ?? "visual_" + visualIndex.ToString(CultureInfo.InvariantCulture),
                    Origin = ReadPose(visual.Element("origin")),
                    Geometry = ReadGeometry(visual.Element("geometry")),
                    Material = ReadMaterial(visual.Element("material"), globalMaterials)
                });
                visualIndex++;
            }
            int collisionIndex = 0;
            foreach (XElement collision in element.Elements("collision"))
            {
                link.Collisions.Add(new CollisionDocument
                {
                    Name = Attribute(collision, "name") ?? "collision_" + collisionIndex.ToString(CultureInfo.InvariantCulture),
                    Origin = ReadPose(collision.Element("origin")),
                    Geometry = ReadGeometry(collision.Element("geometry"))
                });
                collisionIndex++;
            }
            return link;
        }

        private static JointDocument ReadJoint(XElement element)
        {
            string name = Attribute(element, "name") ?? string.Empty;
            XElement axis = element.Element("axis");
            XElement limit = element.Element("limit");
            XElement dynamics = element.Element("dynamics");
            XElement mimic = element.Element("mimic");
            return new JointDocument
            {
                Id = StableId.Create("joint", name),
                Name = name,
                Type = Attribute(element, "type") ?? string.Empty,
                Parent = Attribute(element.Element("parent"), "link") ?? string.Empty,
                Child = Attribute(element.Element("child"), "link") ?? string.Empty,
                Origin = ReadPose(element.Element("origin")),
                Axis = axis == null ? null : ParseVector3(Attribute(axis, "xyz"), Vector3Document.UnitX()),
                Limit = limit == null ? null : new JointLimitDocument
                {
                    Lower = ReadDouble(limit, "lower"),
                    Upper = ReadDouble(limit, "upper"),
                    Effort = ReadDouble(limit, "effort"),
                    Velocity = ReadDouble(limit, "velocity")
                },
                Dynamics = dynamics == null ? null : new JointDynamicsDocument
                {
                    Damping = ReadDouble(dynamics, "damping"),
                    Friction = ReadDouble(dynamics, "friction")
                },
                Mimic = mimic == null ? null : new MimicDocument
                {
                    Joint = Attribute(mimic, "joint") ?? string.Empty,
                    Multiplier = ReadDouble(mimic, "multiplier"),
                    Offset = ReadDouble(mimic, "offset")
                },
                Source = SourceProvenance.ImportedUrdf()
            };
        }

        private static PoseDocument ReadPose(XElement element)
        {
            if (element == null)
            {
                return PoseDocument.Zero();
            }
            return new PoseDocument
            {
                Xyz = ParseVector3(Attribute(element, "xyz"), Vector3Document.Zero()),
                Rpy = ParseVector3(Attribute(element, "rpy"), Vector3Document.Zero())
            };
        }

        private static InertiaTensorDocument ReadInertia(XElement element)
        {
            if (element == null)
            {
                return new InertiaTensorDocument();
            }
            return new InertiaTensorDocument
            {
                Ixx = ReadDouble(element, "ixx") ?? 0.0,
                Ixy = ReadDouble(element, "ixy") ?? 0.0,
                Ixz = ReadDouble(element, "ixz") ?? 0.0,
                Iyy = ReadDouble(element, "iyy") ?? 0.0,
                Iyz = ReadDouble(element, "iyz") ?? 0.0,
                Izz = ReadDouble(element, "izz") ?? 0.0
            };
        }

        private static GeometryDocument ReadGeometry(XElement geometry)
        {
            if (geometry == null)
            {
                return new GeometryDocument();
            }
            XElement mesh = geometry.Element("mesh");
            if (mesh != null)
            {
                return new GeometryDocument
                {
                    Type = "mesh",
                    Uri = Attribute(mesh, "filename"),
                    Scale = ParseVector3(Attribute(mesh, "scale"), null)
                };
            }
            XElement box = geometry.Element("box");
            if (box != null)
            {
                return new GeometryDocument
                {
                    Type = "box",
                    Size = ParseVector3(Attribute(box, "size"), null)
                };
            }
            XElement cylinder = geometry.Element("cylinder");
            if (cylinder != null)
            {
                return new GeometryDocument
                {
                    Type = "cylinder",
                    Radius = ReadDouble(cylinder, "radius"),
                    Length = ReadDouble(cylinder, "length")
                };
            }
            XElement sphere = geometry.Element("sphere");
            if (sphere != null)
            {
                return new GeometryDocument
                {
                    Type = "sphere",
                    Radius = ReadDouble(sphere, "radius")
                };
            }
            return new GeometryDocument();
        }

        private static MaterialDocument ReadMaterial(
            XElement element,
            IDictionary<string, MaterialDocument> globalMaterials)
        {
            if (element == null)
            {
                return null;
            }
            MaterialDocument material = ReadMaterialDefinition(element);
            MaterialDocument global;
            if (!string.IsNullOrWhiteSpace(material.Name) &&
                globalMaterials != null && globalMaterials.TryGetValue(material.Name, out global))
            {
                if (material.Rgba == null)
                {
                    material.Rgba = CloneVector4(global.Rgba);
                }
                if (string.IsNullOrWhiteSpace(material.TextureUri))
                {
                    material.TextureUri = global.TextureUri;
                }
            }
            return material;
        }

        private static MaterialDocument ReadMaterialDefinition(XElement element)
        {
            XElement color = element.Element("color");
            XElement texture = element.Element("texture");
            return new MaterialDocument
            {
                Name = Attribute(element, "name"),
                Rgba = ParseVector4(Attribute(color, "rgba")),
                TextureUri = Attribute(texture, "filename")
            };
        }

        private static Vector4Document CloneVector4(Vector4Document value)
        {
            return value == null
                ? null
                : new Vector4Document { X = value.X, Y = value.Y, Z = value.Z, W = value.W };
        }

        private static void WriteLink(XmlWriter writer, LinkDocument link, bool portableAssetPaths)
        {
            writer.WriteStartElement("link");
            writer.WriteAttributeString("name", link.Name);
            if (link.Inertial != null)
            {
                writer.WriteStartElement("inertial");
                WritePose(writer, link.Inertial.Origin);
                writer.WriteStartElement("mass");
                writer.WriteAttributeString("value", Format(link.Inertial.Mass));
                writer.WriteEndElement();
                InertiaTensorDocument inertia = link.Inertial.Inertia ?? new InertiaTensorDocument();
                writer.WriteStartElement("inertia");
                writer.WriteAttributeString("ixx", Format(inertia.Ixx));
                writer.WriteAttributeString("ixy", Format(inertia.Ixy));
                writer.WriteAttributeString("ixz", Format(inertia.Ixz));
                writer.WriteAttributeString("iyy", Format(inertia.Iyy));
                writer.WriteAttributeString("iyz", Format(inertia.Iyz));
                writer.WriteAttributeString("izz", Format(inertia.Izz));
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            foreach (VisualDocument visual in link.Visuals ?? Enumerable.Empty<VisualDocument>())
            {
                writer.WriteStartElement("visual");
                WriteOptionalName(writer, visual.Name);
                WritePose(writer, visual.Origin);
                WriteGeometry(writer, visual.Geometry, portableAssetPaths);
                WriteMaterial(writer, visual.Material, portableAssetPaths);
                writer.WriteEndElement();
            }
            foreach (CollisionDocument collision in link.Collisions ?? Enumerable.Empty<CollisionDocument>())
            {
                writer.WriteStartElement("collision");
                WriteOptionalName(writer, collision.Name);
                WritePose(writer, collision.Origin);
                WriteGeometry(writer, collision.Geometry, portableAssetPaths);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        private static void WriteJoint(XmlWriter writer, JointDocument joint)
        {
            writer.WriteStartElement("joint");
            writer.WriteAttributeString("name", joint.Name);
            writer.WriteAttributeString("type", joint.Type);
            writer.WriteStartElement("parent");
            writer.WriteAttributeString("link", joint.Parent);
            writer.WriteEndElement();
            writer.WriteStartElement("child");
            writer.WriteAttributeString("link", joint.Child);
            writer.WriteEndElement();
            WritePose(writer, joint.Origin);

            if (RobotSchema.MovingJointTypes.Contains(joint.Type) && joint.Axis != null)
            {
                Vector3Document axis = Normalize(joint.Axis);
                writer.WriteStartElement("axis");
                writer.WriteAttributeString("xyz", Format(axis));
                writer.WriteEndElement();
            }
            if (joint.Limit != null && joint.Type != "fixed" && joint.Type != "floating" && joint.Type != "planar")
            {
                writer.WriteStartElement("limit");
                WriteOptionalDouble(writer, "lower", joint.Type == "continuous" ? null : joint.Limit.Lower);
                WriteOptionalDouble(writer, "upper", joint.Type == "continuous" ? null : joint.Limit.Upper);
                WriteOptionalDouble(writer, "effort", joint.Limit.Effort);
                WriteOptionalDouble(writer, "velocity", joint.Limit.Velocity);
                writer.WriteEndElement();
            }
            if (joint.Dynamics != null && (joint.Dynamics.Damping.HasValue || joint.Dynamics.Friction.HasValue))
            {
                writer.WriteStartElement("dynamics");
                WriteOptionalDouble(writer, "damping", joint.Dynamics.Damping);
                WriteOptionalDouble(writer, "friction", joint.Dynamics.Friction);
                writer.WriteEndElement();
            }
            if (joint.Mimic != null)
            {
                writer.WriteStartElement("mimic");
                writer.WriteAttributeString("joint", joint.Mimic.Joint);
                WriteOptionalDouble(writer, "multiplier", joint.Mimic.Multiplier);
                WriteOptionalDouble(writer, "offset", joint.Mimic.Offset);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        private static void WritePose(XmlWriter writer, PoseDocument pose)
        {
            pose = pose ?? PoseDocument.Zero();
            writer.WriteStartElement("origin");
            writer.WriteAttributeString("xyz", Format(pose.Xyz ?? Vector3Document.Zero()));
            writer.WriteAttributeString("rpy", Format(pose.Rpy ?? Vector3Document.Zero()));
            writer.WriteEndElement();
        }

        private static void WriteGeometry(XmlWriter writer, GeometryDocument geometry, bool portableAssetPaths)
        {
            writer.WriteStartElement("geometry");
            switch (geometry.Type)
            {
                case "mesh":
                    writer.WriteStartElement("mesh");
                    writer.WriteAttributeString("filename", NormalizeAssetUri(geometry.Uri, portableAssetPaths));
                    if (geometry.Scale != null)
                    {
                        writer.WriteAttributeString("scale", Format(geometry.Scale));
                    }
                    writer.WriteEndElement();
                    break;
                case "box":
                    writer.WriteStartElement("box");
                    writer.WriteAttributeString("size", Format(geometry.Size));
                    writer.WriteEndElement();
                    break;
                case "cylinder":
                    writer.WriteStartElement("cylinder");
                    writer.WriteAttributeString("radius", Format(geometry.Radius ?? 0.0));
                    writer.WriteAttributeString("length", Format(geometry.Length ?? 0.0));
                    writer.WriteEndElement();
                    break;
                case "sphere":
                    writer.WriteStartElement("sphere");
                    writer.WriteAttributeString("radius", Format(geometry.Radius ?? 0.0));
                    writer.WriteEndElement();
                    break;
                default:
                    throw new InvalidDataException("Unsupported geometry type: " + geometry.Type);
            }
            writer.WriteEndElement();
        }

        private static void WriteMaterial(XmlWriter writer, MaterialDocument material, bool portableAssetPaths)
        {
            if (material == null)
            {
                return;
            }
            writer.WriteStartElement("material");
            if (!string.IsNullOrWhiteSpace(material.Name))
            {
                writer.WriteAttributeString("name", material.Name);
            }
            if (material.Rgba != null)
            {
                writer.WriteStartElement("color");
                writer.WriteAttributeString("rgba", Format(material.Rgba));
                writer.WriteEndElement();
            }
            if (!string.IsNullOrWhiteSpace(material.TextureUri))
            {
                writer.WriteStartElement("texture");
                writer.WriteAttributeString("filename", NormalizeAssetUri(material.TextureUri, portableAssetPaths));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        private static Vector3Document ParseVector3(string value, Vector3Document fallback)
        {
            double[] numbers = ParseNumbers(value, 3);
            return numbers == null
                ? fallback
                : new Vector3Document { X = numbers[0], Y = numbers[1], Z = numbers[2] };
        }

        private static Vector4Document ParseVector4(string value)
        {
            double[] numbers = ParseNumbers(value, 4);
            return numbers == null
                ? null
                : new Vector4Document { X = numbers[0], Y = numbers[1], Z = numbers[2], W = numbers[3] };
        }

        private static double[] ParseNumbers(string value, int count)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            string[] fields = value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != count)
            {
                throw new InvalidDataException("Expected " + count + " numbers but found " + fields.Length + ": " + value);
            }
            double[] result = new double[count];
            for (int index = 0; index < count; index++)
            {
                if (!double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out result[index]) ||
                    double.IsNaN(result[index]) || double.IsInfinity(result[index]))
                {
                    throw new InvalidDataException("Invalid numeric value: " + fields[index]);
                }
            }
            return result;
        }

        private static double? ReadDouble(XElement element, string attribute)
        {
            string value = Attribute(element, attribute);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            double result;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                double.IsNaN(result) || double.IsInfinity(result))
            {
                throw new InvalidDataException("Invalid value for " + attribute + ": " + value);
            }
            return result;
        }

        private static string Attribute(XElement element, string name)
        {
            return element?.Attribute(name)?.Value;
        }

        private static void WriteOptionalName(XmlWriter writer, string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                writer.WriteAttributeString("name", name);
            }
        }

        private static void WriteOptionalDouble(XmlWriter writer, string name, double? value)
        {
            if (value.HasValue)
            {
                writer.WriteAttributeString(name, Format(value.Value));
            }
        }

        private static Vector3Document Normalize(Vector3Document vector)
        {
            double magnitude = Math.Sqrt(vector.SquaredMagnitude());
            if (magnitude <= 1e-12)
            {
                return vector;
            }
            return new Vector3Document
            {
                X = vector.X / magnitude,
                Y = vector.Y / magnitude,
                Z = vector.Z / magnitude
            };
        }

        private static string NormalizeAssetUri(string uri, bool portable)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                return uri;
            }
            string normalized = uri.Replace('\\', '/');
            if (portable && normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }
            return normalized;
        }

        private static void PublishVerifiedFile(string temporaryPath, string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                File.Move(temporaryPath, destinationPath);
                return;
            }
            try
            {
                File.Replace(temporaryPath, destinationPath, null);
                return;
            }
            catch (PlatformNotSupportedException)
            {
                // Use the recoverable rename sequence below on filesystems without File.Replace.
            }
            catch (IOException)
            {
                // A portable rename may still work on filesystems that do not implement File.Replace.
            }

            string previousPath = destinationPath + ".previous-" + Guid.NewGuid().ToString("N");
            File.Move(destinationPath, previousPath);
            try
            {
                File.Move(temporaryPath, destinationPath);
                File.Delete(previousPath);
            }
            catch
            {
                if (!File.Exists(destinationPath) && File.Exists(previousPath))
                {
                    File.Move(previousPath, destinationPath);
                }
                throw;
            }
        }

        private static string Format(Vector3Document vector)
        {
            return Format(vector.X) + " " + Format(vector.Y) + " " + Format(vector.Z);
        }

        private static string Format(Vector4Document vector)
        {
            return Format(vector.X) + " " + Format(vector.Y) + " " + Format(vector.Z) + " " + Format(vector.W);
        }

        private static string Format(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        private static string ToHex(byte[] digest)
        {
            StringBuilder builder = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest)
            {
                builder.Append(value.ToString("x2"));
            }
            return builder.ToString();
        }
    }
}
