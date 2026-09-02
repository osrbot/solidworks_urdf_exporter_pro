using System.Collections.Generic;
using Newtonsoft.Json;

namespace OSURDF.Core.Model
{
    public sealed class RobotProfiles
    {
        [JsonProperty("package", Order = 0)]
        public PackageMetadataProfile Package { get; set; } = new PackageMetadataProfile();

        [JsonProperty("ros2", Order = 1)]
        public Ros2ExportProfile Ros2 { get; set; } = new Ros2ExportProfile();

        [JsonProperty("ros1", Order = 2)]
        public Ros1ExportProfile Ros1 { get; set; } = new Ros1ExportProfile();

        [JsonProperty("isaac", Order = 3)]
        public IsaacExportProfile Isaac { get; set; } = new IsaacExportProfile();

        [JsonProperty("isaacLab", Order = 4)]
        public IsaacLabProfile IsaacLab { get; set; } = new IsaacLabProfile();

        [JsonProperty("usdSimulation", Order = 5)]
        public UsdSimulationProfile UsdSimulation { get; set; } = new UsdSimulationProfile();
    }

    public sealed class PackageMetadataProfile
    {
        [JsonProperty("packageName", Order = 0)] public string PackageName { get; set; }
        [JsonProperty("version", Order = 1)] public string Version { get; set; } = "0.1.0";
        [JsonProperty("description", Order = 2)] public string Description { get; set; }
        [JsonProperty("maintainerName", Order = 3)] public string MaintainerName { get; set; }
        [JsonProperty("maintainerEmail", Order = 4)] public string MaintainerEmail { get; set; }
        [JsonProperty("license", Order = 5)] public string License { get; set; }
    }

    public sealed class Ros2ExportProfile
    {
        [JsonProperty("enabled", Order = 0)] public bool Enabled { get; set; }
        [JsonProperty("distribution", Order = 1)] public string Distribution { get; set; } = "lyrical";
        [JsonProperty("gazeboDistribution", Order = 2)] public string GazeboDistribution { get; set; } = "jetty";
        [JsonProperty("modernGazebo", Order = 3)] public bool ModernGazebo { get; set; } = true;
        [JsonProperty("ros2Control", Order = 4)] public Ros2ControlProfile Ros2Control { get; set; } = new Ros2ControlProfile();
    }

    public sealed class Ros1ExportProfile
    {
        [JsonProperty("enabled", Order = 0)] public bool Enabled { get; set; }
        [JsonProperty("legacy", Order = 1)] public bool Legacy { get; set; } = true;
    }

    public sealed class Ros2ControlProfile
    {
        [JsonProperty("enabled", Order = 0)] public bool Enabled { get; set; }
        [JsonProperty("name", Order = 1)] public string Name { get; set; } = "robot_system";
        [JsonProperty("type", Order = 2)] public string Type { get; set; } = "system";
        [JsonProperty("plugin", Order = 3)] public string Plugin { get; set; }
        [JsonProperty("joints", Order = 4)] public List<Ros2ControlJointProfile> Joints { get; set; } = new List<Ros2ControlJointProfile>();
        [JsonProperty("controllers", Order = 5)] public List<Ros2ControllerProfile> Controllers { get; set; } = new List<Ros2ControllerProfile>();
        [JsonProperty("controllerManagerUpdateRate", Order = 6)] public int ControllerManagerUpdateRate { get; set; } = 100;
        [JsonProperty("gazeboPluginEnabled", Order = 7)] public bool GazeboPluginEnabled { get; set; }
        [JsonProperty("gazeboPluginFilename", Order = 8)] public string GazeboPluginFilename { get; set; } = "libgz_ros2_control-system.so";
        [JsonProperty("gazeboPluginClass", Order = 9)] public string GazeboPluginClass { get; set; } = "gz_ros2_control::GazeboSimROS2ControlPlugin";
    }

    public sealed class Ros2ControlJointProfile
    {
        [JsonProperty("joint", Order = 0)] public string Joint { get; set; }
        [JsonProperty("commandInterfaces", Order = 1)] public List<string> CommandInterfaces { get; set; } = new List<string>();
        [JsonProperty("stateInterfaces", Order = 2)] public List<string> StateInterfaces { get; set; } = new List<string>();
    }

    public sealed class Ros2ControllerProfile
    {
        [JsonProperty("name", Order = 0)] public string Name { get; set; }
        [JsonProperty("type", Order = 1)] public string Type { get; set; }
        [JsonProperty("joints", Order = 2)] public List<string> Joints { get; set; } = new List<string>();
        [JsonProperty("commandInterfaces", Order = 3)] public List<string> CommandInterfaces { get; set; } = new List<string>();
        [JsonProperty("stateInterfaces", Order = 4)] public List<string> StateInterfaces { get; set; } = new List<string>();
    }

