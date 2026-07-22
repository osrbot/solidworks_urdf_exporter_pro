using SolidWorks.Interop.sldworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace SW2URDF.Test
{
    [CollectionDefinition("Requires SW Test Collection", DisableParallelization = true)]
    public sealed class SWTestCollection : ICollectionFixture<SWTestFixture>
    {
    }

    /// <summary>
    /// TestFixture which gets passed to each Test Class. For now it just provides 
    /// the reference to the SolidWorks app.
    /// </summary>
    public class SWTestFixture : IDisposable
    {
        private static readonly object InitializationLock = new object();
        private static bool initialized;
        private static int ownedProcessId;
        private static bool ownsProcess;
        public static SldWorks SwApp { get; private set; }

        public static void Initialize()
        {
            lock (InitializationLock)
            {
                if (initialized)
                {
                    return;
                }

                HashSet<int> existingProcessIds = new HashSet<int>();
                foreach (Process process in Process.GetProcessesByName("SLDWORKS"))
                {
                    using (process)
                    {
                        existingProcessIds.Add(process.Id);
                    }
                }

                SwApp = (SldWorks)Activator.CreateInstance(
                    Type.GetTypeFromProgID("SldWorks.Application"));
                ownedProcessId = SwApp.GetProcessID();
                ownsProcess = ownedProcessId > 0 && !existingProcessIds.Contains(ownedProcessId);
                SwApp.Visible = true;
                initialized = true;
            }
        }

        public static SldWorks GetInitializedApplication()
        {
            lock (InitializationLock)
            {
                return initialized ? SwApp : null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            lock (InitializationLock)
            {
                SldWorks application = SwApp;
                int processId = ownedProcessId;
                bool terminateIfStillRunning = ownsProcess;
                SwApp = null;
                initialized = false;
                ownedProcessId = 0;
                ownsProcess = false;
                if (application == null)
                {
                    return;
                }

                Exception cleanupFailure = null;
                try
                {
                    application.CloseAllDocuments(true);
                }
                catch (Exception e)
                {
                    cleanupFailure = e;
                }

                try
                {
                    application.ExitApp();
                }
                catch (Exception e)
                {
                    if (cleanupFailure == null)
                    {
                        cleanupFailure = e;
                    }
                }
                finally
                {
                    if (Marshal.IsComObject(application))
                    {
                        Marshal.FinalReleaseComObject(application);
                    }
                }

                if (terminateIfStillRunning && processId > 0)
                {
                    TerminateOwnedProcessIfNeeded(processId);
                }

                if (cleanupFailure != null)
                {
                    throw new InvalidOperationException(
                        "Cleaning up the SolidWorks test application failed.",
                        cleanupFailure);
                }
            }
        }

        private static void TerminateOwnedProcessIfNeeded(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    if (!process.WaitForExit(10000))
                    {
                        process.Kill();
                        process.WaitForExit(10000);
                    }
                }
            }
            catch (ArgumentException)
            {
                // The process already exited between ExitApp and this check.
            }
        }
    }
}
