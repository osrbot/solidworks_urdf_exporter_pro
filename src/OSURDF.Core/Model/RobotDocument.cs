using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace OSURDF.Core.Model
{
    public static class RobotSchema
    {
        public const int CurrentVersion = 3;
        public const string UnitSystem = "SI";

        public static readonly ISet<string> JointTypes = new HashSet<string>(
            new[] { "fixed", "continuous", "revolute", "prismatic", "floating", "planar" },
            StringComparer.Ordinal);

        public static readonly ISet<string> MovingJointTypes = new HashSet<string>(
            new[] { "continuous", "revolute", "prismatic", "planar" },
            StringComparer.Ordinal);
    }

    public sealed class RobotDocument
    {
        [JsonProperty("schemaVersion", Order = 0)]
        public int SchemaVersion { get; set; } = RobotSchema.CurrentVersion;

        [JsonProperty("name", Order = 1)]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("units", Order = 2)]
        public string Units { get; set; } = RobotSchema.UnitSystem;

        [JsonProperty("metadata", Order = 3)]
        public RobotMetadata Metadata { get; set; } = new RobotMetadata();

        [JsonProperty("links", Order = 4)]
        public List<LinkDocument> Links { get; set; } = new List<LinkDocument>();

        [JsonProperty("joints", Order = 5)]
        public List<JointDocument> Joints { get; set; } = new List<JointDocument>();

        [JsonProperty("profiles", Order = 6)]
        public RobotProfiles Profiles { get; set; } = new RobotProfiles();

        public LinkDocument FindLink(string name)
        {
            return (Links ?? new List<LinkDocument>())
                .FirstOrDefault(link => link != null && string.Equals(link.Name, name, StringComparison.Ordinal));
        }

        public JointDocument FindJoint(string name)
        {
            return (Joints ?? new List<JointDocument>())
                .FirstOrDefault(joint => joint != null && string.Equals(joint.Name, name, StringComparison.Ordinal));
        }
    }

    public sealed class RobotMetadata
    {
        [JsonProperty("generator", Order = 0)]
        public string Generator { get; set; } = "OSURDF";

        [JsonProperty("generatorVersion", Order = 1)]
        public string GeneratorVersion { get; set; } = "unknown";

        [JsonProperty("commit", Order = 2)]
        public string Commit { get; set; } = "unknown";

        [JsonProperty("sourceFormat", Order = 3)]
        public string SourceFormat { get; set; } = "unknown";

        [JsonProperty("sourceDigest", Order = 4)]
        public string SourceDigest { get; set; }

        [JsonProperty("modelLicense", Order = 5)]
        public string ModelLicense { get; set; }

        [JsonProperty("modelAuthor", Order = 6)]
        public string ModelAuthor { get; set; }
    }

    public sealed class LinkDocument
    {
        [JsonProperty("id", Order = 0)]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name", Order = 1)]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("inertial", Order = 2)]
        public InertialDocument Inertial { get; set; }

        [JsonProperty("visuals", Order = 3)]
        public List<VisualDocument> Visuals { get; set; } = new List<VisualDocument>();

        [JsonProperty("collisions", Order = 4)]
        public List<CollisionDocument> Collisions { get; set; } = new List<CollisionDocument>();

        [JsonProperty("source", Order = 5)]
        public SourceProvenance Source { get; set; } = SourceProvenance.Unknown();
    }

    public sealed class JointDocument
    {
        [JsonProperty("id", Order = 0)]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name", Order = 1)]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("type", Order = 2)]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("parent", Order = 3)]
        public string Parent { get; set; } = string.Empty;

        [JsonProperty("child", Order = 4)]
        public string Child { get; set; } = string.Empty;

        [JsonProperty("origin", Order = 5)]
        public PoseDocument Origin { get; set; } = PoseDocument.Zero();

        [JsonProperty("axis", Order = 6)]
        public Vector3Document Axis { get; set; }

        [JsonProperty("limit", Order = 7)]
        public JointLimitDocument Limit { get; set; }

        [JsonProperty("dynamics", Order = 8)]
        public JointDynamicsDocument Dynamics { get; set; }

        [JsonProperty("mimic", Order = 9)]
        public MimicDocument Mimic { get; set; }

        [JsonProperty("source", Order = 10)]
        public SourceProvenance Source { get; set; } = SourceProvenance.Unknown();
    }

    public sealed class InertialDocument
    {
        [JsonProperty("origin", Order = 0)]
        public PoseDocument Origin { get; set; } = PoseDocument.Zero();

        [JsonProperty("mass", Order = 1)]
        public double Mass { get; set; }

        [JsonProperty("inertia", Order = 2)]
        public InertiaTensorDocument Inertia { get; set; } = new InertiaTensorDocument();
    }

    public sealed class InertiaTensorDocument
    {
        [JsonProperty("ixx", Order = 0)] public double Ixx { get; set; }
        [JsonProperty("ixy", Order = 1)] public double Ixy { get; set; }
        [JsonProperty("ixz", Order = 2)] public double Ixz { get; set; }
        [JsonProperty("iyy", Order = 3)] public double Iyy { get; set; }
        [JsonProperty("iyz", Order = 4)] public double Iyz { get; set; }
        [JsonProperty("izz", Order = 5)] public double Izz { get; set; }
    }

    public abstract class GeometryInstanceDocument
    {
        [JsonProperty("name", Order = 0)]
        public string Name { get; set; }

        [JsonProperty("origin", Order = 1)]
        public PoseDocument Origin { get; set; } = PoseDocument.Zero();

        [JsonProperty("geometry", Order = 2)]
        public GeometryDocument Geometry { get; set; } = new GeometryDocument();
    }

    public sealed class VisualDocument : GeometryInstanceDocument
    {
        [JsonProperty("material", Order = 3)]
        public MaterialDocument Material { get; set; }
    }

    public sealed class CollisionDocument : GeometryInstanceDocument
    {
    }

    public sealed class GeometryDocument
    {
        [JsonProperty("type", Order = 0)]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("uri", Order = 1)]
        public string Uri { get; set; }

        [JsonProperty("scale", Order = 2)]
        public Vector3Document Scale { get; set; }

        [JsonProperty("size", Order = 3)]
        public Vector3Document Size { get; set; }

        [JsonProperty("radius", Order = 4)]
        public double? Radius { get; set; }

        [JsonProperty("length", Order = 5)]
        public double? Length { get; set; }
    }

    public sealed class MaterialDocument
    {
        [JsonProperty("name", Order = 0)]
        public string Name { get; set; }

        [JsonProperty("rgba", Order = 1)]
        public Vector4Document Rgba { get; set; }

        [JsonProperty("textureUri", Order = 2)]
        public string TextureUri { get; set; }
    }

    public sealed class JointLimitDocument
    {
        [JsonProperty("lower", Order = 0)] public double? Lower { get; set; }
        [JsonProperty("upper", Order = 1)] public double? Upper { get; set; }
        [JsonProperty("effort", Order = 2)] public double? Effort { get; set; }
        [JsonProperty("velocity", Order = 3)] public double? Velocity { get; set; }
    }

    public sealed class JointDynamicsDocument
    {
        [JsonProperty("damping", Order = 0)] public double? Damping { get; set; }
        [JsonProperty("friction", Order = 1)] public double? Friction { get; set; }
    }

    public sealed class MimicDocument
    {
        [JsonProperty("joint", Order = 0)] public string Joint { get; set; } = string.Empty;
        [JsonProperty("multiplier", Order = 1)] public double? Multiplier { get; set; }
        [JsonProperty("offset", Order = 2)] public double? Offset { get; set; }
    }

    public sealed class PoseDocument
    {
        [JsonProperty("xyz", Order = 0)]
        public Vector3Document Xyz { get; set; } = Vector3Document.Zero();

        [JsonProperty("rpy", Order = 1)]
        public Vector3Document Rpy { get; set; } = Vector3Document.Zero();

        public static PoseDocument Zero()
        {
            return new PoseDocument();
        }
    }

    public sealed class Vector3Document
    {
        [JsonProperty("x", Order = 0)] public double X { get; set; }
        [JsonProperty("y", Order = 1)] public double Y { get; set; }
        [JsonProperty("z", Order = 2)] public double Z { get; set; }

        public static Vector3Document Zero()
        {
            return new Vector3Document();
        }

        public static Vector3Document UnitX()
        {
            return new Vector3Document { X = 1.0 };
        }

        public double SquaredMagnitude()
        {
            return X * X + Y * Y + Z * Z;
        }
    }

    public sealed class Vector4Document
    {
        [JsonProperty("x", Order = 0)] public double X { get; set; }
        [JsonProperty("y", Order = 1)] public double Y { get; set; }
        [JsonProperty("z", Order = 2)] public double Z { get; set; }
        [JsonProperty("w", Order = 3)] public double W { get; set; }
    }

    public sealed class QuaternionWxyzDocument
    {
        [JsonProperty("w", Order = 0)] public double W { get; set; } = 1.0;
        [JsonProperty("x", Order = 1)] public double X { get; set; }
        [JsonProperty("y", Order = 2)] public double Y { get; set; }
        [JsonProperty("z", Order = 3)] public double Z { get; set; }
    }

    public sealed class SourceProvenance
    {
        [JsonProperty("kind", Order = 0)]
        public string Kind { get; set; } = "unknown";

        [JsonProperty("evidence", Order = 1)]
        public string Evidence { get; set; }

        [JsonProperty("reference", Order = 2)]
        public string Reference { get; set; }

        [JsonProperty("userConfirmed", Order = 3)]
        public bool UserConfirmed { get; set; }

        public static SourceProvenance Unknown()
        {
            return new SourceProvenance();
        }

        public static SourceProvenance ImportedUrdf()
        {
            return new SourceProvenance
            {
                Kind = "imported_urdf",
                Evidence = "Parsed from an existing URDF document.",
                UserConfirmed = false
            };
        }
    }

    public static class StableId
    {
        public static string Create(string kind, string identity)
        {
            string input = (kind ?? string.Empty) + "\n" + (identity ?? string.Empty);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder builder = new StringBuilder(kind ?? "item");
                builder.Append('-');
                for (int index = 0; index < 10; index++)
                {
                    builder.Append(digest[index].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
