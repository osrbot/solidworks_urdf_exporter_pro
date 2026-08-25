using SW2URDF.UI;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SW2URDF.Test
{
    public class TestUrdfExportTutorial
    {
        [Fact]
        public void TestStateStoreDefaultsAndRoundTripsProgress()
        {
            string directory = CreateTemporaryDirectory();
            string filePath = Path.Combine(directory, "tutorial.state");
            try
            {
                FileUrdfExportTutorialStateStore store =
                    new FileUrdfExportTutorialStateStore(filePath);

                UrdfExportTutorialProgress initial = store.Load();
                Assert.Equal(UrdfExportTutorialStatus.NotStarted, initial.Status);
                Assert.Equal(0, initial.StepIndex);

                Assert.True(store.Save(new UrdfExportTutorialProgress(
                    UrdfExportTutorialStatus.InProgress,
                    4)));
                UrdfExportTutorialProgress inProgress = store.Load();
                Assert.Equal(UrdfExportTutorialStatus.InProgress, inProgress.Status);
                Assert.Equal(4, inProgress.StepIndex);

                Assert.True(store.Save(new UrdfExportTutorialProgress(
                    UrdfExportTutorialStatus.Completed,
                    7)));
                UrdfExportTutorialProgress completed = store.Load();
                Assert.Equal(UrdfExportTutorialStatus.Completed, completed.Status);
                Assert.Equal(7, completed.StepIndex);
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        [Fact]
        public void TestCorruptOrFutureStateDoesNotBlockFirstUse()
        {
            string directory = CreateTemporaryDirectory();
            string filePath = Path.Combine(directory, "tutorial.state");
            try
            {
                File.WriteAllText(filePath, "version=999\r\nstatus=Unknown\r\nstep=-4\r\n");
                FileUrdfExportTutorialStateStore store =
                    new FileUrdfExportTutorialStateStore(filePath);

                UrdfExportTutorialProgress progress = store.Load();

                Assert.Equal(UrdfExportTutorialStatus.NotStarted, progress.Status);
                Assert.Equal(0, progress.StepIndex);
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        [Fact]
        public void TestTutorialCoversTheCompleteExportWorkflow()
        {
            IList<UrdfExportTutorialStep> steps = UrdfExportTutorialContent.Build(false);

            Assert.Equal(8, steps.Count);
            string completeText = String.Join(
                "\n",
                new List<UrdfExportTutorialStep>(steps).ConvertAll(
                    step => step.BuildDisplayText(false)).ToArray());
            Assert.Contains("Origin_global", completeText);
            Assert.Contains("# base_link", completeText);
            Assert.Contains("camera_joint", completeText);
            Assert.Contains("ComponentBoxes", completeText);
            Assert.Contains("inertial_validation.csv", completeText);
            Assert.Contains("mesh_manifest.csv", completeText);
            Assert.Contains("ROS1", completeText);
            Assert.Contains("ROS2", completeText);
            Assert.Contains("robot_state_publisher", completeText);
        }

        [Fact]
        public void TestChineseAndEnglishTutorialsHaveMatchingStepIds()
        {
            IList<UrdfExportTutorialStep> english = UrdfExportTutorialContent.Build(false);
            IList<UrdfExportTutorialStep> chinese = UrdfExportTutorialContent.Build(true);

            Assert.Equal(english.Count, chinese.Count);
            for (int i = 0; i < english.Count; i++)
            {
                Assert.Equal(english[i].Id, chinese[i].Id);
                Assert.False(String.IsNullOrWhiteSpace(chinese[i].Title));
                Assert.False(String.IsNullOrWhiteSpace(chinese[i].Instructions));
                Assert.False(String.IsNullOrWhiteSpace(chinese[i].Verification));
            }
        }

        [Fact]
        public void TestTutorialFormRestoresSavedStep()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                FileUrdfExportTutorialStateStore store = new FileUrdfExportTutorialStateStore(
                    Path.Combine(directory, "tutorial.state"));
                using (UrdfExportTutorialForm form = new UrdfExportTutorialForm(
                    store,
                    new UrdfExportTutorialProgress(UrdfExportTutorialStatus.InProgress, 5),
                    false))
                {
                    Assert.Equal(8, form.StepCount);
                    Assert.Equal(5, form.CurrentStepIndex);
                    Assert.Equal(System.Windows.Forms.AutoScaleMode.Dpi, form.AutoScaleMode);
                    Assert.True(form.MinimumSize.Width >= 780);
                    Assert.True(form.MinimumSize.Height >= 560);
                }
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        [Fact]
        public void TestExplicitReopenResumesWorkButRestartsCompletedTutorial()
        {
            UrdfExportTutorialProgress inProgress =
                UrdfExportTutorialController.ResolveExplicitProgress(
                    new UrdfExportTutorialProgress(
                        UrdfExportTutorialStatus.InProgress,
                        4));
            UrdfExportTutorialProgress completed =
                UrdfExportTutorialController.ResolveExplicitProgress(
                    new UrdfExportTutorialProgress(
                        UrdfExportTutorialStatus.Completed,
                        7));
            UrdfExportTutorialProgress dismissed =
                UrdfExportTutorialController.ResolveExplicitProgress(
                    new UrdfExportTutorialProgress(
                        UrdfExportTutorialStatus.Dismissed,
                        5));

            Assert.Equal(4, inProgress.StepIndex);
            Assert.Equal(0, completed.StepIndex);
            Assert.Equal(UrdfExportTutorialStatus.Completed, completed.Status);
            Assert.Equal(0, dismissed.StepIndex);
            Assert.Equal(UrdfExportTutorialStatus.Dismissed, dismissed.Status);
        }

        private static string CreateTemporaryDirectory()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-tutorial-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void DeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch
            {
                // Test cleanup should not hide the assertion result.
            }
        }
    }
}
