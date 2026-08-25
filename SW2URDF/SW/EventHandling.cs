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
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Collections;
using System.Runtime.InteropServices;

namespace SW2URDF.SW
{
    public class DocumentEventHandler
    {
        protected ISldWorks iSwApp;
        protected ModelDoc2 document;
        protected SwAddin userAddin;

        protected Hashtable openModelViews;

        public DocumentEventHandler(ModelDoc2 modDoc, SwAddin addin)
        {
            document = modDoc;
            userAddin = addin;
            iSwApp = userAddin.SwApp;
            openModelViews = new Hashtable();
        }

        public virtual bool AttachEventHandlers()
        {
            return true;
        }

        public virtual bool DetachEventHandlers()
        {
            return true;
        }

        public bool ConnectModelViews()
        {
            IModelView mView;
            mView = (IModelView)document.GetFirstModelView();

            while (mView != null)
            {
                if (!openModelViews.Contains(mView))
                {
                    DocView dView = new DocView(mView, this);
                    dView.AttachEventHandlers();
                    openModelViews.Add(mView, dView);
                }
                mView = (IModelView)mView.GetNext();
            }
            return true;
        }

        public bool DisconnectModelViews()
        {
            //Close events on all currently open docs
            DocView dView;
            int numKeys;
            numKeys = openModelViews.Count;

            if (numKeys == 0)
            {
                return false;
            }

            object[] keys = new object[numKeys];

            //Remove all ModelView event handlers
            openModelViews.Keys.CopyTo(keys, 0);
            foreach (ModelView key in keys)
            {
                dView = (DocView)openModelViews[key];
                dView.DetachEventHandlers();
                openModelViews.Remove(key);
                dView = null;
            }
            return true;
        }

        public bool DetachModelViewEventHandler(ModelView mView)
        {
            if (openModelViews.Contains(mView))
            {
                openModelViews.Remove(mView);
            }
            return true;
        }
    }

    public class PartEventHandler : DocumentEventHandler
    {
        private readonly PartDoc doc;

        public PartEventHandler(ModelDoc2 modDoc, SwAddin addin)
            : base(modDoc, addin)
        {
            doc = (PartDoc)document;
        }

        public override bool AttachEventHandlers()
        {
            doc.DestroyNotify += new DPartDocEvents_DestroyNotifyEventHandler(OnDestroy);
            doc.NewSelectionNotify += new DPartDocEvents_NewSelectionNotifyEventHandler(OnNewSelection);

            ConnectModelViews();

            return true;
        }

        public override bool DetachEventHandlers()
        {
            doc.DestroyNotify -= new DPartDocEvents_DestroyNotifyEventHandler(OnDestroy);
            doc.NewSelectionNotify -= new DPartDocEvents_NewSelectionNotifyEventHandler(OnNewSelection);

            DisconnectModelViews();

            userAddin.DetachModelEventHandler(document);
            return true;
        }

        //Event Handlers
        public int OnDestroy()
        {
            DetachEventHandlers();
            return 0;
        }

        public static int OnNewSelection()
        {
            return 0;
        }
    }

    public class AssemblyEventHandler : DocumentEventHandler
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();
        private readonly AssemblyDoc doc;
        private readonly SwAddin swAddin;

        public AssemblyEventHandler(ModelDoc2 modDoc, SwAddin addin)
            : base(modDoc, addin)
        {
            doc = (AssemblyDoc)document;
            swAddin = addin;
        }

        public override bool AttachEventHandlers()
        {
            doc.DestroyNotify += new DAssemblyDocEvents_DestroyNotifyEventHandler(OnDestroy);
            doc.NewSelectionNotify += new DAssemblyDocEvents_NewSelectionNotifyEventHandler(OnNewSelection);
            doc.ComponentStateChangeNotify2 += new DAssemblyDocEvents_ComponentStateChangeNotify2EventHandler(ComponentStateChangeNotify2);
            doc.ComponentStateChangeNotify += new DAssemblyDocEvents_ComponentStateChangeNotifyEventHandler(ComponentStateChangeNotify);
            doc.ComponentVisualPropertiesChangeNotify += new DAssemblyDocEvents_ComponentVisualPropertiesChangeNotifyEventHandler(ComponentVisualPropertiesChangeNotify);
            doc.ComponentDisplayStateChangeNotify += new DAssemblyDocEvents_ComponentDisplayStateChangeNotifyEventHandler(ComponentDisplayStateChangeNotify);
            ConnectModelViews();

            return true;
        }

        public override bool DetachEventHandlers()
        {
            doc.DestroyNotify -= new DAssemblyDocEvents_DestroyNotifyEventHandler(OnDestroy);
            doc.NewSelectionNotify -= new DAssemblyDocEvents_NewSelectionNotifyEventHandler(OnNewSelection);
            doc.ComponentStateChangeNotify2 -= new DAssemblyDocEvents_ComponentStateChangeNotify2EventHandler(ComponentStateChangeNotify2);
            doc.ComponentStateChangeNotify -= new DAssemblyDocEvents_ComponentStateChangeNotifyEventHandler(ComponentStateChangeNotify);
            doc.ComponentVisualPropertiesChangeNotify -= new DAssemblyDocEvents_ComponentVisualPropertiesChangeNotifyEventHandler(ComponentVisualPropertiesChangeNotify);
            doc.ComponentDisplayStateChangeNotify -= new DAssemblyDocEvents_ComponentDisplayStateChangeNotifyEventHandler(ComponentDisplayStateChangeNotify);
            DisconnectModelViews();

            userAddin.DetachModelEventHandler(document);
            return true;
        }

