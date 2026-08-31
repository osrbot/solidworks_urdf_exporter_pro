using System;
using System.Linq;
using System.Runtime.Serialization;

namespace SW2URDF.URDFExport
{
    internal static class ReferenceGeometryFeatureTypeNames
    {
        public const string CoordinateSystem = "CoordSys";
        public const string Axis = "RefAxis";
    }

    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public enum ReferenceGeometryKind
    {
        [EnumMember]
        CoordinateSystem,

        [EnumMember]
        Axis
    }

    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public enum ReferenceSelectionMode
    {
        [EnumMember]
        Explicit,

        [EnumMember]
        Automatic,

        [EnumMember]
        None
    }

    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public enum ReferenceGeometryOwnerScope
    {
        [EnumMember]
        Unspecified,

        [EnumMember]
        RootDocument,

        [EnumMember]
        ComponentInstance
    }

    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public sealed class CadFeatureReference : IEquatable<CadFeatureReference>
    {
        [DataMember(Order = 1, IsRequired = true)]
        public ReferenceSelectionMode Mode { get; private set; }

        [DataMember(Order = 2, IsRequired = true)]
        public ReferenceGeometryKind Kind { get; private set; }

        [DataMember(Order = 3, IsRequired = true)]
        public ReferenceGeometryOwnerScope OwnerScope { get; private set; }

        [DataMember(Order = 4, EmitDefaultValue = false, Name = "ComponentPersistentId")]
        private byte[] componentPersistentId;

        [DataMember(Order = 5, EmitDefaultValue = false, Name = "FeaturePersistentId")]
        private byte[] featurePersistentId;

        [DataMember(Order = 6, EmitDefaultValue = false)]
        public string ReferencedConfiguration { get; private set; }

        private CadFeatureReference()
        {
            ReferencedConfiguration = string.Empty;
        }

        private CadFeatureReference(
            ReferenceSelectionMode mode,
            ReferenceGeometryKind kind,
            ReferenceGeometryOwnerScope ownerScope,
            byte[] componentPersistentId,
            byte[] featurePersistentId,
            string referencedConfiguration)
        {
            if (!HasValidShape(
                    mode,
                    ownerScope,
                    componentPersistentId,
                    featurePersistentId))
            {
                throw new ArgumentException(
                    "The CAD reference owner scope and persistent IDs are inconsistent.");
            }

            Mode = mode;
            Kind = kind;
            OwnerScope = ownerScope;
            this.componentPersistentId = CloneBytes(componentPersistentId);
            this.featurePersistentId = CloneBytes(featurePersistentId);
            ReferencedConfiguration = referencedConfiguration ?? string.Empty;
        }

        public bool IsExplicit
        {
            get { return Mode == ReferenceSelectionMode.Explicit; }
        }

        public byte[] ComponentPersistentId
        {
            get { return CloneBytes(componentPersistentId); }
        }

        public byte[] FeaturePersistentId
        {
            get { return CloneBytes(featurePersistentId); }
        }

        public static CadFeatureReference ExplicitRoot(
            ReferenceGeometryKind kind,
            byte[] featurePersistentId,
            string referencedConfiguration = "")
        {
            return new CadFeatureReference(
                ReferenceSelectionMode.Explicit,
                kind,
                ReferenceGeometryOwnerScope.RootDocument,
                null,
                featurePersistentId,
                referencedConfiguration);
        }

        public static CadFeatureReference ExplicitComponent(
            ReferenceGeometryKind kind,
            byte[] componentPersistentId,
            byte[] featurePersistentId,
            string referencedConfiguration)
        {
            return new CadFeatureReference(
                ReferenceSelectionMode.Explicit,
                kind,
                ReferenceGeometryOwnerScope.ComponentInstance,
                componentPersistentId,
                featurePersistentId,
                referencedConfiguration);
        }

        public static CadFeatureReference Automatic(ReferenceGeometryKind kind)
        {
            return new CadFeatureReference(
                ReferenceSelectionMode.Automatic,
                kind,
                ReferenceGeometryOwnerScope.Unspecified,
                null,
                null,
                string.Empty);
        }

        public static CadFeatureReference None(ReferenceGeometryKind kind)
        {
            return new CadFeatureReference(
                ReferenceSelectionMode.None,
                kind,
                ReferenceGeometryOwnerScope.Unspecified,
                null,
                null,
                string.Empty);
        }

        public CadFeatureReference Clone()
        {
            return new CadFeatureReference(
                Mode,
                Kind,
                OwnerScope,
                componentPersistentId,
                featurePersistentId,
                ReferencedConfiguration);
        }

        public bool Equals(CadFeatureReference other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            return Mode == other.Mode &&
                Kind == other.Kind &&
                OwnerScope == other.OwnerScope &&
                ByteArraysEqual(componentPersistentId, other.componentPersistentId) &&
                ByteArraysEqual(featurePersistentId, other.featurePersistentId);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as CadFeatureReference);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Mode.GetHashCode();
                hash = hash * 31 + Kind.GetHashCode();
                hash = hash * 31 + OwnerScope.GetHashCode();
                hash = hash * 31 + ByteArrayHash(componentPersistentId);
                hash = hash * 31 + ByteArrayHash(featurePersistentId);
                return hash;
            }
        }

        internal string IdentityKey
        {
            get
            {
                return string.Join("|", new[]
                {
                    Mode.ToString(),
                    Kind.ToString(),
                    OwnerScope.ToString(),
                    ConvertToBase64(componentPersistentId),
                    ConvertToBase64(featurePersistentId)
                });
            }
        }

        internal bool IsValidFor(
            ReferenceGeometryKind expectedKind,
            bool allowNone)
        {
            if (Kind != expectedKind ||
                !Enum.IsDefined(typeof(ReferenceGeometryKind), Kind) ||
                !Enum.IsDefined(typeof(ReferenceSelectionMode), Mode) ||
                !Enum.IsDefined(
                    typeof(ReferenceGeometryOwnerScope),
                    OwnerScope))
            {
                return false;
            }

            return HasValidShape(
                    Mode,
                    OwnerScope,
                    componentPersistentId,
                    featurePersistentId) &&
                (Mode == ReferenceSelectionMode.Automatic ||
                 Mode == ReferenceSelectionMode.Explicit ||
                 (allowNone && Mode == ReferenceSelectionMode.None));
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            ReferencedConfiguration = ReferencedConfiguration ?? string.Empty;
            if (!Enum.IsDefined(typeof(ReferenceGeometryKind), Kind) ||
                !Enum.IsDefined(typeof(ReferenceSelectionMode), Mode) ||
                !Enum.IsDefined(
                    typeof(ReferenceGeometryOwnerScope),
                    OwnerScope))
            {
                throw new SerializationException(
                    "The CAD reference contains an unsupported kind, selection mode, or owner scope.");
            }

            if (!HasValidShape(
                    Mode,
                    OwnerScope,
                    componentPersistentId,
                    featurePersistentId))
            {
                throw new SerializationException(
                    "The CAD reference owner scope and persistent IDs are inconsistent.");
            }
        }

        private static bool HasValidShape(
            ReferenceSelectionMode mode,
            ReferenceGeometryOwnerScope ownerScope,
            byte[] componentId,
            byte[] featureId)
        {
            bool hasComponentId = componentId != null && componentId.Length > 0;
            bool hasFeatureId = featureId != null && featureId.Length > 0;
            if (mode == ReferenceSelectionMode.Explicit)
            {
                return hasFeatureId &&
                    ((ownerScope == ReferenceGeometryOwnerScope.RootDocument &&
                      !hasComponentId) ||
                     (ownerScope == ReferenceGeometryOwnerScope.ComponentInstance &&
                      hasComponentId));
            }

            return ownerScope == ReferenceGeometryOwnerScope.Unspecified &&
                !hasComponentId &&
                !hasFeatureId;
        }

        private static byte[] CloneBytes(byte[] value)
        {
            return value == null ? null : (byte[])value.Clone();
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left == null || right == null)
            {
                return false;
            }
            return left.SequenceEqual(right);
        }

        private static int ByteArrayHash(byte[] value)
        {
            if (value == null)
            {
                return 0;
            }

            unchecked
            {
                int hash = 17;
                foreach (byte item in value)
                {
                    hash = hash * 31 + item;
                }
                return hash;
            }
        }

        private static string ConvertToBase64(byte[] value)
        {
            return value == null || value.Length == 0
                ? string.Empty
                : Convert.ToBase64String(value);
        }
    }
}
