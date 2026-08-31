using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW2URDF.URDFExport
{
    internal static class LinkTreeRootJointPolicy
    {
        public static void Normalize(LinkNode root)
        {
            Normalize(root == null ? null : root.Link);
        }

        public static void Normalize(Link root)
        {
            if (root == null || root.Joint == null)
            {
                return;
            }

            root.Joint.Unset();
            root.Joint.Name = string.Empty;
            root.Joint.Type = string.Empty;
            root.Joint.AxisReference = CadFeatureReference.None(
                ReferenceGeometryKind.Axis);
        }
    }

    internal sealed class LinkTreeNameValidationResult
    {
        public LinkTreeNameValidationResult(
            IList<string> duplicateLinkNames,
            IList<string> duplicateJointNames)
        {
            DuplicateLinkNames = duplicateLinkNames;
            DuplicateJointNames = duplicateJointNames;
        }

        public IList<string> DuplicateLinkNames { get; private set; }
        public IList<string> DuplicateJointNames { get; private set; }

        public bool IsValid
        {
            get { return DuplicateLinkNames.Count == 0 && DuplicateJointNames.Count == 0; }
        }
    }

    internal static class LinkTreeNameValidator
    {
        public static LinkTreeNameValidationResult Validate(LinkNode root)
        {
            if (root == null)
            {
                return new LinkTreeNameValidationResult(
                    new List<string>(),
                    new List<string>());
            }

            List<LinkNode> nodes = Flatten(root).ToList();
            return new LinkTreeNameValidationResult(
                FindDuplicates(nodes.Select(node => node.Link == null ? null : node.Link.Name)),
                FindDuplicates(nodes
                    .Where(node => !node.IsBaseNode)
                    .Select(node => node.Link == null || node.Link.Joint == null
                        ? null
                        : node.Link.Joint.Name)));
        }

        private static IEnumerable<LinkNode> Flatten(LinkNode node)
        {
            yield return node;
            foreach (LinkNode child in node.Nodes)
            {
                foreach (LinkNode descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }

        private static IList<string> FindDuplicates(IEnumerable<string> names)
        {
            return names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }
    }
}
