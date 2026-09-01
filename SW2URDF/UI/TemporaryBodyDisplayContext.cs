using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SW2URDF.UI
{
    /// <summary>
    /// Owns the API-valid SolidWorks host used by temporary preview bodies.
    /// Display3 accepts a PartDoc or a top-level part Component2, never an
    /// AssemblyDoc or a component nested in a subassembly.
    /// </summary>
    internal sealed class TemporaryBodyDisplayContext : IDisposable
    {
        internal const string TemporaryHostName = "SW2URDF_Preview_Host";

        private readonly bool ownsTransform;
        private readonly bool ownsDisplayTarget;
        private readonly bool ownsHideTarget;
        private readonly ModelDoc2 temporaryHostModel;
        private readonly AssemblyDoc temporaryHostAssembly;
        private bool disposed;

        private TemporaryBodyDisplayContext(
            object displayTarget,
            ModelDoc2 hideTarget,
            MathTransform linkToDisplayTarget,
            Matrix<double> displayTargetToDocument,
            bool ownsTransform,
            bool ownsDisplayTarget,
            bool ownsHideTarget,
            ModelDoc2 temporaryHostModel,
            AssemblyDoc temporaryHostAssembly)
        {
            DisplayTarget = displayTarget;
            HideTarget = hideTarget;
            LinkToDisplayTarget = linkToDisplayTarget;
            DisplayTargetToDocument = displayTargetToDocument;
            this.ownsTransform = ownsTransform;
            this.ownsDisplayTarget = ownsDisplayTarget;
            this.ownsHideTarget = ownsHideTarget;
            this.temporaryHostModel = temporaryHostModel;
            this.temporaryHostAssembly = temporaryHostAssembly;
        }

        public object DisplayTarget { get; }

        public ModelDoc2 HideTarget { get; }

        public MathTransform LinkToDisplayTarget { get; }

        public Matrix<double> DisplayTargetToDocument { get; }

        internal bool UsesTemporaryHost => temporaryHostAssembly != null;

        public static bool TryCreate(
            SldWorks swApp,
            ModelDoc2 model,
            MathTransform linkToDocument,
            out TemporaryBodyDisplayContext context,
            out string error)
        {
            context = null;
            error = null;
            if (swApp == null || model == null || linkToDocument == null)
            {
                error = "The SolidWorks application, model, or Link transform is missing.";
                return false;
            }

            int documentType;
            try
            {
                documentType = model.GetType();
            }
            catch (COMException exception)
            {
                error = "SolidWorks could not inspect the active display document: " +
                    exception.Message;
                return false;
            }

            if (!IsSupportedRootDocument(documentType))
            {
                error = "Temporary-body previews require an active part or assembly document.";
                return false;
            }

            Matrix<double> linkToDocumentMatrix =
                MathOps.GetTransformation(linkToDocument);
            if (documentType == (int)swDocumentTypes_e.swDocPART)
            {
                context = new TemporaryBodyDisplayContext(
                    model,
                    model,
                    linkToDocument,
                    Matrix<double>.Build.DenseIdentity(4),
                    false,
                    false,
                    false,
                    null,
                    null);
                return true;
            }

            AssemblyDoc assembly = model as AssemblyDoc;
            if (assembly == null)
            {
                error = "SolidWorks could not access the active assembly document.";
                return false;
            }

            Component2 displayComponent = null;
            ModelDoc2 displayDocument = null;
            Matrix<double> displayComponentToDocument = null;
            bool ownsTemporaryHost = false;
            if (!TryResolveTopLevelPart(
                assembly,
                out displayComponent,
                out displayDocument,
                out displayComponentToDocument))
            {
                if (!TryCreateTemporaryTopLevelPart(
                    model,
                    assembly,
                    out displayComponent,
                    out displayDocument,
                    out displayComponentToDocument,
                    out error))
                {
                    return false;
                }
                ownsTemporaryHost = true;
            }

            MathUtility mathUtility = null;
            MathTransform ownedLinkToTarget = null;
            bool transferred = false;
            try
            {
                Matrix<double> linkToTarget = BuildLinkToDisplayTarget(
                    linkToDocumentMatrix,
                    displayComponentToDocument);
                mathUtility = swApp.GetMathUtility() as MathUtility;
                if (mathUtility == null)
                {
                    error = "SolidWorks MathUtility is unavailable.";
                    return false;
                }
                ownedLinkToTarget = mathUtility.CreateTransform(
                    ToSolidWorksTransformData(linkToTarget)) as MathTransform;
                if (ownedLinkToTarget == null)
                {
                    error = "SolidWorks could not create the Link-to-preview-host transform.";
                    return false;
                }

                context = new TemporaryBodyDisplayContext(
                    displayComponent,
                    displayDocument,
                    ownedLinkToTarget,
                    displayComponentToDocument.Clone(),
                    true,
                    true,
                    true,
                    ownsTemporaryHost ? model : null,
                    ownsTemporaryHost ? assembly : null);
                ownedLinkToTarget = null;
                transferred = true;
                return true;
            }
            catch (COMException exception)
            {
                error = "SolidWorks could not create the temporary-body display context: " +
                    exception.Message;
                return false;
            }
            finally
            {
                ReleaseComReference(ownedLinkToTarget);
                ReleaseComReference(mathUtility);
                if (!transferred)
                {
                    if (ownsTemporaryHost)
                    {
                        RemoveTemporaryHost(model, assembly, displayComponent);
                    }
                    ReleaseComReference(displayDocument);
                    ReleaseComReference(displayComponent);
                }
            }
        }

        internal static bool IsSupportedRootDocument(int documentType)
        {
            return documentType == (int)swDocumentTypes_e.swDocPART ||
                documentType == (int)swDocumentTypes_e.swDocASSEMBLY;
        }

        internal static bool IsValidDisplayTargetDocument(int documentType)
        {
            return documentType == (int)swDocumentTypes_e.swDocPART;
        }

        internal static Matrix<double> BuildLinkToDisplayTarget(
            Matrix<double> linkToDocument,
            Matrix<double> displayTargetToDocument)
        {
            RequireTransform(linkToDocument, "The Link-to-document transform");
            RequireTransform(displayTargetToDocument,
                "The display-target-to-document transform");
            return displayTargetToDocument.Inverse() * linkToDocument;
        }

        internal static Matrix<double> BuildBodyToDisplayTarget(
            Matrix<double> bodyToDocument,
            Matrix<double> displayTargetToDocument)
        {
            RequireTransform(bodyToDocument, "The body-to-document transform");
            RequireTransform(displayTargetToDocument,
                "The display-target-to-document transform");
            return displayTargetToDocument.Inverse() * bodyToDocument;
        }

        internal static double[] ToSolidWorksTransformData(Matrix<double> transform)
        {
            RequireTransform(transform, "The transform");
            return new[]
            {
                transform[0, 0], transform[1, 0], transform[2, 0],
                transform[0, 1], transform[1, 1], transform[2, 1],
                transform[0, 2], transform[1, 2], transform[2, 2],
                transform[0, 3], transform[1, 3], transform[2, 3],
                1.0, 0.0, 0.0, 0.0
            };
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;

            if (temporaryHostAssembly != null)
            {
                RemoveTemporaryHost(
                    temporaryHostModel,
                    temporaryHostAssembly,
                    DisplayTarget as Component2);
            }
            if (ownsTransform)
            {
                ReleaseComReference(LinkToDisplayTarget);
            }
            if (ownsHideTarget)
            {
                ReleaseComReference(HideTarget);
            }
            if (ownsDisplayTarget)
            {
                ReleaseComReference(DisplayTarget);
            }
        }

        private static bool TryResolveTopLevelPart(
            AssemblyDoc assembly,
            out Component2 displayComponent,
            out ModelDoc2 displayDocument,
            out Matrix<double> componentToDocument)
        {
            displayComponent = null;
            displayDocument = null;
            componentToDocument = null;
            object[] components = null;
            try
            {
                components = assembly.GetComponents(true) as object[];
                foreach (Component2 component in
                    (components ?? new object[0]).OfType<Component2>())
                {
                    ModelDoc2 candidateDocument = null;
                    MathTransform candidateTransform = null;
                    try
                    {
                        if (component.IsHidden(false))
                        {
                            continue;
                        }
                        candidateDocument = component.GetModelDoc2() as ModelDoc2;
                        if (candidateDocument == null ||
                            !IsValidDisplayTargetDocument(candidateDocument.GetType()))
                        {
                            continue;
                        }
                        candidateTransform = ReferenceGeometryResolver
                            .GetComponentToRootTransform(component);
                        if (candidateTransform == null)
                        {
                            continue;
                        }

                        displayComponent = component;
                        displayDocument = candidateDocument;
                        componentToDocument =
                            MathOps.GetTransformation(candidateTransform);
                        candidateDocument = null;
                        RemoveOwnedReference(components, component);
                        return true;
                    }
                    catch (COMException)
                    {
                        // Lightweight, suppressed, and unavailable components are skipped.
                    }
                    finally
                    {
                        ReleaseComReference(candidateTransform);
                        ReleaseComReference(candidateDocument);
                    }
                }
                return false;
            }
            finally
            {
                ReleaseComReferences(components);
            }
        }

        private static bool TryCreateTemporaryTopLevelPart(
            ModelDoc2 model,
            AssemblyDoc assembly,
            out Component2 component,
            out ModelDoc2 partDocument,
            out Matrix<double> componentToDocument,
            out string error)
        {
            component = null;
            partDocument = null;
            componentToDocument = null;
            error = null;
            Feature planeFeature = null;
            RefPlane plane = null;
            MathTransform transform = null;
            try
            {
                if (!TryGetRootReferencePlane(model, out planeFeature, out plane))
                {
                    error = ChineseUiText.Translate(
                        "SolidWorks could not find a root reference plane for the temporary preview host.",
                        "SolidWorks 无法找到用于临时预览宿主的根参考基准面。");
                    return false;
                }

                int result = assembly.InsertNewVirtualPart(plane, out component);
                try { assembly.EditAssembly(); }
                catch (COMException) { }
                if (result != (int)swInsertNewPartErrorCode_e.swInsertNewPartError_NoError ||
                    component == null)
                {
                    error = ChineseUiText.Translate(
                        "SolidWorks could not create a top-level virtual Part preview host (error " +
                            result + ").",
                        "SolidWorks 无法创建顶层虚拟零件预览宿主（错误 " + result + "）。");
                    return false;
                }

                try { component.Name2 = TemporaryHostName; }
                catch (COMException) { }
                try
                {
                    component.Visible =
                        (int)swComponentVisibilityState_e.swComponentVisible;
                }
                catch (COMException) { }

                Component2 parent = null;
                try
                {
                    parent = component.GetParent();
                    if (parent != null)
                    {
                        error = ChineseUiText.Translate(
                            "SolidWorks created the preview host below the root assembly.",
                            "SolidWorks 将预览宿主创建在了根装配体以下，无法用于 Display3。");
                        return false;
                    }
                }
                finally
                {
                    ReleaseComReference(parent);
                }

                partDocument = component.GetModelDoc2() as ModelDoc2;
                if (partDocument == null ||
                    !IsValidDisplayTargetDocument(partDocument.GetType()))
                {
                    error = ChineseUiText.Translate(
                        "The temporary preview host did not resolve to a Part document.",
                        "临时预览宿主未解析为零件文档。");
                    return false;
                }

                transform = ReferenceGeometryResolver
                    .GetComponentToRootTransform(component);
                componentToDocument = transform == null
                    ? Matrix<double>.Build.DenseIdentity(4)
                    : MathOps.GetTransformation(transform);
                return true;
            }
            catch (COMException exception)
            {
                error = ChineseUiText.Translate(
                    "SolidWorks could not create a safe temporary Part preview host: ",
                    "SolidWorks 无法创建安全的临时零件预览宿主：") + exception.Message;
                return false;
            }
            finally
            {
                ReleaseComReference(transform);
                ReleaseComReference(plane);
                ReleaseComReference(planeFeature);
                if (error != null && component != null)
                {
                    RemoveTemporaryHost(model, assembly, component);
                    ReleaseComReference(partDocument);
                    partDocument = null;
                    ReleaseComReference(component);
                    component = null;
                    componentToDocument = null;
                }
            }
        }

        private static bool TryGetRootReferencePlane(
            ModelDoc2 model,
            out Feature planeFeature,
            out RefPlane plane)
        {
            planeFeature = null;
            plane = null;
            Feature current = null;
            try
            {
                current = model.FirstFeature() as Feature;
                while (current != null)
                {
                    string typeName = null;
                    try { typeName = current.GetTypeName2(); }
                    catch (COMException) { }
                    if (String.Equals(typeName, "RefPlane", StringComparison.Ordinal))
                    {
                        plane = current.GetSpecificFeature2() as RefPlane;
                        if (plane != null)
                        {
                            planeFeature = current;
                            current = null;
                            return true;
                        }
                    }

                    Feature next = null;
                    try { next = current.GetNextFeature() as Feature; }
                    finally { ReleaseComReference(current); }
                    current = next;
                }
                return false;
            }
            finally
            {
                ReleaseComReference(current);
            }
        }

        private static void RemoveTemporaryHost(
            ModelDoc2 model,
            AssemblyDoc assembly,
            Component2 component)
        {
            if (model == null || assembly == null || component == null)
            {
                return;
            }

            try
            {
                try { assembly.EditAssembly(); }
                catch (COMException) { }
                model.ClearSelection2(true);
                if (!component.Select4(false, null, false))
                {
                    return;
                }
                if (!assembly.DeleteSelections(
                    (int)swAssemblyDeleteOptions_e.swDelete_SelectedComponents))
                {
                    ModelDocExtension extension = null;
                    try
                    {
                        extension = model.Extension;
                        extension?.DeleteSelection2(0);
                    }
                    finally
                    {
                        ReleaseComReference(extension);
                    }
                }
            }
            catch (COMException)
            {
                // The component can already be gone after a rebuild or document close.
            }
            finally
            {
                try { model.ClearSelection2(true); }
                catch (COMException) { }
            }
        }

        private static void RemoveOwnedReference(object[] values, object transferred)
        {
            if (values == null || transferred == null)
            {
                return;
            }
            for (int index = 0; index < values.Length; index++)
            {
                if (ReferenceEquals(values[index], transferred))
                {
                    values[index] = null;
                    return;
                }
            }
        }

        private static void ReleaseComReferences(IEnumerable<object> values)
        {
            if (values == null)
            {
                return;
            }
            foreach (object value in values)
            {
                ReleaseComReference(value);
            }
        }

        private static void RequireTransform(Matrix<double> transform, string name)
        {
            if (transform == null || transform.RowCount != 4 ||
                transform.ColumnCount != 4)
            {
                throw new ArgumentException(name + " must be a 4x4 matrix.");
            }
        }

        private static void ReleaseComReference(object value)
        {
            try
            {
                if (value != null && Marshal.IsComObject(value))
                {
                    Marshal.ReleaseComObject(value);
                }
            }
            catch (InvalidComObjectException) { }
            catch (COMException) { }
        }
    }
}