    public sealed class IsaacExportProfile
    {
        [JsonProperty("schemaVersion", Order = 0)] public int SchemaVersion { get; set; } = 1;
        [JsonProperty("enabled", Order = 1)] public bool Enabled { get; set; }
        [JsonProperty("isaacSimVersion", Order = 2)] public string IsaacSimVersion { get; set; }
        [JsonProperty("robotType", Order = 3)] public string RobotType { get; set; } = "Default";
        [JsonProperty("baseType", Order = 4)] public string BaseType { get; set; } = "Source";
        [JsonProperty("mergeMesh", Order = 5)] public bool MergeMesh { get; set; } = true;
        [JsonProperty("mergeFixedJoints", Order = 6)] public bool MergeFixedJoints { get; set; }
        [JsonProperty("allowSelfCollision", Order = 7)] public bool AllowSelfCollision { get; set; }
        [JsonProperty("collisionFromVisuals", Order = 8)] public bool CollisionFromVisuals { get; set; }
        [JsonProperty("collisionType", Order = 9)] public string CollisionType { get; set; } = "convex_hull";
        [JsonProperty("debugMode", Order = 10)] public bool DebugMode { get; set; }
        [JsonProperty("runAssetTransformer", Order = 11)] public bool RunAssetTransformer { get; set; }
        [JsonProperty("runMultiPhysicsConversion", Order = 12)] public bool RunMultiPhysicsConversion { get; set; }
        [JsonProperty("packageMappings", Order = 13)] public Dictionary<string, string> PackageMappings { get; set; } = new Dictionary<string, string>();
    }

    public sealed class IsaacLabProfile
    {
        [JsonProperty("schemaVersion", Order = 0)] public int SchemaVersion { get; set; } = 1;
        [JsonProperty("enabled", Order = 1)] public bool Enabled { get; set; }
        [JsonProperty("isaacLabVersion", Order = 2)] public string IsaacLabVersion { get; set; }
        [JsonProperty("backend", Order = 3)] public string Backend { get; set; } = "physx";
        [JsonProperty("primPath", Order = 4)] public string PrimPath { get; set; } = "{ENV_REGEX_NS}/Robot";
        [JsonProperty("rootPosition", Order = 5)] public Vector3Document RootPosition { get; set; } = new Vector3Document { Z = 1.0 };
        [JsonProperty("rootRotationWxyz", Order = 6)] public QuaternionWxyzDocument RootRotationWxyz { get; set; } = new QuaternionWxyzDocument();
        [JsonProperty("jointPositions", Order = 7)] public Dictionary<string, double> JointPositions { get; set; } = new Dictionary<string, double>();
        [JsonProperty("jointVelocities", Order = 8)] public Dictionary<string, double> JointVelocities { get; set; } = new Dictionary<string, double>();
        [JsonProperty("actuatorGroups", Order = 9)] public List<ActuatorGroupProfile> ActuatorGroups { get; set; } = new List<ActuatorGroupProfile>();
        [JsonProperty("physics", Order = 10)] public IsaacPhysicsProfile Physics { get; set; } = new IsaacPhysicsProfile();
        [JsonProperty("smokeEnvironmentCount", Order = 11)] public int SmokeEnvironmentCount { get; set; } = 64;
        [JsonProperty("smokeStepCount", Order = 12)] public int SmokeStepCount { get; set; } = 1000;
    }

    public sealed class UsdSimulationProfile
    {
        [JsonProperty("baseMode", Order = 0)] public string BaseMode { get; set; } = "source";
        [JsonProperty("robotType", Order = 1)] public string RobotType { get; set; } = "default";
        [JsonProperty("allowSelfCollision", Order = 2)] public bool AllowSelfCollision { get; set; }
        [JsonProperty("gainUnits", Order = 3)] public string GainUnits { get; set; } = "SI";
        [JsonProperty("jointDrives", Order = 4)] public List<UsdJointDriveProfile> JointDrives { get; set; } = new List<UsdJointDriveProfile>();
    }

    public sealed class UsdJointDriveProfile
    {
        [JsonProperty("joint", Order = 0)] public string Joint { get; set; } = string.Empty;
        [JsonProperty("mode", Order = 1)] public string Mode { get; set; } = "passive";
        [JsonProperty("stiffness", Order = 2)] public double? Stiffness { get; set; }
        [JsonProperty("damping", Order = 3)] public double? Damping { get; set; }
    }

    public sealed class IsaacPhysicsProfile
    {
        [JsonProperty("enabledSelfCollisions", Order = 0)] public bool EnabledSelfCollisions { get; set; }
        [JsonProperty("solverPositionIterationCount", Order = 1)] public int SolverPositionIterationCount { get; set; } = 8;
        [JsonProperty("solverVelocityIterationCount", Order = 2)] public int SolverVelocityIterationCount { get; set; } = 2;
        [JsonProperty("enableGyroscopicForces", Order = 3)] public bool EnableGyroscopicForces { get; set; } = true;
        [JsonProperty("maxDepenetrationVelocity", Order = 4)] public double MaxDepenetrationVelocity { get; set; } = 5.0;
    }

    public sealed class ActuatorGroupProfile
    {
        [JsonProperty("name", Order = 0)] public string Name { get; set; } = string.Empty;
        [JsonProperty("controlMode", Order = 1)] public string ControlMode { get; set; } = "unconfigured";
        [JsonProperty("joints", Order = 2)] public List<string> Joints { get; set; } = new List<string>();
        [JsonProperty("stiffness", Order = 3)] public double? Stiffness { get; set; }
        [JsonProperty("damping", Order = 4)] public double? Damping { get; set; }
        [JsonProperty("effortLimit", Order = 5)] public double? EffortLimit { get; set; }
        [JsonProperty("velocityLimit", Order = 6)] public double? VelocityLimit { get; set; }
        [JsonProperty("armature", Order = 7)] public double? Armature { get; set; }
        [JsonProperty("friction", Order = 8)] public double? Friction { get; set; }
    }
}
