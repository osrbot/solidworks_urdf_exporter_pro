using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    // The exporter never shares a Control or a SolidWorks object with this thread.
    internal sealed class ExportProgressSession : IDisposable
    {
        private const int LifecycleTimeoutMilliseconds = 5000;
        private readonly object gate = new object();
        private readonly IntPtr ownerHandle;
        private readonly Rectangle ownerBounds;
        private readonly Rectangle workingArea;
        private readonly CultureInfo culture;
        private readonly CultureInfo uiCulture;
        private readonly Func<ExportProgressForm> createForm;
        private readonly Thread thread;
        private ProgressUpdate latest;
        private Exception failure;
        private int started;
        private int startupFinished;
        private int stopRequested;

        internal ExportProgressSession(Form owner, Func<ExportProgressForm> createForm = null)
        {
            // Capture all owner information on its calling thread. In particular, do not
            // use Show(owner) or assign a native cross-thread owner: either can send messages
            // synchronously to the blocked exporter during activation or destruction.
            if (owner != null && owner.InvokeRequired)
            {
                throw new InvalidOperationException("Capture the progress owner on its UI thread.");
            }
            ownerHandle = owner == null ? IntPtr.Zero : owner.Handle;
            workingArea = owner == null ? Screen.PrimaryScreen.WorkingArea : Screen.FromControl(owner).WorkingArea;
            ownerBounds = owner == null ? workingArea : owner.Bounds;
            culture = CultureInfo.ReadOnly((CultureInfo)CultureInfo.CurrentCulture.Clone());
            uiCulture = CultureInfo.ReadOnly((CultureInfo)CultureInfo.CurrentUICulture.Clone());
            this.createForm = createForm ?? (() => new ExportProgressForm());
            thread = new Thread(RunWindow)
            {
                IsBackground = true,
                Name = "SW2URDF export progress"
            };
            thread.SetApartmentState(ApartmentState.STA);
        }

        internal bool IsRunning { get { return thread.IsAlive; } }
        internal Exception Failure { get { return Volatile.Read(ref failure); } }

        internal void Start(int timeoutMilliseconds = LifecycleTimeoutMilliseconds)
        {
            if (timeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            }
            lock (gate)
            {
                if (Volatile.Read(ref stopRequested) != 0)
                {
                    throw new ObjectDisposedException("ExportProgressSession");
                }
                if (started != 0)
                {
                    throw new InvalidOperationException("The progress session has already started.");
                }
                started = 1;
                long now = Stopwatch.GetTimestamp();
                latest = new ProgressUpdate(String.Empty, TimeSpan.Zero, now, now);
                thread.Start();
            }
            try
            {
                if (!WaitWithoutPumping(() => Volatile.Read(ref startupFinished) != 0, timeoutMilliseconds))
                {
                    throw new TimeoutException("The export progress window did not start in time.");
                }
                if (Failure != null || !IsRunning || Volatile.Read(ref stopRequested) != 0)
                {
                    throw new InvalidOperationException("The export progress window could not start.", Failure);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void UpdateProgress(ExportProgressEventArgs progress)
        {
            if (progress == null)
            {
                return;
            }
            lock (gate)
            {
                if (Volatile.Read(ref stopRequested) != 0)
                {
                    return;
                }
                long now = Stopwatch.GetTimestamp();
                long stageStarted = latest != null && String.Equals(latest.Stage, progress.Stage, StringComparison.Ordinal)
                    ? latest.StageStarted : now;
                TimeSpan elapsed = progress.Elapsed;
                TimeSpan currentElapsed = latest == null ? TimeSpan.Zero : latest.Elapsed + Since(latest.ReportedAt, now);
                if (elapsed < currentElapsed)
                {
                    elapsed = currentElapsed;
                }
                // A single immutable latest value coalesces bursts without flooding the UI queue.
                latest = new ProgressUpdate(progress.Stage, elapsed, now, stageStarted);
            }
        }

        private void RunWindow()
        {
            try
            {
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = uiCulture;
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, true);
                if (Volatile.Read(ref stopRequested) != 0)
                {
                    return;
                }
                using (ExportProgressForm form = createForm())
                using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 200 })
                {
                    form.Location = new Point(
                        Math.Max(workingArea.Left, Math.Min(ownerBounds.Left + (ownerBounds.Width - form.Width) / 2, workingArea.Right - form.Width)),
                        Math.Max(workingArea.Top, Math.Min(ownerBounds.Top + (ownerBounds.Height - form.Height) / 2, workingArea.Bottom - form.Height)));
                    timer.Tick += (sender, args) =>
                    {
                        if (Volatile.Read(ref stopRequested) != 0 ||
                            (ownerHandle != IntPtr.Zero && !IsWindow(ownerHandle)))
                        {
                            form.Finish();
                            return;
                        }
                        ProgressUpdate update;
                        lock (gate)
                        {
                            update = latest;
                        }
                        long now = Stopwatch.GetTimestamp();
                        form.UpdateProgress(String.IsNullOrEmpty(update.Stage)
                            ? ChineseUiText.Translate("Preparing export", "正在准备导出") : update.Stage,
                            update.Elapsed + Since(update.ReportedAt, now), Since(update.StageStarted, now));
                        // Ready means the independent message loop has actually dispatched a tick.
                        Volatile.Write(ref startupFinished, 1);
                    };
                    if (Volatile.Read(ref stopRequested) == 0)
                    {
                        timer.Start();
                        Application.Run(form);
                    }
                }
            }
            catch (Exception exception)
            {
                Volatile.Write(ref failure, exception);
                Trace.TraceError("Export progress window failed: {0}", exception);
            }
            finally
            {
                Interlocked.Exchange(ref stopRequested, 1);
                Volatile.Write(ref startupFinished, 1);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                Interlocked.Exchange(ref stopRequested, 1);
            }
            if (Thread.CurrentThread != thread &&
                !WaitWithoutPumping(() => !thread.IsAlive, LifecycleTimeoutMilliseconds))
            {
                throw new TimeoutException("The export progress thread did not exit in time.");
            }
        }

        private static bool WaitWithoutPumping(Func<bool> completed, int timeoutMilliseconds)
        {
            // Thread.Join and managed STA waits can pump COM/SendMessage. Sleep does not.
            Stopwatch timeout = Stopwatch.StartNew();
            while (!completed())
            {
                if (timeout.ElapsedMilliseconds >= timeoutMilliseconds)
                {
                    return false;
                }
                Thread.Sleep(10);
            }
            return true;
        }

        private static TimeSpan Since(long timestamp, long now)
        {
            return TimeSpan.FromSeconds((now - timestamp) / (double)Stopwatch.Frequency);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr handle);

        private sealed class ProgressUpdate
        {
            internal readonly string Stage;
            internal readonly TimeSpan Elapsed;
            internal readonly long ReportedAt;
            internal readonly long StageStarted;

            internal ProgressUpdate(string stage, TimeSpan elapsed, long reportedAt, long stageStarted)
            {
                Stage = stage;
                Elapsed = elapsed;
                ReportedAt = reportedAt;
                StageStarted = stageStarted;
            }
        }
    }

    internal sealed class ExportProgressForm : Form
    {
        private readonly Label labelStage;
        private readonly Label labelElapsed;
        private readonly Label labelStageElapsed;
        private bool finishing;

        public ExportProgressForm()
        {
            Text = ChineseUiText.Translate("Exporting URDF package", "正在导出 URDF 功能包");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.Manual;
            ControlBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(480, 156);

            labelStage = new Label
            {
                Name = "stage",
                AutoEllipsis = true,
                Location = new Point(16, 16),
                Size = new Size(448, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = ChineseUiText.Translate("Preparing export", "正在准备导出")
            };
            labelElapsed = new Label
            {
                Name = "elapsed",
                Location = new Point(16, 60),
                Size = new Size(448, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            labelStageElapsed = new Label
            {
                Name = "stageElapsed",
                Location = new Point(16, 84),
                Size = new Size(448, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Label labelWaiting = new Label
            {
                Name = "waiting",
                Location = new Point(16, 112),
                Size = new Size(448, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = ChineseUiText.Translate(
                    "Still waiting for the current step to finish.", "仍在等待当前步骤完成。")
            };

            Controls.Add(labelStage);
            Controls.Add(labelElapsed);
            Controls.Add(labelStageElapsed);
            Controls.Add(labelWaiting);
            ChineseUiText.Apply(this);
            UpdateProgress(labelStage.Text, TimeSpan.Zero, TimeSpan.Zero);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE, including mouse activation.
                return parameters;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!finishing && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
            }
        }

        internal void Finish()
        {
            finishing = true;
            Close();
        }

        internal void UpdateProgress(string stage, TimeSpan elapsed, TimeSpan stageElapsed)
        {
            labelStage.Text = stage;
            labelElapsed.Text = String.Format(
                ChineseUiText.Translate("Elapsed: {0}", "用时：{0}"),
                OperationHeartbeat.FormatElapsed(elapsed));
            labelStageElapsed.Text = String.Format(
                ChineseUiText.Translate("Current step: {0}", "当前步骤用时：{0}"),
                OperationHeartbeat.FormatElapsed(stageElapsed));
        }
    }
}
