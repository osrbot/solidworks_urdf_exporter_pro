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
            Assert.Contains("UsePreviousAppDir=yes", installerScript);
            Assert.Contains("DefaultDirName=\"{autopf}\\SolidWorks Corp\\SolidWorks\\URDFExporter\"",
                installerScript);
            Assert.Contains("GetVersionNumbersString(DllLocation)", installerScript);
            Assert.Contains("OutputBaseFilename={#SetupBaseName + InstallerDate + \"_\" + InstallerCommit}",
                installerScript);
            Assert.Contains("Name: \"english\"", installerScript);
            Assert.Contains("Name: \"chinesesimplified\"", installerScript);
            Assert.Contains("chinesesimplified.RegisteringControls=", installerScript);
            Assert.Contains("chinesesimplified.UnregisteringControls=", installerScript);
            Assert.Contains("DotNet48MinimumRelease = 528040", installerScript);
            Assert.Contains("function InitializeSetup(): Boolean", installerScript);
            Assert.Contains("{cm:DotNet48Required}", installerScript);
            Assert.Contains("OSRBot / kitso666", installerScript);
            Assert.Contains("https://github.com/osrbot/solidworks_urdf_exporter_pro", installerScript);
            Assert.Contains("\\*.dll\"}; DestDir: {app}", installerScript);
            Assert.Contains("\\SW2URDF.png\"}; DestDir: {app}", installerScript);
            Assert.Contains("\\images\\*.png\"}; DestDir: {app}\\images", installerScript);
            Assert.Contains("\\schemas\\*\"}; DestDir: {app}\\schemas", installerScript);
            Assert.Contains("\\tools\\isaac_adapter\\*\"}; DestDir: {app}\\tools\\isaac_adapter", installerScript);
            Assert.Contains("THIRD_PARTY_NOTICES.md", installerScript);
            Assert.Contains("\\THIRD_PARTY_LICENSES\\*\"}; DestDir: {app}\\THIRD_PARTY_LICENSES", installerScript);
            Assert.DoesNotContain("\\*.pdb", installerScript);
            Assert.DoesNotContain("\\*.xml", installerScript);
        }

        [Fact]
        public void TestDeepReferenceFixtureGeneratorIsRepositorySelfContained()
        {
            string script = ReadRepositoryFile(
                "scripts",
                "create_deep_reference_fixture.py");

            Assert.Contains(
                "DispatchEx(\"SldWorks.Application\")",
                script);
            Assert.Contains(
                "pythoncom.VT_BYREF | pythoncom.VT_I4",
                script);
            Assert.Contains("AddComponent5", script);
            Assert.Contains("model.Extension.SaveAs", script);
            Assert.Contains("--assembly-template", script);
            Assert.Contains("_assert_feature_exists", script);
            Assert.DoesNotContain(".codex", script);
            Assert.DoesNotContain(
                "SOLIDWORKS_AUTOMATION_SCRIPTS",
                script);
            Assert.DoesNotContain("sw_session", script);
            Assert.DoesNotContain("sw_connect", script);
            Assert.DoesNotContain("sw_assembly", script);
        }

        [Fact]
        public void TestBuildInstallerUsesDateAndCommitOutputName()
        {
            string buildScript = ReadRepositoryFile("scripts", "BuildInstaller.ps1");

            Assert.Contains("Get-Date -Format \"yyyyMMdd\"", buildScript);
            Assert.Contains("rev-parse --short=7 HEAD", buildScript);
            Assert.Contains("\"/DInstallerDate=$InstallerDate\"", buildScript);
            Assert.Contains("\"/DInstallerCommit=$InstallerCommit\"", buildScript);
            Assert.Contains("\"/DBuildConfiguration=$Configuration\"", buildScript);
            Assert.Contains("\"/DBuildPlatform=$Platform\"", buildScript);
            Assert.Contains("Refusing to package uncommitted source changes", buildScript);
            Assert.Contains("Configuration=Release and Platform=x64", buildScript);
            Assert.Contains("Remove-Item -LiteralPath $ResolvedBuildOutput -Recurse -Force", buildScript);
            Assert.Contains("packages.release.config", buildScript);
            Assert.Contains("packages.release.lock.json", buildScript);
            Assert.Contains("NuGet.Config", buildScript);
            Assert.Contains("Downloaded NuGet CLI does not match the pinned SHA256", buildScript);
            Assert.Contains("NuGet package $($_.id) $($_.version) does not match", buildScript);
            Assert.Contains("worktree add", buildScript);
            Assert.Contains("worktree remove", buildScript);
            Assert.Contains("local-build-from-immutable-git-worktree", buildScript);
            Assert.Contains("$StagedSolidWorksDirectory", buildScript);
            Assert.Contains(
                "$VsWhere -latest -products * -requires Microsoft.Component.MSBuild",
                buildScript);
            Assert.Contains("$VsWhere -latest -products * -find", buildScript);
            Assert.Contains("Inno Setup compiler was not found", buildScript);
            Assert.Contains("requires Inno Setup 6.3.0 through 6.3.3", buildScript);
            Assert.Contains("Source changed during packaging. The installer was not promoted", buildScript);
            Assert.Contains("$PostBuildChanges", buildScript);
            Assert.Contains("$BuildStatusExitCode", buildScript);
            Assert.Contains("$SourceStatusExitCode", buildScript);
            Assert.Contains("$ProvenancePath", buildScript);
            Assert.Contains("payloadInputs = $PayloadInputs", buildScript);
            Assert.Contains("Refusing to overwrite an existing release artifact", buildScript);
            Assert.Contains("$ResolvedIntermediateOutput", buildScript);
            Assert.Contains("Remove-Item -LiteralPath $ResolvedIntermediateOutput -Recurse -Force", buildScript);
            Assert.Contains("$Project = Join-Path $BuildRoot \"SW2URDF\\SW2URDF.csproj\"", buildScript);
            Assert.Contains("\"/p:SolutionDir=$SolutionDir\"", buildScript);
            Assert.Contains("$RequiredPayloadFiles", buildScript);
            Assert.Contains("\"solidworkstools.dll\"", buildScript);
            Assert.Contains("\"OSURDF.Core.dll\"", buildScript);
            Assert.Contains("\"Newtonsoft.Json.dll\"", buildScript);
            Assert.Contains("\"log4net.dll\"", buildScript);
            Assert.Contains("\"APACHE-2.0.txt\"", buildScript);
            Assert.Contains("\"MIT.txt\"", buildScript);
            Assert.Contains("\"osurdf_isaac_adapter.py\"", buildScript);
            Assert.Contains("\"robot.schema.v2.json\"", buildScript);
            Assert.Contains("RestoreLockedMode=true", buildScript);
            Assert.Contains("SW2URDFBaseIntermediateOutputPath", buildScript);
            Assert.Contains("sdkPackageLocks = $SdkPackageLocks", buildScript);
            Assert.Contains("TestRunner\\packages.lock.json", buildScript);
            Assert.Contains("SW2URDF_TEST_ASSEMBLY", buildScript);
            Assert.Contains("SolidWorks regression suite failed", buildScript);
            Assert.Contains("pluginTests = $PluginTestEvidence", buildScript);
            Assert.Contains("required installer payload: $RequiredPayloadFile", buildScript);
            Assert.DoesNotContain("& $MSBuild $Solution", buildScript);
            Assert.DoesNotContain("$NuGetCommand.Source", buildScript);
        }

        [Fact]
        public void TestReleaseBuildExcludesTestsAndTestFrameworks()
        {
            string project = ReadRepositoryFile("SW2URDF", "SW2URDF.csproj");

            Assert.Contains("<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>", project);
            Assert.Contains("<BootstrapperPackage Include=\".NETFramework,Version=v4.8\">", project);
            Assert.Contains("<ItemGroup Condition=\"'$(Configuration)' != 'Release'\">", project);
            Assert.Contains("<Compile Include=\"Test\\TestLinkTreeCanvas.cs\" />", project);
            Assert.Contains("xunit.assert", project);
            Assert.Contains("Condition=\"'$(Configuration)' != 'Release'\"", project);
            Assert.Contains("'$(Configuration)' != 'Release' And !Exists('..\\packages\\xunit.core", project);
            Assert.Contains("'$(Configuration)' != 'Release' And Exists('..\\packages\\xunit.core.2.4.1\\build\\xunit.core.targets')", project);

            string releasePackages = ReadRepositoryFile("SW2URDF", "packages.release.config");
            Assert.Contains("Microsoft.Net.Compilers", releasePackages);
            Assert.Contains("CsvHelper", releasePackages);
            Assert.Contains("log4net\" version=\"3.4.0", releasePackages);
            Assert.Contains("MathNet.Numerics.Signed", releasePackages);
            Assert.DoesNotContain("id=\"MathNet.Numerics\"", releasePackages);
            Assert.DoesNotContain("xunit", releasePackages);
            Assert.DoesNotContain("Moq", releasePackages);

            string assemblyInfo = ReadRepositoryFile("SW2URDF", "AssemblyInfo.cs");
            Assert.Contains("AssemblyVersion(\"1.6.0.0\")", assemblyInfo);
            Assert.Contains("AssemblyFileVersion(\"1.6.0.0\")", assemblyInfo);
            Assert.DoesNotContain("AssemblyVersion(\"1.6.*\")", assemblyInfo);
            Assert.Contains("<Deterministic>true</Deterministic>", project);
            Assert.Contains("<PathMap>$(MSBuildProjectDirectory)=/_/SW2URDF</PathMap>", project);
            Assert.Contains("<DebugType>none</DebugType>", project);
            Assert.Contains("<HintPath>$(SolidWorksInstallDir)\\solidworkstools.dll</HintPath>", project);
            Assert.Contains("<Private>True</Private>", project);
            Assert.Contains("<VersionInfoStrictArgument Condition=", project);
            Assert.Contains("$(VersionInfoStrictArgument)", project);

            string versionScript = ReadRepositoryFile("scripts", "UpdateVersionInfo.ps1");
            Assert.Contains("[switch]$Strict", versionScript);
            Assert.Contains("Unable to resolve the Git commit for Release version metadata", versionScript);
            Assert.Contains("Unable to resolve the Git commit time for Release version metadata", versionScript);

            string nugetConfig = ReadRepositoryFile("NuGet.Config");
            Assert.Contains("<clear />", nugetConfig);
            Assert.Contains("https://api.nuget.org/v3/index.json", nugetConfig);
            string releaseLock = ReadRepositoryFile(
                "SW2URDF",
                "packages.release.lock.json");
            Assert.Contains("\"version\": \"6.11.1\"", releaseLock);
            Assert.Contains("\"sha256\"", releaseLock);
            Assert.DoesNotContain("\"id\": \"MathNet.Numerics\",", releaseLock);
            Assert.Contains("\"id\": \"log4net\"", releaseLock);
            Assert.Contains("..\\packages\\log4net.3.4.0\\lib\\net462\\log4net.dll", project);
            Assert.DoesNotContain("<HintPath>lib\\log4net.dll</HintPath>", project);
            Assert.Contains("..\\THIRD_PARTY_LICENSES\\*.txt", project);

            string testRunnerProject = ReadRepositoryFile("TestRunner", "TestRunner.csproj");
            Assert.Contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", testRunnerProject);
            Assert.Contains("<PlatformTarget>x64</PlatformTarget>", testRunnerProject);
            Assert.Contains("<Prefer32Bit>false</Prefer32Bit>", testRunnerProject);
            Assert.Contains("<RuntimeIdentifier>win7-x64</RuntimeIdentifier>", testRunnerProject);
            Assert.Contains(
                "<AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>",
                testRunnerProject);
            Assert.Contains("Microsoft.NETFramework.ReferenceAssemblies", testRunnerProject);
            Assert.Contains("xunit.runner.utility\" Version=\"2.4.1\"", testRunnerProject);
            string testRunnerLock = ReadRepositoryFile("TestRunner", "packages.lock.json");
            Assert.Contains("\"xunit.runner.utility\"", testRunnerLock);
            Assert.Contains("\"resolved\": \"2.4.1\"", testRunnerLock);

            string testRunner = ReadRepositoryFile("TestRunner", "TestRunner.cs");
            Assert.Contains("No SW2URDF tests were discovered or executed", testRunner);
            Assert.Contains("testsDiscovered <= 0 || info.TotalTests <= 0", testRunner);
        }

        [Fact]
        public void TestReleaseWorkflowIgnoresDeletedInstallers()
        {
            string workflow = ReadRepositoryFile(
                ".github", "workflows", "publish-installer-release.yml");

            Assert.Contains("git diff --diff-filter=AMR", workflow);
            Assert.Contains("publish=false", workflow);
            Assert.Contains("if: steps.installer.outputs.publish == 'true'", workflow);
            Assert.Contains("RELEASE_COMMIT", workflow);
            Assert.Contains("git log -1 --format=%ct", workflow);
            Assert.Contains("gh release create", workflow);
            Assert.Contains("group: publish-installer-release", workflow);
            Assert.Contains("git rev-parse --verify", workflow);
            Assert.Contains("artifact_parent", workflow);
            Assert.Contains("Artifact commit must change only the installer and its two sidecars", workflow);
            Assert.Contains("--target \"$RELEASE_SOURCE_SHA\"", workflow);
            Assert.Contains("INPUT_INSTALLER: ${{ github.event.inputs.installer }}", workflow);
            Assert.Contains("sha256sum --check", workflow);
            Assert.Contains("installerSha256", workflow);
            Assert.Contains("sourceTree", workflow);
            Assert.Contains("packages.release.lock.json", workflow);
            Assert.Contains(".pluginTests.result == \"passed\"", workflow);
            Assert.Contains("TestRunner/bin/Release/net48/TestRunner.exe", workflow);
            Assert.Contains("innoextract", workflow);
            Assert.Contains("Installer payload does not match the provenance manifest", workflow);
            Assert.Contains("Extracted installer payload hash mismatch", workflow);
            Assert.Contains("THIRD_PARTY_LICENSES/APACHE-2.0.txt", workflow);
            Assert.Contains("THIRD_PARTY_LICENSES/MIT.txt", workflow);
            Assert.Contains("--json isDraft", workflow);
            Assert.Contains("--draft", workflow);
            Assert.DoesNotContain("--draft=false", workflow);
            Assert.DoesNotContain("gh release edit", workflow);
            Assert.Contains(".github/release-notes/${RELEASE_TAG}.md", workflow);
            Assert.Contains("reviewed bilingual release notes", workflow);
            Assert.Contains("## English", workflow);
            Assert.Contains("## \u7b80\u4f53\u4e2d\u6587", workflow);
            Assert.Contains("{{INSTALLER_SHA256}}", workflow);
            Assert.Contains("Release notes contain an unresolved placeholder", workflow);
            Assert.Contains("Daily releases are immutable", workflow);
            Assert.Contains("Removing an incomplete draft", workflow);
            Assert.Contains("--cleanup-tag", workflow);
            Assert.Contains(
                "actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2",
                workflow);
            Assert.Contains("persist-credentials: false", workflow);
            Assert.Contains("timeout-minutes: 60", workflow);
            Assert.DoesNotContain("uses: actions/checkout@v4", workflow);
            Assert.DoesNotContain("git tag --force", workflow);
            Assert.DoesNotContain("gh release delete-asset", workflow);
            Assert.DoesNotContain("-printf \"%T@ %p", workflow);
        }

        [Theory]
        [InlineData("solidworks-integration.yml")]
        [InlineData("ros-integration.yml")]
        [InlineData("isaac-integration.yml")]
        public void TestSelfHostedIntegrationWorkflowsRequireManualDispatch(string workflowName)
        {
            string workflow = ReadRepositoryFile(
                ".github", "workflows", workflowName);

            Assert.Contains("workflow_dispatch:", workflow);
            Assert.Contains("runs-on: [self-hosted,", workflow);
            Assert.Contains("persist-credentials: false", workflow);
            Assert.Contains("timeout-minutes:", workflow);
            Assert.DoesNotContain("pull_request:", workflow);
            Assert.DoesNotContain("pull_request_target:", workflow);
        }

        [Fact]
        public void TestReleaseNotesTemplateIsBilingualAndTraceable()
        {
            string notes = ReadRepositoryFile(
                ".github", "release-notes", "v20260827.md");

            Assert.Contains("## English", notes);
            Assert.Contains("## \u7b80\u4f53\u4e2d\u6587", notes);
            Assert.Contains("### Added", notes);
            Assert.Contains("### \u65b0\u589e\u529f\u80fd", notes);
            Assert.Contains("{{RELEASE_DATE}}", notes);
            Assert.Contains("{{INSTALLER_FILE}}", notes);
            Assert.Contains("{{INSTALLER_SHA256}}", notes);
            Assert.Contains("{{RELEASE_SOURCE_SHA}}", notes);
            Assert.Contains("{{RELEASE_ARTIFACT_SHA}}", notes);
            Assert.Contains("Draft only", notes);
            Assert.Contains("\u5f53\u524d\u4ec5\u4e3a Draft", notes);
        }

        [Fact]
        public void TestThirdPartyRuntimeLicensesArePackagedAndScoped()
        {
            string notices = ReadRepositoryFile("THIRD_PARTY_NOTICES.md");
            string mit = ReadRepositoryFile("THIRD_PARTY_LICENSES", "MIT.txt");
            string apache = ReadRepositoryFile("THIRD_PARTY_LICENSES", "APACHE-2.0.txt");

            Assert.Contains("Apache log4net 3.4.0", notices);
            Assert.Contains("CsvHelper 7.1.1", notices);
            Assert.Contains("MathNet.Numerics.Signed 4.7.0", notices);
            Assert.Contains("Newtonsoft.Json 13.0.3", notices);
            Assert.Contains("System.Runtime.CompilerServices.Unsafe 4.5.0", notices);
            Assert.Contains("System.Threading.Tasks.Extensions 4.5.1", notices);
            Assert.Contains("solidworkstools.dll", notices);
            Assert.Contains("does not grant redistribution rights", notices);
            Assert.Contains("Permission is hereby granted", mit);
            Assert.Contains("Apache License", apache);
            Assert.Contains("Version 2.0, January 2004", apache);
        }

        [Fact]
        public void TestToolbarImagesFollowTheSelectedInstallDirectory()
        {
            string addin = ReadRepositoryFile("SW2URDF", "SW", "SwAddin.cs");

            Assert.Contains("Path.GetDirectoryName(typeof(SwAddin).Assembly.Location)", addin);
            Assert.Contains("Path.Combine(imageDirectory, \"ros_logo_20x20.png\")", addin);
            Assert.DoesNotContain("C:\\\\Program Files\\\\SOLIDWORKS Corp", addin);
        }

        [Fact]
        public void TestInstallerRegistrationUsesSelectedInstallDirectory()
        {
            string installerScript = ReadRepositoryFile("INSTALL", "Install.iss");

            Assert.Contains("Parameters: \"\"\"{app}\\SW2URDF.dll\"\" \"\"/codebase\"\"\"",
                installerScript);
            Assert.Contains("Parameters:  \"\"\"{app}\\SW2URDF.dll\"\" \"\"/unregister\"\"\"",
                installerScript);
            Assert.Contains("Check: IsWin64 and CurrentInstallOwnsComRegistration",
                installerScript);
            Assert.Contains("RegQueryStringValue(", installerScript);
            Assert.Contains("HKLM64, Sw2UrdfComRegistrationKey", installerScript);
            Assert.Contains("\\InprocServer32';", installerScript);
            Assert.Contains("'CodeBase'", installerScript);
            Assert.Contains("ExpandConstant('{app}\\SW2URDF.dll')", installerScript);
            Assert.DoesNotContain("Flags: dontcreatekey deletekey", installerScript);
        }

        [Fact]
        public void TestUnregisterOnlyDeletesPrivateAddinKeysIdempotently()
        {
            string addin = ReadRepositoryFile("SW2URDF", "SW", "SwAddin.cs");

            Assert.Contains("DeleteRegistrySubKeyIfPresent(", addin);
            Assert.Contains("root.DeleteSubKey(keyName, false)", addin);
            Assert.Contains("SOFTWARE\\\\SolidWorks\\\\Addins\\\\{", addin);
            Assert.Contains("Software\\\\SolidWorks\\\\AddInsStartup\\\\{", addin);
            Assert.DoesNotContain("DeleteSubKeyTree", addin);
            Assert.DoesNotContain("DeleteSubKey(\"SOFTWARE\\\\SolidWorks\"", addin);
        }

        [Fact]
        public void TestChineseInstallerMessagesAreUtf8()
        {
            string chineseMessages = ReadRepositoryFile("INSTALL", "Languages", "ChineseSimplified.isl");

            Assert.Contains("LanguageName=\u7b80\u4f53\u4e2d\u6587", chineseMessages);
            Assert.Contains("\u5b89\u88c5", chineseMessages);
        }

        [Fact]
        public void TestConfigurationPersistenceUsesValidatedRecoverySlotAndStrictVersionTwoBoundary()
        {
            string serialization = ReadRepositoryFile(
                "SW2URDF", "URDFExport", "ConfigurationSerialization.cs");

            Assert.Contains(
                "URDF Export Configuration (v2 recovery)",
                serialization);
            Assert.Contains("WriteAndValidateConfigurationSlot(", serialization);
            Assert.Contains("GetCurrentConfigurationCandidate(model)", serialization);
            Assert.Contains(
                "Invalidate an existing slot before changing any value",
                serialization);
            Assert.Contains(
                "The nonzero revision is the commit marker",
                serialization);
            Assert.Contains("DeleteRecoveryAttribute(model)", serialization);
            Assert.Contains("WriteSaveAttribute(", serialization);
            Assert.Contains("AddRequiredAttributeParameter(", serialization);
            Assert.Contains("EnsureConfigurationAttributeSchema(", serialization);
            Assert.Contains(
                "UrdfConfigurationDefinitionPrefix + Guid.NewGuid().ToString(\"N\")",
                serialization);
            Assert.Contains("if (!definition.Register())", serialization);
            Assert.Contains(
                "ReferenceEquals(configurationAttributeDefinitionOwner, swApp)",
                serialization);
            Assert.Contains("configurationAttributeDefinition = definition", serialization);
            Assert.Contains("TryBeginConfigurationSave()", serialization);
            Assert.Contains("EndConfigurationSave()", serialization);
            Assert.Contains("saveExporterAttribute == null", serialization);
            Assert.Contains("SerializationVersion = 2.0", serialization);
            Assert.Contains("URDF Export Configuration (v2)", serialization);
            Assert.Contains("HasLegacyConfiguration(model)", serialization);
            Assert.Contains("name-based URDF configuration", serialization);
            Assert.Contains(
                "SolidWorks did not persist and validate the complete URDF",
                serialization);
            Assert.Contains("!parameter.SetStringValue2", serialization);
            Assert.Contains("!parameter.SetDoubleValue2", serialization);
            Assert.Contains("oldData == newData", serialization);
            Assert.Contains("Saving was stopped to protect it", serialization);
            Assert.DoesNotContain("SaveAttributeSnapshot", serialization);
            Assert.DoesNotContain("AggregateException", serialization);
            Assert.DoesNotContain("PREVIOUS_URDF_CONFIGURATION_NAMES", serialization);
            Assert.DoesNotContain("LoadConfigFromStringXML", serialization);
            Assert.DoesNotContain("MessageBox.", serialization);

            string interaction = ReadRepositoryFile(
                "SW2URDF", "UI", "ConfigurationSaveInteraction.cs");
            Assert.Contains("ConfigurationSaveStatus.ConfirmationRequired", interaction);
            Assert.Contains("MessageBox.Show", interaction);
            Assert.Contains("logger.Info(result.InformationMessage)", interaction);
            Assert.DoesNotContain("MessageBox.Show(result.InformationMessage", interaction);
        }

        [Fact]
        public void TestExportValidatesLinksBeforePersistingConfiguration()
        {
            string form = ReadRepositoryFile("SW2URDF", "UI", "AssemblyExportForm.cs");
            int createRobot = form.IndexOf(
                "Exporter.URDFRobot = CreateRobotFromTreeView",
                StringComparison.Ordinal);
            int validateLinks = form.IndexOf(
                "CheckLinksForErrors(Exporter.URDFRobot.BaseLink)",
                createRobot,
                StringComparison.Ordinal);
            int saveConfiguration = form.IndexOf(
                "SaveConfigTree(ActiveSWModel, BaseNode, false)",
                validateLinks,
                StringComparison.Ordinal);

            Assert.True(createRobot >= 0);
            Assert.True(validateLinks > createRobot);
            Assert.True(saveConfiguration > validateLinks);
        }

        [Fact]
        public void TestUrdfOnlyExportCannotSelectBundleOrIsaacTargets()
        {
            string form = ReadRepositoryFile("SW2URDF", "UI", "AssemblyExportForm.cs");
            int finishExport = form.IndexOf(
                "private void FinishExport(bool exportSTL)",
                StringComparison.Ordinal);
            int captureTargets = form.IndexOf(
                "? CaptureExportTargetOptions()",
                finishExport,
                StringComparison.Ordinal);
            int legacyTargets = form.IndexOf(
                ": ExportTargetOptions.LegacyCompatibilityDefaults();",
                captureTargets,
                StringComparison.Ordinal);
            int validateTargets = form.IndexOf(
                "Exporter.ExportTargets.Validate()",
                legacyTargets,
                StringComparison.Ordinal);

            Assert.True(finishExport >= 0);
            Assert.True(captureTargets > finishExport);
            Assert.True(legacyTargets > captureTargets);
            Assert.True(validateTargets > legacyTargets);
            Assert.Contains(
                "Robot Bundle, profile, and Isaac outputs require a complete mesh export",
                form.Substring(finishExport, validateTargets - finishExport));
        }

        [Fact]
        public void TestPreviewTransitionDoesNotWriteSolidWorksFeaturesWhilePropertyManagerIsOpen()
        {
            string propertyManager = ReadRepositoryFile(
                "SW2URDF", "URDFExport", "ExportPropertyManager.cs");
            int methodStart = propertyManager.IndexOf(
                "private void ExportButtonPress()",
                StringComparison.Ordinal);
            int methodEnd = propertyManager.IndexOf(
                "private void EnableControl",
                methodStart,
                StringComparison.Ordinal);

            Assert.True(methodStart >= 0);
            Assert.True(methodEnd > methodStart);
            string previewTransition = propertyManager.Substring(
                methodStart,
                methodEnd - methodStart);
            Assert.DoesNotContain("SaveConfigTree(", previewTransition);
            Assert.Contains("SaveExportSessionDraft(baseNode)", previewTransition);
            Assert.True(
                previewTransition.IndexOf("SaveExportSessionDraft(baseNode)", StringComparison.Ordinal) <
                previewTransition.IndexOf("PMPage.Close(true)", StringComparison.Ordinal));
        }

        [Fact]
        public void TestPropertyManagerHasAnAddinOwnerUntilAfterClose()
        {
            string addin = ReadRepositoryFile("SW2URDF", "SW", "SwAddin.cs");
            string propertyManager = ReadRepositoryFile(
                "SW2URDF", "URDFExport", "ExportPropertyManager.cs");

            Assert.Contains(
                "private ExportPropertyManager activeAssemblyExportPropertyManager;",
                addin);
            Assert.Contains(
                "propertyManager.Closed += AssemblyExportPropertyManagerClosed;",
                addin);
            Assert.Contains(
                "activeAssemblyExportPropertyManager = propertyManager;",
                addin);
            Assert.Contains("internal event EventHandler Closed;", propertyManager);
            int onClose = propertyManager.IndexOf(
                "void IPropertyManagerPage2Handler9.OnClose(int Reason)",
                StringComparison.Ordinal);
            int onCloseEnd = propertyManager.IndexOf(
                "private LinkNode CaptureCurrentLinkTreeProjection()",
                onClose,
                StringComparison.Ordinal);
            Assert.True(onClose >= 0);
            Assert.True(onCloseEnd > onClose);
            Assert.DoesNotContain(
                "throw new COMException(",
                propertyManager.Substring(onClose, onCloseEnd - onClose));

            int afterClose = propertyManager.IndexOf(
                "void IPropertyManagerPage2Handler9.AfterClose()",
                StringComparison.Ordinal);
            Assert.True(afterClose >= 0);
            Assert.Contains(
                "NotifyClosed();",
                propertyManager.Substring(afterClose));
        }

        [Fact]
        public void TestPropertyManagerDefersPersistenceUntilAfterClose()
        {
            string propertyManager = ReadRepositoryFile(
                "SW2URDF", "URDFExport", "ExportPropertyManager.cs");
            int onClose = propertyManager.IndexOf(
                "void IPropertyManagerPage2Handler9.OnClose(int Reason)",
                StringComparison.Ordinal);
            int onCloseEnd = propertyManager.IndexOf(
                "private LinkNode CaptureCurrentLinkTreeProjection()",
                onClose,
                StringComparison.Ordinal);
            int completeClose = propertyManager.IndexOf(
                "private void CompletePropertyManagerClose()",
                onClose,
                StringComparison.Ordinal);

            Assert.True(onClose >= 0);
            Assert.True(onCloseEnd > onClose);
            Assert.True(completeClose > onClose);
            string onCloseBody = propertyManager.Substring(onClose, onCloseEnd - onClose);
            Assert.DoesNotContain("SaveConfigTree(", onCloseBody);
            Assert.DoesNotContain("SaveExportSessionDraft(", onCloseBody);
            Assert.Contains("SaveConfigTree(", propertyManager.Substring(completeClose));
            Assert.Contains("SaveExportSessionDraft(", propertyManager.Substring(completeClose));
        }

        [Fact]
        public void TestPropertyManagerCapturesRecoveryProjectionBeforeSolidWorksSelection()
        {
            string propertyManager = ReadRepositoryFile(
                "SW2URDF", "URDFExport", "ExportPropertyManager.cs");
            int onClose = propertyManager.IndexOf(
                "void IPropertyManagerPage2Handler9.OnClose(int Reason)",
                StringComparison.Ordinal);
            int onCloseEnd = propertyManager.IndexOf(
                "private LinkNode CaptureCurrentLinkTreeProjection()",
                onClose,
                StringComparison.Ordinal);

            Assert.True(onClose >= 0);
            Assert.True(onCloseEnd > onClose);
            string onCloseBody = propertyManager.Substring(onClose, onCloseEnd - onClose);
            int recoveryProjection = onCloseBody.IndexOf(
                "CaptureCurrentLinkTreeProjection()",
                StringComparison.Ordinal);

            Assert.True(recoveryProjection >= 0);
            Assert.DoesNotContain("SaveActiveNode()", onCloseBody);
            Assert.DoesNotContain("CommitLinkTreeProjection()", onCloseBody);
            Assert.DoesNotContain("PMSelection", onCloseBody);
        }

        [Fact]
        public void TestLinkTreeApplyCreatesRecoveryCheckpoint()
        {
            string extension = ReadRepositoryFile(
                "SW2URDF", "URDFExport", "ExportPropertyManagerExtension.cs");
            int openCanvas = extension.IndexOf(
                "private void OpenLinkTreeCanvas()",
                StringComparison.Ordinal);
            int openCanvasEnd = extension.IndexOf(
                "private void ReplaceLinkTreeRoot",
                openCanvas,
                StringComparison.Ordinal);

            Assert.True(openCanvas >= 0);
            Assert.True(openCanvasEnd > openCanvas);
            string openCanvasBody = extension.Substring(openCanvas, openCanvasEnd - openCanvas);
            int refreshProjection = openCanvasBody.IndexOf(
                "RefreshLinkTreeProjection(selectedNodeId)",
                StringComparison.Ordinal);
            int recoveryCheckpoint = openCanvasBody.IndexOf(
                "SaveExportSessionDraft(linkTreeSession.CreateProjection())",
                StringComparison.Ordinal);

            Assert.True(recoveryCheckpoint >= 0);
            Assert.True(refreshProjection > recoveryCheckpoint);
        }

        [Fact]
        public void TestRecoveryDraftPersistenceDoesNotReadSolidWorksSelectionState()
        {
            string extension = ReadRepositoryFile(
                "SW2URDF", "URDFExport", "ExportPropertyManagerExtension.cs");
            int saveDraft = extension.IndexOf(
                "private void SaveExportSessionDraft(LinkNode root)",
                StringComparison.Ordinal);
            int saveDraftEnd = extension.IndexOf(
                "private void ClearExportSessionDraft()",
                saveDraft,
                StringComparison.Ordinal);

            Assert.True(saveDraft >= 0);
            Assert.True(saveDraftEnd > saveDraft);
            string saveDraftBody = extension.Substring(saveDraft, saveDraftEnd - saveDraft);
            Assert.DoesNotContain("RetrieveSWComponentPIDs", saveDraftBody);
            Assert.DoesNotContain("ActiveSWModel", saveDraftBody);
            Assert.Contains("activeModelPath", saveDraftBody);
            Assert.Contains("exportSessionDraftStore.Save(", saveDraftBody);
        }

        [Fact]
        public void TestDisplayStateCallbacksRejectUnexpectedComObjects()
        {
            string eventHandling = ReadRepositoryFile("SW2URDF", "SW", "EventHandling.cs");

            Assert.DoesNotContain("Component2 component = (Component2)swObject", eventHandling);
            Assert.Contains(
                "CommonSwOperations.TryCastComObject<Component2>",
                eventHandling);
            Assert.Contains("catch (COMException exception)", eventHandling);
        }

        [Fact]
        public void TestExportCoreDoesNotPumpWinFormsEventsDuringRetries()
        {
            string exportHelper = ReadRepositoryFile(
                "SW2URDF", "URDFExport", "ExportHelper.cs");
            string package = ReadRepositoryFile(
                "SW2URDF", "URDFExport", "URDFPackage.cs");

            Assert.DoesNotContain("Application.DoEvents", exportHelper);
            Assert.DoesNotContain("Application.DoEvents", package);
        }

        [Fact]
        public void TestLinkTreeWindowDoesNotForwardReferenceItsOwnResources()
        {
            string xaml = ReadRepositoryFile(
                "SW2URDF", "UI", "LinkTreeCanvas", "LinkTreeCanvasWindow.xaml");
            int resourcesStart = xaml.IndexOf("<Window.Resources>", StringComparison.Ordinal);

            Assert.True(resourcesStart > 0);
            Assert.DoesNotContain("StaticResource", xaml.Substring(0, resourcesStart));
            Assert.Contains("Background=\"#F4F6F8\"", xaml.Substring(0, resourcesStart));
        }

        [Fact]
        public void TestLinkTreeBranchCommandsShareOneCommandGroup()
        {
            string xaml = ReadRepositoryFile(
                "SW2URDF", "UI", "LinkTreeCanvas", "LinkTreeCanvasWindow.xaml");
            int groupStart = xaml.IndexOf(
                "Header=\"分支操作（选中节点及其子节点）\"",
                StringComparison.Ordinal);
            int groupEnd = xaml.IndexOf("</GroupBox>", groupStart, StringComparison.Ordinal);

            Assert.True(groupStart > 0);
            Assert.True(groupEnd > groupStart);
            string commandGroup = xaml.Substring(groupStart, groupEnd - groupStart);
            Assert.Contains("x:Name=\"CopyBranchButton\"", commandGroup);
            Assert.Contains("x:Name=\"PasteBranchButton\"", commandGroup);
            Assert.Contains("x:Name=\"DeleteBranchButton\"", commandGroup);
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
