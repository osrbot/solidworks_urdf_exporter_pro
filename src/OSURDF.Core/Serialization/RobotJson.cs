using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using OSURDF.Core.Model;

namespace OSURDF.Core.Serialization
{
    public static class RobotJson
    {
        private static readonly JsonSerializerSettings Settings = CreateSettings(MissingMemberHandling.Ignore);
        private static readonly JsonSerializerSettings StrictSettings = CreateSettings(MissingMemberHandling.Error);

        public static string Serialize(RobotDocument robot)
        {
            if (robot == null)
            {
                throw new ArgumentNullException(nameof(robot));
            }

            JObject root = JObject.FromObject(robot, JsonSerializer.Create(Settings));
            ValidateFiniteNumbers(root);
            JObject profiles = root["profiles"] as JObject;
            SortObjectProperty(profiles?["isaac"] as JObject, "packageMappings");
            SortObjectProperty(profiles?["isaacLab"] as JObject, "jointPositions");
            SortObjectProperty(profiles?["isaacLab"] as JObject, "jointVelocities");
            return root.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";
        }

        public static RobotDocument Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("Robot JSON is empty.");
            }

            JObject root;
            try
            {
                using (StringReader text = new StringReader(json))
                using (JsonTextReader reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
                {
                    root = JObject.Load(
                        reader,
                        new JsonLoadSettings
                        {
                            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                            LineInfoHandling = LineInfoHandling.Load
                        });
                    if (reader.Read())
                    {
                        throw new JsonReaderException("Additional JSON content follows the robot document.");
                    }
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Robot JSON could not be parsed.", exception);
            }

            ValidateFiniteNumbers(root);

            int sourceVersion = ReadSourceVersion(root);
            if (sourceVersion == RobotSchema.CurrentVersion)
            {
                ValidateCurrentEnvelope(root);
            }
            root = RobotSchemaMigrator.Migrate(root);
            RobotDocument robot;
            try
            {
                robot = root.ToObject<RobotDocument>(JsonSerializer.Create(
                    sourceVersion == RobotSchema.CurrentVersion ? StrictSettings : Settings));
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Robot JSON does not match its declared schema.", exception);
            }
            if (robot == null)
            {
                throw new InvalidDataException("Robot JSON did not contain a robot document.");
            }
            if (sourceVersion == RobotSchema.CurrentVersion)
            {
                JObject canonical = JObject.FromObject(robot, JsonSerializer.Create(Settings));
                ValidateTokenTypes(root, canonical, "$");
            }
            return robot;
        }

        public static RobotDocument Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A robot JSON path is required.", nameof(path));
            }
            return Deserialize(File.ReadAllText(path, Encoding.UTF8));
        }

        public static Ros2ControlProfile DeserializeRos2ControlProfile(string json)
        {
            JObject profile = ParseStandaloneProfile(json, "ros2_control");
            JObject envelope = JObject.Parse(Serialize(new RobotDocument()));
            envelope["profiles"]["ros2"]["ros2Control"] = profile;
            return Deserialize(envelope.ToString(Formatting.None)).Profiles.Ros2.Ros2Control;
        }

        public static IsaacLabProfile DeserializeIsaacLabProfile(string json)
        {
            JObject profile = ParseStandaloneProfile(json, "Isaac Lab");
            JObject envelope = JObject.Parse(Serialize(new RobotDocument()));
            envelope["profiles"]["isaacLab"] = profile;
            return Deserialize(envelope.ToString(Formatting.None)).Profiles.IsaacLab;
        }

        public static void Write(string path, RobotDocument robot, bool createBackup = true)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A robot JSON path is required.", nameof(path));
            }

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("The robot JSON path has no parent directory.");
            }
            Directory.CreateDirectory(directory);
            if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Robot JSON output must not be a symbolic link or reparse point: " + fullPath);
            }
            string configuredBackupPath = createBackup ? fullPath + ".bak" : null;
            if (!string.IsNullOrWhiteSpace(configuredBackupPath) && File.Exists(configuredBackupPath) &&
                (File.GetAttributes(configuredBackupPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Robot JSON backup must not be a symbolic link or reparse point: " + configuredBackupPath);
            }

            string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            Exception writeFailure = null;
            try
            {
                File.WriteAllText(temporaryPath, Serialize(robot), new UTF8Encoding(false));
                RobotDocument verified = Read(temporaryPath);
                if (verified.SchemaVersion != RobotSchema.CurrentVersion)
                {
                    throw new InvalidDataException("Robot JSON verification returned an unexpected schema version.");
                }

                if (File.Exists(fullPath))
                {
                    string backupPath = configuredBackupPath;
                    try
                    {
                        File.Replace(temporaryPath, fullPath, backupPath);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        ReplaceWithPortableFallback(temporaryPath, fullPath, backupPath);
                    }
                    catch (IOException)
                    {
                        ReplaceWithPortableFallback(temporaryPath, fullPath, backupPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
                }
            }
            catch (Exception exception)
            {
                writeFailure = exception;
                throw;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (Exception cleanupFailure) when (
                        writeFailure != null &&
                        (cleanupFailure is IOException || cleanupFailure is UnauthorizedAccessException))
                    {
                        writeFailure.Data["robotJsonTemporaryCleanup"] = cleanupFailure.Message;
                        writeFailure.Data["robotJsonTemporaryPath"] = temporaryPath;
                    }
                }
            }
        }

        public static RobotDocument Clone(RobotDocument robot)
        {
            return Deserialize(Serialize(robot));
        }

        private static JsonSerializerSettings CreateSettings(MissingMemberHandling missingMemberHandling)
        {
            return new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                ContractResolver = new DefaultContractResolver(),
                DateParseHandling = DateParseHandling.None,
                FloatFormatHandling = FloatFormatHandling.DefaultValue,
                FloatParseHandling = FloatParseHandling.Double,
                MissingMemberHandling = missingMemberHandling,
                NullValueHandling = NullValueHandling.Ignore,
                StringEscapeHandling = StringEscapeHandling.Default
            };
        }

        private static JObject ParseStandaloneProfile(string json, string label)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException(label + " profile JSON is empty.");
            }
            try
            {
                using (StringReader text = new StringReader(json))
                using (JsonTextReader reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
                {
                    JObject result = JObject.Load(
                        reader,
                        new JsonLoadSettings
                        {
                            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                            LineInfoHandling = LineInfoHandling.Load
                        });
                    if (reader.Read())
                    {
                        throw new JsonReaderException("Additional JSON content follows the profile object.");
                    }
                    return result;
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(label + " profile JSON could not be parsed.", exception);
            }
        }

        private static void SortObjectProperty(JObject parent, string propertyName)
        {
            JObject source = parent?[propertyName] as JObject;
            if (source == null)
            {
                return;
            }
            parent[propertyName] = new JObject(
                source.Properties()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => new JProperty(property.Name, property.Value.DeepClone())));
        }

        private static void ValidateTokenTypes(JToken source, JToken canonical, string path)
        {
            if (source == null || canonical == null)
            {
                throw new InvalidDataException(
                    "Robot schema v2 property '" + path + "' has a null or non-canonical value.");
            }
            if (source.Type == JTokenType.Null)
            {
                throw new InvalidDataException(
                    "Robot schema v2 property '" + path + "' must not be null; omit optional properties instead.");
            }

            JObject sourceObject = source as JObject;
            if (sourceObject != null)
            {
                JObject canonicalObject = canonical as JObject;
                if (canonicalObject == null)
                {
                    throw TokenTypeMismatch(path, source.Type, canonical.Type);
                }
                foreach (JProperty property in sourceObject.Properties())
                {
                    JToken canonicalValue = canonicalObject[property.Name];
                    ValidateTokenTypes(
                        property.Value,
                        canonicalValue,
                        path + "." + property.Name);
                }
                return;
            }

            JArray sourceArray = source as JArray;
            if (sourceArray != null)
            {
                JArray canonicalArray = canonical as JArray;
                if (canonicalArray == null || sourceArray.Count != canonicalArray.Count)
                {
                    throw new InvalidDataException(
                        "Robot schema v2 array '" + path + "' changed shape during deserialization.");
                }
                for (int index = 0; index < sourceArray.Count; index++)
                {
                    ValidateTokenTypes(
                        sourceArray[index],
                        canonicalArray[index],
                        path + "[" + index + "]");
                }
                return;
            }

            bool integerAcceptedAsNumber =
                source.Type == JTokenType.Integer && canonical.Type == JTokenType.Float;
            if (source.Type != canonical.Type && !integerAcceptedAsNumber)
            {
                throw TokenTypeMismatch(path, source.Type, canonical.Type);
            }
        }

        private static void ValidateFiniteNumbers(JToken root)
        {
            if (root.Type == JTokenType.Float)
            {
                double value = root.Value<double>();
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new InvalidDataException(
                        "Robot JSON property '$." + root.Path + "' must be a finite JSON number.");
                }
            }
            foreach (JToken child in root.Children())
            {
                ValidateFiniteNumbers(child);
            }
        }

        private static InvalidDataException TokenTypeMismatch(
            string path,
            JTokenType source,
            JTokenType expected)
        {
            return new InvalidDataException(
                "Robot schema v2 property '" + path + "' must use JSON type " +
                expected.ToString().ToLowerInvariant() + "; found " +
                source.ToString().ToLowerInvariant() + ".");
        }

        private static int ReadSourceVersion(JObject root)
        {
            JToken token = root["schemaVersion"] ?? root["schema_version"] ?? root["version"];
            int version;
            return token != null && int.TryParse(token.ToString(), out version) ? version : 0;
        }

        private static void ValidateCurrentEnvelope(JObject root)
        {
            RequireType(root, "schemaVersion", JTokenType.Integer);
            RequireType(root, "name", JTokenType.String);
            RequireType(root, "units", JTokenType.String);
            RequireType(root, "metadata", JTokenType.Object);
            RequireType(root, "links", JTokenType.Array);
            RequireType(root, "joints", JTokenType.Array);
            RequireType(root, "profiles", JTokenType.Object);

            RequireProperties((JObject)root["metadata"], "$.metadata",
                "generator", "generatorVersion", "commit", "sourceFormat");
            ValidateLinksShape((JArray)root["links"]);
            ValidateJointsShape((JArray)root["joints"]);
            ValidateProfilesShape((JObject)root["profiles"]);
        }

        private static void RequireType(JObject root, string name, JTokenType type)
        {
            JToken value = root[name];
            if (value == null)
            {
                throw new InvalidDataException(
                    "Robot schema v2 requires top-level property '" + name + "'.");
            }
            if (value.Type != type)
            {
                throw new InvalidDataException(
                    "Robot schema v2 property '" + name + "' must be " +
                    type.ToString().ToLowerInvariant() + ".");
            }
        }

        private static void ValidateLinksShape(JArray links)
        {
            for (int index = 0; index < links.Count; index++)
            {
                string path = "$.links[" + index + "]";
                JObject link = RequireObject(links[index], path);
                RequireProperties(link, path, "id", "name", "visuals", "collisions", "source");
                ValidateSourceShape(link["source"], path + ".source");
                ValidateGeometryInstancesShape(link["visuals"], path + ".visuals", true);
                ValidateGeometryInstancesShape(link["collisions"], path + ".collisions", false);
                if (link["inertial"] != null && link["inertial"].Type != JTokenType.Null)
                {
                    JObject inertial = RequireObject(link["inertial"], path + ".inertial");
                    RequireProperties(inertial, path + ".inertial", "origin", "mass", "inertia");
                    ValidatePoseShape(inertial["origin"], path + ".inertial.origin");
                    JObject inertia = RequireObject(inertial["inertia"], path + ".inertial.inertia");
                    RequireProperties(inertia, path + ".inertial.inertia",
                        "ixx", "ixy", "ixz", "iyy", "iyz", "izz");
                }
            }
        }

        private static void ValidateJointsShape(JArray joints)
        {
            for (int index = 0; index < joints.Count; index++)
            {
                string path = "$.joints[" + index + "]";
                JObject joint = RequireObject(joints[index], path);
                RequireProperties(joint, path,
                    "id", "name", "type", "parent", "child", "origin", "source");
                ValidatePoseShape(joint["origin"], path + ".origin");
                ValidateSourceShape(joint["source"], path + ".source");
                if (joint["axis"] != null && joint["axis"].Type != JTokenType.Null)
                {
                    ValidateVectorShape(joint["axis"], path + ".axis");
                }
                if (joint["mimic"] != null && joint["mimic"].Type != JTokenType.Null)
                {
                    RequireProperties(
                        RequireObject(joint["mimic"], path + ".mimic"),
                        path + ".mimic",
                        "joint");
                }
            }
        }

        private static void ValidateGeometryInstancesShape(JToken token, string path, bool visual)
        {
            JArray items = RequireArray(token, path);
            for (int index = 0; index < items.Count; index++)
            {
                string itemPath = path + "[" + index + "]";
                JObject item = RequireObject(items[index], itemPath);
                RequireProperties(item, itemPath, "origin", "geometry");
                ValidatePoseShape(item["origin"], itemPath + ".origin");
                ValidateGeometryShape(item["geometry"], itemPath + ".geometry");
                if (visual && item["material"] != null && item["material"].Type != JTokenType.Null)
                {
                    RequireObject(item["material"], itemPath + ".material");
                }
            }
        }

        private static void ValidateGeometryShape(JToken token, string path)
        {
            JObject geometry = RequireObject(token, path);
            RequireProperties(geometry, path, "type");
            string type = geometry["type"].Type == JTokenType.String
                ? (string)geometry["type"]
                : null;
            if (type == "mesh")
            {
                RequireProperties(geometry, path, "uri");
                if (geometry["scale"] != null && geometry["scale"].Type != JTokenType.Null)
                {
                    ValidateVectorShape(geometry["scale"], path + ".scale");
                }
            }
            else if (type == "box")
            {
                RequireProperties(geometry, path, "size");
                ValidateVectorShape(geometry["size"], path + ".size");
            }
            else if (type == "cylinder")
            {
                RequireProperties(geometry, path, "radius", "length");
            }
            else if (type == "sphere")
            {
                RequireProperties(geometry, path, "radius");
            }
        }

        private static void ValidateSourceShape(JToken token, string path)
        {
            RequireProperties(RequireObject(token, path), path, "kind", "userConfirmed");
        }

        private static void ValidatePoseShape(JToken token, string path)
        {
            JObject pose = RequireObject(token, path);
            RequireProperties(pose, path, "xyz", "rpy");
            ValidateVectorShape(pose["xyz"], path + ".xyz");
            ValidateVectorShape(pose["rpy"], path + ".rpy");
        }

        private static void ValidateVectorShape(JToken token, string path)
        {
            RequireProperties(RequireObject(token, path), path, "x", "y", "z");
        }

        private static void ValidateQuaternionShape(JToken token, string path)
        {
            RequireProperties(RequireObject(token, path), path, "w", "x", "y", "z");
        }

        private static void ValidateProfilesShape(JObject profiles)
        {
            RequireProperties(profiles, "$.profiles", "package", "ros2", "ros1", "isaac", "isaacLab");
            JObject package = RequireObject(profiles["package"], "$.profiles.package");
            RequireProperties(package, "$.profiles.package", "version");

            JObject ros2 = RequireObject(profiles["ros2"], "$.profiles.ros2");
            RequireProperties(ros2, "$.profiles.ros2",
                "enabled", "distribution", "gazeboDistribution", "modernGazebo", "ros2Control");
            JObject control = RequireObject(ros2["ros2Control"], "$.profiles.ros2.ros2Control");
            RequireProperties(control, "$.profiles.ros2.ros2Control",
                "enabled", "name", "type", "joints", "controllers", "controllerManagerUpdateRate",
                "gazeboPluginEnabled", "gazeboPluginFilename", "gazeboPluginClass");
            if (control["enabled"].Type == JTokenType.Boolean && (bool)control["enabled"])
            {
                RequireProperties(control, "$.profiles.ros2.ros2Control", "plugin");
            }
            ValidateProfileObjectArray(control["joints"], "$.profiles.ros2.ros2Control.joints",
                "joint", "commandInterfaces", "stateInterfaces");
            ValidateProfileObjectArray(control["controllers"], "$.profiles.ros2.ros2Control.controllers",
                "name", "type", "joints", "commandInterfaces", "stateInterfaces");

            JObject ros1 = RequireObject(profiles["ros1"], "$.profiles.ros1");
            RequireProperties(ros1, "$.profiles.ros1", "enabled", "legacy");

            JObject isaac = RequireObject(profiles["isaac"], "$.profiles.isaac");
            RequireProperties(isaac, "$.profiles.isaac",
                "schemaVersion", "enabled", "robotType", "baseType", "mergeMesh", "mergeFixedJoints",
                "allowSelfCollision", "collisionFromVisuals", "collisionType", "debugMode",
                "runAssetTransformer", "runMultiPhysicsConversion", "packageMappings");
            if (isaac["enabled"].Type == JTokenType.Boolean && (bool)isaac["enabled"])
            {
                RequireProperties(isaac, "$.profiles.isaac", "isaacSimVersion");
            }

            JObject lab = RequireObject(profiles["isaacLab"], "$.profiles.isaacLab");
            RequireProperties(lab, "$.profiles.isaacLab",
                "schemaVersion", "enabled", "backend", "primPath", "rootPosition", "rootRotationWxyz",
                "jointPositions", "jointVelocities", "actuatorGroups", "physics",
                "smokeEnvironmentCount", "smokeStepCount");
            if (lab["enabled"].Type == JTokenType.Boolean && (bool)lab["enabled"])
            {
                RequireProperties(lab, "$.profiles.isaacLab", "isaacLabVersion");
            }
            ValidateVectorShape(lab["rootPosition"], "$.profiles.isaacLab.rootPosition");
            ValidateQuaternionShape(lab["rootRotationWxyz"], "$.profiles.isaacLab.rootRotationWxyz");
            ValidateProfileObjectArray(lab["actuatorGroups"], "$.profiles.isaacLab.actuatorGroups",
                "name", "controlMode", "joints");
            JObject physics = RequireObject(lab["physics"], "$.profiles.isaacLab.physics");
            RequireProperties(physics, "$.profiles.isaacLab.physics",
                "enabledSelfCollisions", "solverPositionIterationCount", "solverVelocityIterationCount",
                "enableGyroscopicForces", "maxDepenetrationVelocity");
        }

        private static void ValidateProfileObjectArray(JToken token, string path, params string[] required)
        {
            JArray items = RequireArray(token, path);
            for (int index = 0; index < items.Count; index++)
            {
                string itemPath = path + "[" + index + "]";
                RequireProperties(RequireObject(items[index], itemPath), itemPath, required);
            }
        }

        private static JObject RequireObject(JToken token, string path)
        {
            JObject value = token as JObject;
            if (value == null)
            {
                throw new InvalidDataException("Robot schema v2 property '" + path + "' must be an object.");
            }
            return value;
        }

        private static JArray RequireArray(JToken token, string path)
        {
            JArray value = token as JArray;
            if (value == null)
            {
                throw new InvalidDataException("Robot schema v2 property '" + path + "' must be an array.");
            }
            return value;
        }

        private static void RequireProperties(JObject value, string path, params string[] names)
        {
            foreach (string name in names)
            {
                if (value.Property(name, StringComparison.Ordinal) == null)
                {
                    throw new InvalidDataException(
                        "Robot schema v2 requires property '" + path + "." + name + "'.");
                }
            }
        }

        private static void ReplaceWithPortableFallback(
            string temporaryPath,
            string destinationPath,
            string backupPath)
        {
            if (!string.IsNullOrWhiteSpace(backupPath))
            {
                File.Copy(destinationPath, backupPath, true);
            }

            string previousPath = destinationPath + ".previous-" + Guid.NewGuid().ToString("N");
            File.Move(destinationPath, previousPath);
            try
            {
                File.Move(temporaryPath, destinationPath);
                File.Delete(previousPath);
            }
            catch
            {
                if (!File.Exists(destinationPath) && File.Exists(previousPath))
                {
                    File.Move(previousPath, destinationPath);
                }
                throw;
            }
        }
    }

    public static class RobotSchemaMigrator
    {
        public static JObject Migrate(JObject input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            JObject result = (JObject)input.DeepClone();
            int version = ReadVersion(result);
            if (version > RobotSchema.CurrentVersion)
            {
                throw new InvalidDataException(
                    "Robot schema " + version + " is newer than supported schema " +
                    RobotSchema.CurrentVersion + ".");
            }
            if (version < 1)
            {
                throw new InvalidDataException("Robot JSON has no supported schemaVersion.");
            }
            if (version == 1)
            {
                MigrateV1ToV2(result);
                version = 2;
            }
            result["schemaVersion"] = version;
            return result;
        }

        private static int ReadVersion(JObject root)
        {
            JToken token = root["schemaVersion"] ?? root["schema_version"] ?? root["version"];
            int version;
            return token != null && int.TryParse(token.ToString(), out version) ? version : 0;
        }

        private static void MigrateV1ToV2(JObject root)
        {
            if (root["name"] == null && root["robotName"] != null)
            {
                root["name"] = root["robotName"];
            }
            root.Remove("robotName");
            root.Remove("schema_version");
            root.Remove("version");
            if (root["units"] == null)
            {
                root["units"] = RobotSchema.UnitSystem;
            }
            if (root["metadata"] == null)
            {
                root["metadata"] = new JObject
                {
                    ["generator"] = "OSURDF migration",
                    ["generatorVersion"] = "v1-to-v2",
                    ["commit"] = "unknown",
                    ["sourceFormat"] = "robot-json-v1"
                };
            }
            if (root["profiles"] == null)
            {
                root["profiles"] = JObject.FromObject(new RobotProfiles());
            }

            AddStableIds(root["links"] as JArray, "link");
            AddStableIds(root["joints"] as JArray, "joint");
            AddDefaultSources(root["links"] as JArray, "migrated_config");
            AddDefaultSources(root["joints"] as JArray, "migrated_config");
        }

        private static void AddStableIds(JArray items, string kind)
        {
            if (items == null)
            {
                return;
            }
            foreach (JToken token in items)
            {
                JObject item = token as JObject;
                if (item == null)
                {
                    throw new InvalidDataException(
                        "Robot schema v1 " + kind + " entries must be objects.");
                }
                if (item["id"] == null)
                {
                    item["id"] = StableId.Create(kind, (string)item["name"] ?? string.Empty);
                }
            }
        }

        private static void AddDefaultSources(JArray items, string kind)
        {
            if (items == null)
            {
                return;
            }
            foreach (JToken token in items)
            {
                JObject item = token as JObject;
                if (item == null)
                {
                    throw new InvalidDataException(
                        "Robot schema v1 entries must be objects before source provenance can be migrated.");
                }
                if (item["source"] == null)
                {
                    item["source"] = new JObject
                    {
                        ["kind"] = kind,
                        ["evidence"] = "Migrated from Robot JSON schema v1.",
                        ["userConfirmed"] = false
                    };
                }
            }
        }
    }
}
