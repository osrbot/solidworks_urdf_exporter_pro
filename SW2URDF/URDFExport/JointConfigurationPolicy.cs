using SW2URDF.URDF;

namespace SW2URDF.URDFExport
{
    internal static class JointConfigurationPolicy
    {
        public static void Apply(Joint joint, string jointType)
        {
            jointType = Normalize(jointType);
            string previousType = Normalize(joint.Type);
            if (ChangesMotionUnits(previousType, jointType))
            {
                ClearDimensionedMotionConfiguration(joint);
            }
            switch (jointType)
            {
                case "fixed":
                case "floating":
                    ClearMotionConfiguration(joint);
                    break;

                case "planar":
                    joint.Limit.Unset();
                    joint.Calibration.Unset();
                    joint.Safety.Unset();
                    joint.Mimic.Unset();
                    break;

                case "continuous":
                    joint.Limit.ClearPositionBounds();
                    break;
            }

            joint.Type = jointType;
        }

        public static string Normalize(string jointType)
        {
            return jointType;
        }

        public static void ApplyUserSelection(Joint joint, string jointType)
        {
            string normalized = Normalize(jointType);
            bool confirmsMateSuggestion =
                joint.ConfigurationSource == "solidworks_mate_suggestion" &&
                !Joint.IsAutomaticType(normalized) &&
                normalized == Normalize(joint.Type);
            Apply(joint, normalized);
            if (Joint.IsAutomaticType(normalized))
            {
                joint.MarkPendingMateDetection();
            }
            else if (confirmsMateSuggestion)
            {
                joint.ConfirmConfiguration();
            }
            else
            {
                joint.MarkManualConfiguration("URDF joint type was explicitly selected in the exporter UI.");
            }
        }

        public static void ApplyDetectedSuggestion(
            Joint joint,
            string detectedType,
            string evidence)
        {
            Apply(joint, detectedType);
            joint.MarkMateSuggestion(evidence);
        }

        public static bool RequiresUserConfirmation(Joint joint)
        {
            return joint != null &&
                !joint.ConfigurationUserConfirmed;
        }

        public static string ResolveDetectedType(string configuredType, string detectedType)
        {
            string normalizedConfiguredType = Normalize(configuredType);
            return Joint.IsAutomaticType(normalizedConfiguredType)
                ? Normalize(detectedType)
                : normalizedConfiguredType;
        }

        public static void PrepareLimitRecomputation(Joint joint)
        {
            string jointType = Normalize(joint.Type);
            Apply(joint, jointType);
            if (jointType == "revolute" || jointType == "prismatic")
            {
                joint.Limit.ClearPositionBounds();
            }
        }

        public static bool ChangesMotionUnits(string previousType, string nextType)
        {
            previousType = Normalize(previousType);
            nextType = Normalize(nextType);
            bool previousAngular = previousType == "revolute" || previousType == "continuous";
            bool nextAngular = nextType == "revolute" || nextType == "continuous";
            return (previousAngular && nextType == "prismatic") ||
                (previousType == "prismatic" && nextAngular);
        }

        public static bool RequiresMotionAxis(string jointType)
        {
            jointType = Normalize(jointType);
            return Joint.RequiresAxis(jointType);
        }

        public static bool TryClassifyDetectedType(
            int apiResult,
            int rotationalStatus1,
            int rotationalStatus2,
            int linearStatus1,
            int linearStatus2,
            out string detectedType)
        {
            detectedType = null;
            if (apiResult != 0 ||
                !IsBinaryStatus(rotationalStatus1) ||
                !IsBinaryStatus(rotationalStatus2) ||
                !IsBinaryStatus(linearStatus1) ||
                !IsBinaryStatus(linearStatus2))
            {
                return false;
            }

            if (rotationalStatus1 == 0 && rotationalStatus2 == 0 &&
                linearStatus1 == 0 && linearStatus2 == 0)
            {
                // Zero remaining DOFs is not enough to infer URDF semantics. It can mean an
                // intentionally fixed joint, a fully constrained movable joint, or an imported
                // STEP/fixed assembly with no usable SolidWorks Mate information.
                return false;
            }

            if (rotationalStatus1 == 1 && rotationalStatus2 == 0 &&
                linearStatus1 == 0 && linearStatus2 == 0)
            {
                detectedType = "continuous";
                return true;
            }

            if (rotationalStatus1 == 0 && rotationalStatus2 == 0 &&
                linearStatus1 == 1 && linearStatus2 == 0)
            {
                detectedType = "prismatic";
                return true;
            }

            return false;
        }

        public static bool IsDetectedTypeCompatible(string configuredType, string detectedType)
        {
            configuredType = Normalize(configuredType);
            detectedType = Normalize(detectedType);
            if (Joint.IsAutomaticType(configuredType) ||
                configuredType == "fixed" || configuredType == "floating")
            {
                return true;
            }
            if (configuredType == "revolute" || configuredType == "continuous")
            {
                return detectedType == "continuous";
            }
            return configuredType == detectedType;
        }

        private static bool IsBinaryStatus(int status)
        {
            return status == 0 || status == 1;
        }

        private static void ClearMotionConfiguration(Joint joint)
        {
            joint.Axis.Unset();
            joint.AxisReference = CadFeatureReference.None(ReferenceGeometryKind.Axis);
            joint.Limit.Unset();
            joint.Calibration.Unset();
            joint.Dynamics.Unset();
            joint.Safety.Unset();
            joint.Mimic.Unset();
        }

        private static void ClearDimensionedMotionConfiguration(Joint joint)
        {
            joint.Limit.Unset();
            joint.Calibration.Unset();
            joint.Dynamics.Unset();
            joint.Safety.Unset();
            joint.Mimic.ClearOffset();
        }
    }
}
