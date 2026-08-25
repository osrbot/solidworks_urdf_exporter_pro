using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SW2URDF.UI.LinkTreeCanvas
{
    public partial class LinkTreeOutlineWindow : Window
    {
        private readonly LinkTreeDocument source;
        private LinkTreeOutlineParseResult currentResult;

        public LinkTreeDocument ResultDocument { get; private set; }

        public LinkTreeOutlineWindow(LinkTreeDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            InitializeComponent();
            source = document.Clone();
            OutlineTextBox.Text = LinkTreeOutline.Serialize(source);
            OutlineTextBox.CaretIndex = 0;
            ValidateOutline();
        }

        private void OutlineTextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateOutline();
        }

        private void ValidateOutline()
        {
            if (OutlineTextBox == null || ApplyButton == null)
            {
                return;
            }

            currentResult = LinkTreeOutline.Parse(OutlineTextBox.Text, source);
            ApplyButton.IsEnabled = currentResult.IsValid;
            if (currentResult.IsValid)
            {
                int addedCount = currentResult.Document.Nodes.Count(node => source.Find(node.Id) == null);
                int removedCount = source.Nodes.Count(node => currentResult.Document.Find(node.Id) == null);
                ValidationText.Text = addedCount == 0 && removedCount == 0
                    ? string.Empty
                    : "将新增 " + addedCount + " 个 Link，移除 " + removedCount +
                        " 个 Link。新增 Link 需要回到属性页分配 CAD 组件。";
                ValidationText.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
                StatusText.Text = "结构有效，共 " + currentResult.Document.Nodes.Count + " 个 Link";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 83));
            }
            else
            {
                ValidationText.Text = string.Join(Environment.NewLine, currentResult.Errors);
                ValidationText.Foreground = new SolidColorBrush(Color.FromRgb(198, 40, 40));
                StatusText.Text = "请修正右侧列出的格式问题";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(198, 40, 40));
            }
        }

        private void ResetClick(object sender, RoutedEventArgs e)
        {
            OutlineTextBox.Text = LinkTreeOutline.Serialize(source);
            OutlineTextBox.Focus();
            OutlineTextBox.CaretIndex = OutlineTextBox.Text.Length;
        }

        private void CancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ApplyClick(object sender, RoutedEventArgs e)
        {
            ValidateOutline();
            if (!currentResult.IsValid)
            {
                return;
            }

            ResultDocument = currentResult.Document.Clone();
            DialogResult = true;
        }
    }
}
