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
    public sealed class LinkTreeSession : ILinkTreeCanvasHost
    {
        private const double ColumnGap = 300;
        private const double RowGap = 118;

        private LinkTreeDocument currentDocument;
        private Dictionary<Guid, LegacyNodeState> legacyNodes;

        public LinkTreeSession(LinkNode baseNode)
        {
            if (baseNode == null)
            {
                throw new ArgumentNullException(nameof(baseNode));
            }

            legacyNodes = new Dictionary<Guid, LegacyNodeState>();
            CaptureTree(baseNode);
            Revision = 0;
            AppliedRoot = null;
        }

        public LinkNode AppliedRoot { get; private set; }
        public int Revision { get; private set; }

        public LinkTreeDocument LoadTree()
        {
            return currentDocument.Clone();
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

            currentDocument = document.Clone();
            RemoveDeletedStates();
            AppliedRoot = CreateProjection();
            Revision++;
        }

        public void CaptureTree(LinkNode baseNode)
        {
            if (baseNode == null)
            {
                throw new ArgumentNullException(nameof(baseNode));
            }

            Dictionary<Link, Guid> existingIds = legacyNodes.ToDictionary(
                pair => pair.Value.Link,
                pair => pair.Key);
            LinkTreeDocument previousDocument = currentDocument;
            LinkTreeDocument capturedDocument = new LinkTreeDocument();
            Dictionary<Guid, LegacyNodeState> capturedStates = new Dictionary<Guid, LegacyNodeState>();
            int leafRow = 0;
            BuildDocument(
                baseNode,
                null,
                0,
                ref leafRow,
                existingIds,
                previousDocument,
                capturedDocument,
                capturedStates);
            currentDocument = capturedDocument;
            legacyNodes = capturedStates;
            AppliedRoot = baseNode;
            Revision++;
        }

        public LinkNode CreateProjection()
        {
            LinkNode root = BuildLegacyTree(currentDocument, currentDocument.Root, null);
            root.UpdateLinkTree(null);
            AppliedRoot = root;
            return root;
        }

        public string ValidateLinkName(string linkName, Guid editingNodeId)
        {
            return LinkTreeDocument.ValidateRosName(linkName);
        }

        private double BuildDocument(
            LinkNode legacyNode,
            Guid? parentId,
            int depth,
            ref int leafRow,
            IDictionary<Link, Guid> existingIds,
            LinkTreeDocument previousDocument,
            LinkTreeDocument targetDocument,
            IDictionary<Guid, LegacyNodeState> targetStates)
        {
            Link link = legacyNode.Link ?? new Link();
            Guid id;
            if (!existingIds.TryGetValue(link, out id))
            {
                id = Guid.NewGuid();
            }
            LinkTreeNode previousNode = previousDocument == null ? null : previousDocument.Find(id);
            LinkTreeNode node = new LinkTreeNode
            {
                Id = id,
                ParentId = parentId,
                Name = link.Name,
                JointName = parentId.HasValue ? link.Joint.Name : string.Empty,
                JointType = parentId.HasValue ? link.Joint.Type : string.Empty,
                X = previousNode == null ? 80 + depth * ColumnGap : previousNode.X
            };
            targetDocument.Nodes.Add(node);
            targetStates[id] = new LegacyNodeState(legacyNode);

            List<double> childRows = new List<double>();
            foreach (LinkNode child in legacyNode.Nodes)
            {
                childRows.Add(BuildDocument(
                    child,
                    id,
                    depth + 1,
                    ref leafRow,
                    existingIds,
                    previousDocument,
                    targetDocument,
                    targetStates));
            }

            double layoutY = childRows.Count == 0
                ? 90 + leafRow++ * RowGap
                : childRows.Average();
            node.Y = previousNode == null ? layoutY : previousNode.Y;
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
            legacyNodes[source.Id] = new LegacyNodeState(result);

            foreach (LinkTreeNode child in document.ChildrenOf(source.Id))
            {
                LinkNode childNode = BuildLegacyTree(document, child, link);
                result.Nodes.Add(childNode);
                link.Children.Add(childNode.Link);
            }
            return result;
        }

        private void RemoveDeletedStates()
        {
            HashSet<Guid> activeIds = new HashSet<Guid>(currentDocument.Nodes.Select(node => node.Id));
            foreach (Guid deletedId in legacyNodes.Keys.Where(id => !activeIds.Contains(id)).ToList())
            {
                legacyNodes.Remove(deletedId);
            }
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
