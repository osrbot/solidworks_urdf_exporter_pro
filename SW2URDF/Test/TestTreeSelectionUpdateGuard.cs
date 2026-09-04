using Moq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using Xunit;

namespace SW2URDF.Test
{
    public class TestTreeSelectionUpdateGuard
    {
        [Fact]
        public void TestProgrammaticTreeUpdatesSuppressSelectionPersistence()
        {
            TreeSelectionUpdateGuard guard = new TreeSelectionUpdateGuard();

            Assert.False(guard.IsSuppressed);
            using (guard.Suppress())
            {
                Assert.True(guard.IsSuppressed);
                using (guard.Suppress())
                {
                    Assert.True(guard.IsSuppressed);
                }
                Assert.True(guard.IsSuppressed);
            }
            Assert.False(guard.IsSuppressed);
        }

        [Fact]
        public void TestSuppressionScopeCanOnlyReleaseOnce()
        {
            TreeSelectionUpdateGuard guard = new TreeSelectionUpdateGuard();
            System.IDisposable scope = guard.Suppress();

            scope.Dispose();
            scope.Dispose();

            Assert.False(guard.IsSuppressed);
        }

        [Fact]
        public void TestAssemblyTreeHandlersIgnoreProgrammaticSelections()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                TreeSelectionUpdateGuard guard = (TreeSelectionUpdateGuard)
                    typeof(AssemblyExportForm).GetField(
                        "treeSelectionUpdateGuard",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                MethodInfo linkHandler = typeof(AssemblyExportForm).GetMethod(
                    "TreeViewLinkPropertiesAfterSelect",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo jointHandler = typeof(AssemblyExportForm).GetMethod(
                    "TreeViewJointtreeAfterSelect",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                using (guard.Suppress())
                {
                    linkHandler.Invoke(form, new object[] { null, new TreeViewEventArgs(null) });
                    jointHandler.Invoke(form, new object[] { null, new TreeViewEventArgs(null) });
                }
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestSuppressedPropertyManagerSelectionCallbackPreservesBindingsWithoutTouchingPage()
        {
            var manager = (ExportPropertyManager)FormatterServices.GetUninitializedObject(typeof(ExportPropertyManager));
            var guard = new TreeSelectionUpdateGuard();
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(ExportPropertyManager).GetField("treeSelectionUpdateGuard", fields).SetValue(manager, guard);
            Assert.Null(typeof(ExportPropertyManager).GetField("PMPage", fields).GetValue(manager));
            Assert.Null(manager.ActiveSWModel);

            var component = new Mock<Component2>(MockBehavior.Strict).Object;
            var components = new List<Component2> { component };
            var pid = new byte[] { 1, 2, 3 };
            var pids = new List<byte[]> { pid };
            var node = new LinkNode();
            node.Link.SWComponents = components;
            node.Link.SWComponentPIDs = pids;

            using (var tree = new TreeView())
            {
                tree.Nodes.Add(node);
                tree.SelectedNode = node;
                manager.Tree = tree;
                manager.previouslySelectedNode = node;
                var callbacks = (IPropertyManagerPage2Handler9)manager;
                using (guard.Suppress())
                {
                    callbacks.OnSelectionboxListChanged(3, 0);
                    callbacks.OnSelectionboxListChanged(3, 1);
                    callbacks.OnSelectionboxListChanged(4, 0);
                }

                Assert.False(guard.IsSuppressed);
                Assert.Same(components, node.Link.SWComponents);
                Assert.Same(component, Assert.Single(node.Link.SWComponents));
                Assert.Same(pids, node.Link.SWComponentPIDs);
                Assert.Same(pid, Assert.Single(node.Link.SWComponentPIDs));
            }
        }
    }
}
