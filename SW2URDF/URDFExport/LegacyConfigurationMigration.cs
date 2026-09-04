using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

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
            if (version != 1.5)
                throw new SerializationException("Only legacy configuration v1.5 can be migrated. The original was not changed.");
            if (string.IsNullOrWhiteSpace(data))
                throw new SerializationException("The legacy configuration is empty. The original was not changed.");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 32 * 1024 * 1024
            }))
                root = (Link)new DataContractSerializer(typeof(Link)).ReadObject(reader);

            var entries = (catalog ?? throw new ArgumentNullException("catalog")).ToList();
            Visit(root, null, entries, new HashSet<Link>(), new HashSet<string>(StringComparer.Ordinal));
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
            InitializeReferences(root);
            foreach (var item in references)
            {
                if (item.Kind == ReferenceGeometryKind.CoordinateSystem)
                    item.Link.FrameReference = item.Selected.Reference.Clone();
                else
                    item.Link.Joint.AxisReference = item.Selected.Reference.Clone();
            }
            var node = new LinkNode(root.Clone());
            LinkTreeRootJointPolicy.Normalize(node);
            node.NeedsSaving = true;
            return node;
        }

        private static void InitializeReferences(Link link)
        {
            link.FrameReference = CadFeatureReference.Automatic(ReferenceGeometryKind.CoordinateSystem);
            // Empty legacy axis means keep the stored numeric axis, not re-detect it from CAD.
            link.Joint.AxisReference = CadFeatureReference.None(ReferenceGeometryKind.Axis);
            link.Joint.LegacyCoordinateSystemName = null;
            link.Joint.LegacyAxisName = null;
            foreach (var child in link.Children)
                InitializeReferences(child);
        }
    }
}
