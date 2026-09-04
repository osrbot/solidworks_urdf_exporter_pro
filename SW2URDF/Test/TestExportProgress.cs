using SW2URDF.URDFExport;
using SW2URDF.UI;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace SW2URDF.Test
{
    public class TestExportProgress
    {
        [Fact]
        public void TestExportProgressWindowStaysAboveSolidWorks()
        {
            RunOnStaWithoutMessagePump(() =>
            {
                using (Form owner = new Form { Bounds = new Rectangle(100, 100, 700, 500) })
                {
                    owner.Show();
                    owner.Enabled = false;
                    WindowProbe probe = new WindowProbe();
                    using (ExportProgressSession session = new ExportProgressSession(owner, () =>
                    {
                        ExportProgressForm form = probe.CreateForm();
                        form.Shown += (sender, args) => form.Close(); // Alt+F4/UserClosing must be ignored.
                        return form;
                    }))
                    {
                        session.Start();
                        Assert.True(probe.TopMost);
                        Assert.False(probe.ShowInTaskbar);
                        Assert.False(probe.ControlBox);
                        Assert.NotEqual(owner.Handle, probe.NativeOwner);
                        Assert.True(probe.OwnerOnWindowThread);
                        Assert.True(probe.NoActivate);
                        Assert.True(probe.Shown);
                        Assert.False(probe.Disposed);
                        Assert.False(owner.Enabled);
                        Assert.Equal(ApartmentState.STA, probe.Apartment);
                        Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, probe.WindowThread.ManagedThreadId);
                        Rectangle workArea = Screen.FromControl(owner).WorkingArea;
                        Assert.Equal(Math.Max(workArea.Left, Math.Min(
                            owner.Left + (owner.Width - probe.Bounds.Width) / 2, workArea.Right - probe.Bounds.Width)), probe.Bounds.Left);
                        Assert.Equal(Math.Max(workArea.Top, Math.Min(
                            owner.Top + (owner.Height - probe.Bounds.Height) / 2, workArea.Bottom - probe.Bounds.Height)), probe.Bounds.Top);
                    }
                    AssertStopped(probe);
                }
            });
        }

        [Fact]
        public void TestExportProgressTimerAndUpdatesWhileCallerIsBlocked()
        {
            RunOnStaWithoutMessagePump(() =>
            {
                using (Form owner = new Form())
                {
                    int callerCallbacks = 0;
                    WindowProbe probe = new WindowProbe();
                    using (ExportProgressSession session = new ExportProgressSession(owner, probe.CreateForm))
                    {
                        owner.BeginInvoke(new Action(() => Interlocked.Increment(ref callerCallbacks)));
                        session.Start();
                        session.UpdateProgress(new ExportProgressEventArgs("SaveAs mesh", TimeSpan.FromMinutes(16)));
                        // No Run, DoEvents, Join, or STA managed wait on this caller, including disposal.
                        Thread.Sleep(1500);
                        WaitUntil(() => probe.Elapsed.Distinct().Count() >= 3 &&
                            Last(probe.StepElapsed) != "Current step: 00:00");
                        Assert.Equal("SaveAs mesh", Last(probe.Stages));
                        Assert.StartsWith("Elapsed: 16:", Last(probe.Elapsed));
                        Assert.True(probe.Elapsed.Distinct().Count() >= 3);
                        Assert.NotEqual("Current step: 00:00", Last(probe.StepElapsed));
                        Assert.True(probe.TickHasMessageLoop);
                        Assert.Equal(probe.WindowThread.ManagedThreadId, probe.TickThreadId);
                        Assert.Equal("Still waiting for the current step to finish.", probe.WaitingText);
                        Assert.Equal(0, Volatile.Read(ref callerCallbacks));

                        string elapsedBeforeRepeat = Last(probe.Elapsed);
                        session.UpdateProgress(new ExportProgressEventArgs("SaveAs mesh", TimeSpan.FromMinutes(16)));
                        Thread.Sleep(400);
                        Assert.NotEqual("Current step: 00:00", Last(probe.StepElapsed));
                        Assert.True(String.CompareOrdinal(elapsedBeforeRepeat, Last(probe.Elapsed)) <= 0);
                        session.UpdateProgress(new ExportProgressEventArgs("Restore visibility", TimeSpan.FromMinutes(17)));
                        WaitUntil(() => Last(probe.Stages) == "Restore visibility" &&
                            Last(probe.StepElapsed) == "Current step: 00:00");
                        Assert.StartsWith("Elapsed: 17:", Last(probe.Elapsed));
                        session.UpdateProgress(null);
                    }
                    AssertStopped(probe);
                    Assert.Equal(0, Volatile.Read(ref callerCallbacks));
                }
            });
        }

        [Fact]
        public void TestExportProgressCapturesCulturesBeforeStartingWorker()
        {
            RunOnStaWithoutMessagePump(() =>
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-GB");
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("zh-CN");
                WindowProbe probe = new WindowProbe();
                using (ExportProgressSession session = new ExportProgressSession(null, probe.CreateForm))
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo("de-DE");
                    session.Start();
                    Assert.Equal("en-GB", probe.CultureName);
                    Assert.Equal("zh-CN", probe.UiCultureName);
                    Assert.Equal("\u6b63\u5728\u51c6\u5907\u5bfc\u51fa", Last(probe.Stages));
                    Assert.Equal("\u4ecd\u5728\u7b49\u5f85\u5f53\u524d\u6b65\u9aa4\u5b8c\u6210\u3002", probe.WaitingText);
                }
                AssertStopped(probe);
            });
        }

        [Fact]
        public void TestExportProgressStartupFailureLeavesNoThread()
        {
            RunOnStaWithoutMessagePump(() =>
            {
                Thread failedThread = null;
                InvalidOperationException expected = new InvalidOperationException("Injected window startup failure");
                using (ExportProgressSession session = new ExportProgressSession(null, () =>
                {
                    failedThread = Thread.CurrentThread;
                    throw expected;
                }))
                {
                    InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() => session.Start());
                    Assert.Same(expected, actual.InnerException);
                    Assert.Same(expected, session.Failure);
                    Assert.False(session.IsRunning);
                    Assert.False(failedThread.IsAlive);
                    session.Dispose();
                }
            });
        }

        [Fact]
        public void TestExportProgressStartupTimeoutReclaimsLateWindow()
        {
            RunOnStaWithoutMessagePump(() =>
            {
                WindowProbe probe = new WindowProbe();
                using (ExportProgressSession session = new ExportProgressSession(null, () =>
                {
                    Thread.Sleep(750);
                    return probe.CreateForm();
                }))
                {
                    Stopwatch timeout = Stopwatch.StartNew();
                    Assert.Throws<TimeoutException>(() => session.Start(100));
                    Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(6));
                    Assert.False(session.IsRunning);
                    Assert.False(probe.Shown);
                    // If cancellation won before the factory ran, no window was allocated.
                    Assert.True(probe.WindowThread == null || probe.Disposed);
                    if (probe.WindowThread != null)
                    {
                        AssertStopped(probe);
                    }
                }
            });
        }

        [Fact]
        public void TestExportProgressShownFailureDisposesWindowAndThread()
        {
            RunOnStaWithoutMessagePump(() =>
            {
                WindowProbe probe = new WindowProbe();
                InvalidOperationException expected = new InvalidOperationException("Injected shown failure");
                using (ExportProgressSession session = new ExportProgressSession(null, () =>
                {
                    ExportProgressForm form = probe.CreateForm();
                    form.Shown += (sender, args) => { throw expected; };
                    return form;
                }))
                {
                    Assert.Throws<InvalidOperationException>(() => session.Start());
                    Assert.Same(expected, session.Failure);
                    AssertStopped(probe);
                }
            });
        }

        [Fact]
        public void TestExportProgressEarlyMessageLoopExitDoesNotReportReady()
        {
            RunOnStaWithoutMessagePump(() =>
            {
                WindowProbe probe = new WindowProbe();
                using (ExportProgressSession session = new ExportProgressSession(null, () =>
                {
                    ExportProgressForm form = probe.CreateForm();
                    form.Shown += (sender, args) => form.Finish();
                    return form;
                }))
                {
                    Assert.Throws<InvalidOperationException>(() => session.Start());
                    Assert.False(session.IsRunning);
                    AssertStopped(probe);
                }
            });
        }

        [Fact]
        public void TestExportProgressOwnerDestructionStopsWindowWithoutCallerPump()
        {
            RunOnStaWithoutMessagePump(() =>
            {
                WindowProbe probe = new WindowProbe();
                using (Form owner = new Form())
                using (ExportProgressSession session = new ExportProgressSession(owner, probe.CreateForm))
                {
                    session.Start();
                    owner.Dispose();
                    WaitUntil(() => !session.IsRunning);
                    AssertStopped(probe);
                }
            });
        }

        [Fact]
        public void TestExportProgressRepeatedTeardownRacingUpdatesLeavesNoThreads()
        {
            RunOnStaWithoutMessagePump(() =>
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    WindowProbe probe = new WindowProbe();
                    using (ExportProgressSession session = new ExportProgressSession(null, probe.CreateForm))
                    {
                        session.Start();
                        Assert.Throws<InvalidOperationException>(() => session.Start());
                        Exception producerFailure = null;
                        Thread producer = new Thread(() =>
                        {
                            try
                            {
                                for (int index = 0; index < 5000; index++)
                                {
                                    session.UpdateProgress(new ExportProgressEventArgs("Step " + index, TimeSpan.FromSeconds(index)));
                                }
                            }
                            catch (Exception exception)
                            {
                                producerFailure = exception;
                            }
                        }) { IsBackground = true };
                        producer.Start();
                        try
                        {
                            session.Dispose();
                        }
                        finally
                        {
                            WaitUntil(() => !producer.IsAlive);
                        }
                        session.Dispose();
                        session.UpdateProgress(new ExportProgressEventArgs("Late update", TimeSpan.Zero));
                        Assert.Null(producerFailure);
                        Assert.Null(session.Failure);
                        Assert.False(session.IsRunning);
                        AssertStopped(probe);
                    }
                }
            });
        }

        [Fact]
        public void TestExportProgressDisposedBeforeStartNeverCreatesThread()
        {
            RunOnStaWithoutMessagePump(() =>
            {
                WindowProbe probe = new WindowProbe();
                using (ExportProgressSession session = new ExportProgressSession(null, probe.CreateForm))
                {
                    session.Dispose();
                    session.Dispose();
                    Assert.Throws<ObjectDisposedException>(() => session.Start());
                    Assert.False(session.IsRunning);
                    Assert.Null(probe.WindowThread);
                }
            });
        }

        // Only immutable values leave the window thread. Tests never read its Controls.
        private sealed class WindowProbe
        {
            internal readonly ConcurrentQueue<string> Stages = new ConcurrentQueue<string>();
            internal readonly ConcurrentQueue<string> Elapsed = new ConcurrentQueue<string>();
            internal readonly ConcurrentQueue<string> StepElapsed = new ConcurrentQueue<string>();
            internal Thread WindowThread;
            internal ApartmentState Apartment;
            internal string CultureName;
            internal string UiCultureName;
            internal string WaitingText;
            internal Rectangle Bounds;
            internal IntPtr NativeOwner;
            internal bool OwnerOnWindowThread;
            internal bool NoActivate;
            internal bool TopMost;
            internal bool ShowInTaskbar;
            internal bool ControlBox;
            internal volatile bool Shown;
            internal volatile bool Disposed;
            internal volatile bool TickHasMessageLoop;
            internal volatile int TickThreadId;

            internal ExportProgressForm CreateForm()
            {
                WindowThread = Thread.CurrentThread;
                Apartment = WindowThread.GetApartmentState();
                CultureName = CultureInfo.CurrentCulture.Name;
                UiCultureName = CultureInfo.CurrentUICulture.Name;
                ExportProgressForm form = new ExportProgressForm();
                TopMost = form.TopMost;
                ShowInTaskbar = form.ShowInTaskbar;
                ControlBox = form.ControlBox;
                WaitingText = form.Controls["waiting"].Text;
                Stages.Enqueue(form.Controls["stage"].Text);
                Elapsed.Enqueue(form.Controls["elapsed"].Text);
                StepElapsed.Enqueue(form.Controls["stageElapsed"].Text);
                form.Controls["stage"].TextChanged += (sender, args) => Stages.Enqueue(((Control)sender).Text);
                form.Controls["elapsed"].TextChanged += (sender, args) =>
                {
                    TickThreadId = Thread.CurrentThread.ManagedThreadId;
                    TickHasMessageLoop = Application.MessageLoop;
                    Elapsed.Enqueue(((Control)sender).Text);
                };
                form.Controls["stageElapsed"].TextChanged += (sender, args) => StepElapsed.Enqueue(((Control)sender).Text);
                form.Shown += (sender, args) =>
                {
                    Bounds = form.Bounds;
                    NativeOwner = GetWindow(form.Handle, 4); // GW_OWNER
                    // WinForms may create its own hidden taskbar owner, but it must be local.
                    uint processId;
                    OwnerOnWindowThread = NativeOwner == IntPtr.Zero ||
                        GetWindowThreadProcessId(NativeOwner, out processId) == GetWindowThreadProcessId(form.Handle, out processId);
                    NoActivate = (GetWindowLong(form.Handle, -20) & 0x08000000) != 0;
                    Shown = true;
                };
                form.Disposed += (sender, args) => Disposed = true;
                return form;
            }
        }

        private static string Last(ConcurrentQueue<string> values)
        {
            return values.ToArray().LastOrDefault();
        }

        private static void AssertStopped(WindowProbe probe)
        {
            Assert.True(probe.Disposed);
            Assert.NotNull(probe.WindowThread);
            Assert.False(probe.WindowThread.IsAlive);
        }

        private static void WaitUntil(Func<bool> condition, int milliseconds = 5000)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.True(timeout.ElapsedMilliseconds < milliseconds, "Timed out without pumping the caller's messages.");
                Thread.Sleep(10);
            }
        }

        private static void RunOnStaWithoutMessagePump(Action test)
        {
            Exception failure = null;
            Thread caller = new Thread(() =>
            {
                try
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
                    Assert.False(Application.MessageLoop);
                    test();
                    Assert.False(Application.MessageLoop);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }) { IsBackground = true };
            caller.SetApartmentState(ApartmentState.STA);
            caller.Start();
            WaitUntil(() => !caller.IsAlive, 30000);
            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr handle, uint command);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr handle, int index);

        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(1024, "1 KiB")]
        [InlineData(1048576, "1 MiB")]
        public void TestExportSummaryFormatsBinaryFileSize(long bytes, string expected)
        {
            Assert.Equal(expected, ExportResultSummary.FormatBytes(bytes));
        }

        [Fact]
        public void TestExportSummaryCountsOnlyFilesWrittenByCurrentExport()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-export-summary-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            URDFPackage package = new URDFPackage("robot", "robot_description", root);
            package.CreateDirectories();
            string staleFile = Path.Combine(package.WindowsMeshesDirectory, "stale.stl");
            File.WriteAllBytes(staleFile, new byte[11]);
            File.SetLastWriteTimeUtc(staleFile, DateTime.UtcNow.AddMinutes(-5));
            ExportOutputSnapshot beforeExport = ExportOutputSnapshot.Capture(package);
            string currentFile = Path.Combine(package.WindowsRobotsDirectory, "robot.urdf");
            File.WriteAllBytes(currentFile, new byte[23]);

            try
            {
                ExportResultSummary summary = ExportResultSummary.Create(
                    package,
                    beforeExport,
                    TimeSpan.FromSeconds(2));

                Assert.Equal(1, summary.FileCount);
                Assert.Equal(23, summary.TotalBytes);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void TestExportSummaryCountsOverwrittenFilesWithoutTimestampHeuristics()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-export-summary-overwrite-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            URDFPackage package = new URDFPackage("robot", "robot_description", root);
            package.CreateDirectories();
            string output = Path.Combine(package.WindowsRobotsDirectory, "robot.urdf");
            File.WriteAllBytes(output, new byte[11]);
            ExportOutputSnapshot beforeExport = ExportOutputSnapshot.Capture(package);
            File.WriteAllBytes(output, new byte[29]);

            try
            {
                ExportResultSummary summary = ExportResultSummary.Create(
                    package,
                    beforeExport,
                    TimeSpan.FromSeconds(1));

                Assert.Equal(1, summary.FileCount);
                Assert.Equal(29, summary.TotalBytes);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
