using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Runtime.InteropServices;

namespace SW2URDF.URDFExport
{
    public enum ReferenceResolutionStatus
    {
        Resolved,
        NotExplicit,
        ComponentUnavailable,
        FeatureUnavailable,
        FeatureTypeMismatch,
        TransformUnavailable
    }

    public sealed class ResolvedReferenceGeometry
    {
        internal ResolvedReferenceGeometry(
            CadFeatureReference reference,
            ModelDoc2 ownerModel,
            Component2 component,
            Feature feature)
        {
            Reference = reference;
            OwnerModel = ownerModel;
            Component = component;
            Feature = feature;
        }

        public CadFeatureReference Reference { get; private set; }
        public ModelDoc2 OwnerModel { get; private set; }
        public Component2 Component { get; private set; }
        public Feature Feature { get; private set; }
    }

    public sealed class ReferenceGeometryResolution
    {
        private ReferenceGeometryResolution(
            ReferenceResolutionStatus status,
            ResolvedReferenceGeometry geometry,
            string message)
        {
            Status = status;
            Geometry = geometry;
            Message = message ?? string.Empty;
        }

        public ReferenceResolutionStatus Status { get; private set; }
        public ResolvedReferenceGeometry Geometry { get; private set; }
        public string Message { get; private set; }
        public bool IsResolved { get { return Status == ReferenceResolutionStatus.Resolved; } }

        internal static ReferenceGeometryResolution Success(ResolvedReferenceGeometry geometry)
        {
            return new ReferenceGeometryResolution(
                ReferenceResolutionStatus.Resolved,
                geometry,
                string.Empty);
        }

        internal static ReferenceGeometryResolution Failure(
            ReferenceResolutionStatus status,
            string message)
        {
            return new ReferenceGeometryResolution(status, null, message);
        }
    }

    public sealed class ReferenceGeometryResolver
    {
        private readonly ModelDoc2 rootModel;

        public ReferenceGeometryResolver(ModelDoc2 rootModel)
        {
            this.rootModel = rootModel ?? throw new ArgumentNullException("rootModel");
        }

