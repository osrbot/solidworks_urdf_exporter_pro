using SW2URDF.UI.LinkTreeCanvas;
using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Transaction boundary between the pure canvas document and the legacy WinForms LinkNode tree.
    /// </summary>
    public sealed class LinkTreeCanvasHost : ILinkTreeCanvasHost
    {
        private const double ColumnGap = 300;
        private const double RowGap = 118;

        private readonly LinkTreeDocument initialDocument;
        private readonly Dictionary<Guid, LegacyNodeState> legacyNodes;

        public LinkTreeCanvasHost(LinkNode baseNode)
        {
            if (baseNode == null)
            {
                throw new ArgumentNullException(nameof(baseNode));
            }

            legacyNodes = new Dictionary<Guid, LegacyNodeState>();
            initialDocument = new LinkTreeDocument();
            int leafRow = 0;
            BuildDocument(baseNode, null, 0, ref leafRow);
        }

        public LinkNode AppliedRoot { get; private set; }

        public LinkTreeDocument LoadTree()
        {
            return initialDocument.Clone();
        }

        public void ApplyTree(LinkTreeDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            IList<string> errors = document.Validate();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }

            AppliedRoot = BuildLegacyTree(document, document.Root, null);
            AppliedRoot.UpdateLinkTree(null);
        }

        public string ValidateLinkName(string linkName, Guid editingNodeId)
        {
            return LinkTreeDocument.ValidateRosName(linkName);
        }

        private double BuildDocument(
            LinkNode legacyNode,
            Guid? parentId,
            int depth,
            ref int leafRow)
        {
            Guid id = Guid.NewGuid();
            Link link = legacyNode.Link ?? new Link();
            LinkTreeNode node = new LinkTreeNode
            {
                Id = id,
                ParentId = parentId,
                Name = link.Name,
                JointName = parentId.HasValue ? link.Joint.Name : string.Empty,
                JointType = parentId.HasValue ? link.Joint.Type : string.Empty,
                X = 80 + depth * ColumnGap
            };
            initialDocument.Nodes.Add(node);
            legacyNodes[id] = new LegacyNodeState(legacyNode);

            List<double> childRows = new List<double>();
            foreach (LinkNode child in legacyNode.Nodes)
            {
                childRows.Add(BuildDocument(child, id, depth + 1, ref leafRow));
            }

            node.Y = childRows.Count == 0
                ? 90 + leafRow++ * RowGap
                : childRows.Average();
            return node.Y;
        }

        private LinkNode BuildLegacyTree(
            LinkTreeDocument document,
            LinkTreeNode source,
            Link parentLink)
        {
            LegacyNodeState previous;
            Link link;
            if (legacyNodes.TryGetValue(source.Id, out previous))
            {
                link = previous.Link;
            }
            else
            {
                link = new Link();
                link.Joint.AxisName = "Automatically Generate";
                link.Joint.CoordinateSystemName = "Automatically Generate";
            }

            link.Name = source.Name;
            link.Parent = parentLink;
            link.Children.Clear();
            if (source.ParentId.HasValue)
            {
                link.Joint.Name = source.JointName;
                link.Joint.Type = source.JointType;
                link.Joint.Parent.Name = parentLink == null ? string.Empty : parentLink.Name;
                link.Joint.Child.Name = link.Name;
            }

            LinkNode result = new LinkNode
            {
                Link = link,
                Name = link.Name,
                Text = link.Name,
                IsBaseNode = !source.ParentId.HasValue,
                IsIncomplete = previous == null || previous.IsIncomplete,
                NeedsSaving = previous != null && previous.NeedsSaving,
                WhyIncomplete = previous == null ? "SolidWorks components are not assigned." : previous.WhyIncomplete
            };

            foreach (LinkTreeNode child in document.ChildrenOf(source.Id))
            {
                LinkNode childNode = BuildLegacyTree(document, child, link);
                result.Nodes.Add(childNode);
                link.Children.Add(childNode.Link);
            }
            return result;
        }

        private sealed class LegacyNodeState
        {
            public LegacyNodeState(LinkNode node)
            {
                Link = node.Link;
                IsIncomplete = node.IsIncomplete;
                NeedsSaving = node.NeedsSaving;
                WhyIncomplete = node.WhyIncomplete;
            }

            public Link Link { get; private set; }
            public bool IsIncomplete { get; private set; }
            public bool NeedsSaving { get; private set; }
            public string WhyIncomplete { get; private set; }
        }
    }
}
