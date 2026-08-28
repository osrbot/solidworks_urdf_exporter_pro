using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SW2URDF.URDFExport
{
    internal sealed class AutomaticLinkColorAssignment
    {
        internal AutomaticLinkColorAssignment(string materialId, double[] rgba)
        {
            MaterialId = materialId;
            Rgba = (double[])rgba.Clone();
        }

        internal string MaterialId { get; private set; }

        internal double[] Rgba { get; private set; }
    }

    internal static class AutomaticLinkColorScheme
    {
        private static readonly double[][][] LevelPalettes =
        {
            new[]
            {
                Rgba(0.18, 0.42, 0.78), Rgba(0.10, 0.50, 0.72),
                Rgba(0.24, 0.36, 0.68), Rgba(0.12, 0.58, 0.82),
                Rgba(0.30, 0.48, 0.82), Rgba(0.08, 0.38, 0.66)
            },
            new[]
            {
                Rgba(0.05, 0.58, 0.66), Rgba(0.08, 0.66, 0.62),
                Rgba(0.12, 0.52, 0.58), Rgba(0.10, 0.70, 0.72),
                Rgba(0.18, 0.60, 0.70), Rgba(0.04, 0.48, 0.56)
            },
            new[]
            {
                Rgba(0.18, 0.62, 0.34), Rgba(0.34, 0.68, 0.22),
                Rgba(0.08, 0.54, 0.38), Rgba(0.48, 0.70, 0.18),
                Rgba(0.24, 0.58, 0.20), Rgba(0.12, 0.68, 0.48)
            },
            new[]
            {
                Rgba(0.88, 0.58, 0.08), Rgba(0.94, 0.48, 0.06),
                Rgba(0.78, 0.50, 0.10), Rgba(0.92, 0.66, 0.12),
                Rgba(0.82, 0.40, 0.08), Rgba(0.96, 0.56, 0.18)
            },
            new[]
            {
                Rgba(0.82, 0.18, 0.14), Rgba(0.90, 0.28, 0.10),
                Rgba(0.72, 0.16, 0.24), Rgba(0.88, 0.22, 0.32),
                Rgba(0.76, 0.30, 0.12), Rgba(0.92, 0.36, 0.20)
            }
        };

        private static readonly HashSet<string> SideTokens =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "left", "right", "lhs", "rhs", "port", "starboard"
            };

        internal static AutomaticLinkColorAssignment GetAssignment(
            string linkName,
            int depth,
            int maximumDepth)
        {
            int safeDepth = Math.Max(0, depth);
            int safeMaximumDepth = Math.Max(safeDepth, maximumDepth);
            double progress = safeMaximumDepth == 0
                ? 0.0
                : safeDepth / (double)safeMaximumDepth;
            int paletteIndex = (int)Math.Round(
                progress * (LevelPalettes.Length - 1),
                MidpointRounding.AwayFromZero);

            string canonicalName = CanonicalizeName(linkName);
            double[][] palette = LevelPalettes[paletteIndex];
            int colorIndex = (int)(StableHash(canonicalName) % (uint)palette.Length);
            string materialId = String.Format(
                CultureInfo.InvariantCulture,
                "auto_l{0:D2}_{1}",
                safeDepth,
                canonicalName);
            return new AutomaticLinkColorAssignment(materialId, palette[colorIndex]);
        }

        internal static int Apply(Link root)
        {
            if (root == null)
            {
                return 0;
            }

            int maximumDepth = GetMaximumDepth(root, 0);
            return Apply(root, 0, maximumDepth);
        }

        internal static void Apply(Link link, AutomaticLinkColorAssignment assignment)
        {
            if (link == null || link.Visual == null ||
                link.Visual.Material == null || assignment == null)
            {
                return;
            }

            link.Visual.Material.Name = assignment.MaterialId;
            link.Visual.Material.Color.SetColor(assignment.Rgba);
            link.Visual.Material.AppearanceAutomaticallyResolved = false;
        }

        internal static string CanonicalizeName(string linkName)
        {
            string separated = Regex.Replace(
                linkName ?? String.Empty,
                "([a-z0-9])([A-Z])",
                "$1_$2");
            IEnumerable<string> tokens = Regex
                .Split(separated.ToLowerInvariant(), "[^a-z0-9]+")
                .Where(token => token.Length > 0 && !SideTokens.Contains(token));
            string canonicalName = String.Join("_", tokens);
            return String.IsNullOrWhiteSpace(canonicalName) ? "link" : canonicalName;
        }

        private static int Apply(Link link, int depth, int maximumDepth)
        {
            Apply(link, GetAssignment(link.Name, depth, maximumDepth));
            int count = 1;
            if (link.Children == null)
            {
                return count;
            }

            foreach (Link child in link.Children)
            {
                if (child != null)
                {
                    count += Apply(child, depth + 1, maximumDepth);
                }
            }
            return count;
        }

        private static int GetMaximumDepth(Link link, int depth)
        {
            int maximum = depth;
            if (link.Children == null)
            {
                return maximum;
            }

            foreach (Link child in link.Children)
            {
                if (child != null)
                {
                    maximum = Math.Max(maximum, GetMaximumDepth(child, depth + 1));
                }
            }
            return maximum;
        }

        private static uint StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in value ?? String.Empty)
                {
                    hash ^= character;
                    hash *= 16777619;
                }
                return hash;
            }
        }

        private static double[] Rgba(double red, double green, double blue)
        {
            return new[] { red, green, blue, 1.0 };
        }
    }
}
