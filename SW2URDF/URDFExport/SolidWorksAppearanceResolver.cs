using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SwTexture = SolidWorks.Interop.sldworks.Texture;

namespace SW2URDF.URDFExport
{
    public static class SolidWorksAppearanceResolver
    {
        private static readonly HashSet<string> ImageExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".bmp", ".gif", ".jpeg", ".jpg", ".png", ".tif", ".tiff"
            };

        public static bool Resolve(Link link)
        {
            if (link == null || link.Visual == null || link.Visual.Material == null)
            {
                return false;
            }

            IList<Component2> components = link.SWComponents ?? new List<Component2>();
            double[] rgba;
            bool colorResolved = TryResolveComponentMaterial(components, out rgba) ||
                TryResolveModelMaterial(components, out rgba);
            if (colorResolved)
            {
                link.Visual.Material.Color.SetColor(rgba);
            }

            string textureMaterialName;
            string imagePath;
            bool textureResolved = TryResolveTexture(components, out textureMaterialName, out imagePath);
            link.Visual.Material.Name = CreateMaterialName(textureMaterialName, link.Name);

            string existingImagePath = link.Visual.Material.Texture.wFilename;
            if (!String.IsNullOrWhiteSpace(imagePath))
            {
                link.Visual.Material.Texture.wFilename = imagePath;
            }
            else if (!IsExistingImageFile(existingImagePath))
            {
                link.Visual.Material.Texture.wFilename = String.Empty;
            }

            link.Visual.Material.AppearanceAutomaticallyResolved = true;

            return colorResolved || textureResolved;
        }

        public static bool ResolveIfUnset(Link link)
        {
            if (link == null || link.Visual == null || link.Visual.Material == null)
            {
                return false;
            }


            Material material = link.Visual.Material;
            if (!material.AppearanceAutomaticallyResolved && HasExplicitMaterial(material))
            {
                return false;
            }

            return Resolve(link);
        }

