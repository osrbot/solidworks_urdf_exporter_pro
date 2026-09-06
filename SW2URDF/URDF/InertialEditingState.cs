using System.Runtime.Serialization;
using SW2URDF.URDFExport;

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
        [DataMember(EmitDefaultValue = false)] public bool FrameChangePending;
        [DataMember(EmitDefaultValue = false)] public CadFeatureReference InertialFrameReference;

        public InertialEditingState Clone()
        {
            var copy = (InertialEditingState)MemberwiseClone();
            copy.InertialFrameReference = InertialFrameReference == null
                ? null : InertialFrameReference.Clone();
            if (Source != null)
            {
                copy.Source = new Inertial();
                copy.Source.SetElement(Source);
            }
            return copy;
        }
    }
}
