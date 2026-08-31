using Microsoft.VisualBasic.FileIO;
using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.URDFExport.CSV;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Rebuilds current-format test state without teaching production code to read
    /// the name-based configuration embedded in the historical example models.
    /// </summary>
    internal static class TestConfigurationFactory
    {
        private sealed class CadBinding
        {
            public string[] ComponentNames;
            public string FrameName;
            public string AxisName;
        }

        public static LinkNode CreateConfiguredBaseNode(
            ModelDoc2 model,
            string csvPath)
        {
            if (model == null)
            {
                throw new ArgumentNullException("model");
            }
            if (string.IsNullOrWhiteSpace(csvPath))
            {
                throw new ArgumentException("A CSV fixture path is required.", "csvPath");
            }

            List<Link> links;
            using (FileStream stream = File.OpenRead(csvPath))
            {
                links = ImportExport.LoadURDFRobotFromCSV(stream);
            }

            Dictionary<string, CadBinding> bindings = ReadCadBindings(csvPath);
            Link baseLink = BuildTree(links);
            BindCadState(model, baseLink, bindings);

            LinkNode baseNode = new LinkNode(baseLink);
            LinkTreeRootJointPolicy.Normalize(baseNode);
            return baseNode;
        }

        private static Link BuildTree(IList<Link> links)
        {
            Dictionary<string, Link> byName = links.ToDictionary(
                link => link.Name,
                StringComparer.Ordinal);

            foreach (Link link in links)
            {
                link.Parent = null;
                link.Children.Clear();
            }

            Link root = null;
            foreach (Link link in links)
            {
                string parentName = link.Joint == null || link.Joint.Parent == null
                    ? string.Empty
                    : link.Joint.Parent.Name;
                if (string.IsNullOrWhiteSpace(parentName))
                {
                    Assert.Null(root);
                    root = link;
                    continue;
                }

                Assert.True(
                    byName.TryGetValue(parentName, out Link parent),
                    "CSV fixture references an unknown parent Link: " + parentName);
                link.Parent = parent;
                parent.Children.Add(link);
            }

            Assert.NotNull(root);
            return root;
        }

        private static void BindCadState(
            ModelDoc2 model,
            Link root,
            IDictionary<string, CadBinding> bindings)
        {
            ReferenceGeometryCatalog catalog = new ReferenceGeometryCatalog(model);
            AssemblyDoc assembly = model as AssemblyDoc;
            Assert.NotNull(assembly);

            foreach (Link link in Enumerate(root))
            {
                Assert.True(
                    bindings.TryGetValue(link.Name, out CadBinding binding),
                    "CSV fixture has no CAD binding row for Link " + link.Name);

                link.SWComponents.Clear();
                link.SWComponentPIDs.Clear();
                foreach (string componentName in binding.ComponentNames)
                {
                    Component2 component = assembly.GetComponentByName(componentName);
                    Assert.NotNull(component);
                    byte[] componentId = CommonSwOperations.SaveSWComponent(model, component);
                    Assert.NotNull(componentId);
                    link.SWComponents.Add(component);
                    link.SWComponentPIDs.Add(componentId);
                }

                Assert.NotEmpty(link.SWComponents);
                link.SWMainComponent = link.SWComponents[0];
                link.SWMainComponentPID = (byte[])link.SWComponentPIDs[0].Clone();
                link.FrameReference = FindReference(
                    catalog.CoordinateSystems,
                    binding.FrameName,
                    link.Name);

                if (link.Parent == null)
                {
                    link.Joint.AxisReference = CadFeatureReference.None(
                        ReferenceGeometryKind.Axis);
                }
                else
                {
                    link.Joint.AxisReference = FindReference(
                        catalog.Axes,
                        binding.AxisName,
                        link.Name);
                    link.Joint.MarkManualConfiguration(
                        "Current-format integration fixture reconstructed from reviewed sample data.");
                }
            }
        }

        private static CadFeatureReference FindReference(
            IEnumerable<ReferenceGeometryEntry> entries,
            string name,
            string linkName)
        {
            string featureName = name;
            string componentPath = string.Empty;
            int componentMarker = name.LastIndexOf(" <", StringComparison.Ordinal);
            if (componentMarker >= 0 && name.EndsWith(">", StringComparison.Ordinal))
            {
                featureName = name.Substring(0, componentMarker);
                componentPath = name.Substring(
                    componentMarker + 2,
                    name.Length - componentMarker - 3);
            }

            ReferenceGeometryEntry[] matches = entries
                .Where(entry =>
                    string.Equals(entry.DisplayName, featureName, StringComparison.Ordinal) &&
                    ComponentPathMatches(entry.ComponentPath, componentPath))
                .ToArray();
            Assert.True(
                matches.Length == 1,
                string.Format(
                    "Expected one reference named '{0}' for Link '{1}', found {2}.",
                    name,
                    linkName,
                    matches.Length));
            return matches[0].Reference.Clone();
        }

        private static bool ComponentPathMatches(
            string candidatePath,
            string expectedPath)
        {
            candidatePath = candidatePath ?? string.Empty;
            expectedPath = expectedPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expectedPath))
            {
                return string.IsNullOrWhiteSpace(candidatePath);
            }

            return string.Equals(candidatePath, expectedPath, StringComparison.Ordinal) ||
                candidatePath.EndsWith("/" + expectedPath, StringComparison.Ordinal);
        }

        private static IEnumerable<Link> Enumerate(Link root)
        {
            yield return root;
            foreach (Link child in root.Children)
            {
                foreach (Link descendant in Enumerate(child))
                {
                    yield return descendant;
                }
            }
        }

        private static Dictionary<string, CadBinding> ReadCadBindings(string csvPath)
        {
            Dictionary<string, CadBinding> bindings =
                new Dictionary<string, CadBinding>(StringComparer.Ordinal);
            using (TextFieldParser parser = new TextFieldParser(csvPath))
            {
                parser.SetDelimiters(",");
                parser.HasFieldsEnclosedInQuotes = true;
                string[] headers = parser.ReadFields();
                int linkIndex = FindColumn(headers, "Link Name");
                int componentIndex = FindColumn(headers, "SW Components");
                int frameIndex = FindColumn(headers, "Coordinate System");
                int axisIndex = FindColumn(headers, "Axis Name");

                while (!parser.EndOfData)
                {
                    string[] fields = parser.ReadFields();
                    string linkName = ReadField(fields, linkIndex);
                    string components = ReadField(fields, componentIndex);
                    bindings.Add(linkName, new CadBinding
                    {
                        ComponentNames = components
                            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(name => name.Trim())
                            .ToArray(),
                        FrameName = ReadField(fields, frameIndex),
                        AxisName = ReadField(fields, axisIndex)
                    });
                }
            }
            return bindings;
        }

        private static int FindColumn(string[] headers, string name)
        {
            int index = Array.FindIndex(
                headers,
                header => string.Equals(header, name, StringComparison.Ordinal));
            Assert.True(index >= 0, "CSV fixture is missing column " + name);
            return index;
        }

        private static string ReadField(string[] fields, int index)
        {
            return fields != null && index >= 0 && index < fields.Length
                ? fields[index] ?? string.Empty
                : string.Empty;
        }
    }
}
