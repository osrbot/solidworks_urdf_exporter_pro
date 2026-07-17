using System;

namespace LinkTreeCanvasPrototype
{
    /// <summary>
    /// Integration boundary for the future SW2URDF adapter. The prototype owns no CAD objects.
    /// </summary>
    public interface ILinkTreeCanvasHost
    {
        LinkTreeDocument LoadTree();
        void ApplyTree(LinkTreeDocument document);
        string ValidateLinkName(string linkName, Guid editingNodeId);
    }

    public sealed class PrototypeLinkTreeHost : ILinkTreeCanvasHost
    {
        private LinkTreeDocument document;

        public PrototypeLinkTreeHost()
        {
            document = LinkTreeDocument.CreateSample();
        }

        public LinkTreeDocument LoadTree()
        {
            return document.Clone();
        }

        public void ApplyTree(LinkTreeDocument updatedDocument)
        {
            document = updatedDocument.Clone();
        }

        public string ValidateLinkName(string linkName, Guid editingNodeId)
        {
            return LinkTreeDocument.ValidateRosName(linkName);
        }
    }
}
