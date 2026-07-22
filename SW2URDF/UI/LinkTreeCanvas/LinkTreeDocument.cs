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
            while (current != null && current.ParentId.HasValue)
            {
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
            List<string> errors = new List<string>();
            if (Nodes.Count == 0)
            {
                errors.Add("Link 树不能为空。");
                return errors;
            }
            if (Nodes.GroupBy(node => node.Id).Any(group => group.Count() > 1))
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
                if (nameError != null)
                {
                    errors.Add(node.Name + ": " + nameError);
                }
                if (node.ParentId.HasValue && Find(node.ParentId.Value) == null)
                {
                    errors.Add(node.Name + " 的父 Link 不存在。");
                }
                if (node.ParentId.HasValue && ValidateRosName(node.JointName) != null)
                {
                    errors.Add(node.Name + " 的 Joint 名称无效。");
                }
                if (node.ParentId.HasValue && string.IsNullOrWhiteSpace(node.JointType))
                {
                    errors.Add(node.Name + " 的 Joint 类型不能为空。");
                }
                else if (node.ParentId.HasValue && !SupportedJointTypes.Contains(node.JointType))
                {
                    errors.Add(node.Name + " 的 Joint 类型不受支持：" + node.JointType + "。");
                }
            }

            if (Nodes.GroupBy(node => node.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            {
                errors.Add("Link 名称不能重复。");
            }
            if (Nodes.Where(node => node.ParentId.HasValue)
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

        public static string BuildDefaultJointName(string parentName, string linkName)
        {
            return parentName + "_" + linkName + "_joint";
        }

        public static bool UsesDefaultJointName(
            string jointName,
            string parentName,
            string linkName)
        {
            return string.Equals(
                jointName,
                BuildDefaultJointName(parentName, linkName),
                StringComparison.OrdinalIgnoreCase);
        }

        public static LinkTreeNode NewNode(string name, Guid? parentId, double x, double y)
        {
            return new LinkTreeNode
            {
                Id = Guid.NewGuid(),
                ParentId = parentId,
                Name = name,
                JointName = parentId.HasValue ? name.Replace("_link", "") + "_joint" : string.Empty,
                JointType = parentId.HasValue ? "fixed" : string.Empty,
                X = x,
                Y = y
            };
        }
    }
}
