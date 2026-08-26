using SW2URDF.UI;
using System;
using System.Reflection;
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
    }
}