        public ReferenceGeometryResolution Resolve(CadFeatureReference reference)
        {
            if (reference == null || !reference.IsExplicit)
            {
                return ReferenceGeometryResolution.Failure(
                    ReferenceResolutionStatus.NotExplicit,
                    "The CAD reference is not an explicit persistent reference.");
            }

            if (!reference.IsValidFor(reference.Kind, false))
            {
                return ReferenceGeometryResolution.Failure(
                    ReferenceResolutionStatus.NotExplicit,
                    "The CAD reference owner scope and persistent IDs are inconsistent.");
            }

            byte[] componentPersistentId = reference.ComponentPersistentId;
            byte[] featurePersistentId = reference.FeaturePersistentId;
            ModelDoc2 ownerModel = rootModel;
            Component2 component = null;
            if (reference.OwnerScope ==
                ReferenceGeometryOwnerScope.ComponentInstance)
            {
                object componentObject;
                int componentError;
                try
                {
                    ModelDocExtension rootExtension = rootModel.Extension;
                    if (rootExtension == null)
                    {
                        return ReferenceGeometryResolution.Failure(
                            ReferenceResolutionStatus.ComponentUnavailable,
                            "The root model extension is unavailable.");
                    }
                    componentObject = rootExtension.GetObjectByPersistReference3(
                        componentPersistentId,
                        out componentError);
                }
                catch (COMException exception)
                {
                    return ReferenceGeometryResolution.Failure(
                        ReferenceResolutionStatus.ComponentUnavailable,
                        "SolidWorks could not resolve the component persistent reference: " +
                        exception.Message);
                }
                component = componentObject as Component2;
                if (componentError != (int)swPersistReferencedObjectStates_e.swPersistReferencedObject_Ok ||
                    component == null)
                {
                    return ReferenceGeometryResolution.Failure(
                        ReferenceResolutionStatus.ComponentUnavailable,
                        "The component instance persistent reference is unavailable (state " +
                        componentError + ").");
                }

                try
                {
                    ownerModel = component.GetModelDoc2();
                }
                catch (COMException exception)
                {
                    return ReferenceGeometryResolution.Failure(
                        ReferenceResolutionStatus.ComponentUnavailable,
                        "SolidWorks could not open the component model: " +
                        exception.Message);
                }
                if (ownerModel == null)
                {
                    return ReferenceGeometryResolution.Failure(
                        ReferenceResolutionStatus.ComponentUnavailable,
                        "The component model is suppressed, lightweight, or unresolved.");
                }
            }

            object featureObject;
            int featureError;
            try
            {
                ModelDocExtension ownerExtension = ownerModel.Extension;
                if (ownerExtension == null)
                {
                    return ReferenceGeometryResolution.Failure(
                        ReferenceResolutionStatus.FeatureUnavailable,
                        "The reference geometry owner extension is unavailable.");
                }
                featureObject = ownerExtension.GetObjectByPersistReference3(
                    featurePersistentId,
                    out featureError);
            }
            catch (COMException exception)
            {
                return ReferenceGeometryResolution.Failure(
                    ReferenceResolutionStatus.FeatureUnavailable,
                    "SolidWorks could not resolve the feature persistent reference: " +
                    exception.Message);
            }
            Feature feature = featureObject as Feature;
            if (featureError != (int)swPersistReferencedObjectStates_e.swPersistReferencedObject_Ok ||
                feature == null)
            {
                return ReferenceGeometryResolution.Failure(
                    ReferenceResolutionStatus.FeatureUnavailable,
                    "The reference geometry persistent reference is unavailable (state " +
                    featureError + ").");
            }

            string expectedType = reference.Kind == ReferenceGeometryKind.CoordinateSystem
                ? ReferenceGeometryFeatureTypeNames.CoordinateSystem
                : ReferenceGeometryFeatureTypeNames.Axis;
            string actualType;
            try
            {
                actualType = feature.GetTypeName2();
            }
            catch (COMException exception)
            {
                return ReferenceGeometryResolution.Failure(
                    ReferenceResolutionStatus.FeatureUnavailable,
                    "SolidWorks could not read the resolved feature type: " +
                    exception.Message);
            }
            if (!string.Equals(actualType, expectedType, StringComparison.Ordinal))
            {
                return ReferenceGeometryResolution.Failure(
                    ReferenceResolutionStatus.FeatureTypeMismatch,
                    "The persistent reference resolved to " + actualType +
                    " instead of " + expectedType + ".");
            }

            if (component != null)
            {
                try
                {
                    // The feature PID belongs to the underlying document. Map that feature to this
                    // assembly occurrence before reading FeatureData; reused components can
                    // otherwise leak the first instance context.
                    Feature correspondingFeature =
                        component.GetCorresponding(feature) as Feature;
                    if (correspondingFeature == null)
                    {
                        return ReferenceGeometryResolution.Failure(
                            ReferenceResolutionStatus.FeatureUnavailable,
                            "SolidWorks could not map the persistent feature to its specific component instance.");
                    }
                    feature = correspondingFeature;
                }
                catch (COMException exception)
                {
                    return ReferenceGeometryResolution.Failure(
                        ReferenceResolutionStatus.FeatureUnavailable,
                        "SolidWorks could not map the persistent feature to its specific component instance: " +
                        exception.Message);
                }
            }

            return ReferenceGeometryResolution.Success(
                new ResolvedReferenceGeometry(reference, ownerModel, component, feature));
        }

        public MathTransform ResolveCoordinateSystemTransform(
            CadFeatureReference reference,
            out ReferenceGeometryResolution resolution)
        {
            resolution = Resolve(reference);
            if (!resolution.IsResolved ||
                reference.Kind != ReferenceGeometryKind.CoordinateSystem)
            {
                return null;
            }

            ResolvedReferenceGeometry geometry = resolution.Geometry;
            MathTransform localTransform = GetCoordinateSystemFeatureTransform(
                geometry,
                out resolution);
            if (localTransform == null)
            {
                return null;
            }

            if (!TryGetComponentTransform(
                    geometry,
                    out MathTransform componentTransform,
                    out resolution))
            {
                return null;
            }

            if (geometry.Component == null)
            {
                return localTransform;
            }

            try
            {
                MathTransform rootTransform = localTransform.Multiply(componentTransform);
                if (rootTransform == null)
                {
                    resolution = ReferenceGeometryResolution.Failure(
                        ReferenceResolutionStatus.TransformUnavailable,
                        "SolidWorks did not compose the coordinate-system transform.");
                }
                return rootTransform;
            }
            catch (COMException exception)
            {
                resolution = ReferenceGeometryResolution.Failure(
                    ReferenceResolutionStatus.TransformUnavailable,
                    "SolidWorks could not compose the coordinate-system transform: " +
                    exception.Message);
                return null;
            }
        }

