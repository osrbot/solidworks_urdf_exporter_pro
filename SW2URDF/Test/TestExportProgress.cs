using SW2URDF.URDFExport;
using SW2URDF.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        [InlineData(true, false, 1, 1, true, MessageBoxIcon.Warning)]
        [InlineData(false, false, 0, 2, false, MessageBoxIcon.Error)]
        [InlineData(true, true, 2, 0, false, MessageBoxIcon.Information)]
        public void TestExportTargetSummaryOutcomes(bool firstSucceeded, bool secondSucceeded,
            int successes, int failures, bool partial, MessageBoxIcon icon)
        {
            RunOnStaWithoutMessagePump(() =>
            {
                ExportResultSummary summary = new ExportResultSummary("output", 0, 0, TimeSpan.Zero,
                    new[]
                    {
                        new ExportTargetResult("ROS 1", "ros1", firstSucceeded, "", false),
                        new ExportTargetResult("USD", "usd", secondSucceeded, "", !secondSucceeded)
                    });
                Assert.Equal(successes, summary.SucceededCount);
                Assert.Equal(failures, summary.FailedCount);
                Assert.Equal(failures > 0, summary.HasFailures);
                Assert.Equal(partial, summary.HasPartialSuccess);
                using (ExportResultsDialog dialog = new ExportResultsDialog(summary, null, path =>
                {
                    throw new InvalidOperationException("Construction must not open a path.");
                }))
                {
                    Assert.Equal(icon, dialog.ResultIcon);
                    Assert.Equal(FormBorderStyle.Sizable, dialog.FormBorderStyle);
                    TextBox details = (TextBox)dialog.Controls.Find("exportResultsDetails", true).Single();
                    Assert.True(details.ReadOnly);
                    Assert.Equal(ScrollBars.Both, details.ScrollBars);
                    Assert.False(details.WordWrap);
                    Assert.Contains("ROS 1", details.Text);
                    Assert.Contains("USD", details.Text);
                }
            });
        }

        [Fact]
        public void TestExportSummarySnapshotsTargetsAndWarnings()
        {
            ExportTargetResult target = new ExportTargetResult("USD", "output", false, "RAW_ERROR", true, "publish");
            List<ExportTargetResult> targets = new List<ExportTargetResult> { target };
            List<string> warnings = new List<string> { "RAW_WARNING" };
            ExportResultSummary summary = new ExportResultSummary("root", 0, 0, TimeSpan.Zero, targets, warnings);
            targets.Clear();
            warnings[0] = "replaced";
            Assert.Same(target, Assert.Single(summary.Targets));
            Assert.Equal("publish", summary.Targets[0].Phase);
            Assert.Equal("RAW_WARNING", Assert.Single(summary.Warnings));
            Assert.True(summary.Targets.IsReadOnly);
            Assert.True(summary.Warnings.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => summary.Targets.Add(target));
            Assert.Throws<NotSupportedException>(() => summary.Warnings[0] = "changed");

            ExportResultSummary legacy = new ExportResultSummary("root", 0, 0, TimeSpan.Zero);
            Assert.Empty(legacy.Targets);
            Assert.Empty(legacy.Warnings);
            Assert.False(legacy.HasFailures);
            Assert.False(legacy.HasPartialSuccess);
        }

        [Fact]
        public void TestExportSummaryCountsOnlyPublishedTargetsNotRetainedOrUnselectedOutputs()
        {
            WithExportRoot((root, package) =>
            {
                Directory.CreateDirectory(package.WindowsMeshesDirectory);
                Directory.CreateDirectory(package.WindowsRos2PackageDirectory);
                Directory.CreateDirectory(package.WindowsUsdAssetDirectory);
                File.WriteAllBytes(Path.Combine(package.WindowsMeshesDirectory, "published.stl"), new byte[13]);
                string failedOutput = Path.Combine(package.WindowsRos2PackageDirectory, "retained.urdf");
                File.WriteAllBytes(failedOutput, new byte[17]);
                File.WriteAllBytes(Path.Combine(package.WindowsUsdAssetDirectory, "unselected.usda"), new byte[19]);
                ExportOutputSnapshot before = ExportOutputSnapshot.Capture(package);
                File.WriteAllBytes(failedOutput, new byte[23]);
                File.WriteAllBytes(package.WindowsExportReportFile, new byte[29]);
                File.WriteAllBytes(package.WindowsExportLogFile, new byte[31]);

                ExportResultSummary summary = ExportResultSummary.Create(package, before, TimeSpan.Zero,
                    new[]
                    {
                        new ExportTargetResult("ROS 1", package.WindowsPackageDirectory, true, "", false),
                        new ExportTargetResult("ROS 2", package.WindowsRos2PackageDirectory, false, "failed", true)
                    }, new[] { "RAW_WARNING" });

                // The published file still counts when its size and timestamp match the old snapshot.
                Assert.Equal(1, summary.FileCount);
                Assert.Equal(13, summary.TotalBytes);
                Assert.Equal("RAW_WARNING", Assert.Single(summary.Warnings));
            });
        }

        [Fact]
        public void TestExportSummaryAllFailedAndExplicitEmptyTargetsCountNoOldFiles()
        {
            WithExportRoot((root, package) =>
            {
                Directory.CreateDirectory(package.WindowsMeshesDirectory);
                File.WriteAllBytes(Path.Combine(package.WindowsMeshesDirectory, "old.stl"), new byte[13]);
                ExportResultSummary failed = ExportResultSummary.Create(package, null, TimeSpan.Zero,
                    new[] { new ExportTargetResult("ROS 1", package.WindowsPackageDirectory, false, "failed", true) });
                Assert.Equal(0, failed.FileCount);
                Assert.Equal(0, failed.TotalBytes);
                ExportResultSummary empty = ExportResultSummary.Create(package, null, TimeSpan.Zero,
                    Enumerable.Empty<ExportTargetResult>());
                Assert.Equal(0, empty.FileCount);
                Assert.Equal(0, empty.TotalBytes);
            });
        }

        [Fact]
        public void TestExportSummaryDeduplicatesOverlappingSuccessfulDirectories()
        {
            WithExportRoot((root, package) =>
            {
                Directory.CreateDirectory(package.WindowsMeshesDirectory);
                File.WriteAllBytes(Path.Combine(package.WindowsMeshesDirectory, "mesh.stl"), new byte[13]);
                File.WriteAllBytes(Path.Combine(package.WindowsPackageDirectory, "package.xml"), new byte[17]);
                ExportResultSummary summary = ExportResultSummary.Create(package, null, TimeSpan.Zero,
                    new[]
                    {
                        new ExportTargetResult("ROS 1", package.WindowsPackageDirectory, true, "", false),
                        new ExportTargetResult("Alias", Path.Combine(package.WindowsPackageDirectory, "."), true, "", false),
                        new ExportTargetResult("Nested", package.WindowsMeshesDirectory, true, "", false)
                    });
                Assert.Equal(2, summary.FileCount);
                Assert.Equal(30, summary.TotalBytes);
            });
        }

        [Theory]
        [InlineData("en-US", "Succeeded", "Failed", "Warnings:")]
        [InlineData("zh-CN", "\u6210\u529f", "\u5931\u8d25", "\u8b66\u544a:")]
        public void TestExportSummaryFormatsLocalizedTargetsWithoutLosingRawErrors(
            string culture, string success, string failure, string warningHeading)
        {
            RunOnStaWithoutMessagePump(() =>
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                const string rawError = "ERROR USD_WRITE $.links[0]: disk full\r\noriginal detail";
                ExportResultSummary summary = new ExportResultSummary("root", 1, 13, TimeSpan.FromMinutes(16),
                    new[]
                    {
                        new ExportTargetResult("ROS 1", "root/ros1", true, "", false),
                        new ExportTargetResult("USD", "root/usd", false, rawError, true, "publish")
                    }, new[] { "WARNING TEXTURE_MISSING original text" });
                string details = summary.FormatDetails();
                Assert.Contains(success, details);
                Assert.Contains(failure, details);
                Assert.Contains(warningHeading, details);
                Assert.Contains("[SUCCEEDED] ROS 1", details);
                Assert.Contains("[FAILED] USD", details);
                Assert.Contains("root/ros1", details);
                Assert.Contains("root/usd", details);
                Assert.Contains("not updated this run", details);
                Assert.Contains(rawError, details);
                Assert.Contains("publish", details);
                Assert.Contains("WARNING TEXTURE_MISSING original text", details);
                Assert.Contains("16:00", details);
                ExportResultSummary noPrevious = new ExportResultSummary("root", 0, 0, TimeSpan.Zero,
                    new[] { new ExportTargetResult("USD", "root/usd", false, rawError, false) });
                Assert.DoesNotContain("not updated this run", noPrevious.FormatDetails());
            });
        }

        [Fact]
        public void TestExportResultDialogGuardsDirectoryAndLogActions()
        {
            RunOnStaWithoutMessagePump(() => WithExportRoot((root, package) =>
            {
                string successPath = Directory.CreateDirectory(Path.Combine(root, "success")).FullName;
                string oldPath = Directory.CreateDirectory(Path.Combine(root, "retained")).FullName;
                string logPath = Path.Combine(root, "export.log");
                File.WriteAllText(logPath, "log");
                ExportResultSummary summary = new ExportResultSummary(root, 0, 0, TimeSpan.Zero,
                    new[]
                    {
                        new ExportTargetResult("ROS 1", successPath, true, "", false),
                        new ExportTargetResult("USD", oldPath, false, "USD_ERROR", true),
                        new ExportTargetResult("ROS 2", Path.Combine(root, "missing"), true, "", false)
                    });
                List<string> opened = new List<string>();
                using (ExportResultsDialog dialog = new ExportResultsDialog(summary, logPath, opened.Add))
                {
                    DataGridView grid = (DataGridView)dialog.Controls.Find("exportResultsGrid", true).Single();
                    Button openDirectory = (Button)dialog.Controls.Find("exportResultsOpenDirectory", true).Single();
                    Button openLog = (Button)dialog.Controls.Find("exportResultsOpenLog", true).Single();
                    TextBox details = (TextBox)dialog.Controls.Find("exportResultsDetails", true).Single();
                    Assert.Empty(opened);
                    Assert.False(openDirectory.Enabled);
                    Assert.False(dialog.TryOpenSelectedDirectory());
                    Assert.True(openLog.Enabled);
                    Assert.Contains(logPath, details.Text);
                    Assert.Contains("USD_ERROR", details.Text);

                    SelectResult(grid, 1);
                    Assert.False(openDirectory.Enabled);
                    Assert.False(dialog.TryOpenSelectedDirectory());
                    SelectResult(grid, 2);
                    Assert.False(openDirectory.Enabled);
                    Assert.False(dialog.TryOpenSelectedDirectory());
                    SelectResult(grid, 0);
                    Assert.True(openDirectory.Enabled);
                    Assert.Empty(opened);
                    Assert.True(dialog.TryOpenSelectedDirectory());
                    Assert.Equal(successPath, Assert.Single(opened));
                    Directory.Delete(successPath);
                    Assert.False(dialog.TryOpenSelectedDirectory());
                    Assert.False(openDirectory.Enabled);

                    Assert.True(dialog.TryOpenLog());
                    Assert.Equal(new[] { successPath, logPath }, opened);
                    File.Delete(logPath);
                    Assert.False(dialog.TryOpenLog());
                    Assert.False(openLog.Enabled);
                    Assert.Equal(2, opened.Count);
                }
            }));
        }

        [Fact]
        public void TestExportResultDialogKeepsShellErrorsInSearchableDetails()
        {
            RunOnStaWithoutMessagePump(() => WithExportRoot((root, package) =>
            {
                ExportResultSummary summary = new ExportResultSummary(root, 0, 0, TimeSpan.Zero,
                    new[] { new ExportTargetResult("ROS 1", root, true, "", false) }, new[] { "RAW_WARNING" });
                using (ExportResultsDialog dialog = new ExportResultsDialog(summary, Path.Combine(root, "missing.log"), path =>
                {
                    throw new System.ComponentModel.Win32Exception("SHELL_OPEN_ERROR");
                }))
                {
                    Assert.Equal(MessageBoxIcon.Warning, dialog.ResultIcon);
                    Assert.False(dialog.TryOpenLog());
                    SelectResult((DataGridView)dialog.Controls.Find("exportResultsGrid", true).Single(), 0);
                    Assert.False(dialog.TryOpenSelectedDirectory());
                    TextBox details = (TextBox)dialog.Controls.Find("exportResultsDetails", true).Single();
                    Assert.Contains("SHELL_OPEN_ERROR", details.Text);
                    Assert.Contains("RAW_WARNING", details.Text);
                }
            }));
        }

        [Theory]
        [InlineData("en-US", 0, 820, 560)]
        [InlineData("en-US", 2, 820, 560)]
        [InlineData("en-US", 4, 820, 560)]
        [InlineData("zh-CN", 0, 820, 560)]
        [InlineData("zh-CN", 2, 820, 560)]
        [InlineData("zh-CN", 4, 820, 560)]
        [InlineData("en-US", 0, 680, 460)]
        [InlineData("en-US", 2, 680, 460)]
        [InlineData("en-US", 4, 680, 460)]
        [InlineData("zh-CN", 0, 680, 460)]
        [InlineData("zh-CN", 2, 680, 460)]
        [InlineData("zh-CN", 4, 680, 460)]
        public void TestExportResultDialogShowsAllFourTargetsWithoutClipping(
            string culture, int succeededCount, int width, int height)
        {
            RunOnStaWithoutMessagePump(() =>
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                string longPath = @"C:\export\" + new string('x', 180) + @"\robot_description";
                string[] names = { "ROS 1", "ROS 2", "OpenUSD", "MuJoCo MJCF" };
                ExportTargetResult[] targets = names.Select((name, index) => new ExportTargetResult(
                    name, longPath + index, index < succeededCount, "RAW_PUBLISH_ERROR", index >= succeededCount)).ToArray();
                ExportResultSummary summary = new ExportResultSummary("output", 0, 0, TimeSpan.Zero, targets);
                using (ExportResultsDialog dialog = new ExportResultsDialog(summary, null, path =>
                {
                    throw new InvalidOperationException("Layout test must not open a path.");
                }))
                {
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new Point(-24000, -24000);
                    SetWindowLong(dialog.Handle, -20, GetWindowLong(dialog.Handle, -20) | 0x08000000);
                    dialog.Show();
                    dialog.Size = new Size(width, height);
                    dialog.PerformLayout();
                    DataGridView grid = (DataGridView)dialog.Controls.Find("exportResultsGrid", true).Single();
                    grid.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells);
                    Assert.Equal(4, grid.DisplayedRowCount(false));
                    Assert.Equal(0, grid.FirstDisplayedScrollingRowIndex);
                    Assert.False(grid.Controls.OfType<VScrollBar>().Any(scroll => scroll.Visible));
                    Assert.Equal(DataGridViewTriState.False, grid.Columns["output"].DefaultCellStyle.WrapMode);
                    for (int index = 0; index < 4; index++)
                    {
                        Rectangle visible = grid.GetRowDisplayRectangle(index, true);
                        Assert.Equal(grid.Rows[index].Height, visible.Height);
                        Assert.True(visible.Top >= grid.ColumnHeadersHeight);
                        Assert.True(visible.Bottom <= grid.ClientSize.Height);
                        Assert.Equal(targets[index].OutputDirectory, grid.Rows[index].Cells["output"].ToolTipText);
                    }
                    TextBox details = (TextBox)dialog.Controls.Find("exportResultsDetails", true).Single();
                    Assert.True(details.Height >= details.Font.Height * 3);
                    Assert.Contains(longPath, details.Text);
                    foreach (string name in new[] { "exportResultsOpenDirectory", "exportResultsOpenLog", "exportResultsCopy", "exportResultsClose" })
                    {
                        Button button = (Button)dialog.Controls.Find(name, true).Single();
                        Assert.True(button.Parent.ClientRectangle.Contains(button.Bounds),
                            name + ": bounds=" + button.Bounds + "; parent=" + button.Parent.ClientRectangle +
                            "; form=" + dialog.ClientSize + "; font=" + button.Font);
                    }
                }
            });
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr handle, int index, int value);

        private static void SelectResult(DataGridView grid, int index)
        {
            grid.ClearSelection();
            grid.CurrentCell = grid.Rows[index].Cells[0];
            grid.Rows[index].Selected = true;
        }

        private static void WithExportRoot(Action<string, URDFPackage> test)
        {
            string root = Path.Combine(Path.GetTempPath(), "sw2urdf-target-results-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            try
            {
                test(root, new URDFPackage("robot", "robot_description", root));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

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
