using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SW2URDF.URDFExport
{
    public sealed class ReferenceGeometryEntry
    {
        internal ReferenceGeometryEntry(
            CadFeatureReference reference,
            string displayName,
            string componentPath)
        {
            Reference = reference ?? throw new ArgumentNullException("reference");
            DisplayName = displayName ?? string.Empty;
            ComponentPath = componentPath ?? string.Empty;
        }

        public CadFeatureReference Reference { get; private set; }
        public string DisplayName { get; private set; }
        public string ComponentPath { get; private set; }

        public string DisplayLabel
        {
            get
            {
                return string.IsNullOrWhiteSpace(ComponentPath)
                    ? DisplayName
                    : DisplayName + " - " + ComponentPath;
            }
        }

        public override string ToString()
        {
            return DisplayLabel;
        }
    }

    public sealed class ReferenceGeometryCatalog
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();
        private readonly ModelDoc2 rootModel;
        private readonly List<ReferenceGeometryEntry> entries;

        public ReferenceGeometryCatalog(ModelDoc2 rootModel)
        {
            this.rootModel = rootModel ?? throw new ArgumentNullException("rootModel");
            entries = new List<ReferenceGeometryEntry>();
            Refresh();
        }

        public IReadOnlyList<ReferenceGeometryEntry> Entries
        {
            get { return entries.AsReadOnly(); }
        }

        public IReadOnlyList<ReferenceGeometryEntry> CoordinateSystems
        {
            get
            {
                return entries
                    .Where(entry => entry.Reference.Kind == ReferenceGeometryKind.CoordinateSystem)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<ReferenceGeometryEntry> Axes
        {
            get
            {
                return entries
                    .Where(entry => entry.Reference.Kind == ReferenceGeometryKind.Axis)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public void Refresh()
        {
            entries.Clear();
            HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                AddDocumentFeatures(rootModel, null, string.Empty, string.Empty, identities);
            }
            catch (COMException exception)
            {
                logger.Warn("Root reference geometry is temporarily unavailable.", exception);
            }

            int documentType;
            try
            {
                documentType = rootModel.GetType();
            }
            catch (COMException exception)
            {
                logger.Warn("The active SolidWorks document type is unavailable.", exception);
                return;
            }
            if (documentType != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                return;
            }

            AssemblyDoc assembly = rootModel as AssemblyDoc;
            object[] componentObjects = null;
            try
            {
                componentObjects = assembly == null
                    ? null
                    : assembly.GetComponents(false) as object[];
            }
            catch (COMException exception)
            {
                logger.Warn("Assembly components are temporarily unavailable.", exception);
            }
            foreach (Component2 component in CommonSwOperations.EnumerateComObjects<Component2>(
                componentObjects,
                "building the reference geometry catalog"))
            {
                AddComponentFeatures(component, identities);
            }

            entries.Sort((left, right) =>
            {
                int path = string.Compare(
                    left.ComponentPath,
                    right.ComponentPath,
                    StringComparison.Ordinal);
                return path != 0
                    ? path
                    : string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
            });
        }

        public ReferenceGeometryEntry Find(CadFeatureReference reference)
        {
            if (reference == null)
            {
                return null;
            }

            return entries.FirstOrDefault(entry => entry.Reference.Equals(reference));
        }

        private void AddComponentFeatures(
            Component2 component,
            HashSet<string> identities)
        {
            if (component == null)
            {
                return;
            }

            try
            {
                ModelDoc2 componentModel = component.GetModelDoc2();
                if (componentModel == null)
                {
                    logger.Warn("Reference geometry is unavailable for unresolved component " +
                        GetComponentPath(component) + ".");
                    return;
                }

                ModelDocExtension rootExtension = rootModel.Extension;
                byte[] componentPersistentId = rootExtension == null
                    ? null
                    : rootExtension.GetPersistReference3(component) as byte[];
                if (componentPersistentId == null || componentPersistentId.Length == 0)
                {
                    logger.Warn("SolidWorks did not provide a persistent ID for component " +
                        GetComponentPath(component) + ".");
                    return;
                }

                string configuration = string.Empty;
                try
                {
                    configuration = component.ReferencedConfiguration ?? string.Empty;
                }
                catch (COMException)
                {
                    configuration = string.Empty;
                }

                AddDocumentFeatures(
                    componentModel,
                    componentPersistentId,
                    GetComponentPath(component),
                    configuration,
                    identities);
            }
            catch (COMException exception)
            {
                logger.Warn("Reference geometry is unavailable for a component instance.", exception);
            }
        }

        private void AddDocumentFeatures(
            ModelDoc2 model,
            byte[] componentPersistentId,
            string componentPath,
            string configuration,
            HashSet<string> identities)
        {
            if (model == null)
            {
                return;
            }

            ModelDocExtension extension;
            FeatureManager featureManager;
            object[] featureObjects;
            try
            {
                extension = model.Extension;
                featureManager = model.FeatureManager;
                if (extension == null || featureManager == null)
                {
                    return;
                }
                featureObjects = featureManager.GetFeatures(false) as object[];
            }
            catch (COMException exception)
            {
                logger.Warn("Reference geometry features are temporarily unavailable.", exception);
                return;
            }
            foreach (Feature feature in CommonSwOperations.EnumerateComObjects<Feature>(
                featureObjects,
                "enumerating reference geometry"))
            {
                ReferenceGeometryKind kind;
                string typeName;
                try
                {
                    typeName = feature.GetTypeName2();
                }
                catch (COMException)
                {
                    continue;
                }

                if (typeName == ReferenceGeometryFeatureTypeNames.CoordinateSystem)
                {
                    kind = ReferenceGeometryKind.CoordinateSystem;
                }
                else if (typeName == ReferenceGeometryFeatureTypeNames.Axis)
                {
                    kind = ReferenceGeometryKind.Axis;
                }
                else
                {
                    continue;
                }

                byte[] featurePersistentId;
                try
                {
                    featurePersistentId = extension.GetPersistReference3(feature) as byte[];
                }
                catch (COMException exception)
                {
                    logger.Warn("SolidWorks could not create a persistent reference for " +
                        GetFeatureName(feature) + ".", exception);
                    continue;
                }
                if (featurePersistentId == null || featurePersistentId.Length == 0)
                {
                    logger.Warn("SolidWorks did not provide a persistent ID for reference geometry " +
                        GetFeatureName(feature) + ".");
                    continue;
                }

                CadFeatureReference reference = componentPersistentId == null
                    ? CadFeatureReference.ExplicitRoot(
                        kind,
                        featurePersistentId)
                    : CadFeatureReference.ExplicitComponent(
                        kind,
                        componentPersistentId,
                        featurePersistentId,
                        configuration);
                if (!identities.Add(reference.IdentityKey))
                {
                    continue;
                }

                entries.Add(new ReferenceGeometryEntry(
                    reference,
                    GetFeatureName(feature),
                    componentPath));
            }
        }

        private static string GetComponentPath(Component2 component)
        {
            try
            {
                return component == null ? string.Empty : component.Name2 ?? string.Empty;
            }
            catch (COMException)
            {
                return "<unavailable component>";
            }
        }

        private static string GetFeatureName(Feature feature)
        {
            try
            {
                return feature == null ? string.Empty : feature.Name ?? string.Empty;
            }
            catch (COMException)
            {
                return "<unavailable reference geometry>";
            }
        }
    }

    internal sealed class CadFeatureReferenceChoice
    {
        public CadFeatureReferenceChoice(CadFeatureReference reference, string displayText)
        {
            Reference = reference ?? throw new ArgumentNullException("reference");
            DisplayText = displayText ?? string.Empty;
        }

        public CadFeatureReference Reference { get; private set; }
        public string DisplayText { get; private set; }

        public override string ToString()
        {
            return DisplayText;
        }
    }
}
