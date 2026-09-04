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
    public sealed class LinkTreeSession : ILinkTreeCanvasHost, ILinkTreeCandidateValidator
    {
        private const double ColumnGap = 300;
        private const double RowGap = 118;

        private LinkTreeDocument currentDocument;
        private LinkConfigurationStore configurations;
        private CadBindingStore cadBindings;
        private Dictionary<Link, Guid> projectionIds;
        private Dictionary<LinkNode, Guid> computationProjectionIds;

        public LinkTreeSession(LinkNode baseNode)
        {
            if (baseNode == null)
            {
                throw new ArgumentNullException(nameof(baseNode));
            }

            configurations = new LinkConfigurationStore();
            cadBindings = new CadBindingStore();
            projectionIds = new Dictionary<Link, Guid>();
            computationProjectionIds = null;
            CaptureTree(baseNode, projectionIds, null, false, false);
            Revision = 0;
            AppliedRoot = null;
        }

        public LinkNode AppliedRoot { get; private set; }
        public int Revision { get; private set; }
        public bool RequiresJointKinematicsRecompute
        {
            get { return configurations.RequiresJointKinematics(); }
        }
        public bool RequiresJointLimitsRecompute
        {
            get { return configurations.RequiresJointLimits(); }
        }

        public LinkTreeDocument LoadTree()
        {
            return currentDocument.Clone();
        }

        public IList<string> DraftDiagnostics { get { return currentDocument.Validate(); } }

        private LinkTreeSession(LinkTreeSession source)
        {
            currentDocument = source.currentDocument.Clone();
            configurations = source.configurations.Clone();
            cadBindings = source.cadBindings.Clone();
            projectionIds = new Dictionary<Link, Guid>(source.projectionIds);
            Revision = source.Revision;
            AppliedRoot = source.AppliedRoot;
        }

        public void ValidateTree(LinkTreeDocument document)
        {
            new LinkTreeSession(this).ApplyTree(document);
        }

        // Publish only a fully prepared candidate. A rejected/failed publisher leaves this
        // session untouched; the UI publisher is responsible for restoring its own view.
        public bool EditTree(LinkNode projection, Action<LinkTreeDocument> edit,
            Func<LinkTreeDocument, bool> confirm = null, Action<LinkTreeSession> publish = null)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            LinkTreeSession candidate = new LinkTreeSession(this);
            if (projection != null) candidate.CaptureTree(projection);
            LinkTreeDocument document = candidate.LoadTree();
            edit(document);
            candidate.ApplyTree(document);
            if (confirm != null && !confirm(document.Clone())) return false;
            if (publish != null) publish(candidate);
            currentDocument = candidate.currentDocument;
            configurations = candidate.configurations;
            cadBindings = candidate.cadBindings;
            projectionIds = candidate.projectionIds;
            computationProjectionIds = null;
            AppliedRoot = candidate.AppliedRoot;
            Revision++;
            return true;
        }

        public void ApplyTree(LinkTreeDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            IList<string> errors = document.ValidateDraft();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }

            LinkTreeDocument candidateDocument = document.Clone();
            if (currentDocument.Root.Id != candidateDocument.Root.Id)
            {
                throw new InvalidOperationException("The root Link identity cannot be replaced by a topology edit.");
            }
            LinkConfigurationStore candidateConfigurations = configurations.Clone();
            CadBindingStore candidateBindings = cadBindings.Clone();

            PrepareNewNodes(candidateDocument, candidateConfigurations, candidateBindings);
            ApplyJointTypes(candidateDocument, candidateConfigurations);
            MigrateStableMimicReferences(
                currentDocument,
                configurations,
                candidateDocument,
                candidateConfigurations);
            MarkReparentedJointState(candidateDocument, candidateConfigurations);

            HashSet<Guid> activeIds = new HashSet<Guid>(candidateDocument.Nodes.Select(node => node.Id));
            candidateConfigurations.RemoveExcept(activeIds);
            candidateBindings.RemoveExcept(activeIds);

            IList<string> referenceErrors = candidateConfigurations.ValidateMimicReferences(
                candidateDocument.Nodes
                    .Where(node => node.ParentId.HasValue)
                    .ToDictionary(node => node.Id, node => node.JointName));
            if (referenceErrors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, referenceErrors));
            }

            LinkTreeProjection candidateProjection = BuildProjectionSnapshot(
                candidateDocument,
                candidateConfigurations,
                candidateBindings);
            currentDocument = candidateDocument;
            configurations = candidateConfigurations;
            cadBindings = candidateBindings;
            projectionIds = candidateProjection.LinkIds;
            computationProjectionIds = null;
            AppliedRoot = candidateProjection.Root;
            Revision++;
        }

        public void CaptureTree(LinkNode baseNode)
        {
            CaptureTree(baseNode, projectionIds, null, true, true);
            computationProjectionIds = null;
        }

        private void CaptureTree(
            LinkNode baseNode,
            IDictionary<Link, Guid> sourceLinkIds,
            IDictionary<LinkNode, Guid> sourceNodeIds,
            bool validateCandidate,
            bool trackChanges)
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
                capturedProjectionIds,
                sourceLinkIds,
                sourceNodeIds,
                trackChanges,
                false);

            if (previousDocument != null)
            {
                if (validateCandidate && capturedDocument.Root.Id != previousDocument.Root.Id)
                    throw new InvalidOperationException("The root Link identity cannot change during projection capture.");
                MigrateStableMimicReferences(
                    previousDocument,
                    configurations,
                    capturedDocument,
                    capturedConfigurations);
            }

            if (validateCandidate)
            {
                ValidateCapturedTree(capturedDocument, capturedConfigurations);
            }

            currentDocument = capturedDocument;
            configurations = capturedConfigurations;
            cadBindings = capturedBindings;
            projectionIds = capturedProjectionIds;
            AppliedRoot = baseNode;
            Revision++;
        }

        public LinkNode CreateProjection()
        {
            return BuildProjectionSnapshot(currentDocument, configurations, cadBindings).Root;
        }

        public LinkNode CreateActiveProjection()
        {
            LinkTreeProjection projection = BuildProjectionSnapshot(
                currentDocument,
                configurations,
                cadBindings);
            projectionIds = projection.LinkIds;
            computationProjectionIds = null;
            AppliedRoot = projection.Root;
            return projection.Root;
        }

        public LinkNode CreateComputationProjection()
        {
            LinkTreeProjection projection = BuildProjectionSnapshot(
                currentDocument,
                configurations,
                cadBindings);
            computationProjectionIds = projection.NodeIds;
            return projection.Root;
        }

        public Guid? GetProjectionNodeId(Link link)
        {
            Guid id;
            return link != null && projectionIds.TryGetValue(link, out id) ? id : (Guid?)null;
        }

        public void ValidateComputedProjection(LinkNode baseNode)
        {
            if (baseNode == null)
            {
                throw new ArgumentNullException(nameof(baseNode));
            }

            if (computationProjectionIds == null)
            {
                throw new InvalidOperationException(
                    "Only the current computation projection can be accepted after computation.");
            }

            HashSet<Guid> visited = new HashSet<Guid>();
            ValidateComputationNode(baseNode, null, visited);
            if (visited.Count != currentDocument.Nodes.Count)
            {
                throw new InvalidOperationException(
                    "The computation projection cannot add or remove Link nodes.");
            }
        }

        public void AcceptComputedProjection(LinkNode baseNode)
        {
            ValidateComputedProjection(baseNode);
            CaptureTree(baseNode, null, computationProjectionIds, true, false);
            computationProjectionIds = null;
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
            IDictionary<Link, Guid> targetProjectionIds,
            IDictionary<Link, Guid> sourceLinkIds,
            IDictionary<LinkNode, Guid> sourceNodeIds,
            bool trackChanges,
            bool ancestorJointContextChanged)
        {
            Link link = projectionNode.Link ?? new Link();
            Guid id = Guid.Empty;
            bool foundId = sourceNodeIds != null && sourceNodeIds.TryGetValue(projectionNode, out id);
            if (!foundId && sourceLinkIds != null)
            {
                foundId = sourceLinkIds.TryGetValue(link, out id);
            }
            if (!foundId)
            {
                id = Guid.NewGuid();
            }
            bool isNewSessionNode = previousDocument != null && !foundId;

            LinkTreeNode previousNode = previousDocument == null ? null : previousDocument.Find(id);
            bool jointInputsChanged = previousNode != null &&
                !configurations.JointKinematicsInputsMatch(id, link);
            bool cadBindingsChanged = previousNode != null && !cadBindings.Matches(id, link);
            bool parentChanged = previousNode != null && previousNode.ParentId != parentId;
            LinkTreeNode node = new LinkTreeNode
            {
                Id = id,
                ParentId = parentId,
                Name = link.Name,
                JointName = parentId.HasValue ? link.Joint.Name : string.Empty,
                JointType = parentId.HasValue
                    ? JointConfigurationPolicy.Normalize(link.Joint.Type)
                    : string.Empty,
                X = previousNode == null ? 80 + depth * ColumnGap : previousNode.X
            };
            targetDocument.Nodes.Add(node);
            targetConfigurations.Capture(id, projectionNode);
            if (!parentId.HasValue)
            {
                targetConfigurations.NormalizeRoot(id);
            }
            bool typeChanged = previousNode != null && !string.Equals(
                previousNode.JointType,
                node.JointType,
                StringComparison.Ordinal);
            bool jointContextChanged = trackChanges &&
                (isNewSessionNode || typeChanged || parentChanged ||
                 jointInputsChanged || cadBindingsChanged);
            if (parentId.HasValue)
            {
                targetConfigurations.ApplyJointType(id, node.JointType);
                if (ancestorJointContextChanged || jointContextChanged)
                {
                    targetConfigurations.MarkJointKinematicsStale(id);
                    targetConfigurations.MarkJointLimitsStale(id);
                }
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
                    targetProjectionIds,
                    sourceLinkIds,
                    sourceNodeIds,
                    trackChanges,
                    ancestorJointContextChanged || jointContextChanged));
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
            LinkConfigurationStore sourceConfigurations,
            CadBindingStore sourceBindings,
            IDictionary<Link, Guid> createdLinkIds,
            IDictionary<LinkNode, Guid> createdNodeIds)
        {
            Link link = sourceConfigurations.BuildLink(source.Id);
            sourceBindings.Apply(source.Id, link);
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
            else
            {
                LinkTreeRootJointPolicy.Normalize(link);
            }

            LinkConfigurationState state = sourceConfigurations.Get(source.Id);
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
            createdLinkIds[link] = source.Id;
            createdNodeIds[result] = source.Id;

            foreach (LinkTreeNode child in document.ChildrenOf(source.Id))
            {
                LinkNode childNode = BuildProjection(
                    document,
                    child,
                    link,
                    sourceConfigurations,
                    sourceBindings,
                    createdLinkIds,
                    createdNodeIds);
                result.Nodes.Add(childNode);
                link.Children.Add(childNode.Link);
            }
            return result;
        }

        private LinkTreeProjection BuildProjectionSnapshot(
            LinkTreeDocument document,
            LinkConfigurationStore sourceConfigurations,
            CadBindingStore sourceBindings)
        {
            Dictionary<Link, Guid> createdLinkIds = new Dictionary<Link, Guid>();
            Dictionary<LinkNode, Guid> createdNodeIds = new Dictionary<LinkNode, Guid>();
            LinkNode root = BuildProjection(
                document,
                document.Root,
                null,
                sourceConfigurations,
                sourceBindings,
                createdLinkIds,
                createdNodeIds);
            root.UpdateLinkTree(null);
            return new LinkTreeProjection(root, createdLinkIds, createdNodeIds);
        }

        private void PrepareNewNodes(
            LinkTreeDocument candidate,
            LinkConfigurationStore candidateConfigurations,
            CadBindingStore candidateBindings)
        {
            List<LinkTreeNode> newNodes = candidate.Nodes
                .Where(item => !candidateConfigurations.Contains(item.Id))
                .ToList();
            foreach (LinkTreeNode node in newNodes.Where(item => !item.CopySourceId.HasValue))
            {
                candidateConfigurations.CreateDefault(node.Id);
                candidateBindings.CreateEmpty(node.Id);
            }

            List<LinkTreeNode> pendingCopies = newNodes
                .Where(node => node.CopySourceId.HasValue)
                .ToList();
            while (pendingCopies.Count > 0)
            {
                int copiedCount = 0;
                foreach (LinkTreeNode copy in pendingCopies.ToList())
                {
                    if (!candidateConfigurations.Contains(copy.CopySourceId.Value))
                    {
                        continue;
                    }
                    candidateConfigurations.CopyConfiguration(copy.CopySourceId.Value, copy.Id);
                    candidateBindings.CreateEmpty(copy.Id);
                    pendingCopies.Remove(copy);
                    copiedCount++;
                }
                if (copiedCount == 0)
                {
                    throw new InvalidOperationException(
                        "Copied Link references a source that is not available in the current tree.");
                }
            }

            foreach (IGrouping<Guid, LinkTreeNode> batch in newNodes
                .Where(node => node.CopySourceId.HasValue)
                .GroupBy(node => node.CopyBatchId ?? Guid.Empty))
            {
                Dictionary<Guid, LinkTreeNode> copiesBySource = new Dictionary<Guid, LinkTreeNode>();
                foreach (LinkTreeNode copy in batch)
                {
                    if (copiesBySource.ContainsKey(copy.CopySourceId.Value))
                    {
                        throw new InvalidOperationException(
                            "A copied Link group contains the same source more than once.");
                    }
                    copiesBySource[copy.CopySourceId.Value] = copy;
                }

                foreach (LinkTreeNode copy in batch)
                {
                    LinkTreeNode source = currentDocument.Find(copy.CopySourceId.Value) ??
                        candidate.Find(copy.CopySourceId.Value);
                    if (source == null)
                    {
                        continue;
                    }
                    string sourceMimic = candidateConfigurations.GetMimicReference(source.Id);
                    LinkTreeNode sourceTarget = currentDocument.Nodes.FirstOrDefault(
                        node => string.Equals(node.JointName, sourceMimic, StringComparison.Ordinal)) ??
                        candidate.Nodes.FirstOrDefault(
                            node => string.Equals(node.JointName, sourceMimic, StringComparison.Ordinal));
                    LinkTreeNode copiedTarget;
                    if (sourceTarget != null && copiesBySource.TryGetValue(sourceTarget.Id, out copiedTarget))
                    {
                        candidateConfigurations.SetMimicReference(copy.Id, copiedTarget.JointName);
                    }
                    else if (sourceTarget != null)
                    {
                        LinkTreeNode retainedTarget = candidate.Find(sourceTarget.Id);
                        if (retainedTarget == null && currentDocument.Find(sourceTarget.Id) != null)
                        {
                            throw new InvalidOperationException(string.Format(
                                "Copied Mimic target Joint '{0}' was deleted. Include the target in the copy or keep it in the tree.",
                                sourceMimic));
                        }
                        if (retainedTarget != null)
                        {
                            candidateConfigurations.SetMimicReference(
                                copy.Id,
                                retainedTarget.JointName);
                        }
                    }
                }
            }

            foreach (LinkTreeNode copy in newNodes.Where(node => node.CopySourceId.HasValue))
            {
                copy.CopySourceId = null;
                copy.CopyBatchId = null;
            }
        }

        private void ApplyJointTypes(
            LinkTreeDocument candidate,
            LinkConfigurationStore candidateConfigurations)
        {
            foreach (LinkTreeNode node in candidate.Nodes.Where(item => item.ParentId.HasValue))
            {
                node.JointType = JointConfigurationPolicy.Normalize(node.JointType);
                LinkTreeNode current = currentDocument == null ? null : currentDocument.Find(node.Id);
                bool typeChanged = current != null && !string.Equals(
                    current.JointType,
                    node.JointType,
                    StringComparison.Ordinal);
                if (typeChanged || current == null)
                {
                    candidateConfigurations.ApplyJointTypeFromUser(node.Id, node.JointType);
                }
                else
                {
                    candidateConfigurations.ApplyJointType(node.Id, node.JointType);
                }
                if (typeChanged)
                {
                    candidateConfigurations.MarkJointKinematicsStale(node.Id);
                    candidateConfigurations.MarkJointLimitsStale(node.Id);
                }
            }
        }

        private static void MigrateStableMimicReferences(
            LinkTreeDocument sourceDocument,
            LinkConfigurationStore sourceConfigurations,
            LinkTreeDocument candidateDocument,
            LinkConfigurationStore candidateConfigurations)
        {
            if (sourceDocument == null || sourceConfigurations == null)
            {
                return;
            }

            foreach (LinkTreeNode sourceOwner in sourceDocument.Nodes
                .Where(node => node.ParentId.HasValue))
            {
                LinkTreeNode candidateOwner = candidateDocument.Find(sourceOwner.Id);
                if (candidateOwner == null)
                {
                    continue;
                }

                string sourceReference = sourceConfigurations.GetMimicReference(sourceOwner.Id);
                string candidateReference = candidateConfigurations.GetMimicReference(sourceOwner.Id);
                if (string.IsNullOrWhiteSpace(sourceReference) ||
                    !string.Equals(sourceReference, candidateReference, StringComparison.Ordinal))
                {
                    continue;
                }

                LinkTreeNode sourceTarget = sourceDocument.Nodes.SingleOrDefault(node =>
                    node.ParentId.HasValue && string.Equals(
                        node.JointName,
                        sourceReference,
                        StringComparison.Ordinal));
                if (sourceTarget == null)
                {
                    continue;
                }

                LinkTreeNode candidateTarget = candidateDocument.Find(sourceTarget.Id);
                if (candidateTarget == null)
                {
                    throw new InvalidOperationException(string.Format(
                        "Mimic target Joint '{0}' was deleted. Clear or change the Mimic reference before deleting it.",
                        sourceReference));
                }

                if (!candidateTarget.ParentId.HasValue ||
                    string.IsNullOrWhiteSpace(candidateTarget.JointName))
                {
                    throw new InvalidOperationException(
                        "A referenced Mimic target must keep a non-empty Joint name.");
                }

                candidateConfigurations.SetMimicReference(
                    sourceOwner.Id,
                    candidateTarget.JointName);
            }
        }

        private void MarkReparentedJointState(
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
                    MarkJointSubtreeStale(candidate, candidateConfigurations, updated.Id);
                }
            }
        }

        private static void MarkJointSubtreeStale(
            LinkTreeDocument document,
            LinkConfigurationStore candidateConfigurations,
            Guid rootId)
        {
            LinkTreeNode node = document.Find(rootId);
            if (node == null)
            {
                return;
            }
            if (node.ParentId.HasValue)
            {
                candidateConfigurations.MarkJointKinematicsStale(node.Id);
                candidateConfigurations.MarkJointLimitsStale(node.Id);
            }
            foreach (LinkTreeNode child in document.ChildrenOf(node.Id))
            {
                MarkJointSubtreeStale(document, candidateConfigurations, child.Id);
            }
        }

        private void ValidateComputationNode(
            LinkNode node,
            Guid? actualParentId,
            ISet<Guid> visited)
        {
            Guid id;
            if (!computationProjectionIds.TryGetValue(node, out id))
            {
                throw new InvalidOperationException(
                    "The computation projection cannot add Link nodes.");
            }

            LinkTreeNode expected = currentDocument.Find(id);
            if (expected == null || expected.ParentId != actualParentId)
            {
                throw new InvalidOperationException(
                    "The computation projection cannot change Link parent relationships.");
            }

            if (!visited.Add(id))
            {
                throw new InvalidOperationException(
                    "The computation projection contains a duplicate Link node.");
            }

            foreach (LinkNode child in node.Nodes)
            {
                ValidateComputationNode(child, id, visited);
            }
        }

        private static void ValidateCapturedTree(
            LinkTreeDocument document,
            LinkConfigurationStore capturedConfigurations)
        {
            // Configuration completeness is an export concern, not a topology-edit gate.
            List<string> errors = document.ValidateDraft().ToList();
            errors.AddRange(capturedConfigurations.ValidateMimicReferences(
                document.Nodes
                    .Where(node => node.ParentId.HasValue)
                    .ToDictionary(node => node.Id, node => node.JointName)));
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }
        }

        private sealed class LinkTreeProjection
        {
            public LinkTreeProjection(
                LinkNode root,
                Dictionary<Link, Guid> linkIds,
                Dictionary<LinkNode, Guid> nodeIds)
            {
                Root = root;
                LinkIds = linkIds;
                NodeIds = nodeIds;
            }

            public LinkNode Root { get; private set; }
            public Dictionary<Link, Guid> LinkIds { get; private set; }
            public Dictionary<LinkNode, Guid> NodeIds { get; private set; }
        }
    }
}
