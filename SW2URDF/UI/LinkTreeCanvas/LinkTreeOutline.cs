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
    /// Stable heading IDs own configuration; names and positions never infer a rename.
    /// </summary>
    public static class LinkTreeOutline
    {
        private static readonly Regex HeadingPattern = new Regex("^\\s*(#+)\\s*(.*?)\\s*$");
        private static readonly Regex IdentityPattern = new Regex(
            @"^(.*?)\s*<!--\s*link-id:([0-9a-fA-F-]+)\s*-->\s*$");

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
                // Outline editing owns topology only. New Joints remain intentionally
                // unconfigured until the user chooses their types on the canvas.
                errors.AddRange(candidate.ValidateDraft());
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
            builder.Append(node.Name);
            builder.Append(" <!-- link-id:");
            builder.Append(node.Id.ToString("D"));
            builder.AppendLine(" -->");
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
                Guid? identity = null;
                Match identityMatch = IdentityPattern.Match(name);
                if (identityMatch.Success)
                {
                    Guid parsedId;
                    if (!Guid.TryParse(identityMatch.Groups[2].Value, out parsedId) || parsedId == Guid.Empty)
                        errors.Add("Invalid Link identity on line " + (index + 1) + ".");
                    else
                        identity = parsedId;
                    name = identityMatch.Groups[1].Value.Trim();
                }
                string nameError = LinkTreeDocument.ValidateRosName(name);
                if (nameError != null && !identity.HasValue)
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
                result.Add(new OutlineLine(index + 1, level, name, parentOutlineIndex, identity));
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
                .Where(line => !string.IsNullOrWhiteSpace(line.Name))
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
                .Where(node => !string.IsNullOrWhiteSpace(node.Name))
                .GroupBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
            IDictionary<int, LinkTreeNode> matchedOriginals = MatchOriginalNodes(
                outline,
                source,
                sourceByName, errors);
            if (errors.Count > 0) return null;
            Dictionary<string, Guid> reservedJointOwners = matchedOriginals.Values
                .Where(node => node.ParentId.HasValue &&
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
            IDictionary<string, LinkTreeNode> sourceByName,
            IList<string> errors)
        {
            Dictionary<int, LinkTreeNode> matches = new Dictionary<int, LinkTreeNode>();
            HashSet<Guid> usedIds = new HashSet<Guid>();
            if (outline.Count == 0 || source.Root == null)
            {
                return matches;
            }

            for (int index = 0; index < outline.Count; index++)
            {
                if (!outline[index].Identity.HasValue) continue;
                LinkTreeNode original = source.Find(outline[index].Identity.Value);
                if (original == null || !usedIds.Add(original.Id))
                {
                    errors.Add("Unknown or duplicate Link identity on line " + outline[index].LineNumber +
                        ". Reset the outline; keep each existing link-id once and omit it only for a new Link.");
                    continue;
                }
                matches[index] = original;
            }
            if (matches.ContainsKey(0) && matches[0].Id != source.Root.Id)
                errors.Add("The root Link identity cannot be changed.");
            if (!matches.ContainsKey(0))
            {
                LinkTreeNode namedRoot;
                if (sourceByName.TryGetValue(outline[0].Name, out namedRoot) && namedRoot.Id != source.Root.Id)
                    errors.Add("The root Link cannot be replaced by an existing child. Keep the root link-id when renaming it.");
                matches[0] = source.Root;
                if (!usedIds.Add(source.Root.Id)) errors.Add("The root Link cannot become a child.");
            }
            for (int index = 1; index < outline.Count; index++)
            {
                if (matches.ContainsKey(index)) continue;
                LinkTreeNode original;
                if (sourceByName.TryGetValue(outline[index].Name, out original) &&
                    !usedIds.Contains(original.Id))
                {
                    matches[index] = original;
                    usedIds.Add(original.Id);
                }
            }
            if (matches.Count < outline.Count && usedIds.Count < source.Nodes.Count)
            {
                errors.Add("Ambiguous Link rename/add/delete. Reset the outline and keep the link-id comments " +
                    "when renaming or moving existing Links. Apply deletions separately before adding new Links.");
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
            public Guid? Identity { get; private set; }

            public OutlineLine(int lineNumber, int level, string name, int parentOutlineIndex, Guid? identity)
            {
                LineNumber = lineNumber;
                Level = level;
                Name = name;
                ParentOutlineIndex = parentOutlineIndex;
                Identity = identity;
            }
        }
    }
}
