using SolidWorks.Interop.sldworks;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;

namespace SW2URDF.URDF
{
    public enum CollisionMeshStrategy
    {
        VisualMesh,
        SimplifiedMesh,
        AccurateMesh,
        Primitive,
        BoxPrimitive,
        CylinderPrimitive,
        SpherePrimitive,
        ConvexHull,
        ComponentBoxes
    }

    //The link class, it contains many other elements not found in the URDF.
    [DataContract(IsReference = true, Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class Link : URDFElement//, ISerializable
    {
        [DataMember]
        public Link Parent;

        [DataMember]
        public List<Link> Children;

        [DataMember]
        private readonly URDFAttribute NameAttribute;

        public string Name
        {
            get => (string)NameAttribute.Value;
            set => NameAttribute.Value = value;
        }

        [DataMember]
        public Inertial Inertial;

        [DataMember(EmitDefaultValue = false)]
        public InertialEditingState InertialEditing;

        [DataMember]
        public Visual Visual;

        [DataMember]
        public Collision Collision;

        [DataMember]
        public List<Collision> AdditionalCollisions;

        [DataMember]
        public Joint Joint;

        [DataMember]
        public CadFeatureReference FrameReference;

        [DataMember]
        public bool STLQualityFine;

        [DataMember]
        public double MeshReductionRatio;

        [DataMember]
        public CollisionMeshStrategy CollisionMeshStrategy;

        [DataMember]
        public bool JointKinematicsDirty;

        [DataMember]
        public bool JointLimitsDirty;

        [DataMember]
        public bool isIncomplete;

        [DataMember]
        public bool isFixedFrame;

        public Component2 SWMainComponent;

        public List<Component2> SWComponents;

        [DataMember]
        public List<byte[]> SWComponentPIDs;

        [DataMember]
        public byte[] SWMainComponentPID;

        public Link() : base("link", true)
        {
            Parent = null;
            Children = new List<Link>();
            SWComponents = new List<Component2>();
            SWComponentPIDs = new List<byte[]>();
            NameAttribute = new URDFAttribute("name", true, "");

            Inertial = new Inertial();
            Visual = new Visual();
            Collision = new Collision();
            AdditionalCollisions = new List<Collision>();
            Joint = new Joint();
            FrameReference = CadFeatureReference.Automatic(
                ReferenceGeometryKind.CoordinateSystem);

            isFixedFrame = false;
            MeshReductionRatio = 0;
            CollisionMeshStrategy = CollisionMeshStrategy.VisualMesh;
            JointKinematicsDirty = false;
            JointLimitsDirty = false;

            Attributes.Add(NameAttribute);
            ChildElements.Add(Inertial);
            ChildElements.Add(Visual);
            ChildElements.Add(Collision);
            ChildElements.Add(Joint);
        }

        public Link Clone()
        {
            Link cloned = new Link();
            cloned.SetElement(this);
            cloned.SetSWComponents(this);
            foreach (Link child in Children)
            {
                Link clonedChild = child.Clone();
                clonedChild.Parent = cloned;
                cloned.Children.Add(clonedChild);
            }
            return cloned;
        }

        public Link(Link parent) : base("link", true)
        {
            Parent = parent;
            Children = new List<Link>();
            SWComponents = new List<Component2>();
            SWComponentPIDs = new List<byte[]>();
            NameAttribute = new URDFAttribute("name", true, "");

            Inertial = new Inertial();
            Visual = new Visual();
            Collision = new Collision();
            AdditionalCollisions = new List<Collision>();
            Joint = new Joint();
            FrameReference = CadFeatureReference.Automatic(
                ReferenceGeometryKind.CoordinateSystem);

            isFixedFrame = false;
            MeshReductionRatio = 0;
            CollisionMeshStrategy = CollisionMeshStrategy.VisualMesh;
            JointKinematicsDirty = false;
            JointLimitsDirty = false;

            Attributes.Add(NameAttribute);
            ChildElements.Add(Inertial);
            ChildElements.Add(Visual);
            ChildElements.Add(Collision);
            ChildElements.Add(Joint);
        }

        public override void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("link");
            NameAttribute.WriteURDF(writer);

            if (Inertial != null)
            {
                Inertial.WriteURDF(writer);
            }
            if (Visual != null)
            {
                Visual.WriteURDF(writer);
            }
            if (Collision != null)
            {
                Collision.WriteURDF(writer);
            }
            if (AdditionalCollisions != null)
            {
                foreach (Collision collision in AdditionalCollisions)
                {
                    if (collision != null)
                    {
                        collision.WriteURDF(writer);
                    }
                }
            }

            writer.WriteEndElement();
            if (Joint.ElementContainsData())
            {
                Joint.WriteURDF(writer);
            }

            foreach (Link child in Children)
            {
                child.WriteURDF(writer);
            }
        }

        public void ClearAdditionalCollisions()
        {
            if (AdditionalCollisions == null)
            {
                AdditionalCollisions = new List<Collision>();
                return;
            }

            AdditionalCollisions.Clear();
        }

        public void AddAdditionalCollision(Collision collision)
        {
            if (collision == null)
            {
                return;
            }

            if (AdditionalCollisions == null)
            {
                AdditionalCollisions = new List<Collision>();
            }

            AdditionalCollisions.Add(collision);
        }

        public override void AppendToCSVDictionary(List<string> context, OrderedDictionary dictionary)
        {
            IEnumerable<string> componentNames = SWComponents.Select(component => component.Name2);
            string componentNamesStr = string.Join(";", componentNames);
            string componentsContext = "Link.SWComponents";
            dictionary.Add(componentsContext, componentNamesStr);
            dictionary.Add("Link.CollisionMeshStrategy", CollisionMeshStrategy.ToString());

            base.AppendToCSVDictionary(context, dictionary);
        }

        public override void SetElementFromData(List<string> context, StringDictionary dictionary)
        {
            base.SetElementFromData(context, dictionary);

            if (dictionary.ContainsKey("Link.CollisionMeshStrategy"))
            {
                CollisionMeshStrategy strategy;
                if (Enum.TryParse(dictionary["Link.CollisionMeshStrategy"], true, out strategy))
                {
                    CollisionMeshStrategy = strategy;
                }
            }
        }

        public override void SetElement(URDFElement externalElement)
        {
            base.SetElement(externalElement);
            SetExportSettings((Link)externalElement);
        }

        public void SetSWComponents(Link externalLink)
        {
            if (externalLink.SWComponents != null)
            {
                SWComponents = new List<Component2>(externalLink.SWComponents);
            }
            else
            {
                SWComponents = new List<Component2>();
            }
            if (externalLink.SWComponentPIDs != null)
            {
                SWComponentPIDs = externalLink.SWComponentPIDs
                    .Select(CloneBytes)
                    .ToList();
            }
            else
            {
                SWComponentPIDs = new List<byte[]>();
            }

            SWMainComponent = externalLink.SWMainComponent;
            SWMainComponentPID = CloneBytes(externalLink.SWMainComponentPID);
        }

        private void SetExportSettings(Link externalLink)
        {
            InertialEditing = externalLink.InertialEditing == null
                ? null : externalLink.InertialEditing.Clone();
            STLQualityFine = externalLink.STLQualityFine;
            MeshReductionRatio = externalLink.MeshReductionRatio;
            CollisionMeshStrategy = externalLink.CollisionMeshStrategy;
            AdditionalCollisions = new List<Collision>();
            if (externalLink.AdditionalCollisions != null)
            {
                foreach (Collision collision in externalLink.AdditionalCollisions)
                {
                    if (collision == null)
                    {
                        continue;
                    }
                    Collision copy = new Collision();
                    copy.SetElement(collision);
                    AdditionalCollisions.Add(copy);
                }
            }
            isFixedFrame = externalLink.isFixedFrame;
            isIncomplete = externalLink.isIncomplete;
            JointKinematicsDirty = externalLink.JointKinematicsDirty;
            JointLimitsDirty = externalLink.JointLimitsDirty;
            FrameReference = externalLink.FrameReference == null
                ? CadFeatureReference.Automatic(ReferenceGeometryKind.CoordinateSystem)
                : externalLink.FrameReference.Clone();
        }

        private static byte[] CloneBytes(byte[] value)
        {
            return value == null ? null : (byte[])value.Clone();
        }

        public string[] GetJointNames(bool includeFixed)
        {
            List<string> names = new List<string>();

            if (Joint != null &&
                !string.IsNullOrWhiteSpace(Joint.Name) &&
                (includeFixed || Joint.Type != "fixed"))
            {
                names.Add(Joint.Name);
            }
            foreach (Link child in Children)
            {
                names.AddRange(child.GetJointNames(includeFixed));
            }

            return names.ToArray();
        }

        public override bool AreRequiredFieldsSatisfied()
        {
            if (!base.AreRequiredFieldsSatisfied())
            {
                return false;
            }

            foreach (Link child in Children)
            {
                if (!child.AreRequiredFieldsSatisfied())
                {
                    return false;
                }
            }

            return true;
        }
    }
}
