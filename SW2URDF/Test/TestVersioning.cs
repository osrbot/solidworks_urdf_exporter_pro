
using SW2URDF.Versioning;
using SW2URDF.URDF;
using System.IO;
using System.Xml;
using Xunit;

namespace SW2URDF.Test
{
    public class TestVersioning
    {
        [Fact]
        public void TestGetCommitVersion()
        {
            string commitVersion = Version.GetCommitVersion();
            Assert.NotNull(commitVersion);
            Assert.NotEmpty(commitVersion);
        }

        [Fact]
        public void TestGetBuildVersion()
        {
            string buildVersion = Version.GetBuildVersion();
            Assert.NotNull(buildVersion);
            Assert.NotEmpty(buildVersion);
        }

        [Fact]
        public void TestGetReleaseMetadata()
        {
            Assert.NotNull(Version.GetPluginVersion());
            Assert.NotEmpty(Version.GetPluginVersion());
            Assert.NotNull(Version.GetCommitHash());
            Assert.NotEmpty(Version.GetCommitHash());
            Assert.NotNull(Version.GetBuildTimeUtc());
            Assert.NotEmpty(Version.GetBuildTimeUtc());
            Assert.NotNull(Version.GetDirtyState());
            Assert.NotEmpty(Version.GetDirtyState());
        }

        [Fact]
        public void TestRobotUrdfCommentIncludesReleaseMetadata()
        {
            Robot robot = new Robot();
            robot.Name = "metadata_robot";

            using (StringWriter output = new StringWriter())
            using (XmlWriter writer = XmlWriter.Create(output))
            {
                robot.WriteURDF(writer);
                string urdf = output.ToString();

                Assert.Contains("Plugin Version:", urdf);
                Assert.Contains("Commit Version:", urdf);
                Assert.Contains("Commit Hash:", urdf);
                Assert.Contains("Build Version:", urdf);
                Assert.Contains("Build Time UTC:", urdf);
            }
        }
    }
}
