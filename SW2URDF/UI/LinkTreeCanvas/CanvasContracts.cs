using System;

namespace SW2URDF.UI.LinkTreeCanvas
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

}
