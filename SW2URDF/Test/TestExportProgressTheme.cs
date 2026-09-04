using SW2URDF.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace SW2URDF.Test
{
    public class TestExportProgressTheme
    {
        [Theory]
        [InlineData("en-US")]
        [InlineData("zh-CN")]
        public void ProgressUsesSharedThemeAndOwnedFonts(string culture)
        {
            OnSta(culture, () =>
            {
                using (ExportProgressForm form = new ExportProgressForm())
                {
                    Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
                    Assert.Equal(ModernWinFormsTheme.Background, form.BackColor);
                    Assert.Equal(ModernWinFormsTheme.HostFont.FontFamily.Name, form.Font.FontFamily.Name);
                    Assert.True(UiFontResources.OwnsFont(form));
                    foreach (Label label in form.Controls.OfType<Label>())
                    {
                        Assert.Equal(ModernWinFormsTheme.HostFont.FontFamily.Name, label.Font.FontFamily.Name);
                        Assert.True(UiFontResources.OwnsFont(label));
                        Assert.Equal(label.Name == "stage" ? 10F : 9F, label.Font.SizeInPoints);
                        Assert.Equal(label.Name == "stage" ? FontStyle.Bold : FontStyle.Regular, label.Font.Style);
                        Assert.Equal(label.Name == "waiting" ? ModernWinFormsTheme.MutedText : ModernWinFormsTheme.Text,
                            label.ForeColor);
                    }
                    ProgressBar activity = Assert.IsType<ProgressBar>(form.Controls["activity"]);
                    Assert.Equal(ProgressBarStyle.Marquee, activity.Style);
                    Assert.Equal(24, activity.MarqueeAnimationSpeed);
                    Assert.Equal(20, activity.Height);
                    Assert.Equal(AccessibleRole.ProgressBar, activity.AccessibleRole);
                    Assert.Equal(form.Controls["waiting"].Text, activity.AccessibleDescription);
                    Assert.False(activity.TabStop);
                    Assert.True(form.TopMost);
                    Assert.False(form.ControlBox);
                    Assert.False(form.ShowInTaskbar);
                    Assert.Equal(ChineseUiText.Translate("Preparing export", "\u6b63\u5728\u51c6\u5907\u5bfc\u51fa"),
                        form.Controls["stage"].Text);
                }
            });
        }

        [Theory]
        [InlineData("en-US", 1F)]
        [InlineData("zh-CN", 1F)]
        [InlineData("en-US", 2F)]
        [InlineData("zh-CN", 2F)]
        public void ProgressLayoutKeepsReadoutsAndWaitingTextVisible(string culture, float scale)
        {
            OnSta(culture, () =>
            {
                List<Font> previewFonts = new List<Font>();
                try
                {
                    using (ExportProgressForm form = new ExportProgressForm())
                    {
                        if (scale != 1F)
                        {
                            Control[] controls = new[] { (Control)form }.Concat(form.Controls.Cast<Control>()).ToArray();
                            Font[] originalFonts = controls.Select(control => control.Font).ToArray();
                            form.SuspendLayout();
                            form.AutoScaleMode = AutoScaleMode.None;
                            form.Scale(new SizeF(scale, scale));
                            for (int index = 0; index < controls.Length; index++)
                            {
                                Font font = new Font(originalFonts[index].FontFamily,
                                    originalFonts[index].Size * scale, originalFonts[index].Style);
                                previewFonts.Add(font);
                                controls[index].Font = font;
                            }
                            form.ResumeLayout(true);
                        }
                        form.UpdateProgress(ChineseUiText.Translate("Saving mesh: robot_description/base_link",
                            "\u6b63\u5728\u4fdd\u5b58\u7f51\u683c: robot_description/base_link"),
                            TimeSpan.FromMinutes(16), TimeSpan.FromSeconds(95));
                        form.PerformLayout();
                        foreach (Control control in form.Controls)
                        {
                            Assert.True(form.ClientRectangle.Contains(control.Bounds), control.Name);
                        }
                        Control stage = form.Controls["stage"];
                        Control activity = form.Controls["activity"];
                        Control elapsed = form.Controls["elapsed"];
                        Control step = form.Controls["stageElapsed"];
                        Control waiting = form.Controls["waiting"];
                        Assert.True(stage.Bottom < activity.Top);
                        Assert.True(activity.Bottom < elapsed.Top);
                        Assert.False(elapsed.Bounds.IntersectsWith(step.Bounds));
                        Assert.True(elapsed.Bottom < waiting.Top);
                        Assert.True(step.Bottom < waiting.Top);
                        foreach (Control readout in new[] { elapsed, step, waiting })
                        {
                            Size text = TextRenderer.MeasureText(readout.Text, readout.Font, Size.Empty,
                                TextFormatFlags.SingleLine);
                            Assert.True(text.Width <= readout.ClientSize.Width - readout.Padding.Horizontal, readout.Name);
                            Assert.True(text.Height <= readout.ClientSize.Height - readout.Padding.Vertical, readout.Name);
                        }
                        Assert.Contains("16:00", elapsed.Text);
                        Assert.Contains("01:35", step.Text);
                    }
                }
                finally
                {
                    foreach (Font font in previewFonts) font.Dispose();
                }
            });
        }

        [Fact]
        public void HeartbeatDoesNotRestyleRelayoutOrRedrawTheForm()
        {
            OnSta("en-US", () =>
            {
                using (ExportProgressForm form = new ExportProgressForm())
                {
                    ProgressBar activity = (ProgressBar)form.Controls["activity"];
                    Control waiting = form.Controls["waiting"];
                    form.CreateControl();
                    using (Bitmap border = new Bitmap(waiting.Width, waiting.Height))
                    {
                        waiting.DrawToBitmap(border, waiting.ClientRectangle);
                        Label[] labels = form.Controls.OfType<Label>().ToArray();
                        Font[] fonts = labels.Select(label => label.Font).ToArray();
                        int layouts = 0;
                        int invalidations = 0;
                        int activityInvalidations = 0;
                        form.Layout += (sender, args) => layouts++;
                        form.Invalidated += (sender, args) => invalidations++;
                        activity.Invalidated += (sender, args) => activityInvalidations++;
                        form.UpdateProgress(form.Controls["stage"].Text, TimeSpan.Zero, TimeSpan.Zero);
                        Assert.Equal(0, layouts);
                        Assert.Equal(0, invalidations);
                        Assert.Equal(0, activityInvalidations);
                        Assert.Equal(ModernWinFormsTheme.Border.ToArgb(), border.GetPixel(0, 0).ToArgb());
                        Assert.Equal(ProgressBarStyle.Marquee, activity.Style);
                        Assert.Equal(24, activity.MarqueeAnimationSpeed);
                        Assert.Equal(0, activity.Value);
                        for (int index = 0; index < labels.Length; index++)
                            Assert.Same(fonts[index], labels[index].Font);
                    }
                }
            });
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("zh-CN")]
        public void ThemedProgressKeepsTickingWhileCallerSleepsAndDisposesOnUiThread(string culture)
        {
            OnSta(culture, () =>
            {
                int ticks = 0;
                int uiThread = 0;
                int disposedThread = 0;
                using (ExportProgressSession session = new ExportProgressSession(null, () =>
                {
                    ExportProgressForm form = new ExportProgressForm();
                    form.Controls["elapsed"].TextChanged += (sender, args) =>
                    {
                        Volatile.Write(ref uiThread, Thread.CurrentThread.ManagedThreadId);
                        Interlocked.Increment(ref ticks);
                    };
                    form.Disposed += (sender, args) =>
                        Volatile.Write(ref disposedThread, Thread.CurrentThread.ManagedThreadId);
                    return form;
                }))
                {
                    session.Start();
                    int before = Volatile.Read(ref ticks);
                    Thread.Sleep(1500);
                    Assert.True(Volatile.Read(ref ticks) > before);
                    Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, Volatile.Read(ref uiThread));
                    session.Dispose();
                    Assert.False(session.IsRunning);
                    Assert.Null(session.Failure);
                    Assert.Equal(Volatile.Read(ref uiThread), Volatile.Read(ref disposedThread));
                }
            });
        }

        private static void OnSta(string culture, Action test)
        {
            Exception failure = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                    Assert.False(Application.MessageLoop);
                    test();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Stopwatch timeout = Stopwatch.StartNew();
            while (thread.IsAlive && timeout.Elapsed < TimeSpan.FromSeconds(30)) Thread.Sleep(10);
            Assert.False(thread.IsAlive);
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
