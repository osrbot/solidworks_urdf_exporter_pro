using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.Serialization;

namespace SW2URDF.URDFExport
{
    public sealed class LegacyReferenceSelection
    {
        internal Link Link;
        public string LinkName { get { return Link.Name; } }
        public ReferenceGeometryKind Kind { get; internal set; }
        public string LegacyName { get; internal set; }
        public IReadOnlyList<ReferenceGeometryEntry> Choices { get; internal set; }
        public ReferenceGeometryEntry Selected { get; set; }
    }

    /// <summary>Read-only migration plan. No SolidWorks document is written by this class.</summary>
    public sealed class LegacyConfigurationMigration
    {
        private readonly Link root;
        private readonly List<LegacyReferenceSelection> references = new List<LegacyReferenceSelection>();
        public IReadOnlyList<LegacyReferenceSelection> References { get { return references.AsReadOnly(); } }
        public int LinkCount { get; private set; }
        public bool IsResolved
        {
            get { return references.All(item => item.Selected != null && item.Choices.Contains(item.Selected)); }
        }

        public LegacyConfigurationMigration(string data, double version,
            IEnumerable<ReferenceGeometryEntry> catalog)
        {
            if (!IsSupportedVersion(version))
                throw new SerializationException("Unsupported legacy configuration version. Supported storage versions are 1.0 through 1.5. The original was not changed.");
            if (string.IsNullOrWhiteSpace(data))
                throw new SerializationException("The legacy configuration is empty. The original was not changed.");
            // SolidWorks returns decoded text. Early StringWriter payloads declare UTF-16;
            // re-encoding them as UTF-8 bytes contradicts that declaration.
            using (var text = new StringReader(data))
            using (var reader = XmlReader.Create(text, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 32 * 1024 * 1024
            }))
            {
                // ROS uses SerialNode XML before 1.3, and the same Link data contract as
                // the maintained fork for 1.3/1.4/1.5. Never reinterpret an unknown version.
                root = version < 1.3
                    ? ((LegacySerialNode)new XmlSerializer(typeof(LegacySerialNode)).Deserialize(reader)).ToLink(null)
                    : (Link)new DataContractSerializer(typeof(Link)).ReadObject(reader);
            }

            var entries = (catalog ?? throw new ArgumentNullException("catalog")).ToList();
            Visit(root, null, entries, new HashSet<Link>(), new HashSet<string>(StringComparer.Ordinal));
        }

        internal static bool IsSupportedVersion(double version)
        {
            return version == 1.0 || version == 1.1 || version == 1.2 ||
                version == 1.3 || version == 1.4 || version == 1.5;
        }

        private void Visit(Link link, Link parent, List<ReferenceGeometryEntry> catalog,
            HashSet<Link> visited, HashSet<string> names)
        {
            if (link == null || !visited.Add(link) || link.Children == null || link.Joint == null ||
                string.IsNullOrWhiteSpace(link.Name) || !names.Add(link.Name) || link.Parent != parent)
                throw new SerializationException("The legacy Link tree is incomplete or ambiguous. The original was not changed.");
            LinkCount++;
            // The old reader looked up these names in the root assembly, not in child documents.
            AddReference(link, ReferenceGeometryKind.CoordinateSystem, link.Joint.LegacyCoordinateSystemName, catalog);
            AddReference(link, ReferenceGeometryKind.Axis, link.Joint.LegacyAxisName, catalog);
            foreach (var child in link.Children)
                Visit(child, link, catalog, visited, names);
        }

        private void AddReference(Link link, ReferenceGeometryKind kind, string name,
            List<ReferenceGeometryEntry> catalog)
        {
            if (string.IsNullOrEmpty(name))
                return;
            var choices = catalog.Where(entry => entry.Reference.Kind == kind).ToList();
            int qualifier = name.IndexOf(" <", StringComparison.Ordinal);
            bool qualified = qualifier >= 0 && name.EndsWith(">", StringComparison.Ordinal);
            string featureName = qualified ? name.Substring(0, qualifier).Trim() : name;
            string componentPath = qualified ? name.Substring(qualifier + 2, name.Length - qualifier - 3) : null;
            var matches = choices.Where(entry =>
                entry.Reference.OwnerScope == (qualified ? ReferenceGeometryOwnerScope.ComponentInstance : ReferenceGeometryOwnerScope.RootDocument) &&
                (!qualified || string.Equals(entry.ComponentPath, componentPath, StringComparison.Ordinal)) &&
                string.Equals(entry.DisplayName, featureName, StringComparison.Ordinal)).ToList();
            references.Add(new LegacyReferenceSelection
            {
                Link = link, Kind = kind, LegacyName = name, Choices = choices.AsReadOnly(),
                Selected = matches.Count == 1 ? matches[0] : null
            });
        }

        internal static void EnsureComponentBindings(LinkNode node, Func<byte[], bool> resolves)
        {
            var missing = new List<string>();
            CheckComponentBindings(node, resolves, missing);
            if (missing.Count > 0)
                throw new InvalidOperationException("Resolve the missing assembly components before migration: " +
                    string.Join(", ", missing));
        }

