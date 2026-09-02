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
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    internal sealed class ModernCardPanel : TableLayoutPanel
    {
        internal const int CornerRadius = 6;
        private const int PaintedBorderClearance = 2;

        public ModernCardPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Region previous = Region;
            if (Width >= CornerRadius * 2 && Height >= CornerRadius * 2)
            {
                using (GraphicsPath path = ModernWinFormsTheme.CreateRoundedRectangle(
                    new Rectangle(0, 0, Width, Height),
                    CornerRadius))
                {
                    Region = new Region(path);
                }
            }
            else
            {
                Region = null;
            }
            if (previous != null)
            {
                previous.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ModernWinFormsTheme.DrawRoundedBorder(this, e.Graphics, CornerRadius);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size preferred = base.GetPreferredSize(proposedSize);
            return new Size(preferred.Width, preferred.Height + PaintedBorderClearance);
        }
    }

    internal sealed class ModernTabControl : TabControl
    {
        private readonly Dictionary<TabPage, List<TableLayoutPanel>> cachedLayouts =
            new Dictionary<TabPage, List<TableLayoutPanel>>();
        private bool restoringCachedLayouts;

        public ModernTabControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
        }

        protected override void OnSelecting(TabControlCancelEventArgs e)
        {
            base.OnSelecting(e);
            if (!e.Cancel)
            {
                CachePageLayout(SelectedTab);
            }
        }

        protected override void OnSelected(TabControlEventArgs e)
        {
            base.OnSelected(e);
            CachePageLayout(e.TabPage);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            RestoreCachedLayouts();
            base.OnSizeChanged(e);
        }

        internal void CacheSelectedPageLayout()
        {
            CachePageLayout(SelectedTab);
        }

        internal void CacheAllPageLayouts()
        {
            Rectangle pageBounds = DisplayRectangle;
            if (pageBounds.Width <= 0 || pageBounds.Height <= 0)
            {
                return;
            }
            foreach (TabPage page in TabPages)
            {
                ModernTabPage modernPage = page as ModernTabPage;
                if (modernPage != null && !modernPage.CacheAutoSizeLayout)
                {
                    continue;
                }
                if (page.Bounds != pageBounds)
                {
                    page.Bounds = pageBounds;
                }
                CachePageLayout(page);
            }
        }

        internal void InvalidatePageLayout(Control descendant)
        {
            TabPage page = FindOwningPage(descendant);
            if (page == null)
            {
                return;
            }

            RestoreCachedPageLayout(page);
        }

        internal void RebuildPageLayout(Control descendant)
        {
            TabPage page = FindOwningPage(descendant);
            if (page == null)
            {
                return;
            }

            RestoreCachedPageLayout(page);
            Rectangle pageBounds = DisplayRectangle;
            if (pageBounds.Width <= 0 || pageBounds.Height <= 0)
            {
                return;
            }
            if (page.Bounds != pageBounds)
            {
                page.Bounds = pageBounds;
            }
            page.PerformLayout();
            CachePageLayout(page);
        }

        private TabPage FindOwningPage(Control descendant)
        {
            for (Control current = descendant;
                current != null;
                current = current.Parent)
            {
                TabPage page = current as TabPage;
                if (page != null && TabPages.Contains(page))
                {
                    return page;
                }
            }
            return null;
        }

        private void CachePageLayout(TabPage page)
        {
            ModernTabPage modernPage = page as ModernTabPage;
            if (page == null || cachedLayouts.ContainsKey(page) ||
                (modernPage != null && !modernPage.CacheAutoSizeLayout))
            {
                return;
            }

            page.PerformLayout();
            List<TableLayoutPanel> layouts = new List<TableLayoutPanel>();
            FreezeAutoSizeLayouts(page, layouts);
            cachedLayouts.Add(page, layouts);
        }

        private static void FreezeAutoSizeLayouts(
            Control root,
            IList<TableLayoutPanel> layouts)
        {
            foreach (Control child in root.Controls)
            {
                FreezeAutoSizeLayouts(child, layouts);
            }

            TableLayoutPanel layout = root as TableLayoutPanel;
            if (layout == null || !layout.AutoSize)
            {
                return;
            }
            Size size = layout.Size;
            layout.AutoSize = false;
            layout.Size = size;
            layouts.Add(layout);
        }

        private void RestoreCachedLayouts()
        {
            if (restoringCachedLayouts || cachedLayouts.Count == 0)
            {
                return;
            }

            restoringCachedLayouts = true;
            try
            {
                foreach (List<TableLayoutPanel> layouts in cachedLayouts.Values)
                {
                    RestoreAutoSizeLayouts(layouts);
                }
                cachedLayouts.Clear();
            }
            finally
            {
                restoringCachedLayouts = false;
            }
        }

        private void RestoreCachedPageLayout(TabPage page)
        {
            if (page == null || restoringCachedLayouts)
            {
                return;
            }

            List<TableLayoutPanel> layouts;
            if (!cachedLayouts.TryGetValue(page, out layouts))
            {
                return;
            }

            restoringCachedLayouts = true;
            try
            {
                RestoreAutoSizeLayouts(layouts);
                cachedLayouts.Remove(page);
            }
            finally
            {
                restoringCachedLayouts = false;
            }
        }

        private static void RestoreAutoSizeLayouts(
            IList<TableLayoutPanel> layouts)
        {
            for (int index = layouts.Count - 1; index >= 0; index--)
            {
                TableLayoutPanel layout = layouts[index];
                if (!layout.IsDisposed)
                {
                    layout.AutoSize = true;
                }
            }
        }

    }

    internal sealed class ModernTabPage : TabPage
    {
        internal ModernTabPage()
        {
            CacheAutoSizeLayout = true;
        }

        internal bool CacheAutoSizeLayout { get; set; }
    }

    internal static class UiFontResources
    {
        private sealed class OwnedFont
        {
            internal OwnedFont(Font font)
            {
                Font = font;
            }

            internal Font Font { get; private set; }
        }

        private static readonly ConditionalWeakTable<Control, OwnedFont> Fonts =
            new ConditionalWeakTable<Control, OwnedFont>();

        internal static void SetFont(
            Control control,
            string family,
            float size,
            FontStyle style)
        {
            if (control == null)
            {
                throw new ArgumentNullException("control");
            }

            Font replacement = new Font(family, size, style, GraphicsUnit.Point);
            ReplaceOwnedFont(control, replacement);
        }

        internal static void SetFont(
            Control control,
            Font prototype,
            float size,
            FontStyle style)
        {
            if (prototype == null)
            {
                throw new ArgumentNullException("prototype");
            }

            if (control == null)
            {
                throw new ArgumentNullException("control");
            }

            Font replacement = new Font(
                prototype.FontFamily,
                size,
                style,
                GraphicsUnit.Point,
                prototype.GdiCharSet,
                prototype.GdiVerticalFont);
            ReplaceOwnedFont(control, replacement);
        }

        private static void ReplaceOwnedFont(Control control, Font replacement)
        {
            OwnedFont previous;
            bool hadPrevious = Fonts.TryGetValue(control, out previous);
            if (hadPrevious &&
                Object.ReferenceEquals(control.Font, previous.Font) &&
                previous.Font.Equals(replacement))
            {
                replacement.Dispose();
                return;
            }

            try
            {
                control.Font = replacement;
            }
            catch
            {
                replacement.Dispose();
                throw;
            }

            if (hadPrevious)
            {
                Fonts.Remove(control);
                control.Disposed -= ControlDisposed;
            }

            Fonts.Add(control, new OwnedFont(replacement));
            control.Disposed += ControlDisposed;
            if (hadPrevious)
            {
                previous.Font.Dispose();
            }
        }

        internal static bool OwnsFont(Control control)
        {
            OwnedFont ownedFont;
            return control != null && Fonts.TryGetValue(control, out ownedFont);
        }

        private static void ControlDisposed(object sender, EventArgs e)
        {
            Control control = sender as Control;
            if (control == null)
            {
                return;
            }

            OwnedFont ownedFont;
            if (Fonts.TryGetValue(control, out ownedFont))
            {
                Fonts.Remove(control);
                ownedFont.Font.Dispose();
            }
            control.Disposed -= ControlDisposed;
        }
    }

    internal static class ModernWinFormsTheme
    {
        internal static readonly Color Background = Color.FromArgb(242, 248, 251);
        internal static readonly Color Surface = Color.White;
        internal static readonly Color SurfaceAlt = Color.FromArgb(237, 247, 251);
        internal static readonly Color Border = Color.FromArgb(196, 216, 226);
        internal static readonly Color Text = Color.FromArgb(24, 43, 56);
        internal static readonly Color MutedText = Color.FromArgb(77, 101, 115);
        internal static readonly Color Accent = Color.FromArgb(12, 116, 190);
        internal static readonly Color AccentHover = Color.FromArgb(0, 124, 146);
        internal static readonly Color AccentTint = Color.FromArgb(226, 247, 251);

        internal static Font HostFont
        {
            // A SolidWorks-hosted WinForms dialog should follow the current
            // Windows dialog font so locale and fallback glyphs stay aligned.
            get { return SystemFonts.MessageBoxFont; }
        }

        internal static void SetFont(Control control, float size, FontStyle style)
        {
            UiFontResources.SetFont(control, HostFont, size, style);
        }

        internal static Label CreateTextLabel(string text, float size, FontStyle style)
        {
            Label label = new Label
            {
                AutoSize = true,
                Text = text,
                ForeColor = Text,
                Margin = new Padding(0)
            };
            SetFont(label, size, style);
            return label;
        }

        internal static TableLayoutPanel CreateCard(string name)
        {
            TableLayoutPanel card = new ModernCardPanel
            {
                Name = name,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Surface,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(16, 14, 16, 16),
                RowCount = 0
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return card;
        }

        internal static IDisposable SuspendRedraw(Control control)
        {
            return new RedrawScope(control);
        }

        internal static void Apply(Form form)
        {
            form.SuspendLayout();
            try
            {
                form.BackColor = Background;
                SetFont(form, HostFont.SizeInPoints, FontStyle.Regular);
                ApplyControlTree(form);
            }
            finally
            {
                form.ResumeLayout(true);
            }
        }

        internal static void ApplyControlTree(Control root)
        {
            StyleControl(root);
            foreach (Control child in root.Controls)
            {
                ApplyControlTree(child);
            }
        }

        internal static void StylePrimaryButton(Button button)
        {
            StyleButton(button);
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = Accent;
            button.FlatAppearance.MouseOverBackColor = AccentHover;
            button.FlatAppearance.MouseDownBackColor = AccentHover;
        }

        internal static void StyleSecondaryButton(Button button)
        {
            StyleButton(button);
            button.BackColor = Surface;
            button.ForeColor = Text;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = SurfaceAlt;
            button.FlatAppearance.MouseDownBackColor = SurfaceAlt;
        }

        internal static void StyleReadoutLabel(Label label)
        {
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.BackColor = SurfaceAlt;
            label.BorderStyle = BorderStyle.FixedSingle;
            label.Dock = DockStyle.Fill;
            SetFont(label, 9F, FontStyle.Bold);
            label.ForeColor = Text;
            label.MinimumSize = new Size(0, 30);
            label.Padding = new Padding(8, 5, 8, 5);
            label.TextAlign = ContentAlignment.MiddleLeft;
        }

        internal static void StyleFieldLabel(Label label)
        {
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            SetFont(
                label,
                9F,
                (label.Font.Style & FontStyle.Bold) == FontStyle.Bold
                    ? FontStyle.Bold
                    : FontStyle.Regular);
            label.ForeColor = MutedText;
            label.Margin = new Padding(0, 4, 8, 4);
            label.TextAlign = ContentAlignment.MiddleLeft;
        }

        internal static void StyleInput(Control control)
        {
            control.BackColor = Surface;
            SetFont(control, 9F, FontStyle.Regular);
            control.ForeColor = Text;
            control.Margin = new Padding(0, 4, 0, 4);

            TextBox textBox = control as TextBox;
            if (textBox != null)
            {
                textBox.BorderStyle = BorderStyle.FixedSingle;
                if (textBox.ReadOnly)
                {
                    textBox.BackColor = SurfaceAlt;
                }
            }

            UpDownBase upDown = control as UpDownBase;
            if (upDown != null)
            {
                upDown.BorderStyle = BorderStyle.FixedSingle;
            }

            ComboBox comboBox = control as ComboBox;
            if (comboBox != null)
            {
                comboBox.FlatStyle = FlatStyle.Flat;
            }
        }

        internal static void DrawCardBorder(object sender, PaintEventArgs e)
        {
            Control control = sender as Control;
            if (control == null || control.Width < 2 || control.Height < 2)
            {
                return;
            }

            DrawRoundedBorder(control, e.Graphics, ModernCardPanel.CornerRadius);
        }

        internal static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(1, radius * 2);
            int right = bounds.Right - diameter;
            int bottom = bounds.Bottom - diameter;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(right, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(right, bottom, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bottom, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void DrawRoundedBorder(Control control, Graphics graphics, int radius)
        {
            if (control == null || graphics == null ||
                control.Width < radius * 2 || control.Height < radius * 2)
            {
                return;
            }

            SmoothingMode previousMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(Border))
            using (GraphicsPath path = CreateRoundedRectangle(
                new Rectangle(0, 0, control.Width - 1, control.Height - 1),
                radius))
            {
                graphics.DrawPath(pen, path);
            }
            graphics.SmoothingMode = previousMode;
        }

        internal static void DrawBottomBorder(object sender, PaintEventArgs e)
        {
            Control control = sender as Control;
            if (control == null)
            {
                return;
            }

            using (Pen pen = new Pen(Border))
            {
                e.Graphics.DrawLine(pen, 0, control.Height - 1, control.Width, control.Height - 1);
            }
        }

        internal static void DrawTopBorder(object sender, PaintEventArgs e)
        {
            Control control = sender as Control;
            if (control == null)
            {
                return;
            }

            using (Pen pen = new Pen(Border))
            {
                e.Graphics.DrawLine(pen, 0, 0, control.Width, 0);
            }
        }

        private static void StyleControl(Control control)
        {
            Button button = control as Button;
            if (button != null)
            {
                StyleSecondaryButton(button);
                if (button.Name == "button_finish")
                {
                    StylePrimaryButton(button);
                }
                return;
            }

            TreeView treeView = control as TreeView;
            if (treeView != null)
            {
                treeView.BackColor = Surface;
                treeView.BorderStyle = BorderStyle.None;
                SetFont(treeView, 9F, FontStyle.Regular);
                treeView.ForeColor = Text;
                treeView.FullRowSelect = true;
                treeView.HideSelection = false;
                treeView.ItemHeight = Math.Max(24, treeView.Font.Height + 9);
                treeView.ShowLines = false;
                treeView.ShowRootLines = false;
                return;
            }

            TextBox textBox = control as TextBox;
            if (textBox != null)
            {
                StyleInput(textBox);
                return;
            }

            ComboBox comboBox = control as ComboBox;
            if (comboBox != null)
            {
                StyleInput(comboBox);
                return;
            }

            UpDownBase upDown = control as UpDownBase;
            if (upDown != null)
            {
                StyleInput(upDown);
                return;
            }

            CheckBox checkBox = control as CheckBox;
            if (checkBox != null)
            {
                SetFont(checkBox, 9F, FontStyle.Regular);
                checkBox.ForeColor = Text;
                return;
            }

            RadioButton radioButton = control as RadioButton;
            if (radioButton != null)
            {
                SetFont(radioButton, 9F, FontStyle.Regular);
                radioButton.ForeColor = Text;
                return;
            }

            TrackBar trackBar = control as TrackBar;
            if (trackBar != null)
            {
                trackBar.BackColor = Surface;
                return;
            }

            GroupBox groupBox = control as GroupBox;
            if (groupBox != null)
            {
                groupBox.BackColor = Surface;
                groupBox.ForeColor = Text;
                SetFont(groupBox, 9F, FontStyle.Regular);
                return;
            }

            Label label = control as Label;
            if (label != null)
            {
                if (label.ForeColor == SystemColors.ControlText ||
                    label.ForeColor == Color.Black)
                {
                    label.ForeColor = Text;
                }
            }
        }

        private static void StyleButton(Button button)
        {
            button.AutoSize = false;
            button.Cursor = Cursors.Hand;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            SetFont(button, 9F, FontStyle.Regular);
            button.Padding = new Padding(10, 0, 10, 0);
            button.UseVisualStyleBackColor = false;
            button.SizeChanged -= RoundedButtonSizeChanged;
            button.SizeChanged += RoundedButtonSizeChanged;
            ApplyRoundedButtonRegion(button);
        }

        private static void RoundedButtonSizeChanged(object sender, EventArgs e)
        {
            ApplyRoundedButtonRegion(sender as Button);
        }

        private static void ApplyRoundedButtonRegion(Button button)
        {
            if (button == null || button.Width < 12 || button.Height < 12)
            {
                return;
            }

            Region previous = button.Region;
            using (GraphicsPath path = CreateRoundedRectangle(
                new Rectangle(0, 0, button.Width, button.Height),
                ModernCardPanel.CornerRadius))
            {
                button.Region = new Region(path);
            }
            if (previous != null)
            {
                previous.Dispose();
            }
        }

        private sealed class RedrawScope : IDisposable
        {
            private const int WmSetRedraw = 0x000B;
            private readonly Control control;
            private readonly IntPtr handle;
            private bool disposed;

            public RedrawScope(Control control)
            {
                this.control = control;
                if (control != null && control.IsHandleCreated)
                {
                    handle = control.Handle;
                    SendMessage(handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
                }
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                if (handle != IntPtr.Zero)
                {
                    SendMessage(handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                    if (control != null && !control.IsDisposed)
                    {
                        control.Invalidate(true);
                    }
                }
            }

            [DllImport("user32.dll")]
            private static extern IntPtr SendMessage(
                IntPtr windowHandle,
                int message,
                IntPtr wordParameter,
                IntPtr longParameter);
        }
    }
}
