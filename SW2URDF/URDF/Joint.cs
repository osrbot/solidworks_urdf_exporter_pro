using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.Serialization;
using System.Windows.Forms;
using SW2URDF.URDFExport;

namespace SW2URDF.URDF
{
    //The joint class. There is one for every link but the base link
    [DataContract(IsReference = true, Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class Joint : URDFElement
    {
        public const string AutomaticallyDetectType = "Automatically Detect";

        public static readonly ReadOnlyCollection<string> AvailableTypes =
            new ReadOnlyCollection<string>(new[]
            {
                "revolute", "continuous", "prismatic", "fixed", "floating", "planar"
            });

        public static readonly ReadOnlyCollection<string> SelectableTypes =
            CreateSelectableTypes();

        private static ReadOnlyCollection<string> CreateSelectableTypes()
        {
            List<string> types = new List<string>(AvailableTypes);
            types.Add(AutomaticallyDetectType);
            return types.AsReadOnly();
        }

        [DataMember]
        private readonly URDFAttribute NameAttribute;

        public string Name
        {
            get => (string)NameAttribute.Value;
            set => NameAttribute.Value = value;
        }

        [DataMember]
        private readonly URDFAttribute TypeAttribute;

        public string Type
        {
            get => (string)TypeAttribute.Value;
            set => TypeAttribute.Value = value;
        }

        [DataMember]
        public readonly Origin Origin;

        [DataMember]
        public readonly ParentLink Parent;

        [DataMember]
        public readonly ChildLink Child;

        [DataMember]
        public readonly Axis Axis;

        [DataMember]
        public readonly Limit Limit;

        [DataMember]
        public readonly Calibration Calibration;

        [DataMember]
        public readonly Dynamics Dynamics;

        [DataMember]
        public readonly SafetyController Safety;

        [DataMember(IsRequired = false)]
        public readonly Mimic Mimic;

        [DataMember]
        public CadFeatureReference AxisReference;

        // Read only during the explicit v1.5 migration; cleared before saving v2.
        [DataMember(Name = "AxisName", IsRequired = false, EmitDefaultValue = false)]
        internal string LegacyAxisName;

        [DataMember(Name = "CoordinateSystemName", IsRequired = false, EmitDefaultValue = false)]
        internal string LegacyCoordinateSystemName;

        [DataMember(IsRequired = false, EmitDefaultValue = false)]
        public string ConfigurationSource;

        [DataMember(IsRequired = false, EmitDefaultValue = false)]
        public string ConfigurationEvidence;

        [DataMember(IsRequired = false)]
        public bool ConfigurationUserConfirmed;

        public Joint() : base("joint", false)
        {
            Origin = new Origin(false);
            Parent = new ParentLink();
            Child = new ChildLink();
            Axis = new Axis();

            Limit = new Limit();
            Calibration = new Calibration();
            Dynamics = new Dynamics();
            Safety = new SafetyController();
            Mimic = new Mimic();

            NameAttribute = new URDFAttribute("name", true, "");
            TypeAttribute = new URDFAttribute("type", true, "");
            ConfigurationSource = "unknown";
            ConfigurationEvidence = string.Empty;
            ConfigurationUserConfirmed = false;
            AxisReference = CadFeatureReference.Automatic(ReferenceGeometryKind.Axis);

            Attributes.Add(NameAttribute);
            Attributes.Add(TypeAttribute);

            ChildElements.Add(Origin);
            ChildElements.Add(Parent);
            ChildElements.Add(Child);
            ChildElements.Add(Axis);

            ChildElements.Add(Limit);
            ChildElements.Add(Calibration);
            ChildElements.Add(Dynamics);
            ChildElements.Add(Safety);
            ChildElements.Add(Mimic);
        }

        public void FillBoxes(TextBox boxName, ComboBox boxType)
        {
            boxName.Text = Name;
            boxType.Text = Type;
        }

        public void Update(TextBox boxName, ComboBox boxType)
        {
            Name = boxName.Text;
            Type = boxType.Text;
        }

        public override bool ElementContainsData()
        {
            return !string.IsNullOrWhiteSpace(Name) && AvailableTypes.Contains(Type);
        }

        public static bool IsAutomaticType(string jointType)
        {
            return jointType == AutomaticallyDetectType;
        }

        public static bool RequiresAxis(string jointType)
        {
            return jointType == "revolute" || jointType == "continuous" ||
                jointType == "prismatic" || jointType == "planar";
        }

        public override bool AreRequiredFieldsSatisfied()
        {
            if (!AvailableTypes.Contains(Type))
            {
                return false;
            }
            if (RequiresAxis(Type) && !Axis.HasValidDirection())
            {
                return false;
            }
            Limit.SetRequired((Type == "prismatic" || Type == "revolute"));
            return base.AreRequiredFieldsSatisfied();
        }

        public override void AppendToCSVDictionary(List<string> context, OrderedDictionary dictionary)
        {
            base.AppendToCSVDictionary(context, dictionary);
        }

        public override void SetElement(URDFElement externalElement)
        {
            base.SetElement(externalElement);

            // The base method already performs the type check, so we don't have to for this cast
            Joint joint = (Joint)externalElement;

            AxisReference = joint.AxisReference == null
                ? CadFeatureReference.None(ReferenceGeometryKind.Axis)
                : joint.AxisReference.Clone();
            ConfigurationSource = joint.ConfigurationSource;
            ConfigurationEvidence = joint.ConfigurationEvidence;
            ConfigurationUserConfirmed = joint.ConfigurationUserConfirmed;
        }

        public override void SetElementFromData(List<string> context, StringDictionary dictionary)
        {
            base.SetElementFromData(context, dictionary);
        }

        public void SetJointKinematics(Joint joint)
        {
            AxisReference = joint.AxisReference == null
                ? CadFeatureReference.None(ReferenceGeometryKind.Axis)
                : joint.AxisReference.Clone();
            Type = joint.Type;
            Axis.SetElement(joint.Axis);
            Origin.SetElement(joint.Origin);
            ConfigurationSource = joint.ConfigurationSource;
            ConfigurationEvidence = joint.ConfigurationEvidence;
            ConfigurationUserConfirmed = joint.ConfigurationUserConfirmed;
        }

        public void SetJointNonKinematics(Joint joint)
        {
            Limit.SetElement(joint.Limit);
            Calibration.SetElement(joint.Calibration);
            Dynamics.SetElement(joint.Dynamics);
            Safety.SetElement(joint.Safety);
        }

        public void MarkManualConfiguration(string evidence)
        {
            ConfigurationSource = "manual_configuration";
            ConfigurationEvidence = evidence;
            ConfigurationUserConfirmed = true;
        }

        public void MarkMateSuggestion(string evidence)
        {
            ConfigurationSource = "solidworks_mate_suggestion";
            ConfigurationEvidence = evidence;
            ConfigurationUserConfirmed = false;
        }

        public void MarkPendingMateDetection()
        {
            ConfigurationSource = "solidworks_mate_suggestion";
            ConfigurationEvidence = "User requested conditional SolidWorks Mate/DOF detection; no result is confirmed yet.";
            ConfigurationUserConfirmed = false;
        }

        public void MarkTopologyFixedFrame()
        {
            ConfigurationSource = "topology_fixed_frame";
            ConfigurationEvidence = "The Link is an explicit fixed-frame node in the configured Link tree.";
            ConfigurationUserConfirmed = true;
        }

        public void ConfirmConfiguration()
        {
            ConfigurationUserConfirmed = true;
        }
    }
}
