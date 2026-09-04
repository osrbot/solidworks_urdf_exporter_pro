# REVIEW BEFORE RUNNING. This creates disposable documents ONLY in a proven new,
# initially empty SolidWorks process. No ROT attachment, original models, forced
# process termination, or explicit COM release. Run in a fresh Windows PowerShell.
# Uses a temporary copy of the repository sample, two part configurations and an assembly.
# -PartOnly skips assembly creation and the component SelectedItems A/B/A test.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $BuildDll,
    [Parameter(Mandatory = $true)] [string] $InteropDirectory,
    [string] $AssemblyTemplate = '',
    [string] $SeedPart = '',
    [switch] $PartOnly
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SeedPart)) {
    $SeedPart = Join-Path $PSScriptRoot '..\examples\TOY_BLOCK\BlockA.SLDPRT'
}
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Use powershell.exe -NoProfile -STA -File Test-EffectiveMassOwnedSolidWorks.ps1'
}
$BuildDll = (Resolve-Path -LiteralPath $BuildDll).Path
$SeedPart = (Resolve-Path -LiteralPath $SeedPart).Path
if ([IO.Path]::GetExtension($SeedPart) -ine '.sldprt') { throw 'SeedPart must be an existing SLDPRT file.' }
$interop = (Resolve-Path -LiteralPath (Join-Path $InteropDirectory 'SolidWorks.Interop.sldworks.dll')).Path
$constants = (Resolve-Path -LiteralPath (Join-Path $InteropDirectory 'SolidWorks.Interop.swconst.dll')).Path
Write-Host 'PHASE compile helper only; no SolidWorks connection yet'
[Reflection.Assembly]::LoadFrom($interop) | Out-Null
[Reflection.Assembly]::LoadFrom($constants) | Out-Null

$source = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

public static class OwnedEffectiveMassProbe
{
    private static SldWorks sw;
    private static bool ownsPid;
    private static bool started;
    private static int pid;
    private static DateTime processStart;
    private static readonly List<ModelDoc2> created = new List<ModelDoc2>();
    private static MethodInfo reader;
    private static readonly double[] Center = { 0.007, -0.011, 0.013 };
    private static readonly double[] Moment = { 0.002, 0, 0, 0, 0.003, 0, 0, 0, 0.004 };

