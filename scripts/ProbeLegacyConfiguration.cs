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
            var sw = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
            var model = (ModelDoc2)sw.ActiveDoc;
            if (model == null || model.GetType() != 2)
                throw new InvalidOperationException("An assembly must already be active.");
            bool dirty = model.GetSaveFlag();
            string original;
            double version;
            if (!ConfigurationSerialization.TryReadLegacyConfiguration(model, out original, out version))
                throw new InvalidOperationException("No migratable legacy configuration in the active assembly.");
            Console.WriteLine("ASSEMBLY " + model.GetPathName());
            var catalog = new ReferenceGeometryCatalog(model, false);
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
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(original)))
                oldRoot = (Link)new DataContractSerializer(typeof(Link)).ReadObject(stream);
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
            Console.WriteLine("PASS: references resolve; parameters/PIDs preserved; strict v2 round-trip; components restored; original unchanged.");
            if (args.Length == 1)
            {
                string output = Path.GetFullPath(args[0]);
                if (Directory.Exists(output))
                    throw new InvalidOperationException("Use a new output directory so prior evidence is not overwritten.");
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "legacy-configuration.xml"), original, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(output, "migrated-configuration.xml"), payload, new UTF8Encoding(false));
                CheckDialog(plan, output);
                CheckSavedCopy(sw, model, tree, oldRoot, original, output);
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
        Link oldRoot, string original, string output)
    {
        if (source.GetSaveFlag())
            throw new InvalidOperationException("Save-copy testing requires a source with no unsaved edits.");
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
            bool retained = false;
            foreach (Feature feature in (object[])copy.FeatureManager.GetFeatures(true))
            {
                if (feature.Name != "URDF Export Configuration (v1.5)")
                    continue;
                var attribute = (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
                retained = ((Parameter)attribute.GetParameter("data")).GetStringValue() == original;
            }
            if (!retained)
                throw new InvalidOperationException("The old configuration was not retained byte-for-byte in the copy.");
            Console.WriteLine("PASS: assembly copy saved/reopened; v2 loads; parameters/PIDs retained; original v1.5 attribute retained.");
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
        if (source.GetSaveFlag() || original != after || version != 1.5)
            throw new InvalidOperationException("Original assembly changed during copy verification.");
        Console.WriteLine("PASS: original assembly still unmodified.");
    }

    private static void Compare(Link source, LinkNode target, bool root)
    {
        if (source.Name != target.Link.Name || source.Children.Count != target.Nodes.Count ||
            !BytesEqual(source.SWMainComponentPID, target.Link.SWMainComponentPID) ||
            source.SWComponentPIDs.Count != target.Link.SWComponentPIDs.Count ||
            source.STLQualityFine != target.Link.STLQualityFine ||
            source.CollisionMeshStrategy != target.Link.CollisionMeshStrategy ||
            source.MeshReductionRatio != target.Link.MeshReductionRatio)
            throw new InvalidOperationException("Tree or component identity changed: " + source.Name);
        for (int i = 0; i < source.SWComponentPIDs.Count; i++)
            if (!BytesEqual(source.SWComponentPIDs[i], target.Link.SWComponentPIDs[i]))
                throw new InvalidOperationException("Component PID changed: " + source.Name);
        CompareElement(source.Inertial, target.Link.Inertial);
        CompareElement(source.Visual, target.Link.Visual);
        CompareElement(source.Collision, target.Link.Collision);
        if (!root)
            CompareElement(source.Joint, target.Link.Joint);
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
