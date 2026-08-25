using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SW2URDF.UI.LinkTreeCanvas
{
    public sealed class LinkTreeOutlineParseResult
    {
        public LinkTreeDocument Document { get; private set; }
        public IList<string> Errors { get; private set; }

        public bool IsValid
        {
            get { return Document != null && Errors.Count == 0; }
        }

        public LinkTreeOutlineParseResult(LinkTreeDocument document, IList<string> errors)
        {
            Document = document;
            Errors = errors ?? new List<string>();
        }
    }

    /// <summary>
    /// Converts Link topology to and from a small Markdown heading outline.
    /// Joint and CAD data remain owned by existing nodes matched by Link name.
    /// </summary>
    public static class LinkTreeOutline
    {
        private static readonly Regex HeadingPattern = new Regex("^\\s*(#+)\\s*(.*?)\\s*$");

        public static string Serialize(LinkTreeDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            StringBuilder builder = new StringBuilder();
            LinkTreeNode root = document.Root;
            if (root != null)
            {
                AppendNode(builder, document, root, 1);
            }
            return builder.ToString().TrimEnd('\r', '\n');
        }

        public static LinkTreeOutlineParseResult Parse(string text, LinkTreeDocument source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            List<string> errors = new List<string>();
            List<OutlineLine> outline = ParseLines(text, errors);
            if (errors.Count > 0)
            {
                return new LinkTreeOutlineParseResult(null, errors);
            }

            LinkTreeDocument candidate = BuildDocument(outline, source, errors);
            if (candidate != null)
            {
                errors.AddRange(candidate.Validate());
            }
            return new LinkTreeOutlineParseResult(errors.Count == 0 ? candidate : null, errors);
        }

        private static void AppendNode(
            StringBuilder builder,
            LinkTreeDocument document,
            LinkTreeNode node,
            int level)
        {
            builder.Append(new string('#', level));
            builder.Append(' ');
            builder.AppendLine(node.Name);
            foreach (LinkTreeNode child in document.ChildrenOf(node.Id))
            {
                AppendNode(builder, document, child, level + 1);
            }
        }

        private static List<OutlineLine> ParseLines(string text, IList<string> errors)
        {
            List<OutlineLine> result = new List<OutlineLine>();
            string[] lines = Regex.Split(text ?? string.Empty, "\\r\\n|\\n|\\r");
            Dictionary<int, int> lastOutlineIndexAtLevel = new Dictionary<int, int>();
            int previousLevel = 0;

            for (int index = 0; index < lines.Length; index++)
            {
                string raw = lines[index];
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                Match match = HeadingPattern.Match(raw);
                if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[2].Value))
                {
                    errors.Add("第 " + (index + 1) + " 行必须使用 # LinkName 格式。");
                    continue;
                }

                int level = match.Groups[1].Value.Length;
                string name = match.Groups[2].Value.Trim();
                string nameError = LinkTreeDocument.ValidateRosName(name);
                if (nameError != null)
                {
                    errors.Add("第 " + (index + 1) + " 行：" + nameError);
                }
                if (result.Count == 0 && level != 1)
                {
                    errors.Add("第一个 Link 必须是一级标题 #。");
                }
                else if (result.Count > 0 && level == 1)
                {
                    errors.Add("第 " + (index + 1) + " 行创建了第二个根 Link。");
                }
                if (previousLevel > 0 && level > previousLevel + 1)
                {
                    errors.Add("第 " + (index + 1) + " 行层级跳跃，不能从 " +
                        previousLevel + " 级直接跳到 " + level + " 级。");
                }

                int parentOutlineIndex = -1;
                if (level > 1)
                {
                    lastOutlineIndexAtLevel.TryGetValue(level - 1, out parentOutlineIndex);
                }
                result.Add(new OutlineLine(index + 1, level, name, parentOutlineIndex));
                lastOutlineIndexAtLevel[level] = result.Count - 1;
                foreach (int deeperLevel in lastOutlineIndexAtLevel.Keys
                    .Where(item => item > level)
                    .ToList())
                {
                    lastOutlineIndexAtLevel.Remove(deeperLevel);
                }
                previousLevel = level;
            }

            if (result.Count == 0)
            {
                errors.Add("Link 树大纲不能为空。");
            }
            foreach (IGrouping<string, OutlineLine> duplicate in result
                .GroupBy(line => line.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
            {
                errors.Add("Link 名称不能重复：" + duplicate.Key + "。");
            }
            return result;
        }

        private static LinkTreeDocument BuildDocument(
            IList<OutlineLine> outline,
            LinkTreeDocument source,
            IList<string> errors)
        {
            if (outline.Count == 0)
            {
                return null;
            }

            Dictionary<string, LinkTreeNode> sourceByName = source.Nodes
                .GroupBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
            IDictionary<int, LinkTreeNode> matchedOriginals = MatchOriginalNodes(
                outline,
                source,
                sourceByName);
            HashSet<string> outlineNames = new HashSet<string>(
                outline.Select(line => line.Name),
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Guid> reservedJointOwners = source.Nodes
                .Where(node => node.ParentId.HasValue &&
                    outlineNames.Contains(node.Name) &&
                    !string.IsNullOrWhiteSpace(node.JointName))
                .GroupBy(node => node.JointName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Id,
                    StringComparer.OrdinalIgnoreCase);
            Dictionary<int, LinkTreeNode> parentAtLevel = new Dictionary<int, LinkTreeNode>();
            LinkTreeDocument candidate = new LinkTreeDocument();

            for (int index = 0; index < outline.Count; index++)
            {
                OutlineLine line = outline[index];
                LinkTreeNode parent = null;
                if (line.Level > 1 && !parentAtLevel.TryGetValue(line.Level - 1, out parent))
                {
                    errors.Add("第 " + line.LineNumber + " 行找不到上一级 Link。");
                    continue;
                }

                LinkTreeNode original;
                matchedOriginals.TryGetValue(index, out original);

                LinkTreeNode node = original == null
                    ? LinkTreeDocument.NewNode(line.Name, parent == null ? (Guid?)null : parent.Id, 0, 0)
                    : original.Clone();
                LinkTreeNode previousParent = original != null && original.ParentId.HasValue
                    ? source.Find(original.ParentId.Value)
                    : null;
                bool usedGeneratedJointName = original != null && previousParent != null &&
                    LinkTreeDocument.UsesDefaultJointName(
                        original.JointName,
                        previousParent.Name,
                        original.Name);

                node.Name = line.Name;
                node.ParentId = parent == null ? (Guid?)null : parent.Id;
                node.X = 80 + (line.Level - 1) * 300;
                node.Y = 90 + index * 118;

                if (parent == null)
                {
                    node.JointName = string.Empty;
                    node.JointType = string.Empty;
                }
                else if (original == null || usedGeneratedJointName)
                {
                    node.JointName = MakeUniqueJointName(
                        candidate,
                        reservedJointOwners,
                        LinkTreeDocument.BuildDefaultJointName(node.Name),
                        original == null ? (Guid?)null : original.Id);
                    if (string.IsNullOrWhiteSpace(node.JointType))
                    {
                        node.JointType = "fixed";
                    }
                }

                candidate.Nodes.Add(node);
                parentAtLevel[line.Level] = node;
                foreach (int deeperLevel in parentAtLevel.Keys.Where(level => level > line.Level).ToList())
                {
                    parentAtLevel.Remove(deeperLevel);
                }
            }
            return errors.Count == 0 ? candidate : null;
        }

        private static IDictionary<int, LinkTreeNode> MatchOriginalNodes(
            IList<OutlineLine> outline,
            LinkTreeDocument source,
            IDictionary<string, LinkTreeNode> sourceByName)
        {
            Dictionary<int, LinkTreeNode> matches = new Dictionary<int, LinkTreeNode>();
            HashSet<Guid> usedIds = new HashSet<Guid>();
            if (outline.Count == 0 || source.Root == null)
            {
                return matches;
            }

            matches[0] = source.Root;
            usedIds.Add(source.Root.Id);

            for (int index = 1; index < outline.Count; index++)
            {
                LinkTreeNode exactMatch;
                if (sourceByName.TryGetValue(outline[index].Name, out exactMatch) &&
                    usedIds.Add(exactMatch.Id))
                {
                    matches[index] = exactMatch;
                }
            }

            int maximumLevel = outline.Max(line => line.Level);
            for (int parentLevel = 1; parentLevel < maximumLevel; parentLevel++)
            {
                for (int parentIndex = 0; parentIndex < outline.Count; parentIndex++)
                {
                    if (outline[parentIndex].Level != parentLevel)
                    {
                        continue;
                    }

                    LinkTreeNode originalParent;
                    if (!matches.TryGetValue(parentIndex, out originalParent))
                    {
                        continue;
                    }

                    List<int> unmatchedChildren = Enumerable.Range(0, outline.Count)
                        .Where(index => outline[index].ParentOutlineIndex == parentIndex &&
                            !matches.ContainsKey(index))
                        .ToList();
                    List<LinkTreeNode> unmatchedOriginalChildren = source
                        .ChildrenOf(originalParent.Id)
                        .Where(node => !usedIds.Contains(node.Id))
                        .ToList();

                    // Equal unmatched counts are the only unambiguous plain-text rename case.
                    if (unmatchedChildren.Count == 0 ||
                        unmatchedChildren.Count != unmatchedOriginalChildren.Count)
                    {
                        continue;
                    }

                    for (int childIndex = 0; childIndex < unmatchedChildren.Count; childIndex++)
                    {
                        LinkTreeNode original = unmatchedOriginalChildren[childIndex];
                        matches[unmatchedChildren[childIndex]] = original;
                        usedIds.Add(original.Id);
                    }
                }
            }
            return matches;
        }

        private static string MakeUniqueJointName(
            LinkTreeDocument document,
            IDictionary<string, Guid> reservedJointOwners,
            string baseName,
            Guid? ownerId)
        {
            string candidate = baseName;
            int suffix = 1;
            Guid reservedOwner;
            while (document.Nodes.Any(node =>
                    string.Equals(node.JointName, candidate, StringComparison.OrdinalIgnoreCase)) ||
                (reservedJointOwners.TryGetValue(candidate, out reservedOwner) &&
                    (!ownerId.HasValue || reservedOwner != ownerId.Value)))
            {
                candidate = baseName + "_" + suffix++;
            }
            return candidate;
        }

        private sealed class OutlineLine
        {
            public int LineNumber { get; private set; }
            public int Level { get; private set; }
            public string Name { get; private set; }
            public int ParentOutlineIndex { get; private set; }

            public OutlineLine(int lineNumber, int level, string name, int parentOutlineIndex)
            {
                LineNumber = lineNumber;
                Level = level;
                Name = name;
                ParentOutlineIndex = parentOutlineIndex;
            }
        }
    }
}
