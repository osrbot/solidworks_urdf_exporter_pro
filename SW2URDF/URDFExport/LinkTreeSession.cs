using SW2URDF.UI.LinkTreeCanvas;
using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Coordinates topology transactions. URDF configuration and CAD bindings are owned by
    /// separate stores and are combined only when a legacy/export projection is requested.
    /// </summary>
    public sealed class LinkTreeSession : ILinkTreeCanvasHost
    {
        private const double ColumnGap = 300;
        private const double RowGap = 118;

        private LinkTreeDocument currentDocument;
        private LinkConfigurationStore configurations;
        private CadBindingStore cadBindings;
        private Dictionary<Link, Guid> projectionIds;

        public LinkTreeSession(LinkNode baseNode)
        {
            if (baseNode == null)
            {
                throw new ArgumentNullException(nameof(baseNode));
            }

            configurations = new LinkConfigurationStore();
            cadBindings = new CadBindingStore();
            projectionIds = new Dictionary<Link, Guid>();
            CaptureTree(baseNode);
            Revision = 0;
            AppliedRoot = null;
        }

        public LinkNode AppliedRoot { get; private set; }
        public int Revision { get; private set; }
        public bool RequiresJointKinematicsRecompute
        {
            get { return configurations.RequiresJointKinematics(); }
        }

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

            LinkTreeDocument candidateDocument = document.Clone();
            LinkConfigurationStore candidateConfigurations = configurations.Clone();
            CadBindingStore candidateBindings = cadBindings.Clone();

            PrepareNewNodes(candidateDocument, candidateConfigurations, candidateBindings);
            MigrateRenamedJointReferences(candidateDocument, candidateConfigurations);
            MarkReparentedJointKinematics(candidateDocument, candidateConfigurations);

            HashSet<Guid> activeIds = new HashSet<Guid>(candidateDocument.Nodes.Select(node => node.Id));
            candidateConfigurations.RemoveExcept(activeIds);
            candidateBindings.RemoveExcept(activeIds);

            IList<string> referenceErrors = candidateConfigurations.ValidateMimicReferences(
                candidateDocument.Nodes
                    .Where(node => node.ParentId.HasValue)
                    .Select(node => node.JointName));
            if (referenceErrors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, referenceErrors));
            }

            currentDocument = candidateDocument;
            configurations = candidateConfigurations;
            cadBindings = candidateBindings;
            AppliedRoot = CreateProjection();
            Revision++;
        }

        public void CaptureTree(LinkNode baseNode)
        {
            if (baseNode == null)
            {
                throw new ArgumentNullException(nameof(baseNode));
            }

            LinkTreeDocument previousDocument = currentDocument;
            LinkTreeDocument capturedDocument = new LinkTreeDocument();
            LinkConfigurationStore capturedConfigurations = new LinkConfigurationStore();
            CadBindingStore capturedBindings = new CadBindingStore();
            Dictionary<Link, Guid> capturedProjectionIds = new Dictionary<Link, Guid>();
            int leafRow = 0;
            BuildDocument(
                baseNode,
                null,
                0,
                ref leafRow,
                previousDocument,
                capturedDocument,
                capturedConfigurations,
                capturedBindings,
                capturedProjectionIds);

            currentDocument = capturedDocument;
            configurations = capturedConfigurations;
            cadBindings = capturedBindings;
            projectionIds = capturedProjectionIds;
            AppliedRoot = baseNode;
            Revision++;
        }

        public LinkNode CreateProjection()
        {
            Dictionary<Link, Guid> createdProjectionIds = new Dictionary<Link, Guid>();
            LinkNode root = BuildProjection(currentDocument, currentDocument.Root, null, createdProjectionIds);
            root.UpdateLinkTree(null);
            projectionIds = createdProjectionIds;
            AppliedRoot = root;
            return root;
        }

        public string ValidateLinkName(string linkName, Guid editingNodeId)
        {
            return LinkTreeDocument.ValidateRosName(linkName);
        }

        private double BuildDocument(
            LinkNode projectionNode,
            Guid? parentId,
            int depth,
            ref int leafRow,
            LinkTreeDocument previousDocument,
            LinkTreeDocument targetDocument,
            LinkConfigurationStore targetConfigurations,
            CadBindingStore targetBindings,
            IDictionary<Link, Guid> targetProjectionIds)
        {
            Link link = projectionNode.Link ?? new Link();
            Guid id;
            if (!projectionIds.TryGetValue(link, out id))
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
            targetConfigurations.Capture(id, projectionNode);
            if (configurations.Contains(id) && configurations.RequiresJointKinematics(id))
            {
                targetConfigurations.MarkJointKinematicsStale(id);
            }
            targetBindings.Capture(id, link);
            targetProjectionIds[link] = id;

            List<double> childRows = new List<double>();
            foreach (LinkNode child in projectionNode.Nodes)
            {
                childRows.Add(BuildDocument(
                    child,
                    id,
                    depth + 1,
                    ref leafRow,
                    previousDocument,
                    targetDocument,
                    targetConfigurations,
                    targetBindings,
                    targetProjectionIds));
            }

            double layoutY = childRows.Count == 0
                ? 90 + leafRow++ * RowGap
                : childRows.Average();
            node.Y = previousNode == null ? layoutY : previousNode.Y;
            return node.Y;
        }

        private LinkNode BuildProjection(
            LinkTreeDocument document,
            LinkTreeNode source,
            Link parentLink,
            IDictionary<Link, Guid> createdProjectionIds)
        {
            Link link = configurations.BuildLink(source.Id);
            cadBindings.Apply(source.Id, link);
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

            LinkConfigurationState state = configurations.Get(source.Id);
            LinkNode result = new LinkNode
            {
                Link = link,
                Name = link.Name,
                Text = link.Name,
                IsBaseNode = !source.ParentId.HasValue,
                IsIncomplete = state.IsIncomplete,
                NeedsSaving = state.NeedsSaving,
                WhyIncomplete = state.WhyIncomplete
            };
            createdProjectionIds[link] = source.Id;

            foreach (LinkTreeNode child in document.ChildrenOf(source.Id))
            {
                LinkNode childNode = BuildProjection(document, child, link, createdProjectionIds);
                result.Nodes.Add(childNode);
                link.Children.Add(childNode.Link);
            }
            return result;
        }

        private void PrepareNewNodes(
            LinkTreeDocument candidate,
            LinkConfigurationStore candidateConfigurations,
            CadBindingStore candidateBindings)
        {
            foreach (LinkTreeNode node in candidate.Nodes.Where(item => !candidateConfigurations.Contains(item.Id)))
            {
                if (node.CopySourceId.HasValue && candidateConfigurations.Contains(node.CopySourceId.Value))
                {
                    candidateConfigurations.CopyConfiguration(node.CopySourceId.Value, node.Id);
                }
                else
                {
                    candidateConfigurations.CreateDefault(node.Id);
                }
                candidateBindings.CreateEmpty(node.Id);
            }

            Dictionary<Guid, LinkTreeNode> copiesBySource = candidate.Nodes
                .Where(node => node.CopySourceId.HasValue)
                .GroupBy(node => node.CopySourceId.Value)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (LinkTreeNode copy in candidate.Nodes.Where(node => node.CopySourceId.HasValue))
            {
                LinkTreeNode source = currentDocument.Find(copy.CopySourceId.Value);
                if (source == null)
                {
                    continue;
                }
                string sourceMimic = configurations.Get(source.Id).Configuration.Joint.Mimic.JointName;
                LinkTreeNode sourceTarget = currentDocument.Nodes.FirstOrDefault(
                    node => string.Equals(node.JointName, sourceMimic, StringComparison.OrdinalIgnoreCase));
                LinkTreeNode copiedTarget;
                if (sourceTarget != null && copiesBySource.TryGetValue(sourceTarget.Id, out copiedTarget))
                {
                    candidateConfigurations.SetMimicReference(copy.Id, copiedTarget.JointName);
                }
            }
        }

        private void MigrateRenamedJointReferences(
            LinkTreeDocument candidate,
            LinkConfigurationStore candidateConfigurations)
        {
            if (currentDocument == null)
            {
                return;
            }
            foreach (LinkTreeNode current in currentDocument.Nodes.Where(node => node.ParentId.HasValue))
            {
                LinkTreeNode updated = candidate.Find(current.Id);
                if (updated != null && !string.Equals(
                    current.JointName,
                    updated.JointName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    candidateConfigurations.RenameMimicReference(current.JointName, updated.JointName);
                }
            }
        }

        private void MarkReparentedJointKinematics(
            LinkTreeDocument candidate,
            LinkConfigurationStore candidateConfigurations)
        {
            if (currentDocument == null)
            {
                return;
            }
            foreach (LinkTreeNode updated in candidate.Nodes.Where(node => node.ParentId.HasValue))
            {
                LinkTreeNode current = currentDocument.Find(updated.Id);
                if (current != null && current.ParentId != updated.ParentId)
                {
                    candidateConfigurations.MarkJointKinematicsStale(updated.Id);
                }
            }
        }
    }
}
