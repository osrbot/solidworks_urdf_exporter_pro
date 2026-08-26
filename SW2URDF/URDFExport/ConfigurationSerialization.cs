using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.Legacy;
using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

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
    /// Any changes to the serialization scheme need to support backwards compatibility in some way.
    /// At least in regards to reading the old configuration. I'm also choosing to save any old xml
    /// formats to a second attribute in case they need to be reloaded.
    /// </summary>
    public static class ConfigurationSerialization
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        /// <summary>
        /// Current Serialization version
        /// </summary>
        private const double SerializationVersion = 1.5;

        /// <summary>
        /// Previous versions of serialization were set at 1 in the SW Document. This is
        /// used to denote the version at which the serialization scheme was modified.
        /// </summary>
        private const double MinDataContractVersion = 1.3;

        /// <summary>
        /// The name given to the URDF configuration in the ModelDoc Feature tree. This is displayed to the
        /// user
        /// </summary>
        public const string UrdfConfigurationSwAttributeName= "URDF Export Configuration (v1.5)";

        public static List<string> PREVIOUS_URDF_CONFIGURATION_NAMES = new List<string>() {
            "URDF Export Configuration (v1.4)",
            "URDF Export Configuration (v1.3)",
            "URDF Export Configuration"
            };

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

            LinkNode basenode;
            if (configVersion > SerializationVersion)
            {
                errorMessage = "The configuration saved in this model is newer than what this " +
                    "exporter supports " + string.Format("({0} > {1})", configVersion, SerializationVersion) +
                    ". Please update your exporter version.";
                logger.Error(errorMessage);
                return null;
            }

            if (configVersion >= MinDataContractVersion)
            {
                basenode = DeserializeFromString(data);
            }
            else
            {
                basenode = LoadConfigFromStringXML(data);
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
            try
            {
                string oldData = GetConfigTreeData(model, out double version);
                if (oldData.Length > 0 && version > SerializationVersion)
                {
                    logger.Error(string.Format(
                        "Refusing to overwrite newer URDF configuration version {0} with exporter version {1}.",
                        version,
                        SerializationVersion));
                    return ConfigurationSaveResult.Failed(
                        "The saved URDF configuration is newer than this exporter supports. " +
                        "Saving was stopped to protect the existing configuration.");
                }
                bool requiresUpgrade = oldData.Length > 0 && version < SerializationVersion;
                string informationMessage = string.Empty;
                SolidWorks.Interop.sldworks.Attribute currentAttribute =
                    FindSWSaveAttribute(model, UrdfConfigurationSwAttributeName);
                bool requiresStorageMigration = oldData.Length > 0 && currentAttribute == null;
                if (currentAttribute != null)
                {
                    SaveAttributeSnapshot currentSnapshot;
                    requiresStorageMigration =
                        !SaveAttributeSnapshot.TryCapture(currentAttribute, out currentSnapshot) ||
                        !currentSnapshot.IsComplete;
                }
                if (requiresUpgrade)
                {
                    informationMessage = "You have a URDF configuration with an outdated save format. It was automatically " +
                        "upgraded to the latest version and saved to the configuration named \"" +
                        UrdfConfigurationSwAttributeName + "\". " +
                        "Old configurations can be deleted at your convenience.";
                }
                if (requiresStorageMigration)
                {
                    logger.Info(
                        "The URDF configuration storage needs migration to the current complete attribute.");
                }

                string newData = SerializeToString(BaseNode);
                if (BaseNode != null && string.IsNullOrEmpty(newData))
                {
                    return ConfigurationSaveResult.Failed(
                        "Serializing this link failed. Please email your maintainer with your SW assembly.");
                }
                if (oldData == newData && !requiresUpgrade && !requiresStorageMigration)
                {
                    return ConfigurationSaveResult.Unchanged();
                }
                if (!allowOverwrite && !requiresUpgrade && !requiresStorageMigration)
                {
                    return ConfigurationSaveResult.ConfirmationRequired();
                }

                SaveDataToModelDoc(swApp, model, newData);
                return ConfigurationSaveResult.Saved(informationMessage);
            }
            catch (Exception exception)
            {
                logger.Error("Saving the URDF configuration failed.", exception);
                return ConfigurationSaveResult.Failed(
                    "Saving the URDF configuration failed. Export was stopped. " +
                    "See the SW2URDF log for details.");
            }
        }

        #endregion Public Methods

        #region Private Methods

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

        private static string SerializeToString(LinkNode node)
        {
            if (node == null)
            {
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

        /// <summary>
        /// Load from the deprecated XML serialized scheme
        /// </summary>
        /// <param name="data">Data string to deserialize using XMLSerializer</param>
        /// <returns>TreeView LinkNode</returns>
        private static LinkNode LoadConfigFromStringXML(string data)
        {
            LinkNode baseNode = null;
            if (!string.IsNullOrWhiteSpace(data))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(SerialNode));
                XmlTextReader textReader = new XmlTextReader(new StringReader(data));
                // Not reading external files, so this can set to prohibit. Resolves CA3075
                textReader.DtdProcessing = DtdProcessing.Prohibit;
                SerialNode sNode = (SerialNode)serializer.Deserialize(textReader);
                textReader.Close();

                baseNode = sNode.BuildLinkNodeFromSerialNode();
                LinkTreeRootJointPolicy.Normalize(baseNode);
            }
            return baseNode;
        }

        /// <summary>
        /// Find the SW attribute that contains the URDF configuration serialized string
        /// </summary>
        /// <param name="model">ModelDoc model to load URDF configuration from</param>
        /// <param name="version">Output parameter of the serialization version</param>
        /// <returns>Serialized data string</returns>
        private static string GetConfigTreeData(ModelDoc2 model, out double version)
        {
            string data;
            version = 0.0;

            SolidWorks.Interop.sldworks.Attribute currentAttribute =
                FindSWSaveAttribute(model, UrdfConfigurationSwAttributeName);
            if (TryReadConfigurationAttribute(currentAttribute, out data, out version))
            {
                return data;
            }

            if (currentAttribute != null)
            {
                logger.Warn(
                    "Ignoring an incomplete current URDF configuration attribute and checking previous versions.");
            }

            foreach (string configurationName in PREVIOUS_URDF_CONFIGURATION_NAMES)
            {
                SolidWorks.Interop.sldworks.Attribute previousAttribute =
                    FindSWSaveAttribute(model, configurationName);
                if (TryReadConfigurationAttribute(previousAttribute, out data, out version))
                {
                    return data;
                }
                if (previousAttribute != null)
                {
                    logger.Warn(
                        "Ignoring an unreadable previous URDF configuration attribute: " +
                        configurationName);
                }
            }
            return string.Empty;
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
                if (version <= SerializationVersion &&
                    !IsConfigurationPayloadReadable(data, version))
                {
                    data = string.Empty;
                    version = 0.0;
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

        private static bool IsConfigurationPayloadReadable(string data, double version)
        {
            try
            {
                LinkNode node = version >= MinDataContractVersion
                    ? DeserializeFromString(data)
                    : LoadConfigFromStringXML(data);
                return node != null;
            }
            catch (Exception exception)
            {
                logger.Warn(
                    "URDF configuration payload could not be read; an older attribute will be checked.",
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
                    if (att != null && att.GetName() == featName)
                    {
                        return feature;
                    }
                }
            }
            return null;
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
                return existingAttribute;
            }

            int ConfigurationOptions = (int)swInConfigurationOpts_e.swAllConfiguration;

            int Options = 0;
            AttributeDef saveConfigurationAttributeDef;
            saveConfigurationAttributeDef = swApp.DefineAttribute(UrdfConfigurationSwAttributeName);

            saveConfigurationAttributeDef.AddParameter(
                "data", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "name", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "date", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "exporterVersion", (int)swParamType_e.swParamTypeDouble, SerializationVersion, Options);
            saveConfigurationAttributeDef.Register();

            SolidWorks.Interop.sldworks.Attribute saveExporterAttribute =
                saveConfigurationAttributeDef.CreateInstance5(
                    model, null, UrdfConfigurationSwAttributeName, Options, ConfigurationOptions);
            return saveExporterAttribute;
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
            SolidWorks.Interop.sldworks.Attribute existingAttribute =
                FindSWSaveAttribute(model, UrdfConfigurationSwAttributeName);
            SaveAttributeSnapshot previous = null;
            SolidWorks.Interop.sldworks.Attribute targetAttribute = null;
            bool transactionMutated = false;

            try
            {
                bool replaceExistingAttribute = false;
                if (existingAttribute != null)
                {
                    if (!SaveAttributeSnapshot.TryCapture(existingAttribute, out previous))
                    {
                        throw new InvalidOperationException(
                            "The existing URDF configuration cannot be snapshotted safely. " +
                            "It was left unchanged.");
                    }
                    replaceExistingAttribute = !previous.IsComplete;
                }
                if (replaceExistingAttribute)
                {
                    logger.Warn(
                        "Replacing an incomplete current URDF configuration attribute.");
                    if (!existingAttribute.Delete(true))
                    {
                        throw new InvalidOperationException(
                            "SolidWorks refused to delete the incomplete URDF configuration attribute.");
                    }
                    transactionMutated = true;
                    existingAttribute = null;
                    if (FindSWSaveAttribute(model, UrdfConfigurationSwAttributeName) != null)
                    {
                        throw new InvalidOperationException(
                            "The incomplete URDF configuration attribute still exists after deletion.");
                    }
                }

                if (existingAttribute == null)
                {
                    transactionMutated = true;
                }
                targetAttribute = CreateSWSaveAttribute(
                    swApp,
                    model,
                    UrdfConfigurationSwAttributeName);
                transactionMutated = true;
                WriteSaveAttribute(
                    targetAttribute,
                    data,
                    "config1",
                    DateTime.Now.ToString("O"),
                    SerializationVersion);

                SolidWorks.Interop.sldworks.Attribute persistedAttribute =
                    FindSWSaveAttribute(model, UrdfConfigurationSwAttributeName);
                string persistedData;
                double persistedVersion;
                if (!TryReadConfigurationAttribute(
                        persistedAttribute,
                        out persistedData,
                        out persistedVersion) ||
                    persistedData != data || persistedVersion != SerializationVersion)
                {
                    throw new InvalidOperationException(
                        "SolidWorks did not persist the complete URDF configuration.");
                }
            }
            catch
            {
                if (transactionMutated)
                {
                    try
                    {
                        if (previous == null)
                        {
                            SolidWorks.Interop.sldworks.Attribute createdAttribute =
                                targetAttribute ?? FindSWSaveAttribute(
                                    model,
                                    UrdfConfigurationSwAttributeName);
                            if (createdAttribute != null && !createdAttribute.Delete(true))
                            {
                                throw new InvalidOperationException(
                                    "SolidWorks refused to delete the incomplete URDF configuration attribute.");
                            }
                            if (FindSWSaveAttribute(model, UrdfConfigurationSwAttributeName) != null)
                            {
                                throw new InvalidOperationException(
                                    "The incomplete URDF configuration attribute still exists after rollback.");
                            }
                        }
                        else
                        {
                            SolidWorks.Interop.sldworks.Attribute rollbackAttribute =
                                existingAttribute ?? CreateSWSaveAttribute(
                                    swApp,
                                    model,
                                    UrdfConfigurationSwAttributeName);
                            WriteSaveAttribute(
                                rollbackAttribute,
                                previous.Data,
                                previous.Name,
                                previous.Date,
                                previous.ExporterVersion);
                            if (!previous.Matches(rollbackAttribute))
                            {
                                throw new InvalidOperationException(
                                    "SolidWorks did not restore the previous URDF configuration attribute.");
                            }
                        }
                    }
                    catch (Exception rollbackException)
                    {
                        logger.Error(
                            "Rolling back the URDF configuration attribute failed.",
                            rollbackException);
                    }
                }
                throw;
            }
        }

        private static void WriteSaveAttribute(
            SolidWorks.Interop.sldworks.Attribute attribute,
            string data,
            string name,
            string date,
            double exporterVersion)
        {
            int configurationOptions = (int)swInConfigurationOpts_e.swAllConfiguration;
            SetStringParameter(attribute, "data", data, configurationOptions);
            SetStringParameter(attribute, "name", name, configurationOptions);
            SetStringParameter(attribute, "date", date, configurationOptions);

            Parameter versionParameter = attribute.GetParameter("exporterVersion");
            if (versionParameter == null ||
                !versionParameter.SetDoubleValue2(exporterVersion, configurationOptions, ""))
            {
                throw new InvalidOperationException(
                    "SolidWorks refused to write URDF configuration parameter 'exporterVersion'.");
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

        private sealed class SaveAttributeSnapshot
        {
            public string Data { get; private set; }
            public string Name { get; private set; }
            public string Date { get; private set; }
            public double ExporterVersion { get; private set; }
            public bool IsComplete { get; private set; }

            public static bool TryCapture(
                SolidWorks.Interop.sldworks.Attribute attribute,
                out SaveAttributeSnapshot snapshot)
            {
                snapshot = null;
                Parameter data = attribute.GetParameter("data");
                Parameter version = attribute.GetParameter("exporterVersion");
                if (data == null || version == null)
                {
                    return false;
                }

                string dataValue = data.GetStringValue() ?? string.Empty;
                double versionValue = version.GetDoubleValue();
                Parameter name = attribute.GetParameter("name");
                Parameter date = attribute.GetParameter("date");
                string nameValue = name == null ? string.Empty : name.GetStringValue();
                string dateValue = date == null ? string.Empty : date.GetStringValue();
                bool payloadReadable = !string.IsNullOrWhiteSpace(dataValue) &&
                    (versionValue > SerializationVersion ||
                     ConfigurationSerialization.IsConfigurationPayloadReadable(
                         dataValue,
                         versionValue));

                snapshot = new SaveAttributeSnapshot
                {
                    Data = dataValue,
                    Name = string.IsNullOrWhiteSpace(nameValue) ? "config1" : nameValue,
                    Date = dateValue ?? string.Empty,
                    ExporterVersion = versionValue,
                    IsComplete = payloadReadable &&
                        !string.IsNullOrWhiteSpace(nameValue) &&
                        !string.IsNullOrWhiteSpace(dateValue)
                };
                return true;
            }

            public bool Matches(SolidWorks.Interop.sldworks.Attribute attribute)
            {
                if (attribute == null)
                {
                    return false;
                }
                Parameter data = attribute.GetParameter("data");
                Parameter name = attribute.GetParameter("name");
                Parameter date = attribute.GetParameter("date");
                Parameter version = attribute.GetParameter("exporterVersion");
                return data != null && name != null && date != null && version != null &&
                    string.Equals(Data, data.GetStringValue(), StringComparison.Ordinal) &&
                    string.Equals(Name, name.GetStringValue(), StringComparison.Ordinal) &&
                    string.Equals(Date, date.GetStringValue(), StringComparison.Ordinal) &&
                    ExporterVersion.Equals(version.GetDoubleValue());
            }
        }

        #endregion Private Methods
    }
}