    public static void Run(string buildDll, string assemblyTemplate, bool partOnly, string seedPart)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("The C# probe requires STA.");
        if (started) throw new InvalidOperationException("Use a fresh PowerShell process; this probe runs only once.");
        started = true;
        Assembly build = Assembly.LoadFrom(buildDll);
        reader = build.GetType("SW2URDF.URDFExport.SolidWorksMassPropertyReader", true).GetMethod("Read",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
            new[] { typeof(ModelDoc2), typeof(IList<Component2>) }, null);
        if (reader == null) throw new MissingMethodException("Reader Read(ModelDoc2, IList<Component2>) was not found.");
        Phase("BUILD " + buildDll + " mvid=" + build.ManifestModule.ModuleVersionId);
        var previousPids = new HashSet<int>();
        foreach (Process process in Process.GetProcessesByName("SLDWORKS"))
            using (process) previousPids.Add(process.Id);
        Phase("BEFORE SLDWORKS PIDs=" + String.Join(",", previousPids));
        try
        {
            Phase("Activator.CreateInstance SldWorks.Application (exactly once)");
            sw = (SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application", true));
            Phase("initial GetProcessID");
            pid = sw.GetProcessID();
            if (pid <= 0 || previousPids.Contains(pid))
                throw new InvalidOperationException("Returned an existing/invalid PID " + pid + ". ABORT: no model or cleanup calls permitted.");
            using (Process process = Process.GetProcessById(pid))
            {
                if (!String.Equals(process.ProcessName, "SLDWORKS", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unexpected process identity.");
                processStart = process.StartTime.ToUniversalTime();
            }
            Phase("candidate PID=" + pid + "; fresh GetDocuments ownership check");
            object documents = sw.GetDocuments();
            Array initialDocuments = documents as Array;
            if (documents != null && (initialDocuments == null || initialDocuments.Length != 0))
                throw new InvalidOperationException("New PID is not an empty instance. ABORT without document or cleanup calls.");
            ownsPid = true;
            Phase("OWNERSHIP CONFIRMED pid=" + pid + " start=" + processStart.ToString("O"));

            string root = Path.Combine(Path.GetTempPath(), "SW2URDF-effective-mass-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Phase("DISPOSABLE DIRECTORY " + root + " (retained for review)");
            ModelDoc2 part;
            if (!String.IsNullOrWhiteSpace(seedPart))
            {
                string copiedPart = Path.Combine(root, "seed-copy.SLDPRT");
                File.Copy(Path.GetFullPath(seedPart), copiedPart, false);
                int openErrors = 0, openWarnings = 0;
                part = Com("OpenDoc6 ONLY copied seed " + copiedPart, () => (ModelDoc2)sw.OpenDoc6(copiedPart,
                    (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "", ref openErrors, ref openWarnings));
                if (part == null) throw new InvalidOperationException("Copied seed open failed: " + openErrors);
                created.Add(part);
                CheckOnlyCreatedDocuments();
            }
            else throw new InvalidOperationException("An existing seed part is required.");
            Require(Com("ForceRebuild3 part", () => part.ForceRebuild3(false)), "Part rebuild failed.");
            string configA = ConfigurationName(part);
            SetOverrides(part, true, 3.25);
            if (Com("AddConfiguration3 ProbeB", () => part.AddConfiguration3("ProbeB", "Owned fixture", "", 0)) == null)
                throw new InvalidOperationException("ProbeB configuration creation failed.");
            ActivateConfiguration(part, "ProbeB");
            SetOverrides(part, false, 5.0);

            IMassProperty2 partMetadata = NewMassProperty(part);
            double[] partMoment = null;
            foreach (string config in new[] { configA, "ProbeB", configA })
            {
                ActivateConfiguration(part, config);
                CheckMetadata(part, partMetadata, new Component2[0], config == configA ? "111" : "100");
                partMoment = CheckReader(part, null, config == configA ? "111" : "100", config == configA ? 3.25 : 5.0,
                    config == configA ? Center : null, config == configA ? Moment : null, true);
            }

            if (!partOnly)
            {
                string partPath = Path.Combine(root, "probe-box.SLDPRT");
                if (File.Exists(partPath)) throw new IOException("Refusing to overwrite a fixture file.");
                ModelDocExtension extension = Com("part.Extension for SaveAs", () => part.Extension);
                int errors = 0, warnings = 0;
                Require(Com("SaveAs ONLY new disposable part " + partPath, () => extension.SaveAs(partPath,
                    (int)swSaveAsVersion_e.swSaveAsCurrentVersion, (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null, ref errors, ref warnings)), "Disposable part SaveAs failed: " + errors);
                Require(errors == 0 && File.Exists(partPath), "Saved part missing or SaveAs reported errors.");
                Phase("SaveAs warnings=" + warnings);
                ModelDoc2 assembly = NewDocument(Template(assemblyTemplate, false));
                Component2 a = Com("AddComponent5 config A", () => ((AssemblyDoc)assembly).AddComponent5(partPath,
                    (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                    "", true, configA, -0.05, 0, 0));
                Component2 b = Com("AddComponent5 config B", () => ((AssemblyDoc)assembly).AddComponent5(partPath,
                    (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                    "", true, "ProbeB", 0.05, 0, 0));
                Require(a != null && b != null, "Assembly component insertion failed.");
                Require(Com("A.ReferencedConfiguration", () => a.ReferencedConfiguration) == configA, "A references the wrong configuration.");
                Require(Com("B.ReferencedConfiguration", () => b.ReferencedConfiguration) == "ProbeB", "B references the wrong configuration.");
                Require(Com("ForceRebuild3 assembly", () => assembly.ForceRebuild3(false)), "Assembly rebuild failed.");
                IMassProperty2 metadata = NewMassProperty(assembly);
                CheckMetadata(assembly, metadata, new Component2[0], "000");
                string partConfigurationBefore = ConfigurationName(part);
                Phase("PART CONFIGURATION AFTER ASSEMBLY INSERTIONS " + partConfigurationBefore);
                ExpectConfigurationMismatch(assembly, a);
                Require(ConfigurationName(part) == partConfigurationBefore, "Rejected read changed active part configuration.");
                Com("Align ONLY fixture B referenced configuration", () => b.ReferencedConfiguration = configA);
                ActivateConfiguration(part, configA);
                Require(Com("Rebuild aligned fixture assembly", () => assembly.ForceRebuild3(false)), "Aligned assembly rebuild failed.");
                partConfigurationBefore = ConfigurationName(part);
                double[] centerA = FixtureCenter(a);
                double[] centerB = FixtureCenter(b);
                Require(centerA.Zip(centerB, (x, y) => (x - y) * (x - y)).Sum() > 1e-12,
                    "Fixture occurrences must have distinct translations to detect stale scope results.");
                foreach (Component2 component in new[] { a, b, a })
                {
                    bool isA = Object.Equals(component, a);
                    Phase("ASSEMBLY metadata A/B/A: " + (isA ? "A" : "B"));
                    CheckMetadata(assembly, metadata, new[] { component }, "111");
                    CheckReader(assembly, new[] { component }, "111", 3.25, isA ? centerA : centerB,
                        partMoment, false, isA ? b : a);
                }
                double[] aggregateCenter = centerA.Zip(centerB, (x, y) => (3.25 * x + 3.25 * y) / 6.5).ToArray();
                double[] aggregateMoment = partMoment.Select(value => 2 * value).ToArray();
                foreach (double[] center in new[] { centerA, centerB })
                {
                    double[] offset = center.Zip(aggregateCenter, (x, y) => x - y).ToArray();
                    double distanceSquared = offset.Sum(value => value * value);
                    for (int row = 0; row < 3; row++)
                        for (int column = 0; column < 3; column++)
                            aggregateMoment[row * 3 + column] += 3.25 *
                                ((row == column ? distanceSquared : 0) - offset[row] * offset[column]);
                }
                CheckReader(assembly, new[] { a, b }, "111", 6.5, aggregateCenter, aggregateMoment);
                Require(ConfigurationName(part) == partConfigurationBefore, "Reader changed the part document's active configuration.");
                Phase("PASS component SelectedItems A/B/A plus aggregate values and referenced configurations");
            }
            else Phase("SKIP assembly component SelectedItems validation (-PartOnly)");
            Phase("PASS owned fixture numeric/metadata checks; beginning guarded cleanup");
        }
        catch (Exception error)
        {
            Phase("FAIL " + error);
            throw;
        }
        finally
        {
            if (!ownsPid) Phase("NO CLEANUP: ownership was never established; original SolidWorks instances untouched");
            else
            {
                try
                {
                    // Refuse all cleanup if any document not created by this harness appeared.
                    CheckOnlyCreatedDocuments();
                    for (int index = created.Count - 1; index >= 0; index--)
                    {
                        ModelDoc2 document = created[index];
                        Array remaining = Com("GetDocuments before owned close", () => sw.GetDocuments()) as Array;
                        if (remaining == null || !remaining.Cast<object>().Any(item => Object.Equals(item, document)))
                        {
                            created.RemoveAt(index);
                            Phase("Owned document already closed by SolidWorks; skipping stale RCW");
                            continue;
                        }
                        string title = Com("owned document.GetTitle for CloseDoc", () => document.GetTitle());
                        CheckOnlyCreatedDocuments();
                        Com("CloseDoc ONLY owned fixture " + title, () => sw.CloseDoc(title));
                        created.RemoveAt(index);
                    }
                    CheckOnlyCreatedDocuments();
                    Com("ExitApp ONLY proven owned PID " + pid, () => sw.ExitApp());
                    ownsPid = false;
                    Phase("CLEANUP completed; no explicit COM release or process kill");
                }
                catch (Exception error)
                {
                    Phase("CLEANUP FAILED/SKIPPED; owned PID=" + pid + " retained; no force kill. " + error);
                    throw;
                }
            }
        }
    }

    private static string Template(string supplied, bool part)
    {
        string path = supplied;
        if (String.IsNullOrWhiteSpace(path)) path = Com("GetUserPreferenceStringValue template", () =>
            sw.GetUserPreferenceStringValue((int)(part ? swUserPreferenceStringValue_e.swDefaultTemplatePart :
                swUserPreferenceStringValue_e.swDefaultTemplateAssembly)));
        if (String.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            !String.Equals(Path.GetExtension(path), part ? ".prtdot" : ".asmdot", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Supply an existing " + (part ? "PartTemplate .prtdot" : "AssemblyTemplate .asmdot") + "; no model-file fallback is allowed.");
        return Path.GetFullPath(path);
    }

    private static ModelDoc2 NewDocument(string template)
    {
        ModelDoc2 document = Com("NewDocument from template " + template, () => (ModelDoc2)sw.NewDocument(template, 0, 0, 0));
        if (document == null) throw new InvalidOperationException("NewDocument returned null.");
        created.Add(document);
        CheckOnlyCreatedDocuments();
        return document;
    }

    private static IMassProperty2 NewMassProperty(ModelDoc2 document)
    {
        ModelDocExtension extension = Com("document.Extension", () => document.Extension);
        IMassProperty2 property = Com("CreateMassProperty2", () => extension.CreateMassProperty2() as IMassProperty2);
        if (property == null) throw new InvalidOperationException("CreateMassProperty2 returned null.");
        return property;
    }

    private static void SetOverrides(ModelDoc2 part, bool full, double mass)
    {
        IMassProperty2 property = NewMassProperty(part);
        SetScope(property, new Component2[0]);
        Com("UseSystemUnits=true for override values", () => property.UseSystemUnits = true);
        var options = Com("GetOverrideOptions for SETUP", () => property.GetOverrideOptions() as IMassPropertyOverrideOptions);
        if (options == null) throw new InvalidOperationException("Override options are null.");
        Com("OverrideMass=true", () => options.OverrideMass = true);
        Require(Com("SetOverrideMassValue", () => options.SetOverrideMassValue(mass)), "Cannot set mass override.");
        Com("OverrideCenterOfMass=" + full, () => options.OverrideCenterOfMass = full);
        Com("OverrideMomentsOfInertia=" + full, () => options.OverrideMomentsOfInertia = full);
        if (full)
        {
            Require(Com("SetOverrideCenterOfMassValue document frame", () => options.SetOverrideCenterOfMassValue(Center, "")), "Cannot set COM override.");
            Require(Com("SetOverrideMomentsOfInertiaValue about COM", () => options.SetOverrideMomentsOfInertiaValue(
                (int)swMomentsOfInertiaReferenceFrame_e.swMomentsOfInertiaReferenceFrame_CenterOfMass, Moment, "")), "Cannot set inertia override.");
        }
        Require(Com("SetOverrideOptions THIS CONFIGURATION ONLY", () => property.SetOverrideOptions(options,
            (int)swInConfigurationOpts_e.swThisConfiguration, null)), "Cannot apply override options.");
        Require(Com("Recalculate after SETUP overrides", () => property.Recalculate()), "Setup Recalculate failed.");
    }

    private static void CheckMetadata(ModelDoc2 document, IMassProperty2 metadata, Component2[] scope, string expected)
    {
        SetScope(metadata, scope);
        string before = Flags(metadata);
        IMassProperty2 reference = NewMassProperty(document);
        SetScope(reference, scope);
        Require(Com("fresh reference.Recalculate for comparison only", () => reference.Recalculate()), "Metadata reference recalculation failed.");
        string after = Flags(reference);
        Phase("METADATA expected=" + expected + " before=" + before + " after=" + after);
        if (scope.Length > 0)
        {
            IMassProperty2 numeric = NumericProperty(document, scope);
            Phase("SCOPED DIRECT MASS " + Com("mass for scope metadata comparison", () => numeric.Mass).ToString("R", CultureInfo.InvariantCulture));
        }
        Require(before == expected, "Metadata BEFORE recalculation: expected " + expected + ", got " + before);
        Require(after == expected, "Metadata AFTER recalculation: expected " + expected + ", got " + after);
        Phase("PASS flags no-recalculate=" + before + " recalculated=" + after);
    }

    private static string Flags(IMassProperty2 property)
    {
        var options = Com("GetOverrideOptions", () => property.GetOverrideOptions() as IMassPropertyOverrideOptions);
        if (options == null) throw new InvalidOperationException("GetOverrideOptions returned null.");
        bool mass = Com("OverrideMass GET", () => options.OverrideMass);
        bool center = Com("OverrideCenterOfMass GET", () => options.OverrideCenterOfMass);
        bool inertia = Com("OverrideMomentsOfInertia GET", () => options.OverrideMomentsOfInertia);
        return (mass ? "1" : "0") + (center ? "1" : "0") + (inertia ? "1" : "0");
    }

    private static void SetScope(IMassProperty2 property, Component2[] scope)
    {
        Com("SelectedItems SET count=" + scope.Length, () => property.SelectedItems =
            scope.Length == 0 ? (object)new object[0] :
                scope.Select(component => new System.Runtime.InteropServices.DispatchWrapper(component)).ToArray());
        VerifyScope(property, scope);
    }

    private static void VerifyScope(IMassProperty2 property, Component2[] scope)
    {
        object raw = Com("SelectedItems GET", () => property.SelectedItems);
        Array array = raw as Array;
        Require(raw == null || (array != null && array.Rank == 1), "SelectedItems is not a rank-one array.");
        object[] actual = array == null ? new object[0] : array.Cast<object>().ToArray();
        Require(actual.Length == scope.Length, "SelectedItems count differs: expected=" + scope.Length + " actual=" + actual.Length);
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Component2 component in scope)
        {
            string name = Com("expected component.Name2", () => component.Name2);
            expected.Add(name, Com("expected component.ReferencedConfiguration", () => component.ReferencedConfiguration));
        }
        foreach (object item in actual)
        {
            Component2 component = item as Component2;
            Require(component != null, "SelectedItems returned a non-component.");
            string name = Com("actual component.Name2", () => component.Name2);
            string config = Com("actual component.ReferencedConfiguration", () => component.ReferencedConfiguration);
            string expectedConfig;
            Require(expected.TryGetValue(name, out expectedConfig) && expectedConfig == config, "SelectedItems identity/configuration mismatch.");
            expected.Remove(name);
        }
    }

    private static IMassProperty2 NumericProperty(ModelDoc2 model, Component2[] scope)
    {
        IMassProperty2 property = NewMassProperty(model);
        Com("UseSystemUnits=true", () => property.UseSystemUnits = true);
        Com("IncludeHiddenBodiesOrComponents=true", () => property.IncludeHiddenBodiesOrComponents = true);
        SetScope(property, scope);
        Require(Com("numeric Recalculate", () => property.Recalculate()), "Numeric recalculation failed.");
        VerifyScope(property, scope);
        return property;
    }

    private static double[] CheckReader(ModelDoc2 model, Component2[] selected, string flags, double expectedMass,
        double[] expectedCenter, double[] expectedMoment, bool spectrumOnly = false,
        Component2 preselectedComponent = null)
    {
        Component2[] scope = selected ?? new Component2[0];
        IMassProperty2 centerProperty = NumericProperty(model, scope);
        double[] center = Com("direct CenterOfMass", () => (double[])centerProperty.CenterOfMass);
        double mass = Com("direct Mass", () => centerProperty.Mass);
        IMassProperty2 inertiaProperty = NumericProperty(model, scope);
        double[] moment = Com("direct GetMomentOfInertia about COM", () => (double[])inertiaProperty.GetMomentOfInertia(
            (int)swMassPropertyMoment_e.swMassPropertyMomentAboutCenterOfMass));
        Phase("DIRECT MOMENT " + String.Join(",", moment.Select(entry => entry.ToString("R", CultureInfo.InvariantCulture))));
        Near(mass, expectedMass, "known override mass");
        if (expectedCenter != null) Near(center, expectedCenter, "known override COM");
        if (expectedMoment != null)
        {
            if (spectrumOnly) NearSpectrum(moment, expectedMoment);
            else Near(moment, expectedMoment, "known fixture document-frame inertia");
        }
        // Direct reference reads may change UI selection; only the reader must preserve this snapshot.
        if (preselectedComponent != null)
        {
            Com("Clear fixture selection before explicit preselection", () => model.ClearSelection2(true));
            Require(Com("Select opposite fixture occurrence before reader", () =>
                preselectedComponent.Select4(false, null, false)), "Cannot preselect the opposite fixture occurrence.");
        }
        string before = DocumentState(model);
        Tuple<object, int>[] selectionBefore = CaptureSelection(model);
        if (preselectedComponent != null)
            AssertPreselectedOccurrence(model, preselectedComponent, selectionBefore);
        object snapshot;
        try
        {
            snapshot = Com("reader.Read BEGIN (internal COM calls are inside this phase)", () =>
            {
                try { return reader.Invoke(null, new object[] { model, selected }); }
                catch (TargetInvocationException error) { throw error.InnerException ?? error; }
            });
        }
        finally
        {
            AssertSelectionUnchanged(model, selectionBefore);
            Require(DocumentState(model) == before, "Reader changed path/save flag/configuration/selection count.");
        }
        Phase("reader.Read END");
        Func<string, object> value = name => snapshot.GetType().GetProperty(name).GetValue(snapshot, null);
        Near((double)value("Mass"), mass, "reader/direct mass");
        Near((double[])value("CenterOfMass"), center, "reader/direct COM");
        Near((double[])value("Moment"), moment, "reader/direct inertia");
        string readerFlags = ((bool)value("HasMassOverride") ? "1" : "0") +
            ((bool)value("HasCenterOfMassOverride") ? "1" : "0") + ((bool)value("HasInertiaOverride") ? "1" : "0");
        Require(readerFlags == flags, "Reader override flags differ from fixture settings.");
        Phase("PASS reader/direct kg=" + mass.ToString("R", CultureInfo.InvariantCulture) + " flags=" + readerFlags);
        return (double[])moment.Clone();
    }

    private static double[] FixtureCenter(Component2 component)
    {
        MathTransform transform = Com("fixture component.Transform2", () => component.Transform2);
        Require(transform != null, "Fixture component transform is unavailable.");
        double[] data = Com("fixture transform.ArrayData", () => (double[])transform.ArrayData);
        Require(data != null && data.Length >= 13, "Fixture transform has invalid shape.");
        for (int index = 0; index < 9; index++)
            Near(data[index], index % 4 == 0 ? 1 : 0, "fixture identity rotation[" + index + "]");
        Near(data[12], 1, "fixture unit scale");
        double[] translation = data.Skip(9).Take(3).ToArray();
        foreach (double value in translation) Near(value, value, "fixture finite translation");
        Phase("FIXTURE ACTUAL TRANSLATION " + String.Join(",", translation.Select(value => value.ToString("R", CultureInfo.InvariantCulture))));
        return Center.Zip(translation, (center, offset) => center + offset).ToArray();
    }

    private static void AssertPreselectedOccurrence(ModelDoc2 model, Component2 expected,
        Tuple<object, int>[] selection)
    {
        Require(selection.Length == 1, "Opposite occurrence preselection count: expected=1 actual=" + selection.Length);
        var manager = Com("SelectionManager for preselection occurrence", () => (SelectionMgr)model.SelectionManager);
        // A selected object may be a component feature rather than the Component2 RCW.
        Component2 actual = Com("GetSelectedObjectsComponent4 for preselection", () => manager.GetSelectedObjectsComponent4(1, -1) as Component2);
        string expectedName = Com("preselection expected Name2", () => expected.Name2);
        string expectedConfig = Com("preselection expected ReferencedConfiguration", () => expected.ReferencedConfiguration);
        string actualName = actual == null ? null : Com("preselection actual Name2", () => actual.Name2);
        string actualConfig = actual == null ? null : Com("preselection actual ReferencedConfiguration", () => actual.ReferencedConfiguration);
        string details = "rawType=" + selection[0].Item1.GetType().FullName +
            "; mark=" + selection[0].Item2 + "; expected=" + expectedName + " [" + expectedConfig +
            "]; actual=" + (actualName ?? "<unavailable>") + " [" + (actualConfig ?? "<unavailable>") + "]";
        Phase("PRESELECTION " + details);
        Require(!String.IsNullOrWhiteSpace(expectedName) && !String.IsNullOrWhiteSpace(expectedConfig) &&
            String.Equals(actualName, expectedName, StringComparison.OrdinalIgnoreCase) &&
            String.Equals(actualConfig, expectedConfig, StringComparison.OrdinalIgnoreCase),
            "Opposite occurrence preselection mismatch: " + details);
    }

    private static Tuple<object, int>[] CaptureSelection(ModelDoc2 model)
    {
        var manager = Com("SelectionManager snapshot", () => (SelectionMgr)model.SelectionManager);
        int count = Com("selection snapshot count", () => manager.GetSelectedObjectCount2(-1));
        var result = new Tuple<object, int>[count];
        for (int index = 1; index <= count; index++)
        {
            object selected = Com("selection snapshot object " + index, () => manager.GetSelectedObject6(index, -1));
            Require(selected != null, "Cannot verify identity of a null selected object.");
            int mark = Com("selection snapshot mark " + index, () => manager.GetSelectedObjectMark(index));
            result[index - 1] = Tuple.Create(selected, mark);
        }
        return result;
    }

    private static void AssertSelectionUnchanged(ModelDoc2 model, Tuple<object, int>[] expected)
    {
        Tuple<object, int>[] actual = CaptureSelection(model);
        Require(actual.Length == expected.Length, "Reader changed selection count.");
        for (int index = 0; index < expected.Length; index++)
            Require(Object.Equals(actual[index].Item1, expected[index].Item1) && actual[index].Item2 == expected[index].Item2,
                "Reader changed selection object identity/order or mark at index " + (index + 1));
    }

    private static string ConfigurationName(ModelDoc2 model)
    {
        var manager = Com("ConfigurationManager GET", () => model.ConfigurationManager);
        var config = Com("ActiveConfiguration GET", () => manager.ActiveConfiguration);
        return Com("Configuration.Name GET", () => config.Name);
    }

    private static void ExpectConfigurationMismatch(ModelDoc2 model, Component2 component)
    {
        string before = DocumentState(model);
        Tuple<object, int>[] selectionBefore = CaptureSelection(model);
        Exception rejected = null;
        try
        {
            Com("reader.Read must reject mismatched referenced configuration", () =>
                reader.Invoke(null, new object[] { model, new[] { component } }));
        }
        catch (TargetInvocationException error) { rejected = error.InnerException; }
        finally
        {
            AssertSelectionUnchanged(model, selectionBefore);
            Require(DocumentState(model) == before, "Rejected read changed assembly state.");
        }
        Require(rejected is InvalidOperationException &&
            rejected.Message.IndexOf("configuration", StringComparison.OrdinalIgnoreCase) >= 0,
            "Reader must reject unsupported override configuration instead of returning misleading flags.");
        Phase("PASS explicit configuration guard: " + rejected.Message);
    }

    private static void ActivateConfiguration(ModelDoc2 model, string name)
    {
        if (ConfigurationName(model) != name)
            Require(Com("ShowConfiguration2 " + name, () => model.ShowConfiguration2(name)), "Cannot activate " + name);
        Require(ConfigurationName(model) == name, "Active configuration mismatch: " + name);
    }

    private static string DocumentState(ModelDoc2 model)
    {
        string path = Com("GetPathName", () => model.GetPathName());
        bool save = Com("GetSaveFlag", () => model.GetSaveFlag());
        string config = ConfigurationName(model);
        var selection = Com("SelectionManager GET", () => (SelectionMgr)model.SelectionManager);
        int count = Com("GetSelectedObjectCount2", () => selection.GetSelectedObjectCount2(-1));
        string state = path + "|save=" + save + "|config=" + config + "|selectionCount=" + count;
        Phase("DOCUMENT STATE " + state);
        return state;
    }

    private static void CheckOnlyCreatedDocuments()
    {
        object raw = Com("GetDocuments ownership inventory", () => sw.GetDocuments());
        Array docs = raw as Array;
        Require(raw == null || (docs != null && docs.Rank == 1), "Invalid document inventory; refusing cleanup.");
        foreach (object item in docs == null ? new object[0] : docs.Cast<object>())
            Require(created.Any(document => Object.Equals(document, item)), "An unowned document appeared; refusing further model operations/cleanup.");
    }

    private static void Guard()
    {
        Require(ownsPid && sw != null && pid > 0, "No proven owned process; operation forbidden.");
        using (Process process = Process.GetProcessById(pid))
            Require(process.StartTime.ToUniversalTime() == processStart &&
                String.Equals(process.ProcessName, "SLDWORKS", StringComparison.OrdinalIgnoreCase), "Owned process identity changed.");
        Phase("GUARD GetProcessID (expected " + pid + ")");
        Require(sw.GetProcessID() == pid, "Current COM process is not the owned PID; operation forbidden.");
    }

    private static T Com<T>(string label, Func<T> action)
    {
        Guard();
        Phase("COM " + label);
        return action();
    }

    private static void Com(string label, Action action)
    {
        Guard();
        Phase("COM " + label);
        action();
    }

    private static void Near(double value, double expected, string label)
    {
        Require(!Double.IsNaN(value) && !Double.IsInfinity(value) && !Double.IsNaN(expected) &&
            !Double.IsInfinity(expected) && Math.Abs(value - expected) <= 1e-9 + Math.Abs(expected) * 1e-7,
            label + ": " + value.ToString("R", CultureInfo.InvariantCulture) + " != " + expected.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void Near(double[] values, double[] expected, string label)
    {
        Require(values != null && expected != null && values.Length == expected.Length, label + " has invalid shape.");
        for (int index = 0; index < values.Length; index++) Near(values[index], expected[index], label + "[" + index + "]");
    }

    private static void NearSpectrum(double[] actual, double[] expected)
    {
        // SW may retain the seed part's principal axes for a COM-frame override.
        // Check the imposed eigenvalues independently of that frame; reader/direct
        // comparison below still checks every document-frame tensor entry.
        Func<double[], double> trace = a => a[0] + a[4] + a[8];
        Func<double[], double> square = a => a.Sum(entry => entry * entry);
        Func<double[], double> determinant = a => a[0] * (a[4] * a[8] - a[5] * a[7])
            - a[1] * (a[3] * a[8] - a[5] * a[6]) + a[2] * (a[3] * a[7] - a[4] * a[6]);
        Near(trace(actual) / trace(expected), 1, "known override trace ratio");
        Near(square(actual) / square(expected), 1, "known override squared norm ratio");
        Near(determinant(actual) / determinant(expected), 1, "known override determinant ratio");
        Phase("PASS imposed inertia spectrum; full frame comparison follows");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Phase(string message)
    {
        Console.WriteLine(DateTime.UtcNow.ToString("O") + " " + message);
        Console.Out.Flush();
    }
}
'@

Add-Type -TypeDefinition $source -ReferencedAssemblies @($interop, $constants, 'System.Core.dll')
[OwnedEffectiveMassProbe]::Run($BuildDll, $AssemblyTemplate, $PartOnly.IsPresent, $SeedPart)
