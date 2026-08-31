using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace SW2URDF.URDFExport
{
    public enum ConfigurationSaveStatus
    {
        Saved,
        Unchanged,
        ConfirmationRequired,
        Failed
    }

    public sealed class ConfigurationSaveResult
    {
        private ConfigurationSaveResult(
            ConfigurationSaveStatus status,
            string errorMessage,
            string informationMessage)
        {
            Status = status;
            ErrorMessage = errorMessage ?? string.Empty;
            InformationMessage = informationMessage ?? string.Empty;
        }

        public ConfigurationSaveStatus Status { get; private set; }
        public string ErrorMessage { get; private set; }
        public string InformationMessage { get; private set; }

        public static ConfigurationSaveResult Saved(string informationMessage)
        {
            return new ConfigurationSaveResult(
                ConfigurationSaveStatus.Saved,
                string.Empty,
                informationMessage);
        }

        public static ConfigurationSaveResult Unchanged()
        {
            return new ConfigurationSaveResult(
                ConfigurationSaveStatus.Unchanged,
                string.Empty,
                string.Empty);
        }

        public static ConfigurationSaveResult ConfirmationRequired()
        {
            return new ConfigurationSaveResult(
                ConfigurationSaveStatus.ConfirmationRequired,
                string.Empty,
                string.Empty);
        }

        public static ConfigurationSaveResult Failed(string errorMessage)
        {
            return new ConfigurationSaveResult(
                ConfigurationSaveStatus.Failed,
                errorMessage,
                string.Empty);
        }
    }

    /// <summary>
    /// Class to serialize URDF trees to string so they can be saved to an SW Attribute in the
    /// top-level assembly document.
    ///
    /// Version 2 stores reference geometry by persistent identity. Name-based configurations are
    /// deliberately not migrated because a display name cannot identify nested geometry reliably.
    /// </summary>
    public static class ConfigurationSerialization
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();
        private static readonly object configurationSaveStateLock = new object();
        private static bool configurationSaveInProgress;
        private static readonly object configurationAttributeDefinitionLock = new object();
        private static SldWorks configurationAttributeDefinitionOwner;
        private static AttributeDef configurationAttributeDefinition;

        /// <summary>
        /// Current Serialization version
        /// </summary>
        private const double SerializationVersion = 2.0;

        /// <summary>
        /// The name given to the URDF configuration in the ModelDoc Feature tree. This is displayed to the
        /// user
        /// </summary>
        public const string UrdfConfigurationSwAttributeName = "URDF Export Configuration (v2)";

        private const string UrdfConfigurationRecoveryAttributeName =
            "URDF Export Configuration (v2 recovery)";
        private const string UrdfConfigurationAttributePrefix = "URDF Export Configuration";
        private const string UrdfConfigurationDefinitionPrefix =
            "SW2URDF.Configuration.v2.";

        #region Public Methods

        /// <summary>
        /// Loads the URDF tree from the SW Model Document
        /// </summary>
        /// <param name="model">ModelDoc containing the URDF configuration</param>
        /// <returns>TreeView LinkNode loaded from configuration</returns>
        public static LinkNode LoadBaseNodeFromModel(ModelDoc2 model, out bool error)
        {
            string errorMessage;
            LinkNode baseNode = LoadBaseNodeFromModel(model, out errorMessage);
            error = !string.IsNullOrWhiteSpace(errorMessage);
            return baseNode;
        }

        public static LinkNode LoadBaseNodeFromModel(ModelDoc2 model, out string errorMessage)
        {
            string data = GetConfigTreeData(model, out double configVersion);

            if (string.IsNullOrWhiteSpace(data))
            {
                if (HasCurrentConfigurationAttribute(model))
                {
                    if (HasOnlyPreparedConfigurationSlots(model))
                    {
                        logger.Warn(
                            "Ignoring an interrupted URDF configuration write. " +
                            "No committed version 2 configuration was replaced.");
                        errorMessage = string.Empty;
                        return null;
                    }
                    errorMessage =
                        "The version 2 URDF configuration slots are incomplete or unreadable. " +
                        "They were not overwritten.";
                    logger.Error(errorMessage);
                    return null;
                }
                if (HasLegacyConfiguration(model))
                {
                    errorMessage = "This model contains a name-based URDF configuration from an older " +
                        "exporter. Version 2 cannot identify nested reference geometry from those names. " +
                        "Create and review a new URDF configuration.";
                    logger.Warn(errorMessage);
                    return null;
                }
                errorMessage = string.Empty;
                return null;
            }

            if (!configVersion.Equals(SerializationVersion))
            {
                errorMessage = string.Format(
                    "The saved URDF configuration version ({0}) is not supported by this exporter ({1}). " +
                    "The configuration was not changed.",
                    configVersion,
                    SerializationVersion);
                logger.Error(errorMessage);
                return null;
            }

            LinkNode basenode = DeserializeFromString(data);
            if (basenode == null)
            {
                errorMessage = "The version 2 URDF configuration could not be deserialized. " +
                    "The configuration was not changed.";
                logger.Error(errorMessage);
                return null;
            }
            errorMessage = string.Empty;
            return basenode;
        }

        /// <summary>
        /// Public method to serialize a Treeview LinkNode URDF data to a string and saves it to a SW ModelDoc
        /// </summary>
        /// <param name="swApp">SldWorks application</param>
        /// <param name="model">ModelDoc to which you are saving this data</param>
        /// <param name="BaseNode">TreeView LinkNode which contains the data you are saving</param>
        /// <param name="allowOverwrite">Allow a changed current-format configuration to be overwritten.</param>
        public static ConfigurationSaveResult SaveConfigTreeXML(
            SldWorks swApp,
            ModelDoc2 model,
            LinkNode BaseNode,
            bool allowOverwrite)
        {
            if (!TryBeginConfigurationSave())
            {
                return ConfigurationSaveResult.Failed(
                    "A URDF configuration save is already in progress. " +
                    "Wait for it to finish before saving again.");
            }

            try
            {
                ConfigurationAttributeCandidate current =
                    GetCurrentConfigurationCandidate(model);
                bool hasCurrentAttribute =
                    HasCurrentConfigurationAttribute(model);
                if (current == null &&
                    !hasCurrentAttribute &&
                    HasLegacyConfiguration(model))
                {
                    return ConfigurationSaveResult.Failed(
                        "A name-based URDF configuration from an older exporter still exists. " +
                        "Delete it, create and review a version 2 configuration, then save again.");
                }
                if (current == null &&
                    hasCurrentAttribute &&
                    !HasOnlyPreparedConfigurationSlots(model))
                {
                    return ConfigurationSaveResult.Failed(
                        "The existing version 2 URDF configuration slots are incomplete or unreadable. " +
                        "Saving was stopped to protect it.");
                }
                string oldData = current == null
                    ? string.Empty
                    : current.Data;
                double version = current == null
                    ? 0.0
                    : current.Version;
                if (current != null && !version.Equals(SerializationVersion))
                {
                    return ConfigurationSaveResult.Failed(string.Format(
                        "The existing URDF configuration version ({0}) is not supported by this exporter ({1}). " +
                        "Saving was stopped to protect it.",
                        version,
                        SerializationVersion));
                }
                if (current != null &&
                    !IsConfigurationPayloadReadable(oldData, version))
                {
                    return ConfigurationSaveResult.Failed(
                        "The existing version 2 URDF configuration cannot be deserialized. " +
                        "Saving was stopped to protect it.");
                }

                string newData = SerializeToString(BaseNode);
                if (BaseNode != null && string.IsNullOrEmpty(newData))
                {
                    return ConfigurationSaveResult.Failed(
                        "Serializing this link failed. Please email your maintainer with your SW assembly.");
                }
                if (oldData == newData)
                {
                    return ConfigurationSaveResult.Unchanged();
                }
                if (current != null && !allowOverwrite)
                {
                    return ConfigurationSaveResult.ConfirmationRequired();
                }

                SaveDataToModelDoc(swApp, model, newData);
                return ConfigurationSaveResult.Saved(string.Empty);
            }
            catch (Exception exception)
            {
                logger.Error("Saving the URDF configuration failed.", exception);
                return ConfigurationSaveResult.Failed(
                    "Saving the URDF configuration failed. Export was stopped. " +
                    "See the SW2URDF log for details.");
            }
            finally
            {
                EndConfigurationSave();
            }
        }

        #endregion Public Methods

        #region Private Methods

        internal static bool TryBeginConfigurationSave()
        {
            lock (configurationSaveStateLock)
            {
                if (configurationSaveInProgress)
                {
                    return false;
                }

                configurationSaveInProgress = true;
                return true;
            }
        }

        internal static void EndConfigurationSave()
        {
            lock (configurationSaveStateLock)
            {
                configurationSaveInProgress = false;
            }
        }

        /// <summary>
        /// If someone updates the name of a LinkNode in the Treeview, it needs to be pushed down
        /// to the URDF Link itself.
        /// </summary>
        /// <param name="node">TreeView LinkNode to save properties of to its URDF Link</param>
        private static void SavePropertiesLinkNodeToLink(LinkNode node)
        {
            if (node.Link == null)
            {
                node.Link = new Link();
            }

            node.Link.Name = node.Name;
            node.Link.isIncomplete = node.IsIncomplete;

            foreach (LinkNode child in node.Nodes)
            {
                SavePropertiesLinkNodeToLink(child);
            }
        }

        /// <summary>
        /// Data Contract serialization. All members of an object need to be annotated with a
        /// [DataMember] attribute.
        /// </summary>
        /// <param name="node">TreeView LinkNode to serialize</param>
        /// <returns>A string serialized utilizing DataContract serialization XML scheme</returns>
        internal static string SerializeDraftPayload(LinkNode node)
        {
            return SerializeToString(node);
        }

        internal static LinkNode DeserializeDraftPayload(string data)
        {
            return DeserializeFromString(data);
        }

        internal static bool TryGetSavedConfigurationUtc(
            ModelDoc2 model,
            out DateTime savedUtc)
        {
            savedUtc = default(DateTime);
            try
            {
                ConfigurationAttributeCandidate current =
                    GetCurrentConfigurationCandidate(model);
                if (current == null)
                {
                    return false;
                }

                savedUtc = current.SavedAt.UtcDateTime;
                return true;
            }
            catch (Exception exception)
            {
                logger.Warn("Reading the URDF configuration timestamp failed.", exception);
                return false;
            }
        }

        private static string SerializeToString(LinkNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }
            if (!HasValidPersistentReferences(node))
            {
                logger.Error(
                    "The version 2 URDF configuration contains an invalid persistent reference.");
                return string.Empty;
            }

            LinkNode snapshot = (LinkNode)node.Clone();
            SavePropertiesLinkNodeToLink(snapshot);
            LinkTreeRootJointPolicy.Normalize(snapshot);
            Link link = snapshot.UpdateLinkTree(null);
            string data = "";
            using (MemoryStream stream = new MemoryStream())
            {
                DataContractSerializer ser =
                    new DataContractSerializer(typeof(Link));

                try
                {
                    ser.WriteObject(stream, link);
                    stream.Flush();
                    data = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Position);
                }
                catch (SerializationException e)
                {
                    logger.Error("Serialization failed with exception, returning empty string", e);
                }
            }
            return data;
        }

        /// <summary>
        /// Read a URDF Link from a serialized string
        /// </summary>
        /// <param name="data">Data string to read into a TreeView LinkNode</param>
        /// <returns>Deserialized LinkNode</returns>
        private static LinkNode DeserializeFromString(string data)
        {
            LinkNode baseNode = null;
            if (!string.IsNullOrWhiteSpace(data))
            {
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
                {
                    DataContractSerializer ser =
                        new DataContractSerializer(typeof(Link));

                    try
                    {
                        Link link = (Link)ser.ReadObject(stream);
                        if (!HasValidPersistentReferences(link, new HashSet<Link>()))
                        {
                            throw new SerializationException(
                                "The version 2 URDF configuration contains an invalid persistent reference.");
                        }

                        // By copying this link, we can ensure that all non-serialized properties are setup correctly
                        Link copy = link.Clone();
                        baseNode = new LinkNode(copy);
                        LinkTreeRootJointPolicy.Normalize(baseNode);
                    }
                    catch (SerializationException e)
                    {
                        logger.Error("Deserialization failed with exception, returning empty LinkNode", e);
                    }
                }
            }
            return baseNode;
        }

        private static bool HasValidPersistentReferences(LinkNode node)
        {
            if (node == null || !HasValidPersistentReferences(node.Link))
            {
                return false;
            }

            foreach (LinkNode child in node.Nodes)
            {
                if (!HasValidPersistentReferences(child))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasValidPersistentReferences(
            Link link,
            HashSet<Link> visited)
        {
            if (link == null || !visited.Add(link) ||
                !HasValidPersistentReferences(link))
            {
                return false;
            }
            if (link.Children == null)
            {
                return false;
            }

            foreach (Link child in link.Children)
            {
                if (!HasValidPersistentReferences(child, visited))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasValidPersistentReferences(Link link)
        {
            return link != null &&
                link.FrameReference != null &&
                link.FrameReference.IsValidFor(
                    ReferenceGeometryKind.CoordinateSystem,
                    false) &&
                link.Joint != null &&
                link.Joint.AxisReference != null &&
                link.Joint.AxisReference.IsValidFor(
                    ReferenceGeometryKind.Axis,
                    true);
        }

        /// <summary>
        /// Find the SW attribute that contains the URDF configuration serialized string
        /// </summary>
        /// <param name="model">ModelDoc model to load URDF configuration from</param>
        /// <param name="version">Output parameter of the serialization version</param>
        /// <returns>Serialized data string</returns>
        private static string GetConfigTreeData(ModelDoc2 model, out double version)
        {
            version = 0.0;
            ConfigurationAttributeCandidate current =
                GetCurrentConfigurationCandidate(model);
            if (current != null)
            {
                version = current.Version;
                return current.Data;
            }

            if (HasCurrentConfigurationAttribute(model))
            {
                logger.Warn(
                    "The version 2 URDF configuration slots are incomplete or unreadable.");
            }
            return string.Empty;
        }

        private static bool HasCurrentConfigurationAttribute(ModelDoc2 model)
        {
            return
                FindSWSaveAttribute(
                    model,
                    UrdfConfigurationSwAttributeName) != null ||
                FindSWSaveAttribute(
                    model,
                    UrdfConfigurationRecoveryAttributeName) != null;
        }

        private static bool HasOnlyPreparedConfigurationSlots(ModelDoc2 model)
        {
            SolidWorks.Interop.sldworks.Attribute canonical =
                FindSWSaveAttribute(
                    model,
                    UrdfConfigurationSwAttributeName);
            SolidWorks.Interop.sldworks.Attribute recovery =
                FindSWSaveAttribute(
                    model,
                    UrdfConfigurationRecoveryAttributeName);
            bool hasSlot = false;
            foreach (SolidWorks.Interop.sldworks.Attribute attribute in
                new[] { canonical, recovery })
            {
                if (attribute == null)
                {
                    continue;
                }
                hasSlot = true;
                if (!IsPreparedConfigurationSlot(attribute))
                {
                    return false;
                }
            }
            return hasSlot;
        }

        internal static bool IsPreparedConfigurationSlot(
            SolidWorks.Interop.sldworks.Attribute attribute)
        {
            if (attribute == null)
            {
                return false;
            }

            try
            {
                string[] requiredParameters =
                {
                    "data",
                    "name",
                    "date",
                    "exporterVersion",
                    "revision"
                };
                foreach (string parameterName in requiredParameters)
                {
                    if (attribute.GetParameter(parameterName) == null)
                    {
                        return false;
                    }
                }

                return attribute.GetParameter("revision").GetDoubleValue() == 0.0;
            }
            catch (Exception exception)
            {
                logger.Warn(
                    "Reading an uncommitted URDF configuration slot failed.",
                    exception);
                return false;
            }
        }

        private static bool HasLegacyConfiguration(ModelDoc2 model)
        {
            object[] objects = model.FeatureManager.GetFeatures(true) as object[];
            if (objects == null)
            {
                return false;
            }

            foreach (Feature feature in CommonSwOperations.EnumerateComObjects<Feature>(
                objects,
                "searching for legacy URDF configuration attributes"))
            {
                if (feature.GetTypeName2() != "Attribute")
                {
                    continue;
                }
                SolidWorks.Interop.sldworks.Attribute attribute =
                    CommonSwOperations.TryCastComObject<SolidWorks.Interop.sldworks.Attribute>(
                        feature.GetSpecificFeature2(),
                        "reading a legacy URDF configuration attribute");
                if (attribute == null)
                {
                    continue;
                }
                string featureName = feature.Name ?? string.Empty;
                string definitionName = attribute.GetName() ?? string.Empty;
                bool isCurrent =
                    IsConfigurationSlotName(featureName) ||
                    IsConfigurationSlotName(definitionName);
                bool hasConfigurationPrefix =
                    featureName.StartsWith(
                        UrdfConfigurationAttributePrefix,
                        StringComparison.Ordinal) ||
                    definitionName.StartsWith(
                        UrdfConfigurationAttributePrefix,
                        StringComparison.Ordinal);
                if (!isCurrent && hasConfigurationPrefix)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryReadConfigurationAttribute(
            SolidWorks.Interop.sldworks.Attribute swAttribute,
            out string data,
            out double version)
        {
            data = string.Empty;
            version = 0.0;
            if (swAttribute == null)
            {
                return false;
            }

            try
            {
                Parameter dataParameter = swAttribute.GetParameter("data");
                Parameter versionParameter = swAttribute.GetParameter("exporterVersion");
                if (dataParameter == null || versionParameter == null)
                {
                    return false;
                }
                data = dataParameter.GetStringValue() ?? string.Empty;
                version = versionParameter.GetDoubleValue();
                if (string.IsNullOrWhiteSpace(data))
                {
                    return false;
                }
                logger.Info(string.Format(
                    "URDF configuration found: attribute={0}, version={1}, characters={2}",
                    swAttribute.GetName(),
                    version,
                    data.Length));
                return true;
            }
            catch (Exception exception)
            {
                logger.Warn("Reading a URDF configuration attribute failed.", exception);
                data = string.Empty;
                version = 0.0;
                return false;
            }
        }

        private static ConfigurationAttributeCandidate
            GetCurrentConfigurationCandidate(ModelDoc2 model)
        {
            TryCreateConfigurationCandidate(
                FindSWSaveAttribute(
                    model,
                    UrdfConfigurationSwAttributeName),
                UrdfConfigurationSwAttributeName,
                out ConfigurationAttributeCandidate canonical);
            TryCreateConfigurationCandidate(
                FindSWSaveAttribute(
                    model,
                    UrdfConfigurationRecoveryAttributeName),
                UrdfConfigurationRecoveryAttributeName,
                out ConfigurationAttributeCandidate recovery);

            if (canonical == null)
            {
                return recovery;
            }
            if (recovery == null)
            {
                return canonical;
            }
            if (canonical.Revision != recovery.Revision)
            {
                return canonical.Revision > recovery.Revision
                    ? canonical
                    : recovery;
            }
            if (canonical.SavedAt != recovery.SavedAt)
            {
                return canonical.SavedAt > recovery.SavedAt
                    ? canonical
                    : recovery;
            }
            return canonical;
        }

        private static bool TryCreateConfigurationCandidate(
            SolidWorks.Interop.sldworks.Attribute attribute,
            string attributeName,
            out ConfigurationAttributeCandidate candidate)
        {
            candidate = null;
            if (!TryReadConfigurationAttribute(
                    attribute,
                    out string data,
                    out double version) ||
                !version.Equals(SerializationVersion) ||
                !IsConfigurationPayloadReadable(data, version))
            {
                return false;
            }

            try
            {
                Parameter revisionParameter =
                    attribute.GetParameter("revision");
                Parameter dateParameter = attribute.GetParameter("date");
                if (revisionParameter == null || dateParameter == null)
                {
                    return false;
                }
                double revision = revisionParameter.GetDoubleValue();
                if (Double.IsNaN(revision) ||
                    Double.IsInfinity(revision) ||
                    revision < 1.0 ||
                    revision > 9007199254740991D ||
                    Math.Floor(revision) != revision)
                {
                    return false;
                }
                string date = dateParameter.GetStringValue() ?? string.Empty;
                if (!DateTimeOffset.TryParse(
                        date,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset savedAt))
                {
                    return false;
                }

                candidate = new ConfigurationAttributeCandidate
                {
                    AttributeName = attributeName,
                    Data = data,
                    Version = version,
                    Revision = revision,
                    SavedAt = savedAt
                };
                return true;
            }
            catch (Exception exception)
            {
                logger.Warn(
                    "Reading a committed URDF configuration slot failed.",
                    exception);
                return false;
            }
        }

        private static bool IsConfigurationPayloadReadable(string data, double version)
        {
            if (!version.Equals(SerializationVersion))
            {
                return false;
            }
            try
            {
                return DeserializeFromString(data) != null;
            }
            catch (Exception exception)
            {
                logger.Warn(
                    "The version 2 URDF configuration payload could not be read.",
                    exception);
                return false;
            }
        }

        /// <summary>
        ///  Iterates through features in ModelDoc to find a feature of the correct name
        /// </summary>
        /// <param name="model">ModelDoc of features to iterate through</param>
        /// <param name="featName">The name of the feature to get</param>
        /// <returns>The SolidWorks Feature if found, null otherwise</returns>
        private static Feature GetFeatureAttributeByName(ModelDoc2 model, string featName)
        {
            Object[] objects = model.FeatureManager.GetFeatures(true);
            if (objects == null)
            {
                return null;
            }
            foreach (Feature feature in CommonSwOperations.EnumerateComObjects<Feature>(
                objects,
                "searching URDF configuration attributes"))
            {
                if (feature.GetTypeName2() == "Attribute")
                {
                    SolidWorks.Interop.sldworks.Attribute att =
                        CommonSwOperations.TryCastComObject<SolidWorks.Interop.sldworks.Attribute>(
                            feature.GetSpecificFeature2(),
                            "reading a URDF configuration attribute");
                    if (att != null &&
                        (string.Equals(
                             feature.Name,
                             featName,
                             StringComparison.Ordinal) ||
                         string.Equals(
                             att.GetName(),
                             featName,
                             StringComparison.Ordinal)))
                    {
                        return feature;
                    }
                }
            }
            return null;
        }

        private static bool IsConfigurationSlotName(string name)
        {
            return string.Equals(
                    name,
                    UrdfConfigurationSwAttributeName,
                    StringComparison.Ordinal) ||
                string.Equals(
                    name,
                    UrdfConfigurationRecoveryAttributeName,
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// Finds existing SWSave Attribute in a ModelDoc
        /// </summary>
        /// <param name="model">ModelDoc model to search through</param>
        /// <param name="name">Name of attribute to find</param>
        /// <returns>SolidWorks Attribute if found, otherwise null</returns>
        private static SolidWorks.Interop.sldworks.Attribute
            FindSWSaveAttribute(ModelDoc2 model, string name)
        {
            Feature feature = GetFeatureAttributeByName(model, name);

            if (feature == null)
            {
                return null;
            }
            return (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
        }

        /// <summary>
        /// Builds a SW Attribute for saving our serialized data
        /// </summary>
        /// <param name="swApp">SolidWorks Application to build Feature Definition</param>
        /// <param name="model">ModelDoc in which this attribute will be saved</param>
        /// <param name="name">Name of the attribute to create</param>
        /// <returns>Constructed SolidWorks Attribute</returns>
        private static SolidWorks.Interop.sldworks.Attribute
            CreateSWSaveAttribute(SldWorks swApp, ModelDoc2 model, string name)
        {
            SolidWorks.Interop.sldworks.Attribute existingAttribute =
                FindSWSaveAttribute(model, name);
            if (existingAttribute != null)
            {
                EnsureConfigurationAttributeSchema(existingAttribute, name);
                return existingAttribute;
            }

            return CreateNewConfigurationAttribute(swApp, model, name);
        }

        internal static SolidWorks.Interop.sldworks.Attribute
            CreateNewConfigurationAttribute(
                SldWorks swApp,
                ModelDoc2 model,
                string name)
        {
            if (swApp == null)
            {
                throw new ArgumentNullException("swApp");
            }
            if (model == null)
            {
                throw new ArgumentNullException("model");
            }

            int ConfigurationOptions =
                (int)swInConfigurationOpts_e.swAllConfiguration;
            int CreationOptions = string.Equals(
                name,
                UrdfConfigurationRecoveryAttributeName,
                StringComparison.Ordinal)
                ? 1
                : 0;
            AttributeDef saveConfigurationAttributeDef =
                GetOrCreateConfigurationAttributeDefinition(swApp);
            SolidWorks.Interop.sldworks.Attribute saveExporterAttribute =
                saveConfigurationAttributeDef.CreateInstance5(
                    model, null, name, CreationOptions, ConfigurationOptions);
            if (saveExporterAttribute == null)
            {
                throw new InvalidOperationException(
                    "SolidWorks did not create the URDF configuration attribute instance '" +
                    name + "'.");
            }
            EnsureConfigurationAttributeSchema(saveExporterAttribute, name);
            return saveExporterAttribute;
        }

        private static AttributeDef GetOrCreateConfigurationAttributeDefinition(
            SldWorks swApp)
        {
            lock (configurationAttributeDefinitionLock)
            {
                if (ReferenceEquals(configurationAttributeDefinitionOwner, swApp) &&
                    configurationAttributeDefinition != null)
                {
                    return configurationAttributeDefinition;
                }

                string definitionName =
                    UrdfConfigurationDefinitionPrefix + Guid.NewGuid().ToString("N");
                AttributeDef definition =
                    swApp.DefineAttribute(definitionName) as AttributeDef;
                if (definition == null)
                {
                    throw new InvalidOperationException(
                        "SolidWorks did not provide the URDF configuration attribute definition '" +
                        definitionName + "'.");
                }

                // A unique definition prevents a failed, partially initialized AttributeDef from
                // poisoning retries in the same SolidWorks session. Cache only after every
                // parameter and Register have succeeded.
                AddRequiredAttributeParameter(
                    definition,
                    "data",
                    swParamType_e.swParamTypeString,
                    0,
                    0);
                AddRequiredAttributeParameter(
                    definition,
                    "name",
                    swParamType_e.swParamTypeString,
                    0,
                    0);
                AddRequiredAttributeParameter(
                    definition,
                    "date",
                    swParamType_e.swParamTypeString,
                    0,
                    0);
                AddRequiredAttributeParameter(
                    definition,
                    "exporterVersion",
                    swParamType_e.swParamTypeDouble,
                    SerializationVersion,
                    0);
                AddRequiredAttributeParameter(
                    definition,
                    "revision",
                    swParamType_e.swParamTypeDouble,
                    0,
                    0);
                if (!definition.Register())
                {
                    throw new InvalidOperationException(
                        "SolidWorks refused to register the URDF configuration attribute definition '" +
                        definitionName + "'.");
                }

                configurationAttributeDefinitionOwner = swApp;
                configurationAttributeDefinition = definition;
                return definition;
            }
        }

        internal static void ResetConfigurationAttributeDefinitionCache(
            SldWorks swApp)
        {
            lock (configurationAttributeDefinitionLock)
            {
                if (swApp != null &&
                    configurationAttributeDefinitionOwner != null &&
                    !ReferenceEquals(configurationAttributeDefinitionOwner, swApp))
                {
                    return;
                }

                configurationAttributeDefinition = null;
                configurationAttributeDefinitionOwner = null;
            }
        }

        private static void AddRequiredAttributeParameter(
            AttributeDef definition,
            string parameterName,
            swParamType_e parameterType,
            double defaultValue,
            int parameterOptions)
        {
            if (!definition.AddParameter(
                    parameterName,
                    (int)parameterType,
                    defaultValue,
                    parameterOptions))
            {
                throw new InvalidOperationException(
                    "SolidWorks refused to add URDF configuration parameter '" +
                    parameterName + "' to its attribute definition.");
            }
        }

        private static void EnsureConfigurationAttributeSchema(
            SolidWorks.Interop.sldworks.Attribute attribute,
            string attributeName)
        {
            string[] requiredParameters =
            {
                "data",
                "name",
                "date",
                "exporterVersion",
                "revision"
            };
            foreach (string parameterName in requiredParameters)
            {
                if (attribute.GetParameter(parameterName) == null)
                {
                    try
                    {
                        attribute.Delete(true);
                    }
                    catch (COMException)
                    {
                    }
                    throw new InvalidOperationException(
                        "The SolidWorks URDF configuration attribute '" +
                        attributeName + "' is missing parameter '" +
                        parameterName + "'.");
                }
            }
        }

        /// <summary>
        /// Saves a string of data to the SWModelDoc
        /// </summary>
        /// <param name="swApp">SolidWorks Application</param>
        /// <param name="model">ModelDoc model to save data string to</param>
        /// <param name="data">string to save</param>
        /// <param name="attributeName">Name of attribute to save to</param>
        private static void SaveDataToModelDoc(SldWorks swApp, ModelDoc2 model,
            string data)
        {
            ConfigurationAttributeCandidate current =
                GetCurrentConfigurationCandidate(model);
            double nextRevision = current == null
                ? 1.0
                : current.Revision + 1.0;
            if (nextRevision > 9007199254740991D)
            {
                throw new InvalidOperationException(
                    "The URDF configuration revision exceeded the exact integer range.");
            }

            string savedAt = DateTimeOffset.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture);
            if (current != null &&
                string.Equals(
                    current.AttributeName,
                    UrdfConfigurationRecoveryAttributeName,
                    StringComparison.Ordinal))
            {
                WriteAndValidateConfigurationSlot(
                    swApp,
                    model,
                    UrdfConfigurationSwAttributeName,
                    data,
                    savedAt,
                    nextRevision);
                DeleteRecoveryAttribute(model);
                return;
            }

            WriteAndValidateConfigurationSlot(
                swApp,
                model,
                UrdfConfigurationRecoveryAttributeName,
                data,
                savedAt,
                nextRevision);
            WriteAndValidateConfigurationSlot(
                swApp,
                model,
                UrdfConfigurationSwAttributeName,
                data,
                savedAt,
                nextRevision);
            DeleteRecoveryAttribute(model);
        }

        private static void WriteAndValidateConfigurationSlot(
            SldWorks swApp,
            ModelDoc2 model,
            string attributeName,
            string data,
            string savedAt,
            double revision)
        {
            SolidWorks.Interop.sldworks.Attribute attribute =
                CreateSWSaveAttribute(swApp, model, attributeName);
            WriteSaveAttribute(
                attribute,
                data,
                "config1",
                savedAt,
                SerializationVersion,
                revision);

            SolidWorks.Interop.sldworks.Attribute persisted =
                FindSWSaveAttribute(model, attributeName);
            if (!TryCreateConfigurationCandidate(
                    persisted,
                    attributeName,
                    out ConfigurationAttributeCandidate candidate) ||
                candidate.Data != data ||
                !candidate.Version.Equals(SerializationVersion) ||
                !candidate.Revision.Equals(revision) ||
                !string.Equals(
                    candidate.SavedAt.ToString(
                        "O",
                        CultureInfo.InvariantCulture),
                    savedAt,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "SolidWorks did not persist and validate the complete URDF " +
                    "configuration slot '" + attributeName + "'.");
            }
        }

        private static void DeleteRecoveryAttribute(ModelDoc2 model)
        {
            SolidWorks.Interop.sldworks.Attribute recovery =
                FindSWSaveAttribute(
                    model,
                    UrdfConfigurationRecoveryAttributeName);
            if (recovery != null && !recovery.Delete(true))
            {
                logger.Warn(
                    "The validated recovery configuration could not be removed. " +
                    "It remains available as a redundant committed copy.");
            }
        }

        private static void WriteSaveAttribute(
            SolidWorks.Interop.sldworks.Attribute attribute,
            string data,
            string name,
            string date,
            double exporterVersion,
            double revision)
        {
            int configurationOptions = (int)swInConfigurationOpts_e.swAllConfiguration;
            // Invalidate an existing slot before changing any value. Without this
            // prepare marker, a crash could pair new data with the slot's old valid
            // revision and make an interrupted write look committed.
            SetDoubleParameter(
                attribute,
                "revision",
                0.0,
                configurationOptions);
            SetOptionalStringParameter(attribute, "name", name, configurationOptions);
            SetDoubleParameter(
                attribute,
                "exporterVersion",
                exporterVersion,
                configurationOptions);
            SetStringParameter(attribute, "data", data, configurationOptions);
            SetStringParameter(attribute, "date", date, configurationOptions);
            // The nonzero revision is the commit marker and must be written last.
            SetDoubleParameter(
                attribute,
                "revision",
                revision,
                configurationOptions);
        }

        private static void SetDoubleParameter(
            SolidWorks.Interop.sldworks.Attribute attribute,
            string parameterName,
            double value,
            int configurationOptions)
        {
            Parameter parameter = attribute.GetParameter(parameterName);
            if (parameter == null ||
                !parameter.SetDoubleValue2(value, configurationOptions, ""))
            {
                throw new InvalidOperationException(
                    "SolidWorks refused to write URDF configuration parameter '" +
                    parameterName + "'.");
            }
        }

        private static void SetOptionalStringParameter(
            SolidWorks.Interop.sldworks.Attribute attribute,
            string parameterName,
            string value,
            int configurationOptions)
        {
            Parameter parameter = attribute.GetParameter(parameterName);
            if (parameter != null &&
                !parameter.SetStringValue2(value, configurationOptions, ""))
            {
                logger.Warn("SolidWorks did not update optional URDF configuration parameter '" +
                    parameterName + "'.");
            }
        }

        private static void SetStringParameter(
            SolidWorks.Interop.sldworks.Attribute attribute,
            string parameterName,
            string value,
            int configurationOptions)
        {
            Parameter parameter = attribute.GetParameter(parameterName);
            if (parameter == null ||
                !parameter.SetStringValue2(value, configurationOptions, ""))
            {
                throw new InvalidOperationException(
                    "SolidWorks refused to write URDF configuration parameter '" +
                    parameterName + "'.");
            }
        }

        private sealed class ConfigurationAttributeCandidate
        {
            public string AttributeName { get; set; }
            public string Data { get; set; }
            public double Version { get; set; }
            public double Revision { get; set; }
            public DateTimeOffset SavedAt { get; set; }
        }

        #endregion Private Methods
    }
}
