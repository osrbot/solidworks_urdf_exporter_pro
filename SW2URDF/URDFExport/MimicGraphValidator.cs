using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW2URDF.URDFExport
{
    internal static class MimicGraphValidator
    {
        public static IList<string> Validate(IEnumerable<Joint> joints)
        {
            List<Joint> jointList = joints == null
                ? new List<Joint>()
                : joints.Where(joint => joint != null).ToList();
            HashSet<string> available = new HashSet<string>(
                jointList
                    .Select(joint => joint.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.Ordinal);
            Dictionary<string, string> references = new Dictionary<string, string>(
                StringComparer.Ordinal);
            List<string> errors = new List<string>();

            foreach (Joint joint in jointList)
            {
                if (joint.Mimic == null || !joint.Mimic.ElementContainsData())
                {
                    continue;
                }

                string owner = joint.Name;
                string target = joint.Mimic.JointName;
                if (string.IsNullOrWhiteSpace(owner))
                {
                    errors.Add("A Mimic Joint must have a Joint name.");
                }
                else if (string.IsNullOrWhiteSpace(target))
                {
                    errors.Add("Mimic Joint '" + owner + "' must select a target Joint.");
                }
                else if (!available.Contains(target))
                {
                    errors.Add("Mimic Joint '" + target + "' does not exist.");
                }
                else if (string.Equals(owner, target, StringComparison.Ordinal))
                {
                    errors.Add("Mimic Joint '" + owner + "' cannot reference itself.");
                }
                else
                {
                    references[owner] = target;
                }
            }

            foreach (string owner in references.Keys)
            {
                HashSet<string> path = new HashSet<string>(StringComparer.Ordinal);
                string current = owner;
                string target;
                while (references.TryGetValue(current, out target))
                {
                    if (!path.Add(current))
                    {
                        errors.Add("Mimic Joint references contain a cycle at '" + current + "'.");
                        break;
                    }
                    current = target;
                }
            }

            return errors.Distinct(StringComparer.Ordinal).ToList();
        }
    }
}