        public bool TryGetReferenceAxisParameters(
            CadFeatureReference reference,
            out double[] axisParameters,
            out MathTransform componentTransform,
            out ReferenceGeometryResolution resolution)
        {
            axisParameters = null;
            componentTransform = null;
            resolution = Resolve(reference);
            if (!resolution.IsResolved ||
                reference.Kind != ReferenceGeometryKind.Axis)
            {
                if (resolution.IsResolved)
                {
                    resolution = ReferenceGeometryResolution.Failure(
                        ReferenceResolutionStatus.FeatureTypeMismatch,
                        "The persistent reference is not a reference axis.");
                }
                return false;
            }

            ResolvedReferenceGeometry geometry = resolution.Geometry;
            axisParameters = GetReferenceAxisParameters(
                geometry,
                out resolution);
            if (axisParameters == null)
            {
                return false;
            }

            return TryGetComponentTransform(
                geometry,
                out componentTransform,
                out resolution);
        }

        public bool TryGetComponentTransform(
            ResolvedReferenceGeometry geometry,
            out MathTransform componentTransform,
            out ReferenceGeometryResolution resolution)
        {
            if (geometry == null)
            {
                throw new ArgumentNullException("geometry");
            }

            componentTransform = null;
            resolution = ReferenceGeometryResolution.Success(geometry);
            if (geometry.Component == null)
            {
                return true;
            }

            componentTransform = GetComponentToRootTransform(geometry.Component);
            if (componentTransform != null)
            {
                return true;
            }

            resolution = ReferenceGeometryResolution.Failure(
                ReferenceResolutionStatus.TransformUnavailable,
                "The component-to-root-assembly transform is unavailable. Resolve the component before using its reference geometry.");
            return false;
        }

        public static MathTransform GetComponentToRootTransform(Component2 component)
        {
            if (component == null)
            {
                return null;
            }
            try
            {
                return component.GetTotalTransform(false);
            }
            catch (COMException)
            {
                return null;
            }
        }

        private static MathTransform GetCoordinateSystemFeatureTransform(
            ResolvedReferenceGeometry geometry,
            out ReferenceGeometryResolution resolution)
        {
            resolution = ReferenceGeometryResolution.Success(geometry);
            try
            {
                // Resolve already mapped component references with GetCorresponding. Transform is
                // a read-only result; AccessSelections is for the defining selections and would
                // put the assembly into rollback unnecessarily.
                CoordinateSystemFeatureData definition =
                    geometry.Feature.GetDefinition() as CoordinateSystemFeatureData;
                if (definition == null)
                {
                    resolution = ReferenceGeometryResolution.Failure(
                        ReferenceResolutionStatus.TransformUnavailable,
                        "SolidWorks did not return coordinate-system feature data for the persistent reference.");
                    return null;
                }

                MathTransform transform = definition.Transform;
                if (transform == null)
                {
                    resolution = ReferenceGeometryResolution.Failure(
                        ReferenceResolutionStatus.TransformUnavailable,
                        "SolidWorks did not return a transform for the selected coordinate system.");
                }
                return transform;
            }
            catch (COMException exception)
            {
                resolution = ReferenceGeometryResolution.Failure(
                    ReferenceResolutionStatus.TransformUnavailable,
                    "SolidWorks could not read the selected coordinate-system transform: " +
                    exception.Message);
                return null;
            }
        }

        private double[] GetReferenceAxisParameters(
            ResolvedReferenceGeometry geometry,
            out ReferenceGeometryResolution resolution)
        {
            resolution = ReferenceGeometryResolution.Success(geometry);
            try
            {
                // Read the occurrence-mapped RefAxis geometry directly. IRefAxisFeatureData
                // AccessSelections is only needed for its defining selections and rollback state.
                RefAxis axis = geometry.Feature.GetSpecificFeature2() as RefAxis;
                double[] parameters = axis == null
                    ? null
                    : axis.GetRefAxisParams() as double[];
                if (parameters == null || parameters.Length < 6)
                {
                    resolution = ReferenceGeometryResolution.Failure(
                        ReferenceResolutionStatus.TransformUnavailable,
                        "SolidWorks did not return parameters for the selected reference axis.");
                    return null;
                }
                return (double[])parameters.Clone();
            }
            catch (COMException exception)
            {
                resolution = ReferenceGeometryResolution.Failure(
                    ReferenceResolutionStatus.TransformUnavailable,
                    "SolidWorks could not read the selected reference axis: " +
                    exception.Message);
                return null;
            }
        }
    }
}