        //Event Handlers
        public int OnDestroy()
        {
            DetachEventHandlers();
            return 0;
        }

        public static int OnNewSelection()
        {
            return 0;
        }

        //attach events to a component if it becomes resolved
        protected int ComponentStateChange(object componentModel, short newCompState)
        {
            ModelDoc2 modDoc = CommonSwOperations.TryCastComObject<ModelDoc2>(
                componentModel,
                "handling a component state change");
            if (modDoc == null)
            {
                return 0;
            }
            swComponentSuppressionState_e newState = (swComponentSuppressionState_e)newCompState;

            switch (newState)
            {
                case swComponentSuppressionState_e.swComponentFullyResolved:
                    {
                        if ((modDoc != null) && !swAddin.OpenDocs.Contains(modDoc))
                        {
                            swAddin.AttachModelDocEventHandler(modDoc);
                        }
                        break;
                    }
                case swComponentSuppressionState_e.swComponentResolved:
                    {
                        if ((modDoc != null) && !swAddin.OpenDocs.Contains(modDoc))
                        {
                            swAddin.AttachModelDocEventHandler(modDoc);
                        }
                        break;
                    }
                case swComponentSuppressionState_e.swComponentSuppressed:
                    break;
                case swComponentSuppressionState_e.swComponentLightweight:
                    break;
                case swComponentSuppressionState_e.swComponentFullyLightweight:
                    break;
                case swComponentSuppressionState_e.swComponentInternalIdMismatch:
                    break;
                default:
                    break;
            }
            return 0;
        }

        protected int ComponentStateChange(object componentModel)
        {
            ComponentStateChange(componentModel, (short)swComponentSuppressionState_e.swComponentResolved);
            return 0;
        }

        public int ComponentStateChangeNotify2(object componentModel, string CompName, short oldCompState, short newCompState)
        {
            return ComponentStateChange(componentModel, newCompState);
        }

        private int ComponentStateChangeNotify(object componentModel, short oldCompState, short newCompState)
        {
            return ComponentStateChange(componentModel, newCompState);
        }

        private int ComponentDisplayStateChangeNotify(object swObject)
        {
            return HandleComponentDisplayChange(
                swObject,
                "handling a component display-state change");
        }

        private int ComponentVisualPropertiesChangeNotify(object swObject)
        {
            return HandleComponentDisplayChange(
                swObject,
                "handling a component visual-properties change");
        }

        private int HandleComponentDisplayChange(object swObject, string context)
        {
            try
            {
                Component2 component = CommonSwOperations.TryCastComObject<Component2>(
                    swObject,
                    context);
                if (component == null)
                {
                    return 0;
                }

                ModelDoc2 modDoc = CommonSwOperations.TryCastComObject<ModelDoc2>(
                    component.GetModelDoc(),
                    context);
                if (modDoc == null)
                {
                    return 0;
                }

                return ComponentStateChange(modDoc);
            }
            catch (COMException exception)
            {
                logger.Warn("SolidWorks rejected a COM call while " + context + ".", exception);
                return 0;
            }
            catch (Exception exception)
            {
                logger.Warn("Ignoring an unexpected callback failure while " + context + ".", exception);
                return 0;
            }
        }
    }

    public class DrawingEventHandler : DocumentEventHandler
    {
        private readonly DrawingDoc doc;

        public DrawingEventHandler(ModelDoc2 modDoc, SwAddin addin)
            : base(modDoc, addin)
        {
            doc = (DrawingDoc)document;
        }

        public override bool AttachEventHandlers()
        {
            doc.DestroyNotify += new DDrawingDocEvents_DestroyNotifyEventHandler(OnDestroy);
            doc.NewSelectionNotify += new DDrawingDocEvents_NewSelectionNotifyEventHandler(OnNewSelection);

            ConnectModelViews();

            return true;
        }

        public override bool DetachEventHandlers()
        {
            doc.DestroyNotify -= new DDrawingDocEvents_DestroyNotifyEventHandler(OnDestroy);
            doc.NewSelectionNotify -= new DDrawingDocEvents_NewSelectionNotifyEventHandler(OnNewSelection);

            DisconnectModelViews();

            userAddin.DetachModelEventHandler(document);
            return true;
        }

        //Event Handlers
        public int OnDestroy()
        {
            DetachEventHandlers();
            return 0;
        }

        public static int OnNewSelection()
        {
            return 0;
        }
    }

    public class DocView
    {
        private readonly ModelView mView;
        private readonly DocumentEventHandler parent;

        public DocView(IModelView mv, DocumentEventHandler doc)
        {
            mView = (ModelView)mv;
            parent = doc;
        }

        public bool AttachEventHandlers()
        {
            mView.DestroyNotify2 += new DModelViewEvents_DestroyNotify2EventHandler(OnDestroy);
            mView.RepaintNotify += new DModelViewEvents_RepaintNotifyEventHandler(OnRepaint);
            return true;
        }

        public bool DetachEventHandlers()
        {
            mView.DestroyNotify2 -= new DModelViewEvents_DestroyNotify2EventHandler(OnDestroy);
            mView.RepaintNotify -= new DModelViewEvents_RepaintNotifyEventHandler(OnRepaint);
            parent.DetachModelViewEventHandler(mView);
            return true;
        }

        //EventHandlers
        public static int OnDestroy(int destroyType)
        {
            switch (destroyType)
            {
                case (int)swDestroyNotifyType_e.swDestroyNotifyHidden:
                    return 0;

                case (int)swDestroyNotifyType_e.swDestroyNotifyDestroy:
                    return 0;

                default:
                    return 0;
            }
        }

        public static int OnRepaint(int repaintType)
        {
            return 0;
        }
    }
}
