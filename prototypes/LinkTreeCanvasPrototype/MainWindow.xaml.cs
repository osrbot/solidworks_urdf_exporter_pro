using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LinkTreeCanvasPrototype
{
    public partial class MainWindow : Window
    {
        private const double NodeWidth = 190;
        private const double NodeHeight = 82;
        private const double LayoutColumnGap = 300;
        private const double LayoutRowGap = 118;

        private static readonly Color[] BranchPalette =
        {
            Color.FromRgb(31, 132, 255),
            Color.FromRgb(0, 153, 119),
            Color.FromRgb(218, 96, 50),
            Color.FromRgb(126, 87, 194),
            Color.FromRgb(197, 127, 0),
            Color.FromRgb(0, 137, 174)
        };

        private readonly ILinkTreeCanvasHost host;
        private readonly Dictionary<Guid, Border> nodeViews = new Dictionary<Guid, Border>();
        private LinkTreeDocument document;
        private Guid? selectedNodeId;
        private Guid? draggingNodeId;
        private Point dragStartPointer;
        private Point dragStartNode;
        private bool isNodeDragging;
        private bool isPanning;
        private Point panStartPointer;
        private Vector panStartOffset;
        private bool suppressPropertyEvents;
        private double zoom = 1.0;

        public MainWindow()
        {
            InitializeComponent();
            host = new PrototypeLinkTreeHost();
            document = host.LoadTree();
            RenderDocument();
            SelectNode(document.Root.Id);
        }

        private void RenderDocument()
        {
            Workspace.Children.Clear();
            nodeViews.Clear();

            foreach (LinkTreeNode node in document.Nodes.Where(item => item.ParentId.HasValue))
            {
                DrawConnector(node);
            }

            foreach (LinkTreeNode node in document.Nodes)
            {
                Border view = BuildNodeView(node);
                nodeViews[node.Id] = view;
                Canvas.SetLeft(view, node.X);
                Canvas.SetTop(view, node.Y);
                Panel.SetZIndex(view, 10);
                Workspace.Children.Add(view);
            }

            UpdateStatus("画布已更新");
        }

        private Border BuildNodeView(LinkTreeNode node)
        {
            Color branchColor = GetBranchColor(node);
            Border card = new Border
            {
                Width = NodeWidth,
                Height = NodeHeight,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(branchColor),
                BorderThickness = selectedNodeId == node.Id ? new Thickness(3) : new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Tag = node.Id,
                Cursor = Cursors.SizeAll,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8,
                    ShadowDepth = 1,
                    Opacity = 0.12,
                    Color = Colors.Black
                }
            };

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

            Border colorBar = new Border
            {
                Background = new SolidColorBrush(branchColor),
                CornerRadius = new CornerRadius(4, 4, 0, 0)
            };
            grid.Children.Add(colorBar);

            TextBlock name = new TextBlock
            {
                Text = node.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(29, 41, 57)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 2, 34, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(name, 1);
            grid.Children.Add(name);

            TextBlock detail = new TextBlock
            {
                Text = node.ParentId.HasValue ? node.JointType : "ROOT",
                Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
                FontSize = 11,
                Margin = new Thickness(12, 0, 0, 7),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetRow(detail, 2);
            grid.Children.Add(detail);

            Button addButton = new Button
            {
                Content = "+",
                Width = 26,
                Height = 26,
                MinHeight = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = node.Id,
                ToolTip = "添加子 Link",
                Cursor = Cursors.Hand
            };
            addButton.Click += AddChildFromNodeClick;
            Grid.SetRow(addButton, 1);
            grid.Children.Add(addButton);

            card.Child = grid;
            card.MouseLeftButtonDown += NodeMouseLeftButtonDown;
            card.MouseLeftButtonUp += NodeMouseLeftButtonUp;
            card.MouseMove += NodeMouseMove;
            return card;
        }

        private void DrawConnector(LinkTreeNode child)
        {
            LinkTreeNode parent = document.Find(child.ParentId.Value);
            if (parent == null)
            {
                return;
            }

            Point start = new Point(parent.X + NodeWidth, parent.Y + NodeHeight / 2);
            Point end = new Point(child.X, child.Y + NodeHeight / 2);
            double controlDistance = Math.Max(70, Math.Abs(end.X - start.X) * 0.45);
            PathFigure figure = new PathFigure { StartPoint = start };
            figure.Segments.Add(new BezierSegment(
                new Point(start.X + controlDistance, start.Y),
                new Point(end.X - controlDistance, end.Y),
                end,
                true));

            Color color = GetBranchColor(child);
            Path path = new Path
            {
                Data = new PathGeometry(new[] { figure }),
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2,
                Opacity = 0.78,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(path, 1);
            Workspace.Children.Add(path);

            Polygon arrow = new Polygon
            {
                Fill = new SolidColorBrush(color),
                Points = new PointCollection
                {
                    new Point(end.X, end.Y),
                    new Point(end.X - 11, end.Y - 6),
                    new Point(end.X - 11, end.Y + 6)
                },
                IsHitTestVisible = false
            };
            Panel.SetZIndex(arrow, 2);
            Workspace.Children.Add(arrow);

            Border label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(238, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(217, 222, 229)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock
                {
                    Text = child.JointName + " / " + child.JointType,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103))
                },
                IsHitTestVisible = false
            };
            const double labelProgress = 0.68;
            Canvas.SetLeft(label, start.X + (end.X - start.X) * labelProgress - 55);
            Canvas.SetTop(label, start.Y + (end.Y - start.Y) * labelProgress - 23);
            Panel.SetZIndex(label, 3);
            Workspace.Children.Add(label);
        }

        private Color GetBranchColor(LinkTreeNode node)
        {
            LinkTreeNode root = document.Root;
            if (root == null || node.Id == root.Id)
            {
                return Color.FromRgb(52, 64, 84);
            }

            LinkTreeNode branch = node;
            while (branch.ParentId.HasValue && branch.ParentId.Value != root.Id)
            {
                branch = document.Find(branch.ParentId.Value);
            }
            int index = StableColorIndex(branch.Name, BranchPalette.Length);
            return BranchPalette[index];
        }

        private static int StableColorIndex(string value, int count)
        {
            unchecked
            {
                int hash = 17;
                foreach (char character in value ?? string.Empty)
                {
                    hash = hash * 31 + character;
                }
                return (hash & int.MaxValue) % count;
            }
        }

        private void NodeMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Border card = (Border)sender;
            Guid id = (Guid)card.Tag;
            SelectNode(id);
            if (e.ClickCount == 2)
            {
                LinkNameTextBox.Focus();
                LinkNameTextBox.SelectAll();
                e.Handled = true;
                return;
            }
            draggingNodeId = id;
            dragStartPointer = e.GetPosition(Workspace);
            LinkTreeNode node = document.Find(id);
            dragStartNode = new Point(node.X, node.Y);
            isNodeDragging = false;
            card.CaptureMouse();
            e.Handled = true;
        }

        private void NodeMouseMove(object sender, MouseEventArgs e)
        {
            if (!draggingNodeId.HasValue || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            Point pointer = e.GetPosition(Workspace);
            Vector delta = pointer - dragStartPointer;
            if (!isNodeDragging && delta.Length < 4)
            {
                return;
            }

            isNodeDragging = true;
            LinkTreeNode node = document.Find(draggingNodeId.Value);
            node.X = Math.Max(10, dragStartNode.X + delta.X);
            node.Y = Math.Max(10, dragStartNode.Y + delta.Y);
            Border card = (Border)sender;
            Canvas.SetLeft(card, node.X);
            Canvas.SetTop(card, node.Y);
        }

        private void NodeMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CompleteNodeDrag(e.GetPosition(Workspace));
            e.Handled = true;
        }

        private void WindowPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!draggingNodeId.HasValue)
            {
                return;
            }
            CompleteNodeDrag(e.GetPosition(Workspace));
            e.Handled = true;
        }

        private void CompleteNodeDrag(Point pointer)
        {
            if (Mouse.Captured != null)
            {
                Mouse.Captured.ReleaseMouseCapture();
            }
            if (draggingNodeId.HasValue && isNodeDragging)
            {
                LinkTreeNode dragged = document.Find(draggingNodeId.Value);
                LinkTreeNode target = FindDropTarget(dragged, pointer);
                if (target != null && CanReparent(dragged, target))
                {
                    dragged.ParentId = target.Id;
                    dragged.JointName = MakeUniqueJointName(target.Name + "_" + dragged.Name + "_joint", dragged.Id);
                    int siblingIndex = document.ChildrenOf(target.Id).Count(node => node.Id != dragged.Id);
                    dragged.X = target.X + LayoutColumnGap;
                    dragged.Y = target.Y + siblingIndex * LayoutRowGap;
                    UpdateStatus(dragged.Name + " 已移动到 " + target.Name + " 下");
                }
            }
            draggingNodeId = null;
            isNodeDragging = false;
            RenderDocument();
            RefreshSelectedProperties();
        }

        private void RefreshSelectedProperties()
        {
            if (!selectedNodeId.HasValue)
            {
                return;
            }
            LinkTreeNode selected = document.Find(selectedNodeId.Value);
            if (selected == null)
            {
                return;
            }
            suppressPropertyEvents = true;
            ParentNameText.Text = selected.ParentId.HasValue
                ? document.Find(selected.ParentId.Value).Name
                : "无（根 Link）";
            JointNameTextBox.Text = selected.JointName;
            SelectJointType(selected.JointType);
            suppressPropertyEvents = false;
            SelectionText.Text = "当前：" + selected.Name;
        }

        private LinkTreeNode FindDropTarget(LinkTreeNode dragged, Point pointer)
        {
            Point draggedCenter = new Point(dragged.X + NodeWidth / 2, dragged.Y + NodeHeight / 2);
            LinkTreeNode overlapTarget = document.Nodes.FirstOrDefault(node =>
                node.Id != dragged.Id &&
                draggedCenter.X >= node.X && draggedCenter.X <= node.X + NodeWidth &&
                draggedCenter.Y >= node.Y && draggedCenter.Y <= node.Y + NodeHeight);
            if (overlapTarget != null)
            {
                return overlapTarget;
            }
            return document.Nodes.FirstOrDefault(node =>
                node.Id != dragged.Id &&
                pointer.X >= node.X && pointer.X <= node.X + NodeWidth &&
                pointer.Y >= node.Y && pointer.Y <= node.Y + NodeHeight);
        }

        private bool CanReparent(LinkTreeNode dragged, LinkTreeNode target)
        {
            if (!dragged.ParentId.HasValue)
            {
                UpdateStatus("根 Link 不能改变父级");
                return false;
            }
            if (document.IsDescendant(target.Id, dragged.Id))
            {
                UpdateStatus("不能把节点移动到自身后代下");
                return false;
            }
            return true;
        }

        private void SelectNode(Guid id)
        {
            selectedNodeId = id;
            LinkTreeNode node = document.Find(id);
            suppressPropertyEvents = true;
            LinkNameTextBox.IsEnabled = true;
            LinkNameTextBox.Text = node.Name;
            JointNameTextBox.IsEnabled = node.ParentId.HasValue;
            JointNameTextBox.Text = node.JointName;
            JointTypeComboBox.IsEnabled = node.ParentId.HasValue;
            SelectJointType(node.JointType);
            ParentNameText.Text = node.ParentId.HasValue ? document.Find(node.ParentId.Value).Name : "无（根 Link）";
            NameValidationText.Text = string.Empty;
            suppressPropertyEvents = false;
            SelectionText.Text = "当前：" + node.Name;
            RenderDocument();
        }

        private void SelectJointType(string jointType)
        {
            foreach (ComboBoxItem item in JointTypeComboBox.Items)
            {
                if ((string)item.Content == jointType)
                {
                    JointTypeComboBox.SelectedItem = item;
                    return;
                }
            }
            JointTypeComboBox.SelectedIndex = -1;
        }

        private void AddChildClick(object sender, RoutedEventArgs e)
        {
            if (!selectedNodeId.HasValue)
            {
                return;
            }
            AddChild(selectedNodeId.Value);
        }

        private void AddChildFromNodeClick(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            AddChild((Guid)button.Tag);
            e.Handled = true;
        }

        private void AddChild(Guid parentId)
        {
            LinkTreeNode parent = document.Find(parentId);
            string name = MakeUniqueLinkName("new_link");
            int siblingCount = document.ChildrenOf(parentId).Count();
            LinkTreeNode child = LinkTreeDocument.NewNode(
                name,
                parentId,
                parent.X + LayoutColumnGap,
                parent.Y + siblingCount * LayoutRowGap);
            child.JointName = MakeUniqueJointName(parent.Name + "_" + child.Name + "_joint", child.Id);
            document.Nodes.Add(child);
            RenderDocument();
            SelectNode(child.Id);
            LinkNameTextBox.Focus();
            LinkNameTextBox.SelectAll();
            UpdateStatus("已添加子 Link，请输入名称");
        }

        private string MakeUniqueLinkName(string baseName)
        {
            string candidate = baseName;
            int suffix = 1;
            while (document.Nodes.Any(node => string.Equals(node.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = baseName + "_" + suffix++;
            }
            return candidate;
        }

        private string MakeUniqueJointName(string baseName, Guid editingNodeId)
        {
            string candidate = baseName;
            int suffix = 1;
            while (document.Nodes.Any(node => node.Id != editingNodeId && string.Equals(node.JointName, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = baseName + "_" + suffix++;
            }
            return candidate;
        }

        private void LinkNameCommit(object sender, RoutedEventArgs e)
        {
            CommitLinkName();
        }

        private bool CommitLinkName()
        {
            if (!selectedNodeId.HasValue)
            {
                return false;
            }
            string value = LinkNameTextBox.Text.Trim();
            string error = host.ValidateLinkName(value, selectedNodeId.Value);
            if (error == null && document.Nodes.Any(node => node.Id != selectedNodeId.Value && string.Equals(node.Name, value, StringComparison.OrdinalIgnoreCase)))
            {
                error = "Link 名称不能重复。";
            }
            NameValidationText.Text = error ?? string.Empty;
            if (error != null)
            {
                return false;
            }

            LinkTreeNode selectedNode = document.Find(selectedNodeId.Value);
            string oldName = selectedNode.Name;
            selectedNode.Name = value;
            if (selectedNode.ParentId.HasValue && selectedNode.JointName.Contains(oldName))
            {
                selectedNode.JointName = MakeUniqueJointName(selectedNode.JointName.Replace(oldName, value), selectedNode.Id);
                JointNameTextBox.Text = selectedNode.JointName;
            }
            RenderDocument();
            SelectionText.Text = "当前：" + selectedNode.Name;
            return true;
        }

        private void JointNameCommit(object sender, RoutedEventArgs e)
        {
            if (!selectedNodeId.HasValue)
            {
                return;
            }
            LinkTreeNode node = document.Find(selectedNodeId.Value);
            if (!node.ParentId.HasValue)
            {
                return;
            }
            string value = JointNameTextBox.Text.Trim();
            string error = LinkTreeDocument.ValidateRosName(value);
            if (error == null && document.Nodes.Any(item => item.Id != node.Id && string.Equals(item.JointName, value, StringComparison.OrdinalIgnoreCase)))
            {
                error = "Joint 名称不能重复。";
            }
            if (error != null)
            {
                UpdateStatus(error);
                JointNameTextBox.Text = node.JointName;
                return;
            }
            node.JointName = value;
            RenderDocument();
        }

        private void PropertyTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }

        private void JointTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressPropertyEvents || !selectedNodeId.HasValue || JointTypeComboBox.SelectedItem == null)
            {
                return;
            }
            LinkTreeNode node = document.Find(selectedNodeId.Value);
            if (!node.ParentId.HasValue)
            {
                return;
            }
            node.JointType = (string)((ComboBoxItem)JointTypeComboBox.SelectedItem).Content;
            RenderDocument();
        }

        private void DeleteSelectedClick(object sender, RoutedEventArgs e)
        {
            DeleteSelected();
        }

        private void DeleteSelected()
        {
            if (!selectedNodeId.HasValue)
            {
                return;
            }
            LinkTreeNode node = document.Find(selectedNodeId.Value);
            if (!node.ParentId.HasValue)
            {
                UpdateStatus("根 Link 不能删除");
                return;
            }
            int count = 1 + CountDescendants(node.Id);
            MessageBoxResult result = MessageBox.Show(
                "将删除 " + node.Name + " 及其分支中的 " + count + " 个 Link。是否继续？",
                "删除整个分支",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
            HashSet<Guid> ids = new HashSet<Guid> { node.Id };
            CollectDescendants(node.Id, ids);
            document.Nodes.RemoveAll(item => ids.Contains(item.Id));
            SelectNode(document.Root.Id);
            UpdateStatus("已删除分支");
        }

        private int CountDescendants(Guid parentId)
        {
            return document.ChildrenOf(parentId).Sum(child => 1 + CountDescendants(child.Id));
        }

        private void CollectDescendants(Guid parentId, ISet<Guid> ids)
        {
            foreach (LinkTreeNode child in document.ChildrenOf(parentId).ToList())
            {
                ids.Add(child.Id);
                CollectDescendants(child.Id, ids);
            }
        }

        private void AutoLayoutClick(object sender, RoutedEventArgs e)
        {
            int row = 0;
            LayoutSubtree(document.Root, 0, ref row);
            RenderDocument();
            ResetView();
            UpdateStatus("已按层级自动整理");
        }

        private double LayoutSubtree(LinkTreeNode node, int depth, ref int row)
        {
            List<LinkTreeNode> children = document.ChildrenOf(node.Id).ToList();
            double y;
            if (children.Count == 0)
            {
                y = 90 + row++ * LayoutRowGap;
            }
            else
            {
                List<double> childRows = new List<double>();
                foreach (LinkTreeNode child in children)
                {
                    childRows.Add(LayoutSubtree(child, depth + 1, ref row));
                }
                y = childRows.Average();
            }
            node.X = 80 + depth * LayoutColumnGap;
            node.Y = y;
            return y;
        }

        private void ApplyClick(object sender, RoutedEventArgs e)
        {
            if (!CommitLinkName())
            {
                UpdateStatus("请先修正节点名称");
                return;
            }
            host.ApplyTree(document);
            UpdateStatus("快照已通过预留接口提交：" + document.Nodes.Count + " 个 Link");
        }

        private void LoadSampleClick(object sender, RoutedEventArgs e)
        {
            document = LinkTreeDocument.CreateSample();
            RenderDocument();
            SelectNode(document.Root.Id);
            ResetView();
        }

        private void ResetViewClick(object sender, RoutedEventArgs e)
        {
            ResetView();
        }

        private void ResetView()
        {
            zoom = 1.0;
            ZoomTransform.ScaleX = zoom;
            ZoomTransform.ScaleY = zoom;
            PanTransform.X = 0;
            PanTransform.Y = 0;
            ZoomLabel.Text = "100%";
        }

        private void ViewportMouseWheel(object sender, MouseWheelEventArgs e)
        {
            zoom = Math.Max(0.45, Math.Min(1.8, zoom + (e.Delta > 0 ? 0.1 : -0.1)));
            ZoomTransform.ScaleX = zoom;
            ZoomTransform.ScaleY = zoom;
            ZoomLabel.Text = Math.Round(zoom * 100) + "%";
            e.Handled = true;
        }

        private void ViewportRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            isPanning = true;
            panStartPointer = e.GetPosition(Viewport);
            panStartOffset = new Vector(PanTransform.X, PanTransform.Y);
            Viewport.CaptureMouse();
            Viewport.Cursor = Cursors.Hand;
        }

        private void ViewportRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            isPanning = false;
            Viewport.ReleaseMouseCapture();
            Viewport.Cursor = Cursors.Arrow;
        }

        private void ViewportMouseMove(object sender, MouseEventArgs e)
        {
            if (!isPanning || e.RightButton != MouseButtonState.Pressed)
            {
                return;
            }
            Vector delta = e.GetPosition(Viewport) - panStartPointer;
            PanTransform.X = panStartOffset.X + delta.X;
            PanTransform.Y = panStartOffset.Y + delta.Y;
        }

        private void FocusSelectedClick(object sender, RoutedEventArgs e)
        {
            if (!selectedNodeId.HasValue)
            {
                return;
            }
            LinkTreeNode node = document.Find(selectedNodeId.Value);
            PanTransform.X = Math.Max(0, Viewport.ActualWidth / 2 - node.X * zoom - NodeWidth / 2);
            PanTransform.Y = Math.Max(0, Viewport.ActualHeight / 2 - node.Y * zoom - NodeHeight / 2);
        }

        private void WindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && !(Keyboard.FocusedElement is TextBox))
            {
                DeleteSelected();
            }
            else if (e.Key == Key.F2 && selectedNodeId.HasValue)
            {
                LinkNameTextBox.Focus();
                LinkNameTextBox.SelectAll();
            }
        }

        private void UpdateStatus(string message)
        {
            int joints = document.Nodes.Count(node => node.ParentId.HasValue);
            StatusText.Text = message + "    ·    " + document.Nodes.Count + " 个 Link    ·    " + joints + " 个 Joint";
        }
    }
}
