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
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace SW2URDF.UI
{
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

        internal static void SetFont(Control control, float size, FontStyle style)
        {
            string family = ChineseUiText.ShouldUseChinese()
                ? "Microsoft YaHei UI"
                : "Segoe UI";
            UiFontResources.SetFont(control, family, size, style);
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
            TableLayoutPanel card = new TableLayoutPanel
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
            card.Paint += DrawCardBorder;
            return card;
        }

        internal static void Apply(Form form)
        {
            form.SuspendLayout();
            try
            {
                form.BackColor = Background;
                SetFont(form, 9F, FontStyle.Regular);
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

            using (Pen pen = new Pen(Border))
            {
                Rectangle rectangle = new Rectangle(0, 0, control.Width - 1, control.Height - 1);
                e.Graphics.DrawRectangle(pen, rectangle);
            }
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
        }
    }
}
