namespace SW2URDF.UI.LinkTreeCanvas
{
    /// <summary>
    /// Host boundary for topology editing. The canvas owns no SolidWorks CAD objects.
    /// </summary>
    public interface ILinkTreeCanvasHost
    {
        LinkTreeDocument LoadTree();
        void ApplyTree(LinkTreeDocument document);
    }

    public interface ILinkTreeCandidateValidator
    {
        void ValidateTree(LinkTreeDocument document);
    }

}
