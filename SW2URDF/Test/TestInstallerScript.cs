using System;
using System.IO;
using Xunit;

namespace SW2URDF.Test
{
    public class TestInstallerScript
    {
        [Fact]
        public void TestInstallerSupportsLanguageAndInstallDirectorySelection()
        {
            string installerScript = ReadRepositoryFile("INSTALL", "Install.iss");

            Assert.Contains("DisableDirPage=no", installerScript);
            Assert.Contains("UsePreviousAppDir=no", installerScript);
            Assert.Contains("DefaultDirName=\"{autopf}\\SolidWorks Corp\\SolidWorks\\URDFExporter\"",
                installerScript);
            Assert.Contains("GetVersionNumbersString(DllLocation)", installerScript);
            Assert.Contains("OutputBaseFilename={#SetupBaseName + AppVersionFile + \"_\" + InstallerCommitFilePart}",
                installerScript);
            Assert.Contains("Name: \"english\"", installerScript);
            Assert.Contains("Name: \"chinesesimplified\"", installerScript);
            Assert.Contains("chinesesimplified.RegisteringControls=", installerScript);
            Assert.Contains("chinesesimplified.UnregisteringControls=", installerScript);
        }

        [Fact]
        public void TestInstallerRegistrationUsesSelectedInstallDirectory()
        {
            string installerScript = ReadRepositoryFile("INSTALL", "Install.iss");

            Assert.Contains("Parameters: \"\"\"{app}\\SW2URDF.dll\"\" \"\"/codebase\"\"\"",
                installerScript);
            Assert.Contains("Parameters:  \"\"\"{app}\\SW2URDF.dll\"\" \"\"/unregister\"\"\"",
                installerScript);
            Assert.Contains("SOFTWARE\\SolidWorks\\Addins\\{{65c9fc17-6a74-45a3-8f84-55185900275d}",
                installerScript);
            Assert.Contains("Software\\SolidWorks\\AddInsStartup\\{{65c9fc17-6a74-45a3-8f84-55185900275d}",
                installerScript);
        }

        [Fact]
        public void TestChineseInstallerMessagesAreUtf8()
        {
            string chineseMessages = ReadRepositoryFile("INSTALL", "Languages", "ChineseSimplified.isl");

            Assert.Contains("LanguageName=\u7b80\u4f53\u4e2d\u6587", chineseMessages);
            Assert.Contains("\u5b89\u88c5", chineseMessages);
        }

        private static string ReadRepositoryFile(params string[] pathParts)
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, Path.Combine(pathParts));
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate repository file " + Path.Combine(pathParts));
        }
    }
}
