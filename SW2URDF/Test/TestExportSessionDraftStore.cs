using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace SW2URDF.Test
{
    public class TestExportSessionDraftStore
    {
        [Fact]
        public void DraftRoundTripPreservesTreeAndExportMetadata()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                FileExportSessionDraftStore store = new FileExportSessionDraftStore(directory);
                string modelPath = Path.Combine(directory, "robot.SLDASM");
                LinkNode root = CreateTree();
                root.Link.MeshReductionRatio = 0.35;

                Assert.True(store.Save(modelPath, root, "rover_description", "D:\\exports"));
                Assert.True(store.TryLoad(modelPath, out ExportSessionDraft restored));

                Assert.Equal("base_link", restored.Root.Link.Name);
                Assert.Equal("camera_link", ((LinkNode)restored.Root.Nodes[0]).Link.Name);
                Assert.Equal(0.35, restored.Root.Link.MeshReductionRatio, 12);
                Assert.Equal("rover_description", restored.RosPackageName);
                Assert.Equal("D:\\exports", restored.SavePath);
                Assert.True(restored.SavedUtc > DateTime.UtcNow.AddMinutes(-1));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [Fact]
        public void DraftsAreIsolatedByTheCompleteAssemblyPath()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                FileExportSessionDraftStore store = new FileExportSessionDraftStore(directory);
                string firstModel = Path.Combine(directory, "first", "robot.SLDASM");
                string secondModel = Path.Combine(directory, "second", "robot.SLDASM");

                Assert.True(store.Save(firstModel, CreateTree(), "first_package", directory));
                Assert.False(store.TryLoad(secondModel, out ExportSessionDraft secondDraft));
                Assert.Null(secondDraft);
                Assert.True(store.TryLoad(firstModel, out ExportSessionDraft firstDraft));
                Assert.Equal("first_package", firstDraft.RosPackageName);
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [Fact]
        public void DeletingDraftMakesTheNextLoadStartFromCommittedConfiguration()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                FileExportSessionDraftStore store = new FileExportSessionDraftStore(directory);
                string modelPath = Path.Combine(directory, "robot.SLDASM");

                Assert.True(store.Save(modelPath, CreateTree(), "robot_description", directory));
                Assert.True(store.Delete(modelPath));
                Assert.False(store.TryLoad(modelPath, out ExportSessionDraft draft));
                Assert.Null(draft);
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [Fact]
        public void CorruptDraftIsIgnoredWithoutDeletingOtherState()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                FileExportSessionDraftStore store = new FileExportSessionDraftStore(directory);
                string modelPath = Path.Combine(directory, "robot.SLDASM");
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    store.GetDraftFilePath(modelPath),
                    "not valid XML",
                    new UTF8Encoding(false));

                Assert.False(store.TryLoad(modelPath, out ExportSessionDraft draft));
                Assert.Null(draft);
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        private static LinkNode CreateTree()
        {
            LinkNode root = new LinkNode();
            root.Link.Name = "base_link";
            root.Name = root.Link.Name;
            root.Text = root.Link.Name;
            root.IsBaseNode = true;

            LinkNode child = new LinkNode();
            child.Link.Name = "camera_link";
            child.Link.Joint.Name = "camera_joint";
            child.Link.Joint.Type = "fixed";
            child.Name = child.Link.Name;
            child.Text = child.Link.Name;
            root.Nodes.Add(child);
            return root;
        }

        private static string CreateTemporaryDirectory()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-draft-tests-" + Guid.NewGuid().ToString("N"));
        }

        private static void DeleteTemporaryDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