        private static bool HasExplicitMaterial(Material material)
        {
            if (!String.IsNullOrWhiteSpace(material.Name) ||
                !String.IsNullOrWhiteSpace(material.Texture.wFilename))
            {
                return true;
            }

            double[] rgba = material.Color.GetColor();
            double[] defaults = { 1.0, 1.0, 1.0, 1.0 };
            for (int index = 0; index < defaults.Length; index++)
            {
                if (Math.Abs(rgba[index] - defaults[index]) > 1e-12)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool TryParseMaterialValues(object materialValues, out double[] rgba)
        {
            rgba = null;
            double[] values = materialValues as double[];
            if (values == null || values.Length != 9)
            {
                return false;
            }

            for (int index = 0; index < values.Length; index++)
            {
                double value = values[index];
                if (Double.IsNaN(value) || Double.IsInfinity(value) || value < 0.0 || value > 1.0)
                {
                    return false;
                }
            }

            rgba = new[] { values[0], values[1], values[2], 1.0 - values[7] };
            return true;
        }

        private static bool TryResolveComponentMaterial(
            IEnumerable<Component2> components,
            out double[] rgba)
        {
            foreach (Component2 component in components)
            {
                if (component == null)
                {
                    continue;
                }

                try
                {
                    if (TryParseMaterialValues(component.MaterialPropertyValues, out rgba))
                    {
                        return true;
                    }
                }
                catch (COMException)
                {
                }
                catch (InvalidComObjectException)
                {
                }
            }

            rgba = null;
            return false;
        }

        private static bool TryResolveModelMaterial(
            IEnumerable<Component2> components,
            out double[] rgba)
        {
            rgba = null;
            foreach (Component2 component in components)
            {
                if (component == null)
                {
                    continue;
                }

                string configuration = TryGetReferencedConfiguration(component);
                try
                {
                    if (TryParseMaterialValues(
                        component.GetModelMaterialPropertyValues(configuration),
                        out rgba))
                    {
                        return true;
                    }
                }
                catch (COMException)
                {
                }
                catch (InvalidComObjectException)
                {
                }

                try
                {
                    ModelDoc2 model = component.GetModelDoc2() as ModelDoc2;
                    double[] documentRgba = null;
                    if (model != null &&
                        TryParseMaterialValues(model.MaterialPropertyValues, out documentRgba))
                    {
                        rgba = documentRgba;
                        return true;
                    }
                }
                catch (COMException)
                {
                }
                catch (InvalidComObjectException)
                {
                }
            }

            rgba = null;
            return false;
        }

        private static bool TryResolveTexture(
            IEnumerable<Component2> components,
            out string materialName,
            out string imagePath)
        {
            foreach (Component2 component in components)
            {
                if (component == null)
                {
                    continue;
                }

                string configuration = TryGetReferencedConfiguration(component);
                SwTexture texture = TryGetTexture(component, configuration, false);
                try
                {
                    if (TryReadTexture(texture, out materialName, out imagePath))
                    {
                        return true;
                    }
                }
                finally
                {
                    ReleaseComReference(texture);
                }
            }

            foreach (Component2 component in components)
            {
                if (component == null)
                {
                    continue;
                }

                string configuration = TryGetReferencedConfiguration(component);
                SwTexture texture = TryGetTexture(component, configuration, true);
                try
                {
                    if (TryReadTexture(texture, out materialName, out imagePath))
                    {
                        return true;
                    }
                }
                finally
                {
                    ReleaseComReference(texture);
                }
            }

            materialName = null;
            imagePath = null;
            return false;
        }

        private static SwTexture TryGetTexture(
            Component2 component,
            string configuration,
            bool modelTexture)
        {
            try
            {
                return modelTexture
                    ? component.GetModelTexture(configuration)
                    : component.GetTexture(configuration);
            }
            catch (COMException)
            {
                return null;
            }
            catch (InvalidComObjectException)
            {
                return null;
            }
        }

        private static bool TryReadTexture(
            SwTexture texture,
            out string materialName,
            out string imagePath)
        {
            materialName = null;
            imagePath = null;
            if (texture == null)
            {
                return false;
            }

            try
            {
                materialName = texture.MaterialName;
                if (String.IsNullOrWhiteSpace(materialName))
                {
                    return false;
                }

                if (IsExistingImageFile(materialName))
                {
                    imagePath = Path.GetFullPath(materialName);
                }
                return true;
            }
            catch (COMException)
            {
                materialName = null;
                return false;
            }
            catch (InvalidComObjectException)
            {
                materialName = null;
                return false;
            }
            catch (ArgumentException)
            {
                materialName = null;
                return false;
            }
            catch (NotSupportedException)
            {
                materialName = null;
                return false;
            }
        }

        private static string TryGetReferencedConfiguration(Component2 component)
        {
            try
            {
                return component.ReferencedConfiguration ?? String.Empty;
            }
            catch (COMException)
            {
                return String.Empty;
            }
            catch (InvalidComObjectException)
            {
                return String.Empty;
            }
        }

        private static bool IsExistingImageFile(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                return ImageExtensions.Contains(Path.GetExtension(path)) && File.Exists(path);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        private static string CreateMaterialName(string textureMaterialName, string linkName)
        {
            string source = String.IsNullOrWhiteSpace(textureMaterialName)
                ? linkName
                : GetTextureName(textureMaterialName);
            if (String.IsNullOrWhiteSpace(source))
            {
                source = "material";
            }

            char[] characters = source.Trim().ToCharArray();
            for (int index = 0; index < characters.Length; index++)
            {
                char character = characters[index];
                if (!Char.IsLetterOrDigit(character) && character != '_' && character != '-' && character != '.')
                {
                    characters[index] = '_';
                }
            }

            string stableName = new string(characters);
            return stableName.StartsWith("material_", StringComparison.Ordinal)
                ? stableName
                : "material_" + stableName;
        }

        private static string GetTextureName(string materialName)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(materialName);
                return String.IsNullOrWhiteSpace(fileName) ? materialName : fileName;
            }
            catch (ArgumentException)
            {
                return materialName;
            }
        }

        private static void ReleaseComReference(object value)
        {
            try
            {
                if (value != null && Marshal.IsComObject(value))
                {
                    Marshal.ReleaseComObject(value);
                }
            }
            catch (InvalidComObjectException)
            {
            }
            catch (COMException)
            {
            }
        }
    }
}
