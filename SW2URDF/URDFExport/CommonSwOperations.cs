/*
Copyright (c) 2015 Stephen Brawner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using log4net;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SW2URDF.URDFExport
{
    public static class CommonSwOperations
    {
        private static readonly ILog logger = Logger.GetLogger();

        internal static T TryCastComObject<T>(object value, string context) where T : class
        {
            T typed = value as T;
            if (typed == null && value != null)
            {
                logger.Warn("Ignoring an unexpected SolidWorks COM object while " + context + ".");
            }
            return typed;
        }

        internal static IEnumerable<T> EnumerateComObjects<T>(
            object[] values,
            string context) where T : class
        {
            if (values == null)
            {
                yield break;
            }
            foreach (object value in values)
            {
                T typed = TryCastComObject<T>(value, context);
                if (typed != null)
                {
                    yield return typed;
                }
            }
        }

        //Selects the components of a link. Helps highlight when the associated node is
        // selected from the tree
        public static void SelectComponents(ModelDoc2 model, Link Link, bool clearSelection, int mark = -1)
        {
            if (clearSelection)
            {
                model.ClearSelection2(true);
            }
            SelectionMgr manager = model.SelectionManager;
            SelectData data = manager.CreateSelectData();
            data.Mark = mark;
            SelectComponents(model, Link.SWComponents, false);
            foreach (Link child in Link.Children)
            {
                SelectComponents(model, child, false, mark);
            }
        }

        //Selects components from a list.
        public static void SelectComponents(
            ModelDoc2 model, List<Component2> components, bool clearSelection = true, int mark = -1)
        {
            if (clearSelection)
            {
                model.ClearSelection2(true);
            }
            SelectionMgr manager = model.SelectionManager;
            SelectData data = manager.CreateSelectData();
            data.Mark = mark;
            foreach (Component2 component in components)
            {
                component.Select4(true, data, false);
            }
        }

        //Finds the selected components and returns them, used when pulling the items from
        // the selection box because it would be too hard for SolidWorks to allow you to
        // access the selectionbox components directly.
        public static void GetSelectedComponents(
            ModelDoc2 model, List<Component2> Components, int Mark = -1)
        {
            SelectionMgr selectionManager = model.SelectionManager;
            Components.Clear();
            for (int i = 0; i < selectionManager.GetSelectedObjectCount2(Mark); i++)
            {
                object obj = selectionManager.GetSelectedObject6(i + 1, Mark);
                Component2 comp = TryCastComObject<Component2>(
                    obj,
                    "reading selected components");
                if (comp != null)
                {
                    Components.Add(comp);
                }
            }
        }

        //finds all the hidden components, which will be added to a new display state. Also
        // used when exporting STLs, so that hidden components remain hidden
        public static List<string> FindHiddenComponents(object[] varComp)
        {
            List<string> hiddenComp = new List<string>();
            foreach (Component2 comp in EnumerateComObjects<Component2>(
                varComp,
                "reading assembly component visibility"))
            {
                if (comp.IsHidden(false))
                {
                    hiddenComp.Add(comp.Name2);
                }
            }
            return hiddenComp;
        }

        //Except for an exclusionary list, this shows all the components
        public static void ShowAllComponents(ModelDoc2 model, List<string> hiddenComponents)
        {
            AssemblyDoc assyDoc = (AssemblyDoc)model;
            List<Component2> componentsToShow = new List<Component2>();
            object[] varComps = assyDoc.GetComponents(false);
            foreach (Component2 comp in EnumerateComObjects<Component2>(
                varComps,
                "showing assembly components"))
            {
                if (!hiddenComponents.Contains(comp.Name2))
                {
                    componentsToShow.Add(comp);
                }
            }
            SetComponentVisibility(model, componentsToShow, true);
        }

        //Shows the components in the list. Useful  for exporting STLs
        public static void ShowComponents(ModelDoc2 model, List<Component2> components)
        {
            SetComponentVisibility(model, ExpandComponents(components), true);
        }

        //Hides the components from a list
        public static void HideComponents(ModelDoc2 model, List<Component2> components)
        {
            SetComponentVisibility(model, ExpandComponents(components), false);
        }

        internal static void SetComponentVisibility(
            ModelDoc2 model, IEnumerable<Component2> components, bool visible)
        {
            SetComponentVisibility(model, components, visible,
                component => new DispatchWrapper(component));
        }

        internal static void SetComponentVisibility(ModelDoc2 model,
            IEnumerable<Component2> components, bool visible, Func<Component2, object> prepareSelection)
        {
            // Snapshot restoration is already flat. Do not expand it a second time.
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Component2> targets = (components ?? Enumerable.Empty<Component2>())
                .Where(component => component != null && visited.Add(component.Name2) &&
                    !component.IsSuppressed())
                .ToList();
            int desiredState = visible
                ? (int)swComponentVisibilityState_e.swComponentVisible
                : (int)swComponentVisibilityState_e.swComponentHidden;
            List<Component2> changed = targets.Where(component => component.Visible != desiredState).ToList();
            if (changed.Count == 0)
            {
                return;
            }
            Exception cleanupFailure = null;
            try
            {
                object[] objects = changed.Select(prepareSelection).ToArray();
                int selected = model.Extension.MultiSelect2(objects, false, null);
                if (selected == objects.Length)
                {
                    if (visible) model.ShowComponent2();
                    else model.HideComponent2();
                }
                else
                {
                    logger.Warn(String.Format(
                        "Selected {0}/{1} visibility changes; using direct component visibility to {2}.",
                        selected, objects.Length, visible ? "show" : "hide"));
                }
            }
            catch (Exception exception)
            {
                logger.Warn("Bulk visibility update failed; verifying direct component visibility.", exception);
            }
            finally
            {
                try { model.ClearSelection2(true); }
                catch (Exception cleanupException)
                {
                    cleanupFailure = cleanupException;
                    logger.Error("Clearing the visibility selection failed.", cleanupException);
                }
            }

            // Hidden descendants need not be selectable. Verify states, not selection counts.
            // Parent-first showing and child-first hiding avoid changing a subtree repeatedly.
            IEnumerable<Component2> ordered = visible
                ? targets.OrderBy(component => component.Name2.Count(character => character == '/'))
                : targets.OrderByDescending(component => component.Name2.Count(character => character == '/'));
            List<Exception> failures = new List<Exception>();
            List<string> failedNames = new List<string>();
            foreach (Component2 component in ordered)
            {
                try
                {
                    if (component.Visible != desiredState) component.Visible = desiredState;
                    if (component.Visible != desiredState)
                    {
                        throw new InvalidOperationException("SolidWorks did not apply the requested visibility.");
                    }
                }
                catch (Exception exception)
                {
                    failedNames.Add(component.Name2);
                    failures.Add(exception);
                }
            }
            foreach (Component2 component in targets)
            {
                if (failedNames.Contains(component.Name2)) continue;
                try
                {
                    if (component.Visible != desiredState)
                        throw new InvalidOperationException("A later visibility change altered this component.");
                }
                catch (Exception exception)
                {
                    failedNames.Add(component.Name2);
                    failures.Add(exception);
                }
            }
            if (failedNames.Count > 0)
            {
                if (cleanupFailure != null) failures.Add(cleanupFailure);
                throw new ComponentVisibilityException(
                    "ERROR COMPONENT_VISIBILITY: Could not " + (visible ? "show" : "hide") +
                    " components: " + String.Join(", ", failedNames) + ". No partial mesh will be exported.",
                    new AggregateException(failures), cleanupFailure != null);
            }
            if (cleanupFailure != null)
            {
                throw new ComponentVisibilityException(
                    "ERROR COMPONENT_VISIBILITY: Component states were applied, but the SolidWorks selection could not be cleared.",
                    cleanupFailure, true);
            }
        }

        internal sealed class ComponentVisibilityException : InvalidOperationException
        {
            internal bool SelectionCleanupFailed { get; private set; }

            internal ComponentVisibilityException(string message, Exception inner, bool selectionCleanupFailed)
                : base(message, inner)
            {
                SelectionCleanupFailed = selectionCleanupFailed;
            }
        }

        private static List<Component2> ExpandComponents(IEnumerable<Component2> components)
        {
            return ExpandDistinctComponents(components, component => component.Name2,
                component => EnumerateComObjects<Component2>(component.GetChildren() as object[],
                    "reading component children"));
        }

        internal static List<T> ExpandDistinctComponents<T>(IEnumerable<T> roots,
            Func<T, string> identity, Func<T, IEnumerable<T>> children) where T : class
        {
            List<T> result = new List<T>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Stack<T> pending = new Stack<T>((roots ?? Enumerable.Empty<T>()).Reverse());
            while (pending.Count > 0)
            {
                T component = pending.Pop();
                if (component == null || !visited.Add(identity(component)))
                {
                    continue;
                }
                result.Add(component);
                foreach (T child in (children(component) ?? Enumerable.Empty<T>()).Reverse())
                {
                    pending.Push(child);
                }
            }
            return result;
        }

        public static int GetCount(Link Link)
        {
            int count = 1;
            foreach (Link child in Link.Children)
            {
                count += GetCount(child);
            }
            return count;
        }

        public static int GetCount(TreeNodeCollection nodes)
        {
            int count = 0;
            foreach (LinkNode node in nodes)
            {
                count += 1;
                count += GetCount(node.Nodes);
            }
            return count;
        }

        public static void RetrieveSWComponentPIDs(ModelDoc2 model, LinkNode node)
        {
            RetrieveSWComponentPIDs(model, node.Link);
            foreach (LinkNode child in node.Nodes)
            {
                RetrieveSWComponentPIDs(model, child);
            }
        }

        public static void RetrieveSWComponentPIDs(ModelDoc2 model, Link link)
        {
            link.SWComponentPIDs = new List<byte[]>();
            if (link.SWComponents == null)
            {
                return;
            }
            foreach (IComponent2 component in link.SWComponents)
            {
                link.SWComponentPIDs.Add(model.Extension.GetPersistReference3(component));
            }
        }

        //Converts the SW component references to PIDs
        public static void SaveSWComponents(ModelDoc2 model, Link Link)
        {
            model.ClearSelection2(true);
            byte[] PID = SaveSWComponent(model, Link.SWMainComponent);
            if (PID != null)
            {
                Link.SWMainComponentPID = PID;
            }
            Link.SWComponentPIDs = SaveSWComponents(model, Link.SWComponents);

            foreach (Link Child in Link.Children)
            {
                SaveSWComponents(model, Child);
            }
        }

        //Converts SW component references to PIDs
        public static List<byte[]> SaveSWComponents(ModelDoc2 model, List<Component2> components)
        {
            List<byte[]> PIDs = new List<byte[]>();
            foreach (Component2 component in components)
            {
                byte[] PID = SaveSWComponent(model, component);
                if (PID != null)
                {
                    PIDs.Add(PID);
                }
            }
            return PIDs;
        }

        public static byte[] SaveSWComponent(ModelDoc2 model, Component2 component)
        {
            if (component != null)
            {
                return model.Extension.GetPersistReference3(component);
            }
            return null;
        }

        // Converts the PIDs to actual references to the components and proceeds recursively
        // through the child nodes
        public static void LoadSWComponents(ModelDoc2 model, LinkNode node, List<string> problemLinks)
        {
            logger.Info("Loading SolidWorks components for " +
                node.Link.Name + " from " + model.GetPathName());

            node.Link.SWComponents = LoadSWComponents(model, node.Link.SWComponentPIDs);
            if (node.Link.SWComponents.Count != node.Link.SWComponentPIDs.Count)
            {
                problemLinks.Add(node.Link.Name);
                logger.Error("Link " + node.Link.Name + " did not fully load all components");
            }
            logger.Info("Loaded " + node.Link.SWComponents.Count + " components for link " + node.Link.Name);

            foreach (LinkNode Child in node.Nodes)
            {
                LoadSWComponents(model, Child, problemLinks);
            }
        }

        // Converts the PIDs to actual references to the components
        public static List<Component2> LoadSWComponents(ModelDoc2 model, List<byte[]> PIDs)
        {
            List<Component2> components = new List<Component2>();
            foreach (byte[] PID in PIDs)
            {
                string byteAsString = PIDToString(PID);
                logger.Info("Loading component with PID " + byteAsString);
                Component2 comp = LoadSWComponent(model, PID);
                if (comp == null)
                {
                    logger.Warn("Component with PID " + byteAsString + " failed to load");
                }
                else
                {
                    components.Add(comp);
                    logger.Info("Successfully loaded component " + comp.GetPathName());
                }
            }
            return components;
        }

        // Converts a single PID to a Component2 object
        public static Component2 LoadSWComponent(ModelDoc2 model, byte[] PID)
        {
            if (PID == null)
            {
                throw new System.Exception("PID was null. Is the configuration corrupted?");
            }
            string byteAsString = PIDToString(PID);

            object obj = model.Extension.GetObjectByPersistReference3(PID, out int Errors);
            if (Errors == 0)
            {
                Component2 component = TryCastComObject<Component2>(
                    obj,
                    "loading a component from its persistent reference");
                if (component == null)
                {
                    logger.Error(
                        "The persistent reference resolved successfully but was not a component: " +
                        byteAsString);
                }
                return component;
            }
            switch ((swPersistReferencedObjectStates_e)Errors)
            {
                case swPersistReferencedObjectStates_e.swPersistReferencedObject_Deleted:
                    logger.Error("The component associated with PID " + byteAsString + " was deleted");
                    break;

                case swPersistReferencedObjectStates_e.swPersistReferencedObject_Invalid:
                    logger.Error("The component associated with PID " + byteAsString + " was found to be invalid");
                    break;

                case swPersistReferencedObjectStates_e.swPersistReferencedObject_Suppressed:
                    logger.Error("The component associated with PID " + byteAsString + " is suppressed");
                    break;

                case swPersistReferencedObjectStates_e.swPersistReferencedObject_Ok:
                    break;

                default:
                    logger.Error("The component associated with PID " + byteAsString +
                        " was not loaded due to an unspecified error (" + Errors + ")");
                    break;
            }
            return null;
        }

        public static string PIDToString(byte[] pid)
        {
            return Encoding.ASCII.GetString(pid);
        }

        internal static bool ComReferencesEqual(object left, object right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left == null || right == null)
            {
                return false;
            }

            IntPtr leftIdentity = IntPtr.Zero;
            IntPtr rightIdentity = IntPtr.Zero;
            try
            {
                leftIdentity = Marshal.GetIUnknownForObject(left);
                rightIdentity = Marshal.GetIUnknownForObject(right);
                return leftIdentity == rightIdentity;
            }
            catch (COMException)
            {
                return false;
            }
            catch (InvalidComObjectException)
            {
                return false;
            }
            finally
            {
                if (leftIdentity != IntPtr.Zero)
                {
                    Marshal.Release(leftIdentity);
                }
                if (rightIdentity != IntPtr.Zero)
                {
                    Marshal.Release(rightIdentity);
                }
            }
        }
    }
}
