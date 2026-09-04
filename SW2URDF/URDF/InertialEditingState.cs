using System.Runtime.Serialization;

namespace SW2URDF.URDF
{
    // Optional configuration metadata. The exported inertial element remains unchanged.
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public sealed class InertialEditingState
    {
        [DataMember] public Inertial Source;
        [DataMember] public bool SourceIsSolidWorks;
        [DataMember] public bool SourceHasInertiaOverride;
        [DataMember] public bool MassEdited;
        [DataMember] public bool OriginEdited;
        [DataMember] public bool TensorEdited;
        [DataMember] public bool CalibrationDisabled;
        [DataMember] public bool LegacyValuesPreserved;

        public InertialEditingState Clone()
        {
            var copy = (InertialEditingState)MemberwiseClone();
            if (Source != null)
            {
                copy.Source = new Inertial();
                copy.Source.SetElement(Source);
            }
            return copy;
        }
    }
}
