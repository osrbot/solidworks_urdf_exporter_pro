// Compile against the candidate SW2URDF.dll and SolidWorks interop assemblies.
// With no arguments, only inspects the active assembly. An optional new output directory
// enables a UI smoke test and save/reopen verification on a new assembly copy, never the source.
using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Xml.Linq;

internal static class ProbeLegacyConfiguration
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length == 2 && args[0] == "--render-only")
            {
                RenderArchivedDialog(args[1]);
                return 0;
            }
            int expectedProcessId = 0;
            if (args.Length > 0 && args[0].StartsWith("--owned-pid=", StringComparison.Ordinal))
            {
                expectedProcessId = int.Parse(args[0].Substring("--owned-pid=".Length));
                args = args.Skip(1).ToArray();
            }
            var sw = expectedProcessId > 0
                ? (SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"))
                : (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
            if (expectedProcessId > 0 && sw.GetProcessID() != expectedProcessId)
                throw new InvalidOperationException("SolidWorks returned a different process than the explicitly owned test session.");
            var model = (ModelDoc2)sw.ActiveDoc;
            if (model == null || model.GetType() != 2)
                throw new InvalidOperationException("An assembly must already be active.");
            bool dirty = model.GetSaveFlag();
            string original;
            double version;
            if (!ConfigurationSerialization.TryReadLegacyConfiguration(model, out original, out version))
                throw new InvalidOperationException("No migratable legacy configuration in the active assembly.");
            Console.WriteLine("ASSEMBLY " + model.GetPathName());
            var catalog = new ReferenceGeometryCatalog(model);
            var plan = new LegacyConfigurationMigration(original, version, catalog.Entries);
            Console.WriteLine("PLAN version=" + version + " links=" + plan.LinkCount + " explicitReferences=" + plan.References.Count);
            var resolver = new ReferenceGeometryResolver(model);
            foreach (var item in plan.References)
            {
                Console.WriteLine(item.LinkName + " | " + item.Kind + " | [" + item.LegacyName + "] | " +
                    (item.Selected == null ? "UNRESOLVED" : item.Selected.DisplayLabel));
                if (item.Selected == null || !resolver.Resolve(item.Selected.Reference).IsResolved)
                    throw new InvalidOperationException("Unresolved reference: " + item.LinkName + "/" + item.LegacyName);
            }
            Link oldRoot;
            using (var text = new StringReader(original))
            using (var reader = System.Xml.XmlReader.Create(text, new System.Xml.XmlReaderSettings
            {
                DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                XmlResolver = null
            }))
            {
                if (version < 1.3)
                {
                    var serialNode = (LegacySerialNode)new XmlSerializer(typeof(LegacySerialNode)).Deserialize(reader);
                    oldRoot = (Link)typeof(LegacySerialNode).GetMethod("ToLink",
                        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(serialNode, new object[] { null });
                }
                else
                    oldRoot = (Link)new DataContractSerializer(typeof(Link)).ReadObject(reader);
            }
            var tree = plan.CreateReviewedTree();
            var validateBindings = typeof(LegacyConfigurationMigration).GetMethod(
                "EnsureComponentBindings", BindingFlags.Static | BindingFlags.NonPublic);
            validateBindings.Invoke(null, new object[] { tree,
                new Func<byte[], bool>(pid => CommonSwOperations.LoadSWComponent(model, pid) != null) });
            Compare(oldRoot, tree, true);
            var serialize = typeof(ConfigurationSerialization).GetMethod("SerializeDraftPayload", BindingFlags.Static | BindingFlags.NonPublic);
            var deserialize = typeof(ConfigurationSerialization).GetMethod("DeserializeDraftPayload", BindingFlags.Static | BindingFlags.NonPublic);
            var payload = (string)serialize.Invoke(null, new object[] { tree });
            var restored = (LinkNode)deserialize.Invoke(null, new object[] { payload });
            if (restored == null)
                throw new InvalidOperationException("Strict v2 round-trip failed.");
            Compare(oldRoot, restored, true);
            var problems = new List<string>();
            CommonSwOperations.LoadSWComponents(model, restored, problems);
            if (problems.Count != 0)
                throw new InvalidOperationException("Component bindings failed: " + string.Join(", ", problems));
            string after;
            double afterVersion;
            ConfigurationSerialization.TryReadLegacyConfiguration(model, out after, out afterVersion);
            if (original != after || version != afterVersion || dirty != model.GetSaveFlag())
                throw new InvalidOperationException("The original document or configuration changed during read-only inspection.");
            Console.WriteLine("PASS: references resolve; structure/design settings/PIDs preserved; derived values reset; strict v2 round-trip; original unchanged.");
            if (args.Length == 1)
            {
                string output = Path.GetFullPath(args[0]);
                if (Directory.Exists(output))
                    throw new InvalidOperationException("Use a new output directory so prior evidence is not overwritten.");
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "legacy-configuration.xml"), original, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(output, "migrated-configuration.xml"), payload, new UTF8Encoding(false));
                CheckDialog(plan, output);
                CheckSavedCopy(sw, model, tree, oldRoot, original, version, output);
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void CheckDialog(LegacyConfigurationMigration plan, string output)
    {
        Type type = typeof(ConfigurationSerialization).Assembly.GetType("SW2URDF.UI.LegacyConfigurationMigrationDialog");
        foreach (Size size in new[] { new Size(940, 540), new Size(740, 420) })
        {
            using (var dialog = (Form)Activator.CreateInstance(type,
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { plan }, null))
            {
                dialog.ClientSize = size;
                dialog.Show();
                Application.DoEvents();
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                    bitmap.Save(Path.Combine(output, "migration-" + size.Width + ".png"));
                }
                dialog.CancelButton.PerformClick();
                if (dialog.DialogResult != DialogResult.Cancel)
                    throw new InvalidOperationException("Migration dialog cancellation failed.");
                dialog.Close();
            }
        }
        Console.WriteLine("PASS: migration dialog rendering and cancellation.");
    }

    private static void RenderArchivedDialog(string output)
    {
        string original = File.ReadAllText(Path.Combine(output, "legacy-configuration.xml"), Encoding.UTF8);
        Link before;
        Link after;
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(original)))
            before = (Link)new DataContractSerializer(typeof(Link)).ReadObject(stream);
        using (var stream = File.OpenRead(Path.Combine(output, "migrated-configuration.xml")))
            after = (Link)new DataContractSerializer(typeof(Link)).ReadObject(stream);
        var entries = new List<ReferenceGeometryEntry>();
        CollectArchivedEntries(before, after, entries);
        CheckDialog(new LegacyConfigurationMigration(original, 1.5, entries), output);
    }

    private static void CollectArchivedEntries(Link before, Link after, List<ReferenceGeometryEntry> entries)
    {
        foreach (var pair in new[] {
            Tuple.Create("LegacyCoordinateSystemName", after.FrameReference),
            Tuple.Create("LegacyAxisName", after.Joint.AxisReference) })
        {
            string name = (string)typeof(Joint).GetField(pair.Item1, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(before.Joint);
            if (!string.IsNullOrEmpty(name) && pair.Item2 != null && pair.Item2.IsExplicit)
                entries.Add((ReferenceGeometryEntry)Activator.CreateInstance(typeof(ReferenceGeometryEntry),
                    BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { pair.Item2, name, "" }, null));
        }
        for (int i = 0; i < before.Children.Count; i++)
            CollectArchivedEntries(before.Children[i], after.Children[i], entries);
    }

    private static void CheckSavedCopy(SldWorks sw, ModelDoc2 source, LinkNode tree,
        Link oldRoot, string original, double sourceVersion, string output)
    {
        if (source.GetSaveFlag())
            throw new InvalidOperationException("Save-copy testing requires a source with no unsaved edits.");
        byte[] sourceFile = ReadSharedFile(source.GetPathName());
        string[] expectedBindings = DescribeBindings(source, tree, "root").ToArray();
        string path = Path.Combine(output, "migration-test-copy.SLDASM");
        File.Copy(source.GetPathName(), path, false);
        ModelDoc2 copy = null;
        int errors = 0;
        int warnings = 0;
        try
        {
            copy = (ModelDoc2)sw.OpenDoc6(path, 2, 1, "", ref errors, ref warnings);
            if (copy == null || errors != 0 || !string.Equals(copy.GetPathName(), path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Could not open disposable assembly copy. Errors=" + errors);
            var result = ConfigurationSerialization.SaveConfigTreeXML(sw, copy, tree, true);
            if (result.Status != ConfigurationSaveStatus.Saved)
                throw new InvalidOperationException("Copy configuration save failed: " + result.ErrorMessage);
            if (!copy.Save3(1, ref errors, ref warnings) || errors != 0)
                throw new InvalidOperationException("Saving disposable assembly failed: " + errors);
            sw.CloseDoc(copy.GetTitle());
            copy = null;
            copy = (ModelDoc2)sw.OpenDoc6(path, 2, 1, "", ref errors, ref warnings);
            if (copy == null || errors != 0)
                throw new InvalidOperationException("Reopening disposable assembly failed: " + errors);
            string error;
            var restored = ConfigurationSerialization.LoadBaseNodeFromModel(copy, out error);
            if (restored == null || !string.IsNullOrEmpty(error))
                throw new InvalidOperationException("Saved migration cannot be loaded: " + error);
            Compare(oldRoot, restored, true);
            var problems = new List<string>();
            CommonSwOperations.LoadSWComponents(copy, restored, problems);
            if (problems.Count != 0)
                throw new InvalidOperationException("Saved component bindings failed: " + string.Join(", ", problems));
            string[] actualBindings = DescribeBindings(copy, restored, "root").ToArray();
            if (!expectedBindings.SequenceEqual(actualBindings, StringComparer.Ordinal))
                throw new InvalidOperationException("A saved Link resolves to a different component occurrence, file or configuration.");
            File.WriteAllLines(Path.Combine(output, "component-bindings.txt"), actualBindings, new UTF8Encoding(false));
            bool retained = false;
            foreach (Feature feature in (object[])copy.FeatureManager.GetFeatures(true))
            {
                if (feature.GetTypeName2() != "Attribute" ||
                    !feature.Name.StartsWith("URDF Export Configuration", StringComparison.Ordinal))
                    continue;
                var attribute = (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
                var dataParameter = (Parameter)attribute.GetParameter("data");
                var versionParameter = (Parameter)attribute.GetParameter("exporterVersion");
                retained |= dataParameter != null && versionParameter != null &&
                    dataParameter.GetStringValue() == original && versionParameter.GetDoubleValue() == sourceVersion;
            }
            if (!retained)
                throw new InvalidOperationException("The old configuration was not retained byte-for-byte in the copy.");
            var reopenedResolver = new ReferenceGeometryResolver(copy);
            CheckReferences(restored, reopenedResolver);
            Console.WriteLine("PASS: assembly copy saved/reopened; v2 loads; parameters/PIDs/references retained; original v" + sourceVersion + " attribute retained.");
            sw.ActivateDoc2(copy.GetTitle(), false, ref errors);
            // Match the preview button's existing defaulting step for pre-v2 formats
            // that never stored effort/velocity. Do not invent position bounds.
            typeof(ExportHelper).Assembly.GetType("SW2URDF.UI.AssemblyExportForm")
                .GetMethod("ApplyMissingRequiredJointLimitDefaultsToTree", BindingFlags.Static | BindingFlags.NonPublic,
                    null, new[] { typeof(LinkNode) }, null)
                .Invoke(null, new object[] { restored });
            var exporter = new ExportHelper(sw)
            {
                SavePath = output,
                PackageName = "migration_test",
                RosPackageName = "migration_test"
            };
            if (!exporter.CreateRobotFromTreeView(restored))
                throw new InvalidOperationException("Migrated model preview failed: " + exporter.ExportErrorWhy);
            if (!exporter.ExportRobot())
                throw new InvalidOperationException("Migrated model export failed: " + exporter.ExportErrorWhy);
            string[] urdfPaths = Directory.GetFiles(output, "*.urdf", SearchOption.AllDirectories);
            if (urdfPaths.Length == 0) throw new InvalidOperationException("Export produced no URDF.");
            foreach (string urdfPath in urdfPaths)
            {
                XElement robot = XDocument.Load(urdfPath).Root;
                if (robot == null || robot.Elements("link").Count() != CountLinks(restored) ||
                    robot.Elements("joint").Count() != CountLinks(restored) - 1)
                    throw new InvalidOperationException("Exported Link/Joint counts do not match the migrated tree.");
                foreach (XElement mass in robot.Descendants("mass"))
                {
                    double value = (double)mass.Attribute("value");
                    if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                        throw new InvalidOperationException("Export contains invalid mass.");
                }
            }
            if (!Directory.GetFiles(output, "*.stl", SearchOption.AllDirectories).Any())
                throw new InvalidOperationException("Export produced no STL meshes.");
            if (!expectedBindings.SequenceEqual(DescribeBindings(copy, restored, "root"), StringComparer.Ordinal))
                throw new InvalidOperationException("Export changed a Link component binding.");
            Console.WriteLine("PASS: migrated assembly exported URDF/STL with matching Link/Joint counts and unchanged component bindings.");
        }
        finally
        {
            if (copy != null && string.Equals(copy.GetPathName(), path, StringComparison.OrdinalIgnoreCase))
                sw.CloseDoc(copy.GetTitle());
            sw.ActivateDoc2(source.GetTitle(), false, ref errors);
        }
        string after;
        double version;
        ConfigurationSerialization.TryReadLegacyConfiguration(source, out after, out version);
        if (!sourceFile.SequenceEqual(ReadSharedFile(source.GetPathName())) || original != after || version != sourceVersion)
            throw new InvalidOperationException("Original assembly changed during copy verification.");
        Console.WriteLine("PASS: source file and legacy configuration unchanged; source was not saved. Rebuild dirty flag=" + source.GetSaveFlag());
    }

    private static IEnumerable<string> DescribeBindings(ModelDoc2 model, LinkNode node, string location)
    {
        var bindings = new List<byte[]> { node.Link.SWMainComponentPID };
        bindings.AddRange(node.Link.SWComponentPIDs);
        for (int index = 0; index < bindings.Count; index++)
        {
            byte[] pid = bindings[index];
            string label = location + "/" + node.Name + (index == 0 ? " main" : " component " + (index - 1));
            if (pid == null)
            {
                if (index != 0) throw new InvalidOperationException("Missing component binding: " + label);
                yield return label + " | <none>";
                continue;
            }
            Component2 component = CommonSwOperations.LoadSWComponent(model, pid);
            if (component == null) throw new InvalidOperationException("Unresolved component binding: " + label);
            yield return label + " | " + component.Name2 + " | " + component.GetPathName() +
                " | " + component.ReferencedConfiguration;
        }
        for (int index = 0; index < node.Nodes.Count; index++)
            foreach (string binding in DescribeBindings(model, (LinkNode)node.Nodes[index], location + "/" + index))
                yield return binding;
    }

    private static int CountLinks(LinkNode node)
    {
        return 1 + node.Nodes.Cast<LinkNode>().Sum(child => CountLinks(child));
    }

    private static byte[] ReadSharedFile(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    private static void CheckReferences(LinkNode node, ReferenceGeometryResolver resolver)
    {
        foreach (var reference in new[] { node.Link.FrameReference, node.Link.Joint.AxisReference })
            if (reference != null && reference.IsExplicit && !resolver.Resolve(reference).IsResolved)
                throw new InvalidOperationException("Saved geometry reference no longer resolves: " + node.Name);
        foreach (LinkNode child in node.Nodes)
            CheckReferences(child, resolver);
    }

    private static void Compare(Link source, LinkNode target, bool root)
    {
        if (source.Name != target.Link.Name || source.Children.Count != target.Nodes.Count ||
            !BytesEqual(source.SWMainComponentPID, target.Link.SWMainComponentPID) ||
            source.SWComponentPIDs.Count != target.Link.SWComponentPIDs.Count)
            throw new InvalidOperationException("Tree or component identity changed: " + source.Name);
        for (int i = 0; i < source.SWComponentPIDs.Count; i++)
            if (!BytesEqual(source.SWComponentPIDs[i], target.Link.SWComponentPIDs[i]))
                throw new InvalidOperationException("Component PID changed: " + source.Name);
        CompareElement(new Inertial(), target.Link.Inertial);
        CompareElement(new Visual(), target.Link.Visual);
        CompareElement(new SW2URDF.URDF.Collision(), target.Link.Collision);
        var state = target.Link.InertialEditing;
        if (state == null || state.SourceIsSolidWorks || state.MassEdited || state.OriginEdited ||
            state.TensorEdited || state.LegacyValuesPreserved || target.Link.AdditionalCollisions.Count != 0 ||
            target.Link.STLQualityFine || target.Link.MeshReductionRatio != 0 ||
            target.Link.CollisionMeshStrategy != CollisionMeshStrategy.VisualMesh)
            throw new InvalidOperationException("Derived data was not reset: " + source.Name);
        if (!root)
        {
            if (source.Joint.Name != target.Link.Joint.Name || source.Joint.Type != target.Link.Joint.Type)
                throw new InvalidOperationException("Joint identity changed: " + source.Name);
            CompareElement(new Origin(false), target.Link.Joint.Origin);
            CompareElement(source.Joint.Limit, target.Link.Joint.Limit);
            CompareElement(source.Joint.Dynamics, target.Link.Joint.Dynamics);
        }
        for (int i = 0; i < source.Children.Count; i++)
            Compare(source.Children[i], (LinkNode)target.Nodes[i], false);
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        return (a ?? new byte[0]).SequenceEqual(b ?? new byte[0]);
    }

    private static void CompareElement(URDFElement a, URDFElement b)
    {
        var before = new OrderedDictionary();
        var after = new OrderedDictionary();
        a.AppendToCSVDictionary(new List<string>(), before);
        b.AppendToCSVDictionary(new List<string>(), after);
        if (before.Count != after.Count)
            throw new InvalidOperationException("Parameter count changed for " + a.GetType().Name);
        foreach (DictionaryEntry entry in before)
            if (!after.Contains(entry.Key) || !object.Equals(entry.Value, after[entry.Key]))
                throw new InvalidOperationException("Parameter changed: " + entry.Key);
    }
}
