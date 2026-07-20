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
            states[id] = new LinkConfigurationState(
                CloneConfiguration(node.Link),
                node.IsIncomplete,
                node.NeedsSaving,
                node.WhyIncomplete,
                false);
        }

        public void CreateDefault(Guid id)
        {
            Link link = new Link();
            link.Joint.AxisName = "Automatically Generate";
            link.Joint.CoordinateSystemName = "Automatically Generate";
            states[id] = new LinkConfigurationState(
                link,
                true,
                false,
                "SolidWorks components are not assigned.",
                false);
        }

        public void CopyConfiguration(Guid sourceId, Guid targetId)
        {
            LinkConfigurationState source;
            if (!states.TryGetValue(sourceId, out source))
            {
                CreateDefault(targetId);
                return;
            }

            LinkConfigurationState copy = source.Clone();
            copy.IsIncomplete = true;
            copy.NeedsSaving = true;
            copy.WhyIncomplete = "SolidWorks components are not assigned to this copied Link.";
            states[targetId] = copy;
        }

        public Link BuildLink(Guid id)
        {
            LinkConfigurationState state;
            if (!states.TryGetValue(id, out state))
            {
                throw new InvalidOperationException("Missing URDF configuration for Link node " + id + ".");
            }
            return CloneConfiguration(state.Configuration);
        }

        public LinkConfigurationState Get(Guid id)
        {
            return states[id];
        }

        public void RenameMimicReference(string oldJointName, string newJointName)
        {
            if (string.IsNullOrWhiteSpace(oldJointName) || oldJointName == newJointName)
            {
                return;
            }

            foreach (LinkConfigurationState state in states.Values)
            {
                if (string.Equals(
                    state.Configuration.Joint.Mimic.JointName,
                    oldJointName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    state.Configuration.Joint.Mimic.JointName = newJointName;
                }
            }
        }

        public void SetMimicReference(Guid id, string jointName)
        {
            LinkConfigurationState state;
            if (states.TryGetValue(id, out state) && !string.IsNullOrWhiteSpace(jointName))
            {
                state.Configuration.Joint.Mimic.JointName = jointName;
            }
        }

        public void MarkJointKinematicsStale(Guid id)
        {
            LinkConfigurationState state;
            if (states.TryGetValue(id, out state))
            {
                state.RequiresJointKinematics = true;
            }
        }

        public IList<string> ValidateMimicReferences(IEnumerable<string> jointNames)
        {
            HashSet<string> available = new HashSet<string>(jointNames, StringComparer.OrdinalIgnoreCase);
            List<string> errors = new List<string>();
            foreach (LinkConfigurationState state in states.Values)
            {
                string target = state.Configuration.Joint.Mimic.JointName;
                if (!string.IsNullOrWhiteSpace(target) && !available.Contains(target))
                {
                    errors.Add("Mimic Joint '" + target + "' does not exist.");
                }
            }
            return errors.Distinct().ToList();
        }

        public bool RequiresJointKinematics()
        {
            return states.Values.Any(state => state.RequiresJointKinematics);
        }

        public bool RequiresJointKinematics(Guid id)
        {
            LinkConfigurationState state;
            return states.TryGetValue(id, out state) && state.RequiresJointKinematics;
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
            clone.SWMainComponent = null;
            clone.SWComponents = new List<Component2>();
            clone.SWMainComponentPID = null;
            clone.SWComponentPIDs = new List<byte[]>();
            clone.ClearAdditionalCollisions();
            if (source.AdditionalCollisions != null)
            {
                foreach (SW2URDF.URDF.Collision collision in source.AdditionalCollisions)
                {
                    SW2URDF.URDF.Collision copiedCollision = new SW2URDF.URDF.Collision();
                    copiedCollision.SetElement(collision);
                    clone.AddAdditionalCollision(copiedCollision);
                }
            }
            return clone;
        }
    }

    internal sealed class LinkConfigurationState
    {
        public LinkConfigurationState(
            Link configuration,
            bool isIncomplete,
            bool needsSaving,
            string whyIncomplete,
            bool requiresJointKinematics)
        {
            Configuration = configuration;
            IsIncomplete = isIncomplete;
            NeedsSaving = needsSaving;
            WhyIncomplete = whyIncomplete;
            RequiresJointKinematics = requiresJointKinematics;
        }

        public Link Configuration { get; private set; }
        public bool IsIncomplete { get; set; }
        public bool NeedsSaving { get; set; }
        public string WhyIncomplete { get; set; }
        public bool RequiresJointKinematics { get; set; }

        public LinkConfigurationState Clone()
        {
            return new LinkConfigurationState(
                LinkConfigurationStore.CloneConfiguration(Configuration),
                IsIncomplete,
                NeedsSaving,
                WhyIncomplete,
                RequiresJointKinematics);
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

        private static byte[] CloneBytes(byte[] value)
        {
            return value == null ? null : (byte[])value.Clone();
        }
    }
}