        private static void CheckComponentBindings(LinkNode node, Func<byte[], bool> resolves, List<string> missing)
        {
            var link = node.Link;
            if (link.SWComponentPIDs == null ||
                link.SWComponentPIDs.Any(pid => pid == null || pid.Length == 0 || !resolves(pid)) ||
                (link.SWMainComponentPID != null && !resolves(link.SWMainComponentPID)))
                missing.Add(link.Name);
            foreach (LinkNode child in node.Nodes)
                CheckComponentBindings(child, resolves, missing);
        }

        public LinkNode CreateReviewedTree()
        {
            if (!IsResolved)
                throw new InvalidOperationException("Select every unresolved coordinate system and axis before migration.");
            var copies = new Dictionary<Link, Link>();
            Link migrated = CopyStructure(root, null, copies);
            foreach (var item in references)
            {
                if (item.Kind == ReferenceGeometryKind.CoordinateSystem)
                    copies[item.Link].FrameReference = item.Selected.Reference.Clone();
                else
                    copies[item.Link].Joint.AxisReference = item.Selected.Reference.Clone();
            }
            var node = new LinkNode(migrated);
            LinkTreeRootJointPolicy.Normalize(node);
            node.NeedsSaving = true;
            return node;
        }

        // All supported wire formats converge here. Copy only bindings and design intent;
        // calculated geometry, inertia and editing provenance must start from fresh state.
        private static Link CopyStructure(Link source, Link parent, IDictionary<Link, Link> copies)
        {
            var link = new Link(parent)
            {
                Name = source.Name,
                SWComponentPIDs = source.SWComponentPIDs == null ? null :
                    source.SWComponentPIDs.Select(pid => pid == null ? null : (byte[])pid.Clone()).ToList(),
                SWMainComponentPID = source.SWMainComponentPID == null ? null : (byte[])source.SWMainComponentPID.Clone(),
                isFixedFrame = source.isFixedFrame,
                isIncomplete = source.isIncomplete,
                JointKinematicsDirty = parent != null,
                JointLimitsDirty = parent != null,
                InertialEditing = new InertialEditingState { Source = new Inertial() }
            };
            copies.Add(source, link);
            link.Joint.Name = source.Joint.Name;
            link.Joint.Type = source.Joint.Type;
            // Without a named CAD axis, the numeric direction is the only recorded intent.
            link.Joint.AxisReference = CadFeatureReference.None(ReferenceGeometryKind.Axis);
            if (string.IsNullOrEmpty(source.Joint.LegacyAxisName) && source.Joint.Axis != null)
                link.Joint.Axis.SetElement(source.Joint.Axis);
            // These settings cannot reliably be inferred from geometry. Limit positions
            // remain a fallback until the current CAD limit mates have been evaluated.
            if (source.Joint.Limit != null) link.Joint.Limit.SetElement(source.Joint.Limit);
            if (source.Joint.Dynamics != null) link.Joint.Dynamics.SetElement(source.Joint.Dynamics);
            if (source.Joint.Safety != null) link.Joint.Safety.SetElement(source.Joint.Safety);
            if (source.Joint.Calibration != null) link.Joint.Calibration.SetElement(source.Joint.Calibration);
            if (source.Joint.Mimic != null) link.Joint.Mimic.SetElement(source.Joint.Mimic);
            foreach (var child in source.Children)
                link.Children.Add(CopyStructure(child, link, copies));
            return link;
        }
    }

    // XML wire shape used by ros/solidworks_urdf_exporter Legacy/SerialNode.cs.
    // URDFLink was XmlIgnore: numeric dynamics were never stored in this format.
    [XmlRoot("SerialNode")]
    public sealed class LegacySerialNode
    {
        public string linkName;
        public string jointName;
        public string axisName;
        public string coordsysName;
        public List<byte[]> componentPIDs;
        public string jointType;
        public bool isBaseNode;
        public bool isIncomplete;
        [XmlArrayItem("SerialNode")]
        public List<LegacySerialNode> Nodes = new List<LegacySerialNode>();

        internal Link ToLink(Link parent)
        {
            if (string.IsNullOrWhiteSpace(linkName) || Nodes == null || Nodes.Any(node => node == null))
                throw new SerializationException("The legacy SerialNode tree is incomplete. The original was not changed.");
            var link = new Link { Name = linkName, Parent = parent, isIncomplete = isIncomplete };
            link.Joint.Name = jointName ?? string.Empty;
            link.Joint.Type = jointType ?? string.Empty;
            link.Joint.LegacyAxisName = axisName;
            link.Joint.LegacyCoordinateSystemName = coordsysName;
            link.SWComponentPIDs = componentPIDs ?? new List<byte[]>();
            foreach (var child in Nodes)
                link.Children.Add(child.ToLink(link));
            return link;
        }
    }
}
