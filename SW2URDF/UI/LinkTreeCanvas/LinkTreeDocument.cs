using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SW2URDF.URDF;

namespace SW2URDF.UI.LinkTreeCanvas
{
    public sealed class LinkTreeNode
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
        public string Name { get; set; }
        public string JointName { get; set; }
        public string JointType { get; set; }
        public Guid? CopySourceId { get; set; }
        public Guid? CopyBatchId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }

        public LinkTreeNode Clone()
        {
            return (LinkTreeNode)MemberwiseClone();
        }
    }

    public sealed class LinkTreeDocument
    {
        private static readonly Regex RosNamePattern = new Regex("^[A-Za-z_][A-Za-z0-9_]*$");
        private static readonly HashSet<string> SupportedJointTypes = new HashSet<string>(
            Joint.SelectableTypes,
            StringComparer.Ordinal);

        public List<LinkTreeNode> Nodes { get; private set; }

        public LinkTreeDocument()
        {
            Nodes = new List<LinkTreeNode>();
        }

        public LinkTreeNode Root
        {
            get { return Nodes.SingleOrDefault(node => !node.ParentId.HasValue); }
        }

        public IEnumerable<LinkTreeNode> ChildrenOf(Guid parentId)
        {
            return Nodes.Where(node => node.ParentId == parentId);
        }

        public LinkTreeNode Find(Guid id)
        {
            return Nodes.SingleOrDefault(node => node.Id == id);
        }

        public bool IsDescendant(Guid candidateId, Guid ancestorId)
        {
            LinkTreeNode current = Find(candidateId);
            HashSet<Guid> visited = new HashSet<Guid>();
            while (current != null && current.ParentId.HasValue)
            {
                if (!visited.Add(current.Id))
                {
                    throw new InvalidOperationException("The Link tree contains a cycle.");
                }
                if (current.ParentId.Value == ancestorId)
                {
                    return true;
                }
                current = Find(current.ParentId.Value);
            }
            return false;
        }

        public LinkTreeDocument Clone()
        {
            LinkTreeDocument clone = new LinkTreeDocument();
            clone.Nodes.AddRange(Nodes.Select(node => node.Clone()));
            return clone;
        }

        public LinkTreeNode AddChild(Guid parentId)
        {
            LinkTreeNode parent = Find(parentId);
            if (parent == null) throw new InvalidOperationException("The parent Link no longer exists.");
            string name = UniqueName("new_link", Nodes.Select(node => node.Name));
            LinkTreeNode child = NewNode(name, parentId, parent.X + 300,
                parent.Y + ChildrenOf(parentId).Count() * 118);
            child.JointName = UniqueName(BuildDefaultJointName(name),
                Nodes.Where(node => node.ParentId.HasValue).Select(node => node.JointName));
            Nodes.Add(child);
            return child;
        }

        public void SetChildCount(Guid parentId, int count)
        {
            if (count < 0 || Find(parentId) == null)
                throw new InvalidOperationException("Invalid child count or parent Link.");
            List<LinkTreeNode> children = ChildrenOf(parentId).ToList();
            foreach (LinkTreeNode child in children.Skip(count)) DeleteBranch(child.Id);
            for (int index = children.Count; index < count; index++) AddChild(parentId);
        }

        public void DeleteBranch(Guid id)
        {
            LinkTreeNode node = Find(id);
            if (node == null || !node.ParentId.HasValue)
                throw new InvalidOperationException("The root Link cannot be deleted.");
            HashSet<Guid> removed = new HashSet<Guid>(Nodes
                .Where(item => item.Id == id || IsDescendant(item.Id, id)).Select(item => item.Id));
            Nodes.RemoveAll(item => removed.Contains(item.Id));
        }

        public bool CanReparent(Guid id, Guid parentId)
        {
            LinkTreeNode node = Find(id);
            return node != null && node.ParentId.HasValue && Find(parentId) != null &&
                id != parentId && !IsDescendant(parentId, id);
        }

        public void Reparent(Guid id, Guid parentId)
        {
            if (!CanReparent(id, parentId))
                throw new InvalidOperationException("Cannot move the root Link or move a Link into its own branch.");
            Find(id).ParentId = parentId;
        }

        internal static string UniqueName(string baseName, IEnumerable<string> names)
        {
            HashSet<string> reserved = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            string name = baseName;
            int suffix = 1;
            while (reserved.Contains(name)) name = baseName + "_" + suffix++;
            return name;
        }

        public IList<LinkTreeNode> CreateBranchClipboard(IEnumerable<Guid> selectedNodeIds)
        {
            HashSet<Guid> selected = new HashSet<Guid>(selectedNodeIds ?? Enumerable.Empty<Guid>());
            selected.RemoveWhere(id =>
            {
                LinkTreeNode node = Find(id);
                return node == null || !node.ParentId.HasValue;
            });

            HashSet<Guid> branchRoots = new HashSet<Guid>();
            foreach (Guid id in selected)
            {
                bool hasSelectedAncestor = selected.Any(ancestorId =>
                    ancestorId != id && IsDescendant(id, ancestorId));
                if (!hasSelectedAncestor)
                {
                    branchRoots.Add(id);
                }
            }

            return Nodes
                .Where(node => branchRoots.Contains(node.Id) ||
                    branchRoots.Any(rootId => IsDescendant(node.Id, rootId)))
                .Select(node => node.Clone())
                .ToList();
        }

        public IList<string> ValidateClipboardSources(IEnumerable<LinkTreeNode> copiedNodes)
        {
            List<LinkTreeNode> snapshot = copiedNodes == null
                ? new List<LinkTreeNode>()
                : copiedNodes.ToList();
            HashSet<Guid> copiedIds = new HashSet<Guid>(snapshot.Select(node => node.Id));
            List<string> errors = new List<string>();
            foreach (LinkTreeNode source in snapshot)
            {
                LinkTreeNode current = Find(source.Id);
                if (current == null)
                {
                    errors.Add(source.Name + " 的复制源已不存在，请重新复制。");
                }
                else if (!string.Equals(source.Name, current.Name, StringComparison.Ordinal) ||
                    source.ParentId != current.ParentId ||
                    !string.Equals(source.JointName, current.JointName, StringComparison.Ordinal) ||
                    !string.Equals(source.JointType, current.JointType, StringComparison.Ordinal))
                {
                    errors.Add(source.Name + " 的复制源已修改，请重新复制。");
                }
                if (source.ParentId.HasValue &&
                    !copiedIds.Contains(source.ParentId.Value) &&
                    Find(source.ParentId.Value) == null)
                {
                    errors.Add(source.Name + " 的原父 Link 已不存在，请重新复制。");
                }
            }
            return errors.Distinct().ToList();
        }

        public IList<string> Validate()
        {
            return Validate(false);
        }

        internal IList<string> ValidateDraft()
        {
            return Validate(true);
        }

        private IList<string> Validate(bool allowIncompleteFields)
        {
            List<string> errors = new List<string>();
            if (Nodes.Count == 0)
            {
                errors.Add("Link 树不能为空。");
                return errors;
            }
            if (Nodes.Any(node => node == null || node.Id == Guid.Empty) ||
                Nodes.GroupBy(node => node.Id).Any(group => group.Count() > 1))
            {
                errors.Add("Link 节点标识不能重复。");
                return errors;
            }

            List<LinkTreeNode> roots = Nodes.Where(node => !node.ParentId.HasValue).ToList();
            if (roots.Count != 1)
            {
                errors.Add("Link 树必须且只能有一个根节点。");
            }

            foreach (LinkTreeNode node in Nodes)
            {
                string nameError = ValidateRosName(node.Name);
                if (nameError != null && !allowIncompleteFields)
                {
                    errors.Add(node.Name + ": " + nameError);
                }
                if (node.ParentId.HasValue && Find(node.ParentId.Value) == null)
                {
                    errors.Add(node.Name + " 的父 Link 不存在。");
                }
                if (node.ParentId.HasValue && ValidateRosName(node.JointName) != null &&
                    !allowIncompleteFields)
                {
                    errors.Add(node.Name + " 的 Joint 名称无效。");
                }
                if (node.ParentId.HasValue &&
                    string.IsNullOrWhiteSpace(node.JointType) &&
                    !allowIncompleteFields)
                {
                    errors.Add(node.Name + " 的 Joint 类型尚未选择。");
                }
                else if (node.ParentId.HasValue &&
                    !string.IsNullOrWhiteSpace(node.JointType) &&
                    !SupportedJointTypes.Contains(node.JointType))
                {
                    errors.Add(node.Name + " 的 Joint 类型不受支持：" + node.JointType + "。");
                }
            }

            if (Nodes.Where(node => !allowIncompleteFields || !string.IsNullOrWhiteSpace(node.Name))
                .GroupBy(node => node.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            {
                errors.Add("Link 名称不能重复。");
            }
            if (Nodes.Where(node => node.ParentId.HasValue)
                .Where(node => !allowIncompleteFields || !string.IsNullOrWhiteSpace(node.JointName))
                .GroupBy(node => node.JointName, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
            {
                errors.Add("Joint 名称不能重复。");
            }

            foreach (LinkTreeNode node in Nodes)
            {
                HashSet<Guid> visited = new HashSet<Guid>();
                LinkTreeNode current = node;
                while (current != null && current.ParentId.HasValue)
                {
                    if (!visited.Add(current.Id))
                    {
                        errors.Add("Link 树中存在循环关系。");
                        return errors.Distinct().ToList();
                    }
                    current = Find(current.ParentId.Value);
                }
            }
            return errors.Distinct().ToList();
        }

        public static string ValidateRosName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Link 名称不能为空。";
            }
            if (!RosNamePattern.IsMatch(value))
            {
                return "名称只能包含字母、数字和下划线，且不能以数字开头。";
            }
            return null;
        }

        public static string BuildDefaultJointName(string linkName)
        {
            string baseName = linkName ?? string.Empty;
            if (baseName.EndsWith("_link", StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName.Substring(0, baseName.Length - "_link".Length);
            }
            return baseName + "_joint";
        }

        public static bool UsesDefaultJointName(
            string jointName,
            string parentName,
            string linkName)
        {
            return string.Equals(
                    jointName,
                    BuildDefaultJointName(linkName),
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    jointName,
                    parentName + "_" + linkName + "_joint",
                    StringComparison.OrdinalIgnoreCase);
        }

        public static LinkTreeNode NewNode(string name, Guid? parentId, double x, double y)
        {
            return new LinkTreeNode
            {
                Id = Guid.NewGuid(),
                ParentId = parentId,
                Name = name,
                JointName = parentId.HasValue ? BuildDefaultJointName(name) : string.Empty,
                JointType = string.Empty,
                X = x,
                Y = y
            };
        }
    }
}
