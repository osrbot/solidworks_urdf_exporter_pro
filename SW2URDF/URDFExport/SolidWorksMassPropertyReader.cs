using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Reads effective, uncalibrated properties in the assembly document frame:
    /// kg, m, and the row-major physical inertia tensor about COM in kg*m^2.
    /// Call on the SolidWorks thread with resolved occurrences from the active assembly.
    /// A null selection explicitly requests the entire part/assembly document; an empty
    /// selection is invalid. Reuse the returned baseline for UI edits, not a global COM cache.
    /// </summary>
    internal static class SolidWorksMassPropertyReader
    {
        [Flags]
        private enum Overrides { None = 0, Mass = 1, Center = 2, Inertia = 4 }

        public static MassPropertySnapshot Read(ModelDoc2 assembly, IList<Component2> selectedComponents)
        {
            if (assembly == null) throw new ArgumentNullException("assembly");
            bool wholeDocument = selectedComponents == null;
            if (!wholeDocument && selectedComponents.Count == 0)
                throw new ArgumentException("Select at least one component; an empty Link must not read the whole assembly.", "selectedComponents");

            SelectionMgr selectionManager = null;
            bool selectionSuspended = false;
            Exception readFailure = null;
            try
            {
                int documentType = assembly.GetType();
                if (documentType != (int)swDocumentTypes_e.swDocASSEMBLY &&
                    !(wholeDocument && documentType == (int)swDocumentTypes_e.swDocPART))
                    throw new ArgumentException("Component selections require an assembly; whole-document reads require a part or assembly.", "assembly");

                string configuration = ActiveConfigurationName(assembly);
                var observed = new Dictionary<string, ComponentState>(StringComparer.OrdinalIgnoreCase);
                var selected = new Dictionary<string, Component2>(StringComparer.OrdinalIgnoreCase);
                foreach (Component2 component in selectedComponents ?? new Component2[0])
                {
                    Observe(component, observed);
                    selected[component.Name2] = component;
                }

                var ancestors = new Dictionary<string, Component2>(StringComparer.OrdinalIgnoreCase);
                var bounded = new List<Component2>();
                foreach (Component2 component in selected.Values)
                {
                    bool covered = false;
                    var path = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { component.Name2 };
                    for (Component2 parent = component.GetParent(); parent != null && !parent.IsRoot(); parent = parent.GetParent())
                    {
                        Observe(parent, observed);
                        if (!path.Add(parent.Name2))
                            throw new InvalidOperationException("Cyclic component ancestry: " + parent.Name2);
                        if (selected.ContainsKey(parent.Name2)) covered = true;
                        ancestors[parent.Name2] = parent;
                    }
                    if (!covered) bounded.Add(component);
                }

                ModelDocExtension extension = assembly.Extension;
                if (extension == null) throw new InvalidOperationException("SolidWorks returned no document extension.");

                selectionManager = assembly.SelectionManager as SelectionMgr;
                if (selectionManager == null)
                    throw new InvalidOperationException("SolidWorks returned no selection manager; mass-property selection cannot be isolated.");
                // SelectedItems can change the working selection, and fresh properties inherit it.
                // Suspend once for the entire read; 0 (an empty saved list) is also success.
                selectionManager.SuspendSelectionList();
                selectionSuspended = true;

                // For bounded reads, the empty selection ONLY inspects document-level overrides.
                // This API cannot establish Link ownership of a whole-assembly override.
                IMassProperty2 metadata = CreateProperty(assembly, extension);
                Overrides documentOverrides = ReadOverrides(metadata, new Component2[0]);
                if (!wholeDocument && documentOverrides != Overrides.None)
                    throw new InvalidOperationException(
                        "The active assembly has a whole-assembly mass/COM/inertia override. " +
                        "It cannot be distributed across Link component selections. Apply overrides to individual " +
                        "components or an entire subassembly assigned to one Link instead.");

                foreach (Component2 ancestor in ancestors.Values)
                {
                    if (selected.ContainsKey(ancestor.Name2)) continue;
                    // An ancestor below a selected ancestor is already part of that atomic selection.
                    if (bounded.Any(item => IsDescendantOf(ancestor, item))) continue;
                    if (ReadOverrides(metadata, new[] { ancestor }) != Overrides.None)
                        throw new InvalidOperationException(
                            "Subassembly '" + ancestor.Name2 + "' has a whole-subassembly mass/COM/inertia override, " +
                            "but this Link selects only descendants. Select the whole subassembly in one Link; " +
                            "its override cannot be distributed across Links.");
                }

                Overrides flags = wholeDocument ? documentOverrides : Overrides.None;
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (wholeDocument && documentType == (int)swDocumentTypes_e.swDocASSEMBLY &&
                    flags != (Overrides.Mass | Overrides.Center | Overrides.Inertia))
                {
                    Component2 root = assembly.ConfigurationManager.ActiveConfiguration.GetRootComponent3(false);
                    if (root == null) throw new InvalidOperationException("SolidWorks returned no root for the active assembly configuration.");
                    flags |= ReadChildrenOverrides(metadata, root, observed, visited);
                }
                foreach (Component2 component in bounded)
                    flags |= ReadSubtreeOverrides(metadata, component, observed, visited);

                // Retain the separate-object read discipline of the SW2023 legacy cache workaround.
                // Never ReleaseComObject: these RCWs are owned by SolidWorks and releasing them
                // has terminated the host after repeated Link queries.
                IMassProperty2 centerProperty = CreateScopedProperty(assembly, extension, bounded);
                double[] center = ReadArray(centerProperty.CenterOfMass, 3, "CenterOfMass");
                double mass = centerProperty.Mass;
                IMassProperty2 inertiaProperty = CreateScopedProperty(assembly, extension, bounded);
                double[] moment = ReadArray(inertiaProperty.GetMomentOfInertia(
                    (int)swMassPropertyMoment_e.swMassPropertyMomentAboutCenterOfMass), 9, "GetMomentOfInertia");

                if (!IsFinite(mass) || mass <= 0)
                    throw new InvalidOperationException("SolidWorks returned a non-positive or non-finite effective mass for the selected components.");
                if (configuration != ActiveConfigurationName(assembly) || observed.Values.Any(state =>
                    state.Configuration != state.Component.ReferencedConfiguration))
                    throw new InvalidOperationException("The active or referenced configuration changed while reading mass properties. Retry in a stable assembly configuration.");

                return new MassPropertySnapshot(mass, center, moment,
                    (flags & Overrides.Mass) != 0, (flags & Overrides.Center) != 0, (flags & Overrides.Inertia) != 0);
            }
            catch (COMException error)
            {
                readFailure = new InvalidOperationException(
                    "SolidWorks effective mass-property API failed (0x" + error.ErrorCode.ToString("X8") + "): " +
                    error.Message + " No body-only fallback was used because it could discard overrides.", error);
                throw readFailure;
            }
            catch (Exception error)
            {
                readFailure = error;
                throw;
            }
            finally
            {
                if (selectionSuspended)
                {
                    try
                    {
                        selectionManager.ResumeSelectionList2(false);
                    }
                    catch (Exception restoreError)
                    {
                        throw new InvalidOperationException("SolidWorks could not restore the original selection after reading mass properties.",
                            readFailure == null ? restoreError : new AggregateException(readFailure, restoreError));
                    }
                }
            }
        }

        private static IMassProperty2 CreateProperty(ModelDoc2 document, ModelDocExtension extension)
        {
            // Only called inside the suspended list. Never clear the user's saved selection.
            // Earlier metadata/numeric reads can populate this temporary list again.
            document.ClearSelection2(true);
            if (((SelectionMgr)document.SelectionManager).GetSelectedObjectCount2(-1) != 0)
                throw new InvalidOperationException("SolidWorks did not clear the temporary mass-property selection; refusing inherited component scope.");
            IMassProperty2 property = extension.CreateMassProperty2() as IMassProperty2;
            if (property == null)
                throw new InvalidOperationException(
                    "SolidWorks CreateMassProperty2 is unavailable or returned no applicable mass properties. " +
                    "A resolved solid model and the SolidWorks 2020+ effective-mass API are required; " +
                    "the legacy body-only API cannot preserve overrides.");
            return property;
        }

        private static IMassProperty2 CreateScopedProperty(ModelDoc2 document, ModelDocExtension extension, IList<Component2> components)
        {
            IMassProperty2 property = CreateProperty(document, extension);
            // A fresh object uses the document coordinate system, not a Link coordinate system.
            // UseSystemUnits explicitly requests meters and kilograms (IMassProperty2 contract).
            property.UseSystemUnits = true;
            property.IncludeHiddenBodiesOrComponents = true;
            SetScope(property, components);
            if (!property.Recalculate())
                throw new InvalidOperationException("SolidWorks IMassProperty2.Recalculate failed for the requested component scope.");
            VerifyScope(property, components, "after Recalculate");
            return property;
        }

        private static void SetScope(IMassProperty2 property, IList<Component2> components)
        {
            // SW2023 faults natively on a null setter value, even on a fresh property.
            // An empty SAFEARRAY requests document scope; never send VT_EMPTY here.
            // Nonempty interface arrays must be marshaled as IDispatch, not VARIANT elements.
            property.SelectedItems = components.Count == 0 ? (object)new object[0]
                : components.Select(component => new DispatchWrapper(component)).ToArray();
            VerifyScope(property, components, "scope assignment");
        }

        private static void VerifyScope(IMassProperty2 property, IList<Component2> expected, string stage)
        {
            object value = property.SelectedItems;
            var items = value as Array;
            string diagnostic = " [stage=" + stage + ", expectedCount=" + expected.Count +
                ", actualCount=" + (items == null ? (value == null ? "0" : "<not-array>") : items.Length.ToString()) +
                ", actualType=" + (value == null ? "null" : value.GetType().FullName) +
                ", rank=" + (items == null ? "n/a" : items.Rank.ToString()) + "]";
            if ((value != null && (items == null || items.Rank != 1)) ||
                (items == null ? 0 : items.Length) != expected.Count)
                throw new InvalidOperationException("SolidWorks did not retain the requested mass-property SelectedItems; refusing an unbounded assembly result." + diagnostic);
            var configurations = expected.ToDictionary(component => component.Name2,
                component => component.ReferencedConfiguration, StringComparer.OrdinalIgnoreCase);
            if (items == null) return;
            foreach (object item in items)
            {
                var wrapper = item as DispatchWrapper;
                var component = (wrapper == null ? item : wrapper.WrappedObject) as Component2;
                string configuration;
                if (component == null || !configurations.TryGetValue(component.Name2, out configuration) ||
                    !String.Equals(configuration, component.ReferencedConfiguration, StringComparison.Ordinal))
                    throw new InvalidOperationException("SolidWorks returned different mass-property SelectedItems or referenced configuration; refusing an unbounded assembly result." + diagnostic);
                configurations.Remove(component.Name2);
            }
        }

        private static Overrides ReadOverrides(IMassProperty2 property, IList<Component2> components)
        {
            // Override options describe stored settings, not calculated geometry. Their API
            // has no Recalculate precondition. Reuse this metadata-only object for each scope;
            // leave hidden/unit settings untouched and recalculate only the two numeric objects.
            foreach (Component2 component in components) VerifyMetadataConfiguration(component);
            SetScope(property, components);
            IMassPropertyOverrideOptions options = property.GetOverrideOptions() as IMassPropertyOverrideOptions;
            if (options == null)
                throw new InvalidOperationException("SolidWorks GetOverrideOptions returned no override metadata; effective mass properties cannot be verified.");
            Overrides result = (options.OverrideMass ? Overrides.Mass : Overrides.None) |
                (options.OverrideCenterOfMass ? Overrides.Center : Overrides.None) |
                (options.OverrideMomentsOfInertia ? Overrides.Inertia : Overrides.None);
            foreach (Component2 component in components) VerifyMetadataConfiguration(component);
            VerifyScope(property, components, "after override metadata");
            return result;
        }

        private static void VerifyMetadataConfiguration(Component2 component)
        {
            string occurrence = component.Name2;
            string referenced = component.ReferencedConfiguration;
            string active = null;
            COMException failure = null;
            try
            {
                var document = component.GetModelDoc2() as ModelDoc2;
                ConfigurationManager manager = document == null ? null : document.ConfigurationManager;
                Configuration configuration = manager == null ? null : manager.ActiveConfiguration;
                active = configuration == null ? null : configuration.Name;
            }
            catch (COMException error)
            {
                failure = error;
            }
            // SW2023 can return override flags from the loaded document's active configuration
            // despite retaining a different occurrence reference, even after Recalculate.
            if (failure != null || string.IsNullOrWhiteSpace(referenced) || string.IsNullOrWhiteSpace(active) ||
                !string.Equals(referenced, active, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Cannot verify effective mass override metadata for occurrence '" + occurrence +
                    "': referenced configuration='" + (referenced ?? "<unavailable>") +
                    "', active document configuration='" + (active ?? "<unavailable>") +
                    "'. Resolve the component and make its loaded document active configuration match the " +
                    "referenced configuration before retrying. Mixed configurations of the same document " +
                    "cannot be verified safely in this read; no configuration was switched.", failure);
        }

        private static Overrides ReadSubtreeOverrides(IMassProperty2 metadata, Component2 component,
            IDictionary<string, ComponentState> observed, ISet<string> visited)
        {
            Observe(component, observed);
            if (!visited.Add(component.Name2))
                throw new InvalidOperationException("Duplicate or cyclic component subtree: " + component.Name2);
            // GetOverrideOptions supports only one selected occurrence at a time.
            Overrides flags = ReadOverrides(metadata, new[] { component });
            if (flags == (Overrides.Mass | Overrides.Center | Overrides.Inertia)) return flags;
            return flags | ReadChildrenOverrides(metadata, component, observed, visited);
        }

        private static Overrides ReadChildrenOverrides(IMassProperty2 metadata, Component2 component,
            IDictionary<string, ComponentState> observed, ISet<string> visited)
        {
            Overrides flags = Overrides.None;
            object childrenValue = component.GetChildren();
            if (childrenValue == null) return flags;
            var children = childrenValue as Array;
            if (children == null || children.Rank != 1)
                throw new InvalidOperationException("SolidWorks returned an invalid component subtree in the active configuration.");
            foreach (object item in children)
            {
                var child = item as Component2;
                if (child == null) throw new InvalidOperationException("SolidWorks returned an unresolved child in the active configuration.");
                if (child.GetSuppression2() == (int)swComponentSuppressionState_e.swComponentSuppressed) continue;
                flags |= ReadSubtreeOverrides(metadata, child, observed, visited);
            }
            return flags;
        }

        private static bool IsDescendantOf(Component2 component, Component2 ancestor)
        {
            // Name2 is the full occurrence path; the slash prevents sibling prefix collisions.
            return component.Name2.StartsWith(ancestor.Name2 + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static void Observe(Component2 component, IDictionary<string, ComponentState> observed)
        {
            if (component == null) throw new ArgumentException("The component selection contains a null occurrence.");
            if (component.IsRoot()) throw new ArgumentException("Select component occurrences, not the assembly traversal root.");
            string name = component.Name2;
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("A selected component has no occurrence path.");
            int suppression = component.GetSuppression2();
            if (suppression != (int)swComponentSuppressionState_e.swComponentFullyResolved &&
                suppression != (int)swComponentSuppressionState_e.swComponentResolved)
                throw new InvalidOperationException("Component '" + name + "' is suppressed, lightweight or unresolved. Resolve it in the current assembly configuration before reading effective mass properties.");
            if (!observed.ContainsKey(name))
                observed.Add(name, new ComponentState(component, component.ReferencedConfiguration));
        }

        private static string ActiveConfigurationName(ModelDoc2 assembly)
        {
            Configuration configuration = assembly.ConfigurationManager == null ? null : assembly.ConfigurationManager.ActiveConfiguration;
            if (configuration == null || string.IsNullOrWhiteSpace(configuration.Name))
                throw new InvalidOperationException("SolidWorks returned no active assembly configuration.");
            return configuration.Name;
        }

        private static double[] ReadArray(object value, int length, string name)
        {
            var values = value as double[];
            if (values == null || values.Length != length || values.Any(item => !IsFinite(item)))
                throw new InvalidOperationException("SolidWorks " + name + " returned missing, malformed or non-finite values.");
            return (double[])values.Clone();
        }

        private static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private sealed class ComponentState
        {
            public ComponentState(Component2 component, string configuration)
            {
                Component = component;
                Configuration = configuration;
            }
            public Component2 Component { get; private set; }
            public string Configuration { get; private set; }
        }
    }
}
