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
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport.CSV;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SW2URDF.URDFExport
{
    [ComVisible(true)]
    [Serializable]
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9
    {
        #region class variables

        private static readonly log4net.ILog logger = Logger.GetLogger();
        public SldWorks swApp;
        public ModelDoc2 ActiveSWModel;

        public ExportHelper Exporter;
        public LinkNode previouslySelectedNode;
        public Link previouslySelectedLink;
        public List<Link> linksToVisit;
        public LinkNode rightClickedNode;
        private LinkTreeSession linkTreeSession;
        private bool closingAfterSuccessfulExport;
        private readonly ExportSessionCloseCoordinator closeCoordinator =
            new ExportSessionCloseCoordinator();
        [NonSerialized]
        private readonly IExportSessionDraftStore exportSessionDraftStore;
        private readonly string activeModelPath;
        private readonly ContextMenuStrip docMenu;
        [NonSerialized]
        private readonly TreeSelectionUpdateGuard treeSelectionUpdateGuard;

        //General objects required for the PropertyManager page

        private readonly PropertyManagerPage2 PMPage;
        private PropertyManagerPageGroup PMGroup;
        private PropertyManagerPageSelectionbox PMSelection;
        private PropertyManagerPageButton PMButtonExport;
        private PropertyManagerPageButton PMButtonEditTree;
        private PropertyManagerPageTextbox PMTextBoxLinkName;
        private PropertyManagerPageTextbox PMTextBoxJointName;
        private PropertyManagerPageNumberbox PMNumberBoxChildCount;
        private PropertyManagerPageCombobox PMComboBoxGlobalCoordsys;
        private PropertyManagerPageCombobox PMComboBoxAxes;
        private PropertyManagerPageCombobox PMComboBoxCoordSys;
        private PropertyManagerPageCombobox PMComboBoxJointType;
        private PropertyManagerPageCheckbox PMComputeMassInertia;
        private PropertyManagerPageCheckbox PMComputeVisualCollision;
        private PropertyManagerPageCheckbox PMComputeJointKinematics;
        private PropertyManagerPageCheckbox PMComputeJointLimits;

        private PropertyManagerPageLabel PMLabelJointName;
        private PropertyManagerPageLabel PMLabelParentLink;
        private PropertyManagerPageLabel PMLabelAxes;
        private PropertyManagerPageLabel PMLabelCoordSys;
        private PropertyManagerPageLabel PMLabelJointType;
        private PropertyManagerPageLabel PMLabelGlobalCoordsys;
        private PropertyManagerPageLabel PMLabelChildCount;
        private PropertyManagerPageLabel PMLabelCSVFilename;

        private PropertyManagerPageWindowFromHandle PMTree;

        public TreeView Tree
        { get; set; }

        [field: NonSerialized]
        internal event EventHandler Closed;

        //Each object in the page needs a unique ID

        private const int GroupID = 1;
        private const int TextBoxLinkNameID = 2;
        private const int SelectionID = 3;
        private const int TextBoxJointNameID = 4;
        private const int NumBoxChildCountID = 7;
        private const int LabelLinkNameID = 8;
        private const int LabelJointNameID = 14;
        private const int dotNetTree = 16;
        private const int ButtonExportID = 17;
        private const int ComboBoxAxesID = 18;
        private const int ComboBoxCoordSysID = 19;
        private const int LabelAxesID = 20;
        private const int LabelCoordSysID = 21;
        private const int ComboBoxJointTypeID = 22;
        private const int LabelJointTypeID = 23;
        private const int IDGlobalCoordsys = 24;
        private const int IDLabelGlobalCoordsys = 25;
        private const int ComputeMassInertiaID = 27;
        private const int ComputeVisualCollisionID = 28;
        private const int ComputeJointKinematicsID = 29;
        private const int ComputeJointLimitsID = 30;
        private const int LoadedCSVFilenameID = 31;
        private const int EditLinkTreeID = 32;
        private const int LabelChildCountID = 33;

        #endregion class variables

        public void Show()
        {
            PMPage.Show2(0);
        }

        public void Close(bool ok)
        {
            PMPage.Close(ok);
        }

        //The following runs when a new instance of the class is created
        public ExportPropertyManager(SldWorks swAppPtr)
        {
            exportSessionDraftStore = new FileExportSessionDraftStore();
            treeSelectionUpdateGuard = new TreeSelectionUpdateGuard();
            swApp = swAppPtr;
            ActiveSWModel = swApp.ActiveDoc;
            activeModelPath = ActiveSWModel.GetPathName();
            Exporter = new ExportHelper(swApp);
            Exporter.URDFRobot = new Robot();
            Exporter.URDFRobot.Name = ActiveSWModel.GetTitle();

            linksToVisit = new List<Link>();
            docMenu = new ContextMenuStrip();

            string caption = null;
            string tip = null;
            int longerrors = 0;
            int controlType = 0;
            int alignment = 0;

            ActiveSWModel.ShowConfiguration2("URDF Export");

            #region Create and instantiate components of PM page

            //Set the variables for the page
            string PageTitle = ChineseUiText.Translate(
                "URDF Exporter",
                "URDF \u5bfc\u51fa\u5668");
            long options = (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton +
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton +
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_HandleKeystrokes;

            //Create the PropertyManager page
            PMPage = (PropertyManagerPage2)swApp.CreatePropertyManagerPage(
                PageTitle, (int)options, this, ref longerrors);

            //Make sure that the page was created properly
            if (longerrors == (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
            {
                SetupPropertyManagerPage(ref caption, ref tip, ref options,
                    ref controlType, ref alignment);
            }
            else
            {
                //If the page is not created
                logger.Error("An error occurred while attempting to create the PropertyManager Page\nError: " + longerrors);
                ShowPropertyManagerError(
                    "There was a problem setting up the property manager.",
                    "\u521b\u5efa\u5c5e\u6027\u7ba1\u7406\u5668\u65f6\u53d1\u751f\u9519\u8bef\u3002");
            }

            #endregion Create and instantiate components of PM page
        }

        private void ExceptionHandler(object sender, ThreadExceptionEventArgs e)
        {
            logger.Warn("Exception encountered in URDF configuration form\n" +
                "Email your maintainer with the log file found at " + Logger.GetFileName(),
                e.Exception);
        }

        private void UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            logger.Error("Unhandled exception in URDF configuration form\n" +
                "Email your maintainer with the log file found at " + Logger.GetFileName(),
                (Exception)e.ExceptionObject);
        }

        #region Implemented Property Manager Page Handler Methods

        void IPropertyManagerPage2Handler9.AfterActivation()
        {
            //Turns the selection box blue so that selected components are added to the PMPage
            // selection box
            PMSelection.SetSelectionFocus();
        }

        private void ExportButtonPress()
        {
            SaveActiveNode();

            if (!CheckIfNamesAreUnique((LinkNode)Tree.Nodes[0]) || !CheckNodesComplete(Tree))
            {
                return;
            }

            CommitLinkTreeProjection();

            if (linkTreeSession.RequiresJointKinematicsRecompute)
            {
                PMComputeJointKinematics.Checked = true;
            }
            if (linkTreeSession.RequiresJointLimitsRecompute)
            {
                PMComputeJointLimits.Checked = true;
            }

            Exporter.SetComputeInertial(PMComputeMassInertia.Checked);
            Exporter.SetComputeVisualCollision(PMComputeVisualCollision.Checked);
            Exporter.SetComputeJointKinematics(PMComputeJointKinematics.Checked);
            Exporter.SetComputeJointLimits(PMComputeJointLimits.Checked);

            AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
            int result;
            using (OperationHeartbeat.Start(logger,
                "Resolving all lightweight SolidWorks components"))
            {
                result = assy.ResolveAllLightWeightComponents(true);
            }

            if (result == (int)swComponentResolveStatus_e.swResolveError ||
                result == (int)swComponentResolveStatus_e.swResolveNotPerformed)
            {
                logger.Warn("Resolving components failed. Warning user to do so on their own");
                MessageBox.Show(
                    ChineseUiText.Translate(
                        "Resolving components failed. All components must be resolved before " +
                            "exporting to URDF. Resolve lightweight components manually and try again.",
                        "\u7ec4\u4ef6\u89e3\u6790\u5931\u8d25\u3002\u5bfc\u51fa URDF \u524d\u5fc5\u987b\u89e3\u6790\u6240\u6709\u7ec4\u4ef6\u3002" +
                            "\u8bf7\u624b\u52a8\u89e3\u6790\u8f7b\u91cf\u5316\u7ec4\u4ef6\u540e\u91cd\u8bd5\u3002"));
                return;
            }
            if (result == (int)swComponentResolveStatus_e.swResolveAbortedByUser)
            {
                logger.Warn("Components were not resolved by user");
                MessageBox.Show(
                    ChineseUiText.Translate(
                        "All components must be resolved before exporting to URDF. " +
                            "Resolve them manually or try exporting again.",
                        "\u5bfc\u51fa URDF \u524d\u5fc5\u987b\u89e3\u6790\u6240\u6709\u7ec4\u4ef6\u3002" +
                            "\u8bf7\u624b\u52a8\u89e3\u6790\uff0c\u6216\u91cd\u65b0\u5c1d\u8bd5\u5bfc\u51fa\u3002"));
                return;
            }
            if (result != (int)swComponentResolveStatus_e.swResolveOk)
            {
                logger.Error("SolidWorks returned an unexpected component resolve status: " + result);
                MessageBox.Show(
                    ChineseUiText.Translate(
                        "SolidWorks returned an unexpected component resolve status. " +
                            "Resolve all lightweight components manually and try again.",
                        "SolidWorks 返回了未知的组件解析状态。请手动解析所有轻量化组件后重试。"));
                return;
            }

            List<string> unresolvedComponents = new List<string>();
            LinkNode baseNode = linkTreeSession.CreateComputationProjection();
            CheckModelDocsExist(baseNode, unresolvedComponents);
            if (unresolvedComponents.Count > 0)
            {
                string componentNames = string.Join("\r\n", unresolvedComponents);
                logger.Error("SolidWorks told us the resolve succeeded, but ModelDocs" +
                    " could not be obtained for: " + componentNames);
                MessageBox.Show(
                    ChineseUiText.Translate(
                        "Model documents could not be obtained for the following components. " +
                            "Please resolve them:\r\n",
                        "\u65e0\u6cd5\u83b7\u53d6\u4ee5\u4e0b\u7ec4\u4ef6\u7684\u6a21\u578b\u6587\u6863\uff0c\u8bf7\u5148\u89e3\u6790\u8fd9\u4e9b\u7ec4\u4ef6\uff1a\r\n") +
                    componentNames);
                return;
            }

            if (!Exporter.CreateRobotFromTreeView(baseNode))
            {
                MessageBox.Show(Exporter.ExportErrorWhy);
                return;
            }

            linkTreeSession.ValidateComputedProjection(baseNode);
            linkTreeSession.AcceptComputedProjection(baseNode);
            SaveExportSessionDraft(baseNode);
            closingAfterSuccessfulExport = true;
            PMPage.Close(true);

            AssemblyExportForm exportForm = new AssemblyExportForm(swApp, baseNode, Exporter);
            exportForm.Exporter = Exporter;
            exportForm.Show();
        }

        private void EnableControl(IPropertyManagerPageControl control, bool isEnabled = true)
        {
            control.Enabled = isEnabled;
            control.Visible = true;
        }

        private void TreeMergeCompleted(object sender, TreeMergedEventArgs e)
        {
            if (!e.Success)
            {
                MessageBox.Show(
                    ChineseUiText.Translate(
                        "Merging the loaded CSV configuration with the assembly configuration failed. " +
                            "Check the CSV file. If the error persists, delete the assembly configuration " +
                            "and load a valid CSV.",
                        "\u5408\u5e76 CSV \u914d\u7f6e\u4e0e\u88c5\u914d\u4f53\u914d\u7f6e\u5931\u8d25\u3002\u8bf7\u68c0\u67e5 CSV \u6587\u4ef6\u3002" +
                            "\u5982\u679c\u9519\u8bef\u6301\u7eed\uff0c\u8bf7\u5220\u9664\u88c5\u914d\u4f53\u4e2d\u7684\u914d\u7f6e\u540e\u91cd\u65b0\u52a0\u8f7d\u6709\u6548 CSV\u3002"));
                return;
            }

            LinkNode mergedRoot = null;
            foreach (System.Windows.Controls.TreeViewItem item in e.MergedTree.Items)
            {
                if (mergedRoot != null)
                {
                    throw new InvalidOperationException("Merged Link tree contains more than one root.");
                }
                mergedRoot = LinkNodeFromTreeViewItem(item);
            }
            if (mergedRoot == null)
            {
                throw new InvalidOperationException("Merged Link tree is empty.");
            }
            ReplaceLinkTreeRoot(mergedRoot);

            PMComputeMassInertia.Checked = !e.UsedCSVInertial;
            PMComputeVisualCollision.Checked = !e.UsedCSVVisualCollision;
            PMComputeJointKinematics.Checked = !e.UsedCSVJointKinematics;
            PMComputeJointLimits.Checked = !e.UsedCSVJointOther;
            if (linkTreeSession.RequiresJointKinematicsRecompute)
            {
                PMComputeJointKinematics.Checked = true;
            }
            if (linkTreeSession.RequiresJointLimitsRecompute)
            {
                PMComputeJointLimits.Checked = true;
            }
            PMLabelCSVFilename.Caption = ChineseUiText.Translate(
                "Filename: ",
                "\u6587\u4ef6\u540d\uff1a") + e.CSVFilename;

            // Make the controls visible, but only enable them if values have been loaded from the CSV
            // otherwise they do need to be computed.
            EnableControl((IPropertyManagerPageControl)PMComputeMassInertia, e.UsedCSVInertial);
            EnableControl((IPropertyManagerPageControl)PMComputeVisualCollision, e.UsedCSVVisualCollision);
            EnableControl((IPropertyManagerPageControl)PMComputeJointKinematics, e.UsedCSVJointKinematics);
            EnableControl((IPropertyManagerPageControl)PMComputeJointLimits, e.UsedCSVJointOther);
            EnableControl((IPropertyManagerPageControl)PMLabelCSVFilename);
        }

        private LinkNode LinkNodeFromTreeViewItem(System.Windows.Controls.TreeViewItem item)
        {
            Link itemLink = (Link)item.Tag;
            LinkNode node = new LinkNode
            {
                Link = itemLink,
                Name = itemLink.Name,
                Text = itemLink.Name
            };
            node.IsBaseNode = item.Parent.GetType() != typeof(System.Windows.Controls.TreeViewItem);
            foreach (System.Windows.Controls.TreeViewItem child in item.Items)
            {
                node.Nodes.Add(LinkNodeFromTreeViewItem(child));
            }
            return node;
        }

        private void LoadFromCSV()
        {
            SaveActiveNode();
            CommitLinkTreeProjection();

            LinkNode existingBaseNode = (LinkNode)linkTreeSession.CreateProjection().Clone();
            if (existingBaseNode == null || !existingBaseNode.RebuildLink().AreRequiredFieldsSatisfied())
            {
                logger.Warn("Loading a configuration with an incomplete export");
                if (MessageBox.Show(
                    ChineseUiText.Translate(
                        "This model has not been fully exported and saved. Merging may result in an " +
                            "incomplete URDF. Do you want to continue?",
                        "\u6b64\u6a21\u578b\u5c1a\u672a\u5b8c\u6574\u5bfc\u51fa\u5e76\u4fdd\u5b58\uff0c\u5408\u5e76\u540e\u53ef\u80fd\u4ea7\u751f\u4e0d\u5b8c\u6574\u7684 URDF\u3002" +
                            "\u662f\u5426\u7ee7\u7eed\uff1f"),
                    ChineseUiText.Translate(
                        "Continue with incomplete export?",
                        "\u7ee7\u7eed\u4e0d\u5b8c\u6574\u7684\u5bfc\u51fa\uff1f"),
                    MessageBoxButtons.YesNo) ==
                        DialogResult.No) {
                    return;
                }
            }

            OpenFileDialog loadFileDialog = new OpenFileDialog
            {
                Filter = ChineseUiText.Translate(
                    "CSV (.csv)|*.csv|All files (*.*)|*.*",
                    "CSV (.csv)|*.csv|\u6240\u6709\u6587\u4ef6 (*.*)|*.*"),
                Multiselect = false,
                ValidateNames = true,
                CheckPathExists = true
            };

            if (loadFileDialog.ShowDialog() == DialogResult.OK)
            {
                logger.Info("Loading configuration " + loadFileDialog.FileName);
                using (Stream stream = loadFileDialog.OpenFile())
                {
                    List<Link> loadedLinks = ImportExport.LoadURDFRobotFromCSV(stream);
                    if (loadedLinks == null)
                    {
                        return;
                    }

                    logger.Info("Link successfully loaded");

                    string filename = loadFileDialog.SafeFileName;
                    string assemblyTitle = ActiveSWModel.GetTitle();

                    Link existingBaseLink = existingBaseNode.RebuildLink();
                    TreeMergeWPF wpf = new TreeMergeWPF(existingBaseLink, loadedLinks,
                        filename, assemblyTitle);
                    wpf.TreeMerged += TreeMergeCompleted;
                    wpf.ShowDialog();
                }
            }
        }

        private void OnButtonPress(int Id)
        {
            switch (Id)
            {
                case ButtonExportID:
                    ExportButtonPress();
                    break;

                case EditLinkTreeID:
                    OpenLinkTreeCanvas();
                    break;

                default:
                    break;
            }
        }

        // Called when a PropertyManagerPageButton is pressed. In our case, that's only the
        // export button for now
        void IPropertyManagerPage2Handler9.OnButtonPress(int Id)
        {
            try
            {
                OnButtonPress(Id);
            }
            catch (Exception e)
            {
                logger.Error("Exception caught handling button press " + Id, e);
                ShowPropertyManagerError(
                    "There was a problem with the configuration property manager.",
                    "\u914d\u7f6e\u5c5e\u6027\u7ba1\u7406\u5668\u53d1\u751f\u9519\u8bef\u3002",
                    e);
            }
        }

        void IPropertyManagerPage2Handler9.OnClose(int Reason)
        {
            logger.Info("URDF property manager close requested with reason " + Reason + ".");
            try
            {
                closeCoordinator.Capture(
                    Reason,
                    CaptureCurrentLinkTreeProjection(),
                    closingAfterSuccessfulExport);
            }
            catch (Exception e)
            {
                logger.Error("Exception caught on close ", e);
            }
        }

        private LinkNode CaptureCurrentLinkTreeProjection()
        {
            if (Tree != null && Tree.Nodes.Count > 0)
            {
                try
                {
                    return (LinkNode)((LinkNode)Tree.Nodes[0]).Clone();
                }
                catch (Exception exception)
                {
                    logger.Warn(
                        "The closing PropertyManager tree could not be cloned; " +
                        "falling back to the committed Link-tree session.",
                        exception);
                }
            }

            try
            {
                return linkTreeSession == null ? null : linkTreeSession.CreateProjection();
            }
            catch (Exception exception)
            {
                logger.Warn("The Link-tree close fallback could not be captured.", exception);
                return null;
            }
        }

        private void CompletePropertyManagerClose()
        {
            ExportSessionCloseAction action = closeCoordinator.BeginFinalization(
                (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay,
                out LinkNode projection);
            if (action == ExportSessionCloseAction.None)
            {
                return;
            }

            if (action == ExportSessionCloseAction.SaveConfiguration)
            {
                bool saved = false;
                try
                {
                    saved = SaveConfigTree(ActiveSWModel, projection, false);
                }
                catch (Exception exception)
                {
                    logger.Error(
                        "URDF configuration persistence failed after closing.",
                        exception);
                }

                if (!saved)
                {
                    logger.Error("URDF configuration could not be persisted after closing.");
                    SaveExportSessionDraft(projection);
                    return;
                }

                logger.Info("Configuration saved after closing the PropertyManager.");
                return;
            }

            logger.Info("Configuration editing ended without a formal save; preserving a draft.");
            SaveExportSessionDraft(projection);
        }

        void IPropertyManagerPage2Handler9.OnGainedFocus(int Id)
        {
        }

        bool IPropertyManagerPage2Handler9.OnHelp()
        {
            return true;
        }

        bool IPropertyManagerPage2Handler9.OnKeystroke(int Wparam, int Message, int Lparam, int Id)
        {
            if (Wparam == (int)Keys.Enter)
            {
                return true;
            }
            return false;
        }

        void IPropertyManagerPage2Handler9.OnLostFocus(int Id)
        {
            Debug.Print("Control box " + Id + " has lost focus");
        }

        void IPropertyManagerPage2Handler9.OnNumberboxChanged(int Id, double Value)
        {
            if (Id == NumBoxChildCountID)
            {
                LinkNode node = (LinkNode)Tree.SelectedNode;
                CreateNewNodes(node);
            }
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxFocusChanged(int Id)
        {
            Debug.Print("The focus has moved to selection box " + Id);
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxListChanged(int Id, int Count)
        {
            if (Id == SelectionID)
            {
                UpdateSelectedNodeCadBindings();
            }

            // Move focus to next selection box if right-mouse button pressed
            PMPage.SetCursor((int)swPropertyManagerPageCursors_e.swPropertyManagerPageCursors_Advance);
        }

        bool IPropertyManagerPage2Handler9.OnSubmitSelection(
            int Id, object Selection, int SelType, ref string ItemText)
        {
            // This method must return true for selections to occur
            return true;
        }

        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text)
        {
            LinkNode node = Tree == null ? null : Tree.SelectedNode as LinkNode;
            if (node == null)
            {
                return;
            }

            if (Id == TextBoxLinkNameID)
            {
                node.Link.Name = Text ?? string.Empty;
                node.Text = node.Link.Name;
                node.Name = node.Link.Name;
            }
            else if (Id == TextBoxJointNameID && !node.IsBaseNode)
            {
                node.Link.Joint.Name = Text ?? string.Empty;
            }
        }

        int IPropertyManagerPage2Handler9.OnWindowFromHandleControlCreated(int Id, bool Status)
        {
            return 0;
        }

        #endregion Implemented Property Manager Page Handler Methods

        #region TreeView handler methods

        // Upon selection of a node, the node displayed on the PMPage is saved and the
        // selected one is then set
        private void TreeAfterSelect(object sender, TreeViewEventArgs e)
        {
            try
            {
                if (!treeSelectionUpdateGuard.IsSuppressed && e.Node != null)
                {
                    SwitchActiveNodes((LinkNode)e.Node);
                }
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view AfterSelect ", ex);
                ShowPropertyManagerError(
                    "There was a problem with the property manager.",
                    "\u5c5e\u6027\u7ba1\u7406\u5668\u53d1\u751f\u9519\u8bef\u3002",
                    ex);
            }
        }

        // Captures which node was right clicked
        private void TreeNodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            rightClickedNode = (LinkNode)e.Node;
        }

        //When a keyboard key is pressed on the tree
        private void TreeKeyDown(object sender, KeyEventArgs e)
        {
            if (rightClickedNode.IsEditing)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    rightClickedNode.EndEdit(false);
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    rightClickedNode.EndEdit(true);
                }
            }
        }

        // The callback for the configuration page context menu 'Add Child' option
        private void AddChildClick(object sender, EventArgs e)
        {
            try
            {
                CreateNewNodes(rightClickedNode, 1);
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view add child ", ex);
                ShowPropertyManagerError(
                    "There was a problem with the property manager.",
                    "\u5c5e\u6027\u7ba1\u7406\u5668\u53d1\u751f\u9519\u8bef\u3002",
                    ex);
            }
        }

        // The callback for the configuration page context menu 'Remove Child' option
        private void RemoveChildClick(object sender, EventArgs e)
        {
            try
            {
                LinkNode parent = (LinkNode)rightClickedNode.Parent;
                parent.Nodes.Remove(rightClickedNode);
                CommitLinkTreeProjection();
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view remove child ", ex);
                ShowPropertyManagerError(
                    "There was a problem with the property manager.",
                    "\u5c5e\u6027\u7ba1\u7406\u5668\u53d1\u751f\u9519\u8bef\u3002",
                    ex);
            }
        }

        // The callback for the configuration page context menu 'Rename Child' option
        // This isn't really working right now, so the option was deactivated from the
        // context menu
        private void RenameChildClick(object sender, EventArgs e)
        {
            try
            {
                Tree.SelectedNode = rightClickedNode;
                Tree.LabelEdit = true;
                rightClickedNode.BeginEdit();
                PMPage.SetFocus(dotNetTree);
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view rename child ", ex);
                ShowPropertyManagerError(
                    "There was a problem with the property manager.",
                    "\u5c5e\u6027\u7ba1\u7406\u5668\u53d1\u751f\u9519\u8bef\u3002",
                    ex);
            }
        }

        private void TreeItemDrag(object sender, ItemDragEventArgs e)
        {
            try
            {
                Tree.DoDragDrop(e.Item, DragDropEffects.Move);
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view Drag ", ex);
                ShowPropertyManagerError(
                    "There was a problem with the property manager.",
                    "\u5c5e\u6027\u7ba1\u7406\u5668\u53d1\u751f\u9519\u8bef\u3002",
                    ex);
            }
        }

        private void TreeDragOver(object sender, DragEventArgs e)
        {
            try
            {
                // Retrieve the client coordinates of the mouse position.
                Point targetPoint = Tree.PointToClient(new Point(e.X, e.Y));

                // Select the node at the mouse position.
                Tree.SelectedNode = Tree.GetNodeAt(targetPoint);
                e.Effect = DragDropEffects.Move;
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view Drag Over ", ex);
                ShowPropertyManagerError(
                    "There was a problem with the property manager.",
                    "\u5c5e\u6027\u7ba1\u7406\u5668\u53d1\u751f\u9519\u8bef\u3002",
                    ex);
            }
        }

        private void TreeDragEnter(object sender, DragEventArgs e)
        {
            try
            {
                // Retrieve the client coordinates of the mouse position.
                Point targetPoint = Tree.PointToClient(new Point(e.X, e.Y));

                // Select the node at the mouse position.
                Tree.SelectedNode = Tree.GetNodeAt(targetPoint);
                e.Effect = DragDropEffects.Move;
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view DragEnter ", ex);
                ShowPropertyManagerError(
                    "There was a problem with the property manager.",
                    "\u5c5e\u6027\u7ba1\u7406\u5668\u53d1\u751f\u9519\u8bef\u3002",
                    ex);
            }
        }

        private void DoDragDrop(DragEventArgs e)
        {
            // Retrieve the client coordinates of the drop location.
            Point point = Tree.PointToClient(new Point(e.X, e.Y));

            // Retrieve the node at the drop location.
            LinkNode targetNode = (LinkNode)Tree.GetNodeAt(point);

            LinkNode draggedNode = (LinkNode)e.Data.GetData(typeof(LinkNode));

            // Check if the move is valid, if not then we won't do anything
            if (draggedNode == null || draggedNode == targetNode || draggedNode.TreeView != Tree)
            {
                return;
            }

            // If the it was dropped into the box itself, but not onto an actual node
            targetNode = targetNode ?? (LinkNode)Tree.TopNode;

            draggedNode.Remove();
            targetNode.Nodes.Add(draggedNode);
            targetNode.ExpandAll();
            CommitLinkTreeProjection();
        }

        private void TreeDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                DoDragDrop(e);
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view Drag Drop ", ex);
                ShowPropertyManagerError(
                    "There was a problem with the property manager.",
                    "\u5c5e\u6027\u7ba1\u7406\u5668\u53d1\u751f\u9519\u8bef\u3002",
                    ex);
            }
        }

        #endregion TreeView handler methods

        private static void ShowPropertyManagerError(
            string englishMessage,
            string chineseMessage,
            Exception exception = null)
        {
            string detail = exception == null ? "" : "\r\n\"" + exception.Message + "\"";
            string logFileMessage = ChineseUiText.Translate(
                "\r\nEmail your maintainer with the log file found at ",
                "\r\n\u8bf7\u5c06\u4ee5\u4e0b\u65e5\u5fd7\u6587\u4ef6\u53d1\u9001\u7ed9\u7ef4\u62a4\u4eba\u5458\uff1a");
            MessageBox.Show(
                ChineseUiText.Translate(englishMessage, chineseMessage) +
                detail +
                logFileMessage +
                Logger.GetFileName());
        }

        //A method that sets up the Property Manager Page
        private void SetupPropertyManagerPage(ref string caption, ref string tip,
            ref long options, ref int controlType, ref int alignment)
        {
            //Begin adding the controls to the page
            //Create the group box
            caption = ChineseUiText.Translate(
                "Configure and Organize Links",
                "\u914d\u7f6e\u548c\u7ec4\u7ec7 Link");
            options = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;
            PMGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(GroupID, caption, (int)options);

            //Create the parent link label (static)
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = ChineseUiText.Translate("Parent Link", "\u7236 Link");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;

            //Create the parent link name label, the one that is updated
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible + (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelParentLink = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelLinkNameID, (short)controlType, caption, (short)alignment, (int)options, "");

            //Create the link name text box label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = ChineseUiText.Translate("Link Name", "Link \u540d\u79f0");
            tip = ChineseUiText.Translate(
                "Enter the name of the link",
                "\u8f93\u5165 Link \u540d\u79f0");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            
            //Create the link name text box
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            caption = "base_link";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            tip = ChineseUiText.Translate(
                "Enter the name of the link",
                "\u8f93\u5165 Link \u540d\u79f0");
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMTextBoxLinkName = (PropertyManagerPageTextbox)PMGroup.AddControl2(
                TextBoxLinkNameID, (short)(controlType), caption, (short)alignment, (int)options, tip);

            //Create the joint name text box label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = ChineseUiText.Translate("Joint Name", "Joint \u540d\u79f0");
            tip = ChineseUiText.Translate(
                "Enter the name of the joint",
                "\u8f93\u5165 Joint \u540d\u79f0");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMLabelJointName = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelJointNameID, (short)controlType, caption, (short)alignment, (int)options, tip);

            //Create the joint name text box
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            caption = "";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            tip = ChineseUiText.Translate(
                "Enter the name of the joint",
                "\u8f93\u5165 Joint \u540d\u79f0");
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMTextBoxJointName = (PropertyManagerPageTextbox)PMGroup.AddControl2(
                TextBoxJointNameID, (short)(controlType), caption, (short)alignment, (int)options, tip);

            //Create the global origin coordinate sys label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = ChineseUiText.Translate(
                "Global Origin Coordinate System",
                "\u5168\u5c40\u539f\u70b9\u5750\u6807\u7cfb");
            tip = ChineseUiText.Translate(
                "Select the reference coordinate system for the global origin",
                "\u9009\u62e9\u5168\u5c40\u539f\u70b9\u7684\u53c2\u8003\u5750\u6807\u7cfb");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMLabelGlobalCoordsys = (PropertyManagerPageLabel)PMGroup.AddControl2(
                IDLabelGlobalCoordsys, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Create pull down menu for Coordinate systems
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            caption = ChineseUiText.Translate(
                "Global Origin Coordinate System Name",
                "\u5168\u5c40\u539f\u70b9\u5750\u6807\u7cfb\u540d\u79f0");
            tip = ChineseUiText.Translate(
                "Select the reference coordinate system for the global origin",
                "\u9009\u62e9\u5168\u5c40\u539f\u70b9\u7684\u53c2\u8003\u5750\u6807\u7cfb");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMComboBoxGlobalCoordsys = (PropertyManagerPageCombobox)PMGroup.AddControl2(
                IDGlobalCoordsys, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComboBoxGlobalCoordsys.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;

            //Create the ref coordinate sys label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = ChineseUiText.Translate(
                "Reference Coordinate System",
                "\u53c2\u8003\u5750\u6807\u7cfb");
            tip = ChineseUiText.Translate(
                "Select the reference coordinate system for the joint origin",
                "\u9009\u62e9 Joint \u539f\u70b9\u7684\u53c2\u8003\u5750\u6807\u7cfb");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = 0;
            PMLabelCoordSys = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelCoordSysID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Create pull down menu for Coordinate systems
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            caption = ChineseUiText.Translate(
                "Reference Coordinate System Name",
                "\u53c2\u8003\u5750\u6807\u7cfb\u540d\u79f0");
            tip = ChineseUiText.Translate(
                "Select the reference coordinate system for the joint origin",
                "\u9009\u62e9 Joint \u539f\u70b9\u7684\u53c2\u8003\u5750\u6807\u7cfb");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = 0;
            PMComboBoxCoordSys = (PropertyManagerPageCombobox)PMGroup.AddControl2(
                ComboBoxCoordSysID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComboBoxCoordSys.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;

            //Create the ref axis label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = ChineseUiText.Translate("Reference Axis", "\u53c2\u8003\u8f74");
            tip = ChineseUiText.Translate(
                "Select the reference axis for the joint",
                "\u9009\u62e9 Joint \u7684\u53c2\u8003\u8f74");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMLabelAxes = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelAxesID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Create pull down menu for axes
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            caption = ChineseUiText.Translate(
                "Reference Axis Name",
                "\u53c2\u8003\u8f74\u540d\u79f0");
            tip = ChineseUiText.Translate(
                "Select the reference axis for the joint",
                "\u9009\u62e9 Joint \u7684\u53c2\u8003\u8f74");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMComboBoxAxes = (PropertyManagerPageCombobox)PMGroup.AddControl2(
                ComboBoxAxesID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComboBoxAxes.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;

            //Create the joint type label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = ChineseUiText.Translate("Joint Type", "Joint \u7c7b\u578b");
            tip = ChineseUiText.Translate(
                "Select an explicit URDF joint type. Mate detection is only for native movable " +
                    "SolidWorks assemblies; use an explicit type for STEP or fixed assemblies.",
                "\u8bf7\u660e\u786e\u9009\u62e9 URDF Joint \u7c7b\u578b\u3002Mate \u8bc6\u522b\u4ec5\u9002\u7528\u4e8e SolidWorks \u539f\u751f\u53ef\u52a8" +
                    "\u88c5\u914d\uff1bSTEP \u6216\u56fa\u5b9a\u88c5\u914d\u8bf7\u624b\u52a8\u9009\u62e9\u3002");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMLabelJointType = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelJointTypeID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Create pull down menu for joint type
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            caption = ChineseUiText.Translate("Joint type", "Joint \u7c7b\u578b");
            tip = ChineseUiText.Translate(
                "Select an explicit URDF joint type. Mate detection is only for native movable " +
                    "SolidWorks assemblies; use an explicit type for STEP or fixed assemblies.",
                "\u8bf7\u660e\u786e\u9009\u62e9 URDF Joint \u7c7b\u578b\u3002Mate \u8bc6\u522b\u4ec5\u9002\u7528\u4e8e SolidWorks \u539f\u751f\u53ef\u52a8" +
                    "\u88c5\u914d\uff1bSTEP \u6216\u56fa\u5b9a\u88c5\u914d\u8bf7\u624b\u52a8\u9009\u62e9\u3002");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMComboBoxJointType = (PropertyManagerPageCombobox)PMGroup.AddControl2(
                ComboBoxJointTypeID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComboBoxJointType.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            List<string> jointTypeItems = new List<string>
            {
                ChineseUiText.JointTypeDisplay(String.Empty)
            };
            foreach (string jointType in Joint.SelectableTypes)
            {
                jointTypeItems.Add(ChineseUiText.JointTypeDisplay(jointType));
            }
            PMComboBoxJointType.AddItems(jointTypeItems.ToArray());

            //Create the selection box label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = ChineseUiText.Translate("Link Components", "Link \u7ec4\u4ef6");
            tip = ChineseUiText.Translate(
                "Select components associated with this link",
                "\u9009\u62e9\u5c5e\u4e8e\u6b64 Link \u7684\u7ec4\u4ef6");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            
            //Create selection box
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            caption = ChineseUiText.Translate("Link Components", "Link \u7ec4\u4ef6");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible + (int)swAddControlOptions_e.swControlOptions_Enabled;
            tip = ChineseUiText.Translate(
                "Select components associated with this link",
                "\u9009\u62e9\u5c5e\u4e8e\u6b64 Link \u7684\u7ec4\u4ef6");
            PMSelection = (PropertyManagerPageSelectionbox)PMGroup.AddControl2(
                SelectionID, (short)controlType, caption, (short)alignment, (int)options, tip);

            swSelectType_e[] filters = new swSelectType_e[1];
            filters[0] = swSelectType_e.swSelCOMPONENTS;
            object filterObj = null;
            filterObj = filters;

            PMSelection.AllowSelectInMultipleBoxes = true;
            PMSelection.SingleEntityOnly = false;
            PMSelection.AllowMultipleSelectOfSameEntity = false;
            PMSelection.Height = 50;
            PMSelection.SetSelectionFilters(filterObj);

            //Create the number box label
            //Create the link name text box label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = ChineseUiText.Translate(
                "Number of direct child Links",
                "直接子 Link 数量（下一级）");
            tip = ChineseUiText.Translate(
                "Number of Links directly below the current Link; deeper descendants are not counted",
                "当前 Link 下一级的直接子 Link 数量，不包含更深层后代；修改后会自动增减直接子 Link");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelChildCount = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelChildCountID, (short)controlType, caption, (short)alignment, (int)options, tip);
            
            //Create the number box
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Numberbox;
            caption = "";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            tip = ChineseUiText.Translate(
                "Number of Links directly below the current Link; changing it automatically adds or removes direct child Links",
                "当前 Link 下一级的直接子 Link 数量，不包含更深层后代；修改后会自动增减直接子 Link");
            options = (int)swAddControlOptions_e.swControlOptions_Enabled +
                (int)swAddControlOptions_e.swControlOptions_Visible;
            PMNumberBoxChildCount = PMGroup.AddControl2(
                NumBoxChildCountID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMNumberBoxChildCount.SetRange2(
                (int)swNumberboxUnitType_e.swNumberBox_UnitlessInteger, 0, int.MaxValue, true, 1, 1, 1);
            PMNumberBoxChildCount.Value = 0;

            // Link tree canvas button
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            caption = ChineseUiText.Translate(
                "Edit Link Tree...",
                "编辑 Link 树...");
            tip = ChineseUiText.Translate(
                "Edit the Link hierarchy on a transactional canvas",
                "在自由画布中编辑 Link 层级；仅在点击应用后提交");
            alignment = 0;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMButtonEditTree = PMGroup.AddControl2(
                EditLinkTreeID, (short)controlType, caption, (short)alignment, (int)options, tip);
            (PMButtonEditTree as IPropertyManagerPageControl).Width = 200;

            // Loaded CSV Filename label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = ChineseUiText.Translate(
                "Imported File: ",
                "\u5df2\u5bfc\u5165\u6587\u4ef6\uff1a");
            tip = "";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = 0;
            PMLabelCSVFilename = PMGroup.AddControl2(
                LoadedCSVFilenameID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Create Check Boxes to select whether to recompute values
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Checkbox;
            caption = ChineseUiText.Translate(
                "Compute Mass and Inertia",
                "\u8ba1\u7b97\u8d28\u91cf\u548c\u60ef\u6027");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            tip = ChineseUiText.Translate(
                "External values have been loaded. Check this box to recompute the Mass and Inertia values",
                "\u52fe\u9009\u540e\u5c06\u6839\u636e SolidWorks \u6a21\u578b\u91cd\u65b0\u8ba1\u7b97\u8d28\u91cf\u548c\u60ef\u6027");
            options = 0;
            PMComputeMassInertia = PMGroup.AddControl2(
                ComputeMassInertiaID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComputeMassInertia.Checked = true;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Checkbox;
            caption = ChineseUiText.Translate(
                "Compute Visual and Collision",
                "\u8ba1\u7b97\u53ef\u89c6\u548c\u78b0\u649e\u5c5e\u6027");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            tip = ChineseUiText.Translate(
                "External values have been loaded. Check this box to recompute the visual and collision values",
                "\u52fe\u9009\u540e\u91cd\u65b0\u8ba1\u7b97\u53ef\u89c6\u548c\u78b0\u649e\u5c5e\u6027");
            options = 0;
            PMComputeVisualCollision = PMGroup.AddControl2(
                ComputeVisualCollisionID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComputeVisualCollision.Checked = true;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Checkbox;
            caption = ChineseUiText.Translate(
                "Compute Joint Kinematics",
                "\u8ba1\u7b97 Joint \u8fd0\u52a8\u5b66");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            tip = ChineseUiText.Translate(
                "External values have been loaded. Check this box to recompute the joint kinematics",
                "\u52fe\u9009\u540e\u91cd\u65b0\u8ba1\u7b97 Joint \u8fd0\u52a8\u5b66");
            options = 0;
            PMComputeJointKinematics = PMGroup.AddControl2(
                ComputeJointKinematicsID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComputeJointKinematics.Checked = true;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Checkbox;
            caption = ChineseUiText.Translate(
                "Compute Joint Limits",
                "\u8ba1\u7b97 Joint \u9650\u4f4d");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            tip = ChineseUiText.Translate(
                "External values have been loaded. Check this box to recompute the joint limits",
                "\u52fe\u9009\u540e\u91cd\u65b0\u8ba1\u7b97 Joint \u9650\u4f4d");
            options = 0;
            PMComputeJointLimits = PMGroup.AddControl2(
                ComputeJointLimitsID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComputeJointLimits.Checked = true;

            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMButtonExport = PMGroup.AddControl2(ButtonExportID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                ChineseUiText.Translate(
                    "Preview and Export...",
                    "\u9884\u89c8\u5e76\u5bfc\u51fa..."),
                0,
                (int)options,
                ChineseUiText.Translate(
                    "Preview the generated URDF and export to a URDF package",
                    "\u9884\u89c8\u751f\u6210\u7684 URDF \u5e76\u5bfc\u51fa\u529f\u80fd\u5305"));
            (PMButtonExport as IPropertyManagerPageControl).Width = 200;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle;
            caption = ChineseUiText.Translate("Link Tree", "Link \u6811");
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMTree = PMPage.AddControl2(dotNetTree,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle, caption, 0, (int)options, "");
            PMTree.Height = 163;
            Tree = new TreeView
            {
                Height = 163,
                Visible = true
            };

            Tree.AfterSelect += new TreeViewEventHandler(TreeAfterSelect);
            Tree.NodeMouseClick += new TreeNodeMouseClickEventHandler(TreeNodeMouseClick);
            Tree.KeyDown += new KeyEventHandler(TreeKeyDown);
            Tree.DragDrop += new DragEventHandler(TreeDragDrop);
            Tree.DragOver += new DragEventHandler(TreeDragOver);
            Tree.DragEnter += new DragEventHandler(TreeDragEnter);
            Tree.ItemDrag += new ItemDragEventHandler(TreeItemDrag);
            Tree.AllowDrop = true;
            PMTree.SetWindowHandlex64(Tree.Handle.ToInt64());

            ToolStripMenuItem addChild = new ToolStripMenuItem();
            ToolStripMenuItem removeChild = new ToolStripMenuItem();
            //ToolStripMenuItem renameChild = new ToolStripMenuItem();
            addChild.Text = "Add Child Link";
            addChild.Click += new EventHandler(AddChildClick);

            removeChild.Text = "Remove";
            removeChild.Click += new EventHandler(RemoveChildClick);
            //renameChild.Text = "Rename";
            //renameChild.Click += new System.EventHandler(this.renameChild_Click);
            //docMenu.Items.AddRange(new ToolStripMenuItem[] { addChild, removeChild, renameChild });
            docMenu.Items.AddRange(new ToolStripMenuItem[] { addChild, removeChild });
            LinkNode node = CreateEmptyNode(null);
            node.ContextMenuStrip = docMenu;
            Tree.Nodes.Add(node);
            Tree.SelectedNode = Tree.Nodes[0];
            linkTreeSession = new LinkTreeSession(node);
            PMSelection.SetSelectionFocus();
            PMPage.SetFocus(dotNetTree);
        }

        #region Not implemented handler methods

        // These methods are still active. The exceptions that are thrown only cause the debugger
        // to pause. Comment out the exception if you choose not to implement it, but it gets
        // regularly called anyway
        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked)
        {
            logger.Info("OnCheckboxCheck called. This method no longer throws an Exception. " +
                " It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnComboboxEditChanged(int Id, string Text)
        {
            logger.Info("OnComboboxEditChanged called. This method no longer throws an Exception." +
                " It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item)
        {
            UpdateSelectedNodeComboValue(Id, Item);
        }

        void IPropertyManagerPage2Handler9.OnGroupCheck(int Id, bool Checked)
        {
            logger.Info("OnGroupCheck called. This method no longer throws an Exception. It just " +
                "silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnGroupExpand(int Id, bool Expanded)
        {
            logger.Info("OnGroupExpand called. This method no longer throws an Exception. It just " +
                "silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnListboxSelectionChanged(int Id, int Item)
        {
            logger.Info("OnListboxSelectionChanged called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
        }

        bool IPropertyManagerPage2Handler9.OnNextPage()
        {
            logger.Info("OnNextPage called. This method no longer throws an Exception. It just " + "" +
                "silently does nothing. Ok, except for this logging message");
            return true;
        }

        void IPropertyManagerPage2Handler9.OnOptionCheck(int Id)
        {
            logger.Info("OnOptionCheck called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnPopupMenuItem(int Id)
        {
            logger.Info("OnPopupMenuItem called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnPopupMenuItemUpdate(int Id, ref int retval)
        {
            logger.Info("OnPopupMenuItemUpdate called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
        }

        bool IPropertyManagerPage2Handler9.OnPreview()
        {
            logger.Info("OnPreview called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
            return true;
        }

        bool IPropertyManagerPage2Handler9.OnPreviousPage()
        {
            logger.Info("OnPreviousPage called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
            return true;
        }

        void IPropertyManagerPage2Handler9.OnRedo()
        {
            logger.Info("OnRedo called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutCreated(int Id)
        {
            logger.Info("OnSelectionboxCalloutCreated called. This method no longer throws " +
                " an Exception. It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutDestroyed(int Id)
        {
            logger.Info("OnSelectionboxCalloutDestroyed called. This method no longer throws " +
                "an Exception. It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnSliderPositionChanged(int Id, double Value)
        {
            logger.Info("OnSliderPositionChanged called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnSliderTrackingCompleted(int Id, double Value)
        {
            logger.Info("OnSliderTrackingCompleted called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
        }

        bool IPropertyManagerPage2Handler9.OnTabClicked(int Id)
        {
            logger.Info("OnTabClicked called. This method no longer throws an Exception. It " +
                " just silently does nothing. Ok, except for this logging message");
            return true;
        }

        void IPropertyManagerPage2Handler9.OnUndo()
        {
            logger.Info("OnUndo called. This method no longer throws an Exception. It just " +
                "silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnWhatsNew()
        {
            logger.Info("OnWhatsNew called. This method no longer throws an Exception. It just " +
                " silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnListboxRMBUp(int Id, int PosX, int PosY)
        {
            logger.Info("OnListboxRMBUp called. This method no longer throws an Exception. It " +
                " just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnNumberBoxTrackingCompleted(int Id, double Value)
        {
            logger.Info("OnNumberBoxTrackingCompleted called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.AfterClose()
        {
            try
            {
                CompletePropertyManagerClose();
            }
            catch (Exception exception)
            {
                logger.Error("Exception caught after closing the property manager.", exception);
            }
            finally
            {
                NotifyClosed();
            }
        }

        private void NotifyClosed()
        {
            if (!closeCoordinator.TryClaimClosedNotification())
            {
                return;
            }

            EventHandler closed = Closed;
            if (closed == null)
            {
                return;
            }

            foreach (EventHandler handler in closed.GetInvocationList())
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception exception)
                {
                    logger.Warn("A PropertyManager close subscriber failed.", exception);
                }
            }
        }

        int IPropertyManagerPage2Handler9.OnActiveXControlCreated(int Id, bool Status)
        {
            logger.Info("OnActiveXControlCreated called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
            return 0;
        }

        #endregion Not implemented handler methods
    }
}
