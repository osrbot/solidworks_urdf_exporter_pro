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
using SW2URDF.UI.LinkTreeCanvas;
using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SW2URDF.URDFExport
{
    public partial class ExportPropertyManager : PropertyManagerPage2Handler9
    {
        private List<CadFeatureReference> pmGlobalFrameReferences =
            new List<CadFeatureReference>();
        private List<CadFeatureReference> pmLinkFrameReferences =
            new List<CadFeatureReference>();
        private List<CadFeatureReference> pmAxisReferences =
            new List<CadFeatureReference>();

        private void OpenLinkTreeCanvas()
        {
            if (Tree == null || Tree.Nodes.Count == 0)
            {
                MessageBox.Show("Link tree is empty.");
                return;
            }

            SaveActiveNode();
            Link selectedLink = previouslySelectedNode == null ? null : previouslySelectedNode.Link;
            CommitLinkTreeProjection();
            Guid? selectedNodeId = linkTreeSession.GetProjectionNodeId(selectedLink);

            try
            {
                PendingCanvasEdit pending = new PendingCanvasEdit(linkTreeSession);
                LinkTreeCanvasWindow window = new LinkTreeCanvasWindow(pending);
                bool? accepted = window.ShowDialog();
                if (accepted != true || pending.Document == null)
                {
                    return;
                }

                using (treeSelectionUpdateGuard.Suppress())
                {
                    linkTreeSession.EditTree(null, document =>
                    {
                        document.Nodes.Clear();
                        document.Nodes.AddRange(pending.Document.Clone().Nodes);
                    }, publish: candidate =>
                    {
                        SaveExportSessionDraft(candidate.CreateProjection());
                        try
                        {
                            PublishLinkTree(candidate, selectedNodeId);
                        }
                        catch
                        {
                            SaveExportSessionDraft(linkTreeSession.CreateProjection());
                            throw;
                        }
                    });
                }

                if (linkTreeSession.RequiresJointKinematicsRecompute)
                {
                    PMComputeJointKinematics.Checked = true;
                    logger.Info("Joint topology changed; joint kinematics recomputation was enabled.");
                }
                if (linkTreeSession.RequiresJointLimitsRecompute)
                {
                    PMComputeJointLimits.Checked = true;
                    logger.Info("Joint configuration changed; joint limit recomputation was enabled.");
                }
            }
            catch (Exception exception)
            {
                logger.Error("Failed to edit the Link tree on the canvas.", exception);
                MessageBox.Show(
                    "The Link tree editor could not be opened:\r\n" + exception.Message,
                    "SW2URDF");
            }
        }

        private sealed class PendingCanvasEdit : ILinkTreeCanvasHost, ILinkTreeCandidateValidator
        {
            private readonly LinkTreeSession source;

            public PendingCanvasEdit(LinkTreeSession source) { this.source = source; }

            public LinkTreeDocument Document { get; private set; }
            public LinkTreeDocument LoadTree() { return source.LoadTree(); }
            public void ValidateTree(LinkTreeDocument document) { source.ValidateTree(document); }
            public void ApplyTree(LinkTreeDocument document)
            {
                ValidateTree(document);
                Document = document.Clone();
            }
        }

        private void ReplaceLinkTreeRoot(LinkNode root, Guid? selectedNodeId = null)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }
            LinkTreeRootJointPolicy.Normalize(root);
            linkTreeSession = new LinkTreeSession(root);
            RefreshLinkTreeProjection(selectedNodeId);
        }

        private void CommitLinkTreeProjection()
        {
            if (Tree == null || Tree.Nodes.Count == 0)
            {
                return;
            }
            LinkNode root = (LinkNode)Tree.Nodes[0];
            if (linkTreeSession == null)
            {
                linkTreeSession = new LinkTreeSession(root);
            }
            else
            {
                linkTreeSession.CaptureTree(root);
            }
        }

        private void RefreshLinkTreeProjection(Guid? selectedNodeId = null)
        {
            if (linkTreeSession == null)
            {
                return;
            }

            LinkNode root = linkTreeSession.CreateActiveProjection();
            AddDocMenu(root);
            LinkNode selectedNode = FindNodeById(root, selectedNodeId) ?? root;
            using (treeSelectionUpdateGuard.Suppress())
            {
                previouslySelectedNode = null;
                previouslySelectedLink = null;
                rightClickedNode = null;
                Tree.Nodes.Clear();
                Tree.Nodes.Add(root);
                Tree.ExpandAll();
                Tree.SelectedNode = selectedNode;
            }
            SwitchActiveNodes(selectedNode);
            selectedNode.EnsureVisible();
        }

        private bool EditLinkTree(LinkNode node, Action<LinkTreeDocument, Guid> edit,
            bool confirmRemoval = false)
        {
            if (treeSelectionUpdateGuard.IsSuppressed || node == null || node.TreeView != Tree)
                return false;
            SaveActiveNode();
            if (linkTreeSession == null) CommitLinkTreeProjection();
            Guid? id = linkTreeSession.GetProjectionNodeId(node.Link);
            if (!id.HasValue) throw new InvalidOperationException("The selected Link is stale; reopen the tree.");
            using (treeSelectionUpdateGuard.Suppress())
            {
                try
                {
                    return linkTreeSession.EditTree((LinkNode)Tree.Nodes[0],
                        document => edit(document, id.Value),
                        confirmRemoval ? (Func<LinkTreeDocument, bool>)(document => MessageBox.Show(
                            "Remove the selected Links and their entire branches?", "SW2URDF",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) : null,
                        candidate => PublishLinkTree(candidate, id));
                }
                finally
                {
                    LinkNode selected = Tree.SelectedNode as LinkNode;
                    if (selected != null) PMNumberBoxChildCount.Value = selected.Nodes.Count;
                }
            }
        }

        private void PublishLinkTree(LinkTreeSession candidate, Guid? selectedId)
        {
            LinkTreeSession originalSession = linkTreeSession;
            LinkNode originalRoot = (LinkNode)Tree.Nodes[0];
            LinkNode originalSelected = Tree.SelectedNode as LinkNode;
            LinkNode originalActive = previouslySelectedNode;
            Link originalLink = previouslySelectedLink;
            LinkNode originalRightClick = rightClickedNode;
            int originalHeight = Tree.Height;
            try
            {
                linkTreeSession = candidate;
                RefreshLinkTreeProjection(selectedId);
                int height = MathOps.Envelope(1 + CommonSwOperations.GetCount(Tree.Nodes) * Tree.ItemHeight, 163, 600);
                Tree.Height = height;
                PMTree.Height = height;
            }
            catch
            {
                Tree.Nodes.Clear();
                Tree.Nodes.Add(originalRoot);
                Tree.SelectedNode = originalSelected;
                previouslySelectedNode = originalActive;
                previouslySelectedLink = originalLink;
                rightClickedNode = originalRightClick;
                Tree.Height = originalHeight;
                if (originalActive != null) FillPropertyManager(originalActive);
                throw;
            }
            finally
            {
                linkTreeSession = originalSession;
            }
        }

        private LinkNode FindNodeById(LinkNode node, Guid? nodeId)
        {
            if (nodeId.HasValue && linkTreeSession.GetProjectionNodeId(node.Link) == nodeId)
            {
                return node;
            }
            foreach (LinkNode child in node.Nodes)
            {
                LinkNode match = FindNodeById(child, nodeId);
                if (match != null)
                {
                    return match;
                }
            }
            return null;
        }

        public static readonly double ConfigurationVersion = 1.3;
        public static readonly double SoapMinVersion = 1.3;

        public bool SaveConfigTree(ModelDoc2 model, LinkNode BaseNode, bool warnUser)
        {
            CommonSwOperations.RetrieveSWComponentPIDs(model, BaseNode);
            bool saved = ConfigurationSaveInteraction.Save(
                allowOverwrite => ConfigurationSerialization.SaveConfigTreeXML(
                    swApp,
                    model,
                    BaseNode,
                    allowOverwrite),
                warnUser, out bool persisted);
            if (persisted)
            {
                ClearExportSessionDraft();
            }
            return saved;
        }

        private void SaveExportSessionDraft(LinkNode root)
        {
            try
            {
                if (root == null || String.IsNullOrWhiteSpace(activeModelPath))
                {
                    return;
                }
                if (exportSessionDraftStore.Save(
                    activeModelPath,
                    root,
                    Exporter.RosPackageName,
                    Exporter.SavePath))
                {
                    logger.Info("Saved the URDF export recovery draft for the active assembly.");
                }
            }
            catch (Exception exception)
            {
                logger.Warn("The URDF export recovery draft could not be captured.", exception);
            }
        }

        private void ClearExportSessionDraft()
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(activeModelPath))
                {
                    if (!exportSessionDraftStore.Delete(activeModelPath))
                    {
                        logger.Warn(
                            "The saved URDF configuration is valid, but its older recovery draft could not be cleared. " +
                            "The stale draft will be ignored when the exporter opens again.");
                    }
                }
            }
            catch (Exception exception)
            {
                logger.Warn("The URDF export recovery draft could not be cleared.", exception);
            }
        }

        //As nodes are created and destroyed, this menu gets called a lot. It basically just
        // adds the context menu (right-click menu) to the node
        public void AddDocMenu(LinkNode node)
        {
            node.ContextMenuStrip = docMenu;
            foreach (LinkNode child in node.Nodes)
            {
                AddDocMenu(child);
            }
        }

        private List<CadFeatureReference> FillReferenceComboBox(
            PropertyManagerPageCombobox box,
            List<ReferenceGeometryEntry> entries,
            CadFeatureReference selectedReference,
            ReferenceGeometryKind kind,
            bool includeAutomatic,
            bool includeNone)
        {
            box.Clear();
            List<CadFeatureReference> references = new List<CadFeatureReference>();
            if (includeAutomatic)
            {
                box.AddItems(ChineseUiText.Translate("Automatically generate", "自动生成"));
                references.Add(CadFeatureReference.Automatic(kind));
            }

            bool selectedReferenceAvailable = false;
            foreach (ReferenceGeometryEntry entry in entries)
            {
                box.AddItems(entry.DisplayLabel);
                references.Add(entry.Reference);
                if (selectedReference != null && entry.Reference.Equals(selectedReference))
                {
                    selectedReferenceAvailable = true;
                }
            }
            if (selectedReference != null &&
                selectedReference.IsExplicit &&
                !selectedReferenceAvailable)
            {
                box.AddItems(ChineseUiText.Translate("Unavailable reference", "引用不可用"));
                references.Add(selectedReference.Clone());
            }
            if (includeNone)
            {
                box.AddItems(ChineseUiText.Translate("None", "无"));
                references.Add(CadFeatureReference.None(kind));
            }
            return references;
        }

        private static void SelectReferenceComboBox(
            PropertyManagerPageCombobox box,
            IList<CadFeatureReference> references,
            CadFeatureReference selectedReference)
        {
            box.CurrentSelection = -1;
            if (selectedReference == null)
            {
                if (references.Count > 0)
                {
                    box.CurrentSelection = 0;
                }
                return;
            }
            for (short index = 0; index < references.Count; index++)
            {
                if (references[index].Equals(selectedReference))
                {
                    box.CurrentSelection = index;
                    return;
                }
            }
            if (references.Count > 0)
            {
                box.CurrentSelection = 0;
            }
        }

        private static CadFeatureReference ReadReferenceComboBox(
            PropertyManagerPageCombobox box,
            IList<CadFeatureReference> references,
            CadFeatureReference fallback,
            ReferenceGeometryKind kind)
        {
            int index = box.CurrentSelection;
            if (index >= 0 && index < references.Count)
            {
                return references[index].Clone();
            }
            return fallback == null
                ? CadFeatureReference.Automatic(kind)
                : fallback.Clone();
        }

        // Finds the specified item in a combobox and sets the box to it. I'm not sure why I
        // couldn't do this with a foreach loop or even a for loop, but there is no way to get
        // the current number of items in the menu
        private void SelectComboBox(PropertyManagerPageCombobox box, string item)
        {
            short i = 0;
            string itemtext = "nothing";
            box.CurrentSelection = 0;

            // Cycles through the menu items until it finds what its looking for, it finds
            // blank strings, or itemtext is null
            while (!string.IsNullOrWhiteSpace(itemtext) && itemtext != item)
            {
                // Gets the item text at index in a pull-down menu. No way to now how many
                // items are in the combobox
                itemtext = box.get_ItemText(i);
                if (itemtext == item)
                {
                    box.CurrentSelection = i;
                }
                i++;
            }
        }

        // Adds an asterix to the node text if it is incomplete (not currently used)
        private void UpdateNodeNames(LinkNode node)
        {
            if (node.IsIncomplete)
            {
                node.Text = node.Link.Name + "*";
            }
            foreach (LinkNode child in node.Nodes)
            {
                UpdateNodeNames(child);
            }
        }

        // Determines how many nodes need to be built, and they are added to the current node
        private void CreateNewNodes(LinkNode CurrentlySelectedNode)
        {
            if (CurrentlySelectedNode == null || treeSelectionUpdateGuard.IsSuppressed) return;
            int nodesToBuild = (int)PMNumberBoxChildCount.Value - CurrentlySelectedNode.Nodes.Count;
            CreateNewNodes(CurrentlySelectedNode, nodesToBuild);
        }

        // Adds the number of empty nodes to the currently active node
        private void CreateNewNodes(LinkNode currentNode, int number)
        {
            if (number == 0 || currentNode == null) return;
            int count = currentNode.Nodes.Count + number;
            EditLinkTree(currentNode, (document, id) => document.SetChildCount(id, count), number < 0);
        }

        // When a new node is selected or another node is found that needs to be visited, this
        // method saves the previously active node and fills in the property mananger with the new one
        public void SwitchActiveNodes(LinkNode node)
        {
            SaveActiveNode();

            Font fontRegular = new Font(Tree.Font, FontStyle.Regular);
            Font fontBold = new Font(Tree.Font, FontStyle.Bold);
            if (previouslySelectedNode != null)
            {
                previouslySelectedNode.NodeFont = fontRegular;
            }
            using (treeSelectionUpdateGuard.Suppress())
            {
                // Programmatic combo-box changes raise the same callbacks as user edits. Keep
                // those callbacks from writing child values into the root Link (and vice versa).
                Tree.SelectedNode = node;
                FillPropertyManager(node);
            }

            node.NodeFont = fontBold;
            node.Text = node.Text;
            previouslySelectedNode = node;
            CheckNodeComplete(node);
        }

        // This method runs through first the child nodes of the selected node to see if there are
        // more to visit then it runs through the nodes top to bottom to find the next to visit.
        // Returns the node if one is found otherwise it returns null.
        public LinkNode FindNextLinkToVisit(TreeView tree)
        {
            // First check if SelectedNode has any nodes to visit
            if (tree.SelectedNode != null)
            {
                LinkNode nodeToReturn = FindNextLinkToVisit((LinkNode)tree.SelectedNode);
                if (nodeToReturn != null)
                {
                    return nodeToReturn;
                }
            }

            // Now run through tree to see if any other nodes need to be visited
            return FindNextLinkToVisit((LinkNode)tree.Nodes[0]);
        }

        // Finds the next incomplete node and returns that
        public static LinkNode FindNextLinkToVisit(LinkNode nodeToCheck)
        {
            if (nodeToCheck.IsIncomplete)
            {
                return nodeToCheck;
            }
            foreach (LinkNode node in nodeToCheck.Nodes)
            {
                LinkNode incomplete = FindNextLinkToVisit(node);
                if (incomplete != null)
                {
                    return incomplete;
                }
            }
            return null;
        }

        private void CheckNodeInertialComplete(LinkNode node)
        {
            if (node.Nodes.Count > 0 && node.Link.SWComponents.Count == 0)
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        Links with children cannot be empty. Select its associated components\r\n";
            }
        }

        private void CheckNodeVisualComplete(LinkNode node)
        {
            if (node.Nodes.Count > 0 && node.Link.SWComponents.Count == 0)
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        Links with children cannot be empty. Select its associated components\r\n";
            }
        }

        private void CheckNodeJointComplete(LinkNode node)
        {
            if (node.Link.SWComponents.Count == 0 &&
                node.Link.FrameReference != null &&
                node.Link.FrameReference.Mode == ReferenceSelectionMode.Automatic)
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        The origin reference coordinate system cannot be automatically generated\r\n" +
                    "        without components. Either select an origin or at least one component.\r\n";
            }

            if (node.Link.SWComponents.Count == 0 &&
                node.Link.Joint.AxisReference != null &&
                node.Link.Joint.AxisReference.Mode == ReferenceSelectionMode.Automatic)
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        The reference axis cannot be automatically generated\r\n" +
                    "        without components. Either select an axis or at least one component.\r\n";
            }

            if (String.IsNullOrWhiteSpace(node.Link.Joint.Type))
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        Joint type is required. Select an explicit URDF joint type, or use\r\n" +
                    "        SolidWorks Mate detection only for a native movable assembly.";
            }
            else if (node.Link.SWComponents.Count == 0 &&
                Joint.IsAutomaticType(node.Link.Joint.Type))
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        SolidWorks Mate detection requires components from a native movable\r\n" +
                    "        assembly. Select an explicit type for STEP or fixed assemblies.";
            }
            else if (!Joint.IsAutomaticType(node.Link.Joint.Type) &&
                !Joint.AvailableTypes.Contains(node.Link.Joint.Type))
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        The joint type is not supported. Select a standard URDF joint type.";
            }
        }

        //Sets the node's isIncomplete flag if the node has key items that need to be completed
        public void CheckNodeComplete(LinkNode node)
        {
            node.WhyIncomplete = "";
            node.IsIncomplete = false;
            if (String.IsNullOrWhiteSpace(node.Link.Name))
            {
                node.IsIncomplete = true;
                node.WhyIncomplete += "        Link name is empty. Fill in a unique link name\r\n";
            }
            if (String.IsNullOrWhiteSpace(node.Link.Joint.Name) && !node.IsBaseNode)
            {
                node.IsIncomplete = true;
                node.WhyIncomplete += "        Joint name is empty. Fill in a unique joint name\r\n";
            }

            CheckNodeInertialComplete(node);
            CheckNodeVisualComplete(node);
            if (!node.IsBaseNode)
            {
                CheckNodeJointComplete(node);
            }
            node.Link.isIncomplete = node.IsIncomplete;
        }

        private void CheckModelDocsExist(LinkNode node, List<string> problemComponents)
        {
            foreach (Component2 component in node.Link.SWComponents)
            {
                ModelDoc2 doc = component.GetModelDoc2();
                if (doc == null)
                {
                    problemComponents.Add(component.Name2);
                }
            }

            foreach (LinkNode child in node.Nodes)
            {
                CheckModelDocsExist(child, problemComponents);
            }
        }

        //Recursive function to iterate though nodes and build a message containing those that are incomplete
        public string CheckNodesComplete(LinkNode node, string incompleteNodes)
        {
            // Determine if the node is incomplete
            CheckNodeComplete(node);
            if (node.IsIncomplete)
            {
                //Building the message
                incompleteNodes += "    '" + node.Text + "':\r\n" + node.WhyIncomplete + "\r\n\r\n";
            }
            // Cycle through the rest of the nodes
            foreach (LinkNode child in node.Nodes)
            {
                incompleteNodes = CheckNodesComplete(child, incompleteNodes);
            }
            return incompleteNodes;
        }

        //Finds all the nodes in a TreeView that need to be completed before exporting
        public bool CheckNodesComplete(TreeView tree)
        {
            //Calls the recursive function starting with the base_link node and retrieves a string
            // identifying the incomplete nodes
            string incompleteNodes = CheckNodesComplete((LinkNode)tree.Nodes[0], "");
            if (!String.IsNullOrWhiteSpace(incompleteNodes))
            {
                MessageBox.Show(
                    "The following nodes are incomplete. You need to fix them before continuing.\r\n\r\n" + incompleteNodes);
                return false;
            }
            return true;
        }

        // When the selected node is changed, the previously active node needs to be saved
        public void SaveActiveNode()
        {
            if (treeSelectionUpdateGuard.IsSuppressed || previouslySelectedNode == null)
            {
                return;
            }

            SaveActiveNodeFields(previouslySelectedNode);
            UpdateNodeCadBindings(previouslySelectedNode);
        }

        private void SaveActiveNodeFields(LinkNode node)
        {
            node.Link.Name = PMTextBoxLinkName.Text;
            node.Name = node.Link.Name;
            node.Text = node.Link.Name;
            if (!node.IsBaseNode)
            {
                node.Link.Joint.Name = PMTextBoxJointName.Text;
                node.Link.Joint.AxisReference = ReadReferenceComboBox(
                    PMComboBoxAxes,
                    pmAxisReferences,
                    node.Link.Joint.AxisReference,
                    ReferenceGeometryKind.Axis);
                node.Link.FrameReference = ReadReferenceComboBox(
                    PMComboBoxCoordSys,
                    pmLinkFrameReferences,
                    node.Link.FrameReference,
                    ReferenceGeometryKind.CoordinateSystem);
                node.Link.Joint.Type = ChineseUiText.JointTypeValue(
                    PMComboBoxJointType.get_ItemText(-1));
                return;
            }

            node.Link.FrameReference = ReadReferenceComboBox(
                PMComboBoxGlobalCoordsys,
                pmGlobalFrameReferences,
                node.Link.FrameReference,
                ReferenceGeometryKind.CoordinateSystem);
        }

        private void UpdateSelectedNodeComboValue(int controlId, int item)
        {
            if (treeSelectionUpdateGuard.IsSuppressed)
            {
                return;
            }

            LinkNode node = Tree == null ? null : Tree.SelectedNode as LinkNode;
            if (node == null || item < 0)
            {
                return;
            }

            try
            {
                if (controlId == IDGlobalCoordsys && node.IsBaseNode)
                {
                    node.Link.FrameReference = pmGlobalFrameReferences[item].Clone();
                }
                else if (!node.IsBaseNode && controlId == ComboBoxCoordSysID)
                {
                    node.Link.FrameReference = pmLinkFrameReferences[item].Clone();
                }
                else if (!node.IsBaseNode && controlId == ComboBoxAxesID)
                {
                    node.Link.Joint.AxisReference = pmAxisReferences[item].Clone();
                }
                else if (!node.IsBaseNode && controlId == ComboBoxJointTypeID)
                {
                    node.Link.Joint.Type = ChineseUiText.JointTypeValue(
                        PMComboBoxJointType.get_ItemText((short)item));
                }
            }
            catch (Exception exception)
            {
                logger.Warn("The selected Link field could not be updated.", exception);
            }
        }

        private void UpdateSelectedNodeCadBindings()
        {
            if (treeSelectionUpdateGuard.IsSuppressed) return;
            LinkNode selectedNode = Tree == null ? null : Tree.SelectedNode as LinkNode;
            if (selectedNode == null ||
                !ReferenceEquals(selectedNode, previouslySelectedNode))
            {
                return;
            }

            UpdateNodeCadBindings(selectedNode);
        }

        private void UpdateNodeCadBindings(LinkNode node)
        {
            try
            {
                CommonSwOperations.GetSelectedComponents(
                    ActiveSWModel, node.Link.SWComponents, PMSelection.Mark);
                CommonSwOperations.RetrieveSWComponentPIDs(ActiveSWModel, node.Link);
            }
            catch (Exception exception)
            {
                logger.Warn("The selected Link CAD bindings could not be updated.", exception);
            }
        }

        //Creates an Empty node when children are added to a link
        public LinkNode CreateEmptyNode(LinkNode Parent)
        {
            LinkNode node = new LinkNode();

            if (Parent == null)             //For the base_link node
            {
                node.Link.Name = "base_link";
                node.Link.FrameReference = CadFeatureReference.Automatic(
                    ReferenceGeometryKind.CoordinateSystem);
                node.Link.Joint.AxisReference = CadFeatureReference.None(
                    ReferenceGeometryKind.Axis);
                node.Link.SWComponents = new List<Component2>();
                node.IsBaseNode = true;
                node.IsIncomplete = true;
            }
            else
            {
                node.IsBaseNode = false;
                LinkNode root = Parent;
                while (root.Parent != null) root = (LinkNode)root.Parent;
                LinkTreeDocument names = new LinkTreeSession(root).LoadTree();
                node.Link.Name = LinkTreeDocument.UniqueName("new_link", names.Nodes.Select(item => item.Name));
                node.Link.Joint.Name = LinkTreeDocument.UniqueName(
                    LinkTreeDocument.BuildDefaultJointName(node.Link.Name), names.Nodes.Select(item => item.JointName));
                node.Link.FrameReference = CadFeatureReference.Automatic(
                    ReferenceGeometryKind.CoordinateSystem);
                node.Link.Joint.AxisReference = CadFeatureReference.Automatic(
                    ReferenceGeometryKind.Axis);
                node.Link.Joint.Type = String.Empty;
                node.Link.SWComponents = new List<Component2>();
                node.IsBaseNode = false;
                node.IsIncomplete = true;
            }
            node.Name = node.Link.Name;
            node.Text = node.Link.Name;
            node.ContextMenuStrip = docMenu;
            return node;
        }

        //Sets all the controls in the Property Manager from the Selected Node
        public void FillPropertyManager(LinkNode node)
        {
            using (treeSelectionUpdateGuard.Suppress()) FillPropertyManagerFields(node);
        }

        private void FillPropertyManagerFields(LinkNode node)
        {
            PMTextBoxLinkName.Text = node.Link.Name;
            PMNumberBoxChildCount.Value = node.Nodes.Count;

            //Selecting the associated link components
            CommonSwOperations.SelectComponents(ActiveSWModel, node.Link.SWComponents, true, PMSelection.Mark);

            //Setting joint properties
            if (!node.IsBaseNode && node.Parent != null)
            {
                //Combobox needs to be blanked before de-activating
                SelectComboBox(PMComboBoxGlobalCoordsys, "");

                //Labels need to be activated before changing them
                EnableControls(!node.IsBaseNode);
                PMTextBoxJointName.Text = node.Link.Joint.Name;
                PMLabelParentLink.Caption = node.Parent.Name;

                pmLinkFrameReferences = FillReferenceComboBox(
                    PMComboBoxCoordSys,
                    Exporter.GetRefCoordinateSystems(),
                    node.Link.FrameReference,
                    ReferenceGeometryKind.CoordinateSystem,
                    true,
                    false);
                pmAxisReferences = FillReferenceComboBox(
                    PMComboBoxAxes,
                    Exporter.GetRefAxes(),
                    node.Link.Joint.AxisReference,
                    ReferenceGeometryKind.Axis,
                    JointConfigurationPolicy.RequiresMotionAxis(node.Link.Joint.Type),
                    true);

                SelectReferenceComboBox(
                    PMComboBoxCoordSys,
                    pmLinkFrameReferences,
                    node.Link.FrameReference);
                SelectReferenceComboBox(
                    PMComboBoxAxes,
                    pmAxisReferences,
                    node.Link.Joint.AxisReference);
                SelectComboBox(
                    PMComboBoxJointType,
                    ChineseUiText.JointTypeDisplay(node.Link.Joint.Type));
            }
            else
            {
                //Labels and text box have be blanked before de-activating them
                PMLabelParentLink.Caption = " ";
                SelectComboBox(PMComboBoxCoordSys, "");
                SelectComboBox(PMComboBoxAxes, "");
                SelectComboBox(PMComboBoxJointType, "");
                pmLinkFrameReferences.Clear();
                pmAxisReferences.Clear();

                //Activate controls before changing them
                EnableControls(!node.IsBaseNode);
                pmGlobalFrameReferences = FillReferenceComboBox(
                    PMComboBoxGlobalCoordsys,
                    Exporter.GetRefCoordinateSystems(),
                    node.Link.FrameReference,
                    ReferenceGeometryKind.CoordinateSystem,
                    true,
                    false);
                SelectReferenceComboBox(
                    PMComboBoxGlobalCoordsys,
                    pmGlobalFrameReferences,
                    node.Link.FrameReference);
            }
        }

        //Takes care of activating/deactivating the drop down menus, lables and text box for
        // joint configuration. Generally these are deactivated for the base node
        private void EnableControls(bool enableJoints)
        {
            PropertyManagerPageControl[] pmJointControls =
                new PropertyManagerPageControl[] { (PropertyManagerPageControl)PMTextBoxJointName,
                                                    (PropertyManagerPageControl)PMLabelJointName,
                                                    (PropertyManagerPageControl)PMComboBoxCoordSys,
                                                    (PropertyManagerPageControl)PMLabelCoordSys,
                                                    (PropertyManagerPageControl)PMComboBoxAxes,
                                                    (PropertyManagerPageControl)PMLabelAxes,
                                                    (PropertyManagerPageControl)PMComboBoxJointType,
                                                    (PropertyManagerPageControl)PMLabelJointType };

            PropertyManagerPageControl[] pmGlobalOriginControls = new PropertyManagerPageControl[] {
                (PropertyManagerPageControl)PMComboBoxGlobalCoordsys,
                (PropertyManagerPageControl)PMLabelGlobalCoordsys};

            PropertyManagerPageControl[] pmJointOriginControls = new PropertyManagerPageControl[] {
                (PropertyManagerPageControl)PMComboBoxCoordSys,
                (PropertyManagerPageControl)PMLabelCoordSys};

            foreach (PropertyManagerPageControl control in pmGlobalOriginControls)
            {
                // Make the global origin controls visible when no joint controls are needed
                control.Visible = !enableJoints;
                control.Enabled = !enableJoints;
            }
            foreach (PropertyManagerPageControl control in pmJointOriginControls)
            {
                control.Visible = enableJoints;
                control.Enabled = enableJoints;
            }
            foreach (PropertyManagerPageControl control in pmJointControls)
            {
                control.Enabled = enableJoints;
                control.Visible = enableJoints;
            }
        }

        //Populates the TreeView with the organized links from the robot
        public void FillTreeViewFromRobot(Robot robot)
        {
            LinkNode baseNode = new LinkNode();
            Link baseLink = robot.BaseLink;
            baseNode.Name = baseLink.Name;
            baseNode.Text = baseLink.Name;
            baseNode.Link = baseLink;
            baseNode.IsBaseNode = true;
            baseNode.ContextMenuStrip = docMenu;
            LinkTreeRootJointPolicy.Normalize(baseNode);

            foreach (Link child in baseLink.Children)
            {
                baseNode.Nodes.Add(CreateLinkNodeFromLink(child));
            }
            ReplaceLinkTreeRoot(baseNode);
        }

        // Similar to the AssemblyExportForm method. It creates a LinkNode from a Link object
        public LinkNode CreateLinkNodeFromLink(Link Link)
        {
            LinkNode node = new LinkNode();
            node.Name = Link.Name;
            node.Text = Link.Name;
            node.Link = Link;
            node.IsBaseNode = false;
            node.ContextMenuStrip = docMenu;

            foreach (Link child in Link.Children)
            {
                node.Nodes.Add(CreateLinkNodeFromLink(child));
            }

            // Need to erase the children from the embedded link because they may be rearranged later.
            node.Link.Children.Clear();
            return node;
        }

        /// <summary>
        /// Loads configuration tree into PM Page. If an error occurs, this will do nothing
        /// </summary>
        /// <returns>bool representing success of load. If false, PMPage should not open</returns>
        public bool LoadConfigTree()
        {
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(
                ActiveSWModel,
                out string errorMessage);

            bool restoredDraft = exportSessionDraftStore.TryLoad(
                activeModelPath,
                out ExportSessionDraft draft);
            if (restoredDraft &&
                baseNode != null &&
                string.IsNullOrWhiteSpace(errorMessage) &&
                ConfigurationSerialization.TryGetSavedConfigurationUtc(
                    ActiveSWModel,
                    out DateTime configurationSavedUtc) &&
                !FileExportSessionDraftStore.IsDraftNewerThanConfiguration(
                    draft.SavedUtc,
                    configurationSavedUtc))
            {
                restoredDraft = false;
                logger.Info(
                    "Ignored a recovery draft because the saved URDF configuration is newer.");
                if (!exportSessionDraftStore.Delete(activeModelPath))
                {
                    logger.Warn("The stale URDF recovery draft could not be deleted.");
                }
            }
            if (restoredDraft)
            {
                baseNode = draft.Root;
                if (!String.IsNullOrWhiteSpace(draft.RosPackageName))
                {
                    Exporter.RosPackageName = draft.RosPackageName;
                }
                if (!String.IsNullOrWhiteSpace(draft.SavePath))
                {
                    Exporter.SavePath = draft.SavePath;
                }
                logger.Info(
                    "Restored the URDF export recovery draft saved at " +
                    draft.SavedUtc.ToString("O") + ".");
            }

            if (!string.IsNullOrWhiteSpace(errorMessage) && !restoredDraft)
            {
                try
                {
                    if (ConfigurationSerialization.TryReadLegacyConfiguration(
                        ActiveSWModel, out string legacyData, out double legacyVersion))
                    {
                        var plan = new LegacyConfigurationMigration(legacyData, legacyVersion,
                            new ReferenceGeometryCatalog(ActiveSWModel, false).Entries);
                        if (!plan.IsResolved)
                            plan = new LegacyConfigurationMigration(legacyData, legacyVersion,
                                new ReferenceGeometryCatalog(ActiveSWModel).Entries);
                        using (var dialog = new LegacyConfigurationMigrationDialog(plan))
                        {
                            if (dialog.ShowDialog() != DialogResult.OK)
                                return false;
                        }
                        baseNode = plan.CreateReviewedTree();
                        LegacyConfigurationMigration.EnsureComponentBindings(baseNode,
                            pid => CommonSwOperations.LoadSWComponent(ActiveSWModel, pid) != null);
                        errorMessage = string.Empty;
                    }
                }
                catch (Exception exception)
                {
                    logger.Error("Legacy configuration migration failed without writing the model.", exception);
                    errorMessage = ChineseUiText.Translate(
                        "The old configuration could not be migrated. It has not been changed.\r\n",
                        "旧配置暂时无法迁移，原配置未修改。\r\n") + exception.Message;
                }
            }

            if (!string.IsNullOrWhiteSpace(errorMessage) && !restoredDraft)
            {
                MessageBox.Show(
                    errorMessage,
                    "SW2URDF");
                return false;
            }

            SetConfigTree(baseNode);

            if (restoredDraft)
            {
                MessageBox.Show(
                    ChineseUiText.Translate(
                        "Unsaved URDF export edits from the previous window were restored automatically.",
                        "已自动恢复上次关闭导出窗口前尚未正式保存的 URDF 编辑内容。"),
                    "SW2URDF",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return true;
        }

        private void SetConfigTree(LinkNode baseNode)
        {
            if (baseNode == null)
            {
                logger.Info("Starting new configuration");
                baseNode = CreateEmptyNode(null);
            }
            else
            {
                List<string> problemLinks = new List<string>();
                CommonSwOperations.LoadSWComponents(ActiveSWModel, baseNode, problemLinks);

                if (problemLinks.Count > 0)
                {
                    string msg = "The following links had issues loading their associated SolidWorks components. " +
                        "Please inspect before exporting\r\n\r\n" +
                        string.Join(", ", problemLinks);
                    MessageBox.Show(msg);
                }
            }

            ReplaceLinkTreeRoot(baseNode);
        }

        public void MoveComponentsToFolder(LinkNode node)
        {
            bool needToCreateFolder = true;
            Object[] objects = ActiveSWModel.FeatureManager.GetFeatures(true);
            foreach (Feature feat in CommonSwOperations.EnumerateComObjects<Feature>(
                objects,
                "organizing URDF features"))
            {
                if (feat.Name == "URDF Export Items")
                {
                    needToCreateFolder = false;
                }
            }
            ActiveSWModel.ClearSelection2(true);
            ActiveSWModel.Extension.SelectByID2(
                ConfigurationSerialization.UrdfConfigurationSwAttributeName,
                "ATTRIBUTE",
                0,
                0,
                0,
                true,
                0,
                null,
                0);
            if (needToCreateFolder)
            {
                Feature folderFeature =
                    ActiveSWModel.FeatureManager.InsertFeatureTreeFolder2(
                        (int)swFeatureTreeFolderType_e.swFeatureTreeFolder_Containing);
                folderFeature.Name = "URDF Export Items";
            }
            ActiveSWModel.Extension.SelectByID2
                ("URDF Reference", "SKETCH", 0, 0, 0, true, 0, null, 0);
            ActiveSWModel.FeatureManager.MoveToFolder("URDF Export Items", "", false);
            ActiveSWModel.Extension.SelectByID2
                (ConfigurationSerialization.UrdfConfigurationSwAttributeName, "ATTRIBUTE", 0, 0, 0, true, 0, null, 0);
            ActiveSWModel.FeatureManager.MoveToFolder("URDF Export Items", "", false);
            SelectFeatures(node);
            ActiveSWModel.FeatureManager.MoveToFolder("URDF Export Items", "", false);
        }

        public void SelectFeatures(LinkNode node)
        {
            SelectTopLevelReferenceFeature(node.Link.FrameReference);
            SelectTopLevelReferenceFeature(node.Link.Joint.AxisReference);
            foreach (LinkNode child in node.Nodes)
            {
                SelectFeatures(child);
            }
        }

        private void SelectTopLevelReferenceFeature(CadFeatureReference reference)
        {
            if (reference == null || !reference.IsExplicit)
            {
                return;
            }
            ReferenceGeometryResolution resolution =
                Exporter.ResolveReferenceGeometry(reference);
            if (resolution.IsResolved && resolution.Geometry.Component == null)
            {
                resolution.Geometry.Feature.Select2(true, -1);
            }
        }

        public void CheckIfLinkNamesAreUnique(LinkNode node, string linkName, List<string> conflict)
        {
            if (node.Link.Name == linkName)
            {
                conflict.Add(node.Link.Name);
            }

            foreach (LinkNode child in node.Nodes)
            {
                CheckIfLinkNamesAreUnique(child, linkName, conflict);
            }
        }

        public void CheckIfJointNamesAreUnique(LinkNode node, string jointName, List<string> conflict)
        {
            if (!node.IsBaseNode && node.Link.Joint.Name == jointName)
            {
                conflict.Add(node.Link.Joint.Name);
            }
            foreach (LinkNode child in node.Nodes)
            {
                CheckIfJointNamesAreUnique(child, jointName, conflict);
            }
        }

        public bool CheckIfNamesAreUnique(LinkNode node)
        {
            LinkTreeNameValidationResult result = LinkTreeNameValidator.Validate(node);
            if (result.IsValid)
            {
                return true;
            }

            List<string> sections = new List<string>();
            if (result.DuplicateLinkNames.Count > 0)
            {
                sections.Add(
                    ChineseUiText.Translate(
                        "The following Link names are duplicated:",
                        "以下 Link 名称重复：") +
                    "\r\n\r\n    " + string.Join(", ", result.DuplicateLinkNames));
            }
            if (result.DuplicateJointNames.Count > 0)
            {
                sections.Add(
                    ChineseUiText.Translate(
                        "The following Joint names are duplicated:",
                        "以下 Joint 名称重复：") +
                    "\r\n\r\n    " + string.Join(", ", result.DuplicateJointNames));
            }

            MessageBox.Show(
                string.Join("\r\n\r\n", sections) +
                "\r\n\r\n" + ChineseUiText.Translate(
                    "Please rename the duplicated entries before continuing.",
                    "请先修改重复名称，然后再继续。"),
                "SW2URDF",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        public void CheckIfLinkNamesAreUnique(
            LinkNode basenode, LinkNode currentNode, List<List<string>> conflicts)
        {
            List<string> conflict = new List<string>();

            //Finds the conflicts of the currentNode with all the other nodes
            CheckIfLinkNamesAreUnique(basenode, currentNode.Link.Name, conflict);
            bool alreadyExists = false;
            foreach (List<string> existingConflict in conflicts)
            {
                if (existingConflict.Contains(conflict[0]))
                {
                    alreadyExists = true;
                }
            }
            if (!alreadyExists)
            {
                conflicts.Add(conflict);
            }
            foreach (LinkNode child in currentNode.Nodes)
            {
                //Proceeds recursively through the children nodes and adds to the conflicts
                // list of lists.
                CheckIfLinkNamesAreUnique(basenode, child, conflicts);
            }
        }

        public void CheckIfJointNamesAreUnique(
            LinkNode basenode, LinkNode currentNode, List<List<string>> conflicts)
        {
            if (!currentNode.IsBaseNode)
            {
                List<string> conflict = new List<string>();

                // Finds the conflicts of the current non-root Joint with all other non-root Joints.
                CheckIfJointNamesAreUnique(basenode, currentNode.Link.Joint.Name, conflict);
                bool alreadyExists = false;
                foreach (List<string> existingConflict in conflicts)
                {
                    if (conflict.Count > 0 && existingConflict.Contains(conflict[0]))
                    {
                        alreadyExists = true;
                    }
                }

                if (!alreadyExists)
                {
                    conflicts.Add(conflict);
                }
            }
            foreach (LinkNode child in currentNode.Nodes)
            {
                //Proceeds recursively through the children nodes and adds to the conflicts
                // list of lists.
                CheckIfJointNamesAreUnique(basenode, child, conflicts);
            }
        }
    }
}
