using System;
using System.Collections.Generic;

namespace SW2URDF.UI
{
    internal static class MaterialAppearancePresets
    {
        private static readonly IReadOnlyDictionary<string, double[]> Values =
            new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "black", new[] { 0.03, 0.03, 0.03, 1.0 } },
                { "white", new[] { 0.95, 0.95, 0.95, 1.0 } },
                { "gray", new[] { 0.50, 0.50, 0.50, 1.0 } },
                { "dark_gray", new[] { 0.20, 0.20, 0.20, 1.0 } },
                { "red", new[] { 0.75, 0.05, 0.05, 1.0 } },
                { "green", new[] { 0.05, 0.60, 0.10, 1.0 } },
                { "blue", new[] { 0.05, 0.20, 0.80, 1.0 } },
                { "yellow", new[] { 0.95, 0.80, 0.05, 1.0 } },
                { "orange", new[] { 0.95, 0.35, 0.05, 1.0 } },
                { "silver", new[] { 0.75, 0.77, 0.80, 1.0 } },
                { "aluminum", new[] { 0.68, 0.70, 0.73, 1.0 } },
                { "steel", new[] { 0.38, 0.40, 0.43, 1.0 } },
                { "plastic_black", new[] { 0.04, 0.04, 0.04, 1.0 } },
                { "rubber_black", new[] { 0.02, 0.02, 0.02, 1.0 } },
                { "transparent_blue", new[] { 0.10, 0.35, 0.85, 0.35 } }
            };

        internal static bool TryGet(string name, out double[] rgba)
        {
            if (!String.IsNullOrWhiteSpace(name) &&
                Values.TryGetValue(name.Trim(), out double[] stored))
            {
                rgba = (double[])stored.Clone();
                return true;
            }
            rgba = null;
            return false;
        }
    }
}
