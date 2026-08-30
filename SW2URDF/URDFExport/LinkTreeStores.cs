using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW2URDF.URDFExport
{
    internal sealed class LinkConfigurationStore
    {
        private readonly Dictionary<Guid, LinkConfigurationState> states;

        public LinkConfigurationStore()
        {
            states = new Dictionary<Guid, LinkConfigurationState>();
        }

        private LinkConfigurationStore(Dictionary<Guid, LinkConfigurationState> source)
        {
            states = source.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
        }

        public LinkConfigurationStore Clone()
        {
            return new LinkConfigurationStore(states);
        }

        public bool Contains(Guid id)
        {
            return states.ContainsKey(id);
        }

        public void Capture(Guid id, LinkNode node)
        {
            Link configuration = CloneConfiguration(node.Link);
            configuration.isIncomplete = node.IsIncomplete;
            states[id] = new LinkConfigurationState(
                configuration,
                node.IsIncomplete,
                node.NeedsSaving,
                node.WhyIncomplete);
        }

        public void NormalizeRoot(Guid id)
        {
            LinkConfigurationState state;
            if (states.TryGetValue(id, out state))
            {
                state.NormalizeAsRoot();
            }
        }

        public void CreateDefault(Guid id)
        {
            Link link = new Link();
            link.Joint.AxisName = "Automatically Generate";
            link.Joint.CoordinateSystemName = "Automatically Generate";
            link.JointKinematicsDirty = true;
            link.JointLimitsDirty = true;
            link.isIncomplete = true;
            states[id] = new LinkConfigurationState(
                link,
                true,
                false,
                "SolidWorks components are not assigned.");
        }

        public void CopyConfiguration(Guid sourceId, Guid targetId)
        {
            LinkConfigurationState source;
            if (!states.TryGetValue(sourceId, out source))
            {
                throw new InvalidOperationException(
                    "Cannot copy URDF configuration because the source Link does not exist.");
            }

            LinkConfigurationState copy = source.Clone();
            copy.MarkIncomplete("SolidWorks components are not assigned to this copied Link.");
            copy.MarkJointKinematicsStale();
            copy.MarkJointLimitsStale();
            states[targetId] = copy;
        }

        public Link BuildLink(Guid id)
        {
            LinkConfigurationState state;
            if (!states.TryGetValue(id, out state))
            {
                throw new InvalidOperationException("Missing URDF configuration for Link node " + id + ".");
            }
            return state.BuildLink();
        }

        public LinkConfigurationState Get(Guid id)
        {
            return states[id];
        }

        public string GetMimicReference(Guid id)
        {
            LinkConfigurationState state;
            return states.TryGetValue(id, out state) ? state.GetMimicReference() : string.Empty;
        }

        public void SetMimicReference(Guid id, string jointName)
        {
            LinkConfigurationState state;
            if (states.TryGetValue(id, out state) && !string.IsNullOrWhiteSpace(jointName))
            {
                state.SetMimicReference(jointName);
            }
        }

        public void ApplyJointType(Guid id, string jointType)
        {
            LinkConfigurationState state;
            if (!states.TryGetValue(id, out state))
            {
                throw new InvalidOperationException("Missing URDF configuration for Link node " + id + ".");
            }
            state.ApplyJointType(jointType);
        }

        public void ApplyJointTypeFromUser(Guid id, string jointType)
        {
            LinkConfigurationState state;
            if (!states.TryGetValue(id, out state))
            {
                throw new InvalidOperationException("Missing URDF configuration for Link node " + id + ".");
            }
            state.ApplyJointTypeFromUser(jointType);
        }

        public void MarkJointKinematicsStale(Guid id)
        {
            LinkConfigurationState state;
            if (states.TryGetValue(id, out state))
            {
                state.MarkJointKinematicsStale();
            }
        }

        public void MarkJointLimitsStale(Guid id)
        {
            LinkConfigurationState state;
            if (states.TryGetValue(id, out state))
            {
                state.MarkJointLimitsStale();
            }
        }

        public bool JointKinematicsInputsMatch(Guid id, Link candidate)
        {
            LinkConfigurationState state;
            return states.TryGetValue(id, out state) &&
                state.JointKinematicsInputsMatch(candidate);
        }

        public IList<string> ValidateMimicReferences(
            IDictionary<Guid, string> jointNamesById)
        {
            List<string> errors = new List<string>();
            List<Joint> joints = new List<Joint>();
            foreach (KeyValuePair<Guid, LinkConfigurationState> pair in states)
            {
                string owner;
                if (!jointNamesById.TryGetValue(pair.Key, out owner))
                {
                    if (pair.Value.HasMimicData())
                    {
                        errors.Add("The base Link cannot contain a Mimic Joint.");
                    }
                    continue;
                }

                Link link = pair.Value.BuildLink();
                link.Joint.Name = owner;
                joints.Add(link.Joint);
            }

            errors.AddRange(MimicGraphValidator.Validate(joints));
            return errors.Distinct(StringComparer.Ordinal).ToList();
        }

        public bool RequiresJointKinematics()
        {
            return states.Values.Any(state =>
                state.RequiresJointKinematics() || state.RequiresAutomaticJointTypeResolution());
        }

        public bool RequiresJointLimits()
        {
            return states.Values.Any(state =>
                state.RequiresJointLimits() || state.RequiresAutomaticJointTypeResolution());
        }

        public void RemoveExcept(ISet<Guid> activeIds)
        {
            foreach (Guid id in states.Keys.Where(key => !activeIds.Contains(key)).ToList())
            {
                states.Remove(id);
            }
        }

        internal static Link CloneConfiguration(Link source)
        {
            Link clone = new Link();
            clone.SetElement(source);
            clone.Parent = null;
            clone.Children.Clear();
            return clone;
        }
    }

    internal sealed class LinkConfigurationState
    {
        public LinkConfigurationState(
            Link configuration,
            bool isIncomplete,
            bool needsSaving,
            string whyIncomplete)
        {
            this.configuration = configuration;
            IsIncomplete = isIncomplete;
            NeedsSaving = needsSaving;
            WhyIncomplete = whyIncomplete;
            this.configuration.isIncomplete = isIncomplete;
        }

        private readonly Link configuration;
        public bool IsIncomplete { get; private set; }
        public bool NeedsSaving { get; private set; }
        public string WhyIncomplete { get; private set; }

        public Link BuildLink()
        {
            return LinkConfigurationStore.CloneConfiguration(configuration);
        }

        public string GetMimicReference()
        {
            return configuration.Joint.Mimic.JointName;
        }

        public void SetMimicReference(string jointName)
        {
            configuration.Joint.Mimic.JointName = jointName;
        }

        public void ApplyJointType(string jointType)
        {
            JointConfigurationPolicy.Apply(configuration.Joint, jointType);
        }

        public void ApplyJointTypeFromUser(string jointType)
        {
            JointConfigurationPolicy.ApplyUserSelection(configuration.Joint, jointType);
        }

        public void MarkJointKinematicsStale()
        {
            configuration.JointKinematicsDirty = true;
        }

        public void MarkJointLimitsStale()
        {
            configuration.JointLimitsDirty = true;
        }

        public void MarkIncomplete(string reason)
        {
            IsIncomplete = true;
            NeedsSaving = true;
            WhyIncomplete = reason;
            configuration.isIncomplete = true;
        }

        public void NormalizeAsRoot()
        {
            LinkTreeRootJointPolicy.Normalize(configuration);
        }

        public bool RequiresJointKinematics()
        {
            return configuration.JointKinematicsDirty;
        }

        public bool RequiresJointLimits()
        {
            return configuration.JointLimitsDirty;
        }

        public bool RequiresAutomaticJointTypeResolution()
        {
            return Joint.IsAutomaticType(configuration.Joint.Type);
        }

        public bool HasMimicData()
        {
            return configuration.Joint.Mimic != null &&
                configuration.Joint.Mimic.ElementContainsData();
        }

        public bool JointKinematicsInputsMatch(Link candidate)
        {
            return candidate != null &&
                string.Equals(
                    JointConfigurationPolicy.Normalize(configuration.Joint.Type),
                    JointConfigurationPolicy.Normalize(candidate.Joint.Type),
                    StringComparison.Ordinal) &&
                string.Equals(
                    configuration.Joint.CoordinateSystemName,
                    candidate.Joint.CoordinateSystemName,
                    StringComparison.Ordinal) &&
                string.Equals(
                    configuration.Joint.AxisName,
                    candidate.Joint.AxisName,
                    StringComparison.Ordinal) &&
                configuration.isFixedFrame == candidate.isFixedFrame;
        }

        public LinkConfigurationState Clone()
        {
            return new LinkConfigurationState(
                LinkConfigurationStore.CloneConfiguration(configuration),
                IsIncomplete,
                NeedsSaving,
                WhyIncomplete);
        }
    }

    internal sealed class CadBindingStore
    {
        private readonly Dictionary<Guid, CadBindingState> states;

        public CadBindingStore()
        {
            states = new Dictionary<Guid, CadBindingState>();
        }

        private CadBindingStore(Dictionary<Guid, CadBindingState> source)
        {
            states = source.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
        }

        public CadBindingStore Clone()
        {
            return new CadBindingStore(states);
        }

        public void Capture(Guid id, Link link)
        {
            states[id] = CadBindingState.FromLink(link);
        }

        public void CreateEmpty(Guid id)
        {
            states[id] = new CadBindingState();
        }

        public bool Matches(Guid id, Link candidate)
        {
            CadBindingState state;
            return states.TryGetValue(id, out state) && state.Matches(candidate);
        }

        public void Apply(Guid id, Link link)
        {
            CadBindingState state;
            if (!states.TryGetValue(id, out state))
            {
                throw new InvalidOperationException("Missing SolidWorks CAD binding for Link node " + id + ".");
            }
            state.Apply(link);
        }

        public void RemoveExcept(ISet<Guid> activeIds)
        {
            foreach (Guid id in states.Keys.Where(key => !activeIds.Contains(key)).ToList())
            {
                states.Remove(id);
            }
        }
    }

    internal sealed class CadBindingState
    {
        public CadBindingState()
        {
            Components = new List<Component2>();
            ComponentPids = new List<byte[]>();
        }

        public Component2 MainComponent { get; private set; }
        public List<Component2> Components { get; private set; }
        public byte[] MainComponentPid { get; private set; }
        public List<byte[]> ComponentPids { get; private set; }

        public static CadBindingState FromLink(Link link)
        {
            CadBindingState state = new CadBindingState();
            state.MainComponent = link.SWMainComponent;
            state.Components = link.SWComponents == null
                ? new List<Component2>()
                : new List<Component2>(link.SWComponents);
            state.MainComponentPid = CloneBytes(link.SWMainComponentPID);
            state.ComponentPids = link.SWComponentPIDs == null
                ? new List<byte[]>()
                : link.SWComponentPIDs.Select(CloneBytes).ToList();
            return state;
        }

        public CadBindingState Clone()
        {
            CadBindingState clone = new CadBindingState();
            clone.MainComponent = MainComponent;
            clone.Components = new List<Component2>(Components);
            clone.MainComponentPid = CloneBytes(MainComponentPid);
            clone.ComponentPids = ComponentPids.Select(CloneBytes).ToList();
            return clone;
        }

        public void Apply(Link link)
        {
            link.SWMainComponent = MainComponent;
            link.SWComponents = new List<Component2>(Components);
            link.SWMainComponentPID = CloneBytes(MainComponentPid);
            link.SWComponentPIDs = ComponentPids.Select(CloneBytes).ToList();
        }

        public bool Matches(Link link)
        {
            if (link == null || !MainComponentMatches(link))
            {
                return false;
            }

            List<Component2> candidateComponents = link.SWComponents ?? new List<Component2>();
            if (Components.Count != candidateComponents.Count)
            {
                return false;
            }
            bool hasLiveComponents = Components.Any(component => component != null) ||
                candidateComponents.Any(component => component != null);
            if (hasLiveComponents)
            {
                for (int index = 0; index < Components.Count; index++)
                {
                    if (!CommonSwOperations.ComReferencesEqual(
                        Components[index],
                        candidateComponents[index]))
                    {
                        return false;
                    }
                }
                return true;
            }

            List<byte[]> candidatePids = link.SWComponentPIDs ?? new List<byte[]>();
            if (ComponentPids.Count != candidatePids.Count)
            {
                return false;
            }
            for (int index = 0; index < ComponentPids.Count; index++)
            {
                if (!BytesEqual(ComponentPids[index], candidatePids[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private bool MainComponentMatches(Link link)
        {
            if (MainComponent != null || link.SWMainComponent != null)
            {
                return CommonSwOperations.ComReferencesEqual(
                    MainComponent,
                    link.SWMainComponent);
            }
            if (MainComponentPid != null || link.SWMainComponentPID != null)
            {
                return BytesEqual(MainComponentPid, link.SWMainComponentPID);
            }
            return true;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            return left != null && right != null && left.SequenceEqual(right);
        }

        private static byte[] CloneBytes(byte[] value)
        {
            return value == null ? null : (byte[])value.Clone();
        }
    }
}
