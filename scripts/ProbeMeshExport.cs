// Run against a disposable assembly copy. Starts and closes only its own SolidWorks process.
// Compile beside a candidate SW2URDF.dll and its dependencies; never saves the CAD model.
using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

internal static class ProbeMeshExport
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        SldWorks sw = null;
        bool ownsProcess = false;
        try
        {
            if ((args.Length != 2 && args.Length != 3) || !File.Exists(args[0]) || Directory.Exists(args[1]))
                throw new ArgumentException("Provide a disposable assembly copy and a NEW output directory.");
            string modelPath = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            Environment.SetEnvironmentVariable("SW2URDF_LOG_FILE", Path.Combine(output, "probe.log"));
            if (!modelPath.Contains(Path.DirectorySeparatorChar + ".codex-build" + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Only disposable .codex-build fixtures may be used.");
            var existing = new HashSet<int>(Process.GetProcessesByName("SLDWORKS").Select(p => p.Id));
            sw = args.Length == 3 ? FindTestInstance(Int32.Parse(args[2])) :
                (SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
            int pid = sw.GetProcessID();
            if (args.Length != 3 && existing.Contains(pid))
                throw new InvalidOperationException("SolidWorks returned an existing process; refusing to use it.");
            if (((object[])sw.GetDocuments() ?? new object[0]).Length != 0)
                throw new InvalidOperationException("The new test process unexpectedly has open documents.");
            ownsProcess = true;
            Console.WriteLine("OWNED_PROCESS " + pid);
            sw.Visible = true;
            int errors = 0, warnings = 0;
            var model = (ModelDoc2)sw.OpenDoc6(modelPath, 2, 1, "", ref errors, ref warnings);
            if (model == null || !String.Equals(model.GetPathName(), modelPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Could not open the exact disposable fixture: " + errors);
            Directory.CreateDirectory(output);
            string legacy;
            double version;
            if (!ConfigurationSerialization.TryReadLegacyConfiguration(model, out legacy, out version))
                throw new InvalidOperationException("The fixture must contain a migratable legacy configuration.");
            var plan = new LegacyConfigurationMigration(legacy, version,
                new ReferenceGeometryCatalog(model, false).Entries);
            if (plan.References.Any(reference => reference.Selected == null))
                throw new InvalidOperationException("Fixture references require manual review.");
            var tree = plan.CreateReviewedTree();
            var problems = new List<string>();
            CommonSwOperations.LoadSWComponents(model, tree, problems);
            if (problems.Count > 0) throw new InvalidOperationException(String.Join(", ", problems));
            Link link = tree.RebuildLink();
            var exporter = new ExportHelper(sw);
            object[] all = (object[])((AssemblyDoc)model).GetComponents(false);
            var components = all.OfType<Component2>().ToArray();
            var initial = components.ToDictionary(component => component.Name2, component => component.Visible);
            IList states = (IList)Invoke(typeof(ExportHelper), null, "CaptureComponentVisibility", (object)components);
            bool preferencesSaved = false;
            var total = Stopwatch.StartNew();
            try
            {
                Invoke(typeof(ExportHelper), exporter, "SaveUserPreferences");
                preferencesSaved = true;
                Invoke(typeof(ExportHelper), exporter, "SetSTLExportPreferences");
                var timer = Stopwatch.StartNew();
                Invoke(typeof(CommonSwOperations), null, "SetComponentVisibility", model, components, false);
                Console.WriteLine("HIDE_ALL_SECONDS " + timer.Elapsed.TotalSeconds.ToString("F3"));
                var before = components.ToDictionary(component => component.Name2, component => component.Visible);
                timer.Restart();
                exporter.ExportProgressChanged += (sender, progress) => Console.WriteLine(progress.Stage);
                Invoke(typeof(ExportHelper), exporter, "SaveSTL", link, Path.Combine(output, "base_link.stl"));
                Console.WriteLine("BASE_STL_SECONDS " + timer.Elapsed.TotalSeconds.ToString("F3"));
                AssertVisibility(components, before);
                Console.WriteLine("PASS per-link visibility restored.");
                using (var reader = new BinaryReader(File.OpenRead(Path.Combine(output, "base_link.stl"))))
                {
                    reader.BaseStream.Position = 80;
                    uint count = reader.ReadUInt32();
                    if (count == 0 || reader.BaseStream.Length != 84L + 50L * count)
                        throw new InvalidDataException("Invalid exported binary STL.");
                    Console.WriteLine("STL_TRIANGLES " + count);
                }
            }
            finally
            {
                var timer = Stopwatch.StartNew();
                try { Invoke(typeof(ExportHelper), null, "RestoreComponentVisibility", model, states); }
                finally
                {
                    if (preferencesSaved) Invoke(typeof(ExportHelper), exporter, "ResetUserPreferences");
                }
                Console.WriteLine("RESTORE_ALL_SECONDS " + timer.Elapsed.TotalSeconds.ToString("F3"));
            }
            AssertVisibility(components, initial);
            Console.WriteLine("PASS original component visibility restored; no CAD save was performed.");
            Console.WriteLine("TOTAL_SECONDS " + total.Elapsed.TotalSeconds.ToString("F3"));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            if (sw != null && ownsProcess)
            {
                // Only this newly created, isolated process contains test documents.
                sw.CloseAllDocuments(true);
                sw.ExitApp();
            }
        }
    }

    private static void AssertVisibility(IEnumerable<Component2> components, IDictionary<string, int> expected)
    {
        var changed = components.Where(component => component.Visible != expected[component.Name2])
            .Select(component => component.Name2).ToArray();
        if (changed.Length != 0) throw new InvalidOperationException("Visibility changed: " + String.Join(", ", changed));
    }

    private static object Invoke(Type type, object instance, string name, params object[] args)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Single(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(instance, args);
    }

    private static SldWorks FindTestInstance(int processId)
    {
        IRunningObjectTable table;
        IBindCtx context;
        GetRunningObjectTable(0, out table);
        CreateBindCtx(0, out context);
        try
        {
            IEnumMoniker enumerator;
            table.EnumRunning(out enumerator);
            try
            {
                IMoniker[] moniker = new IMoniker[1];
                while (enumerator.Next(1, moniker, IntPtr.Zero) == 0)
                {
                    try
                    {
                        string name;
                        moniker[0].GetDisplayName(context, null, out name);
                        if (name != "SolidWorks_PID_" + processId) continue;
                        object instance;
                        table.GetObject(moniker[0], out instance);
                        var application = (SldWorks)instance;
                        if (application.GetProcessID() != processId)
                            throw new InvalidOperationException("The test process identity changed.");
                        return application;
                    }
                    finally { Marshal.ReleaseComObject(moniker[0]); }
                }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }
        finally { Marshal.ReleaseComObject(context); Marshal.ReleaseComObject(table); }
        throw new InvalidOperationException("The specified isolated test process is not ready.");
    }

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx context);
}
