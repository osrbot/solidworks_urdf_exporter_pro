using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using OSURDF.Core.Bundle;
using OSURDF.Core.Export;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public sealed class TestIndependentTargetExport : IDisposable
    {
        private static readonly string[] Names = { "ROS 1", "ROS 2", "OpenUSD", "MuJoCo MJCF" };
        private readonly string temporaryDirectory = Path.Combine(
            Path.GetTempPath(), "sw2urdf-independent-" + Guid.NewGuid().ToString("N"));
        private readonly string outputRoot;

        public TestIndependentTargetExport()
        {
            outputRoot = ScopedPath("output");
            Directory.CreateDirectory(outputRoot);
        }

        public static IEnumerable<object[]> FailureCases()
        {
            for (int index = 0; index < Names.Length; index++)
            {
                foreach (string phase in new[] { "build", "validate" })
                {
                    yield return new object[] { index, phase, false };
                    yield return new object[] { index, phase, true };
                }
                // A file occupying the destination is a publication fault, not an old folder.
                yield return new object[] { index, "publication", false };
            }
        }

        [Theory]
        [MemberData(nameof(FailureCases))]
        public void FailureIsIsolatedAndOnlyCommittedSiblingsAreCurrent(
            int failedIndex, string phase, bool hadPrevious)
        {
            int[] builds = new int[Names.Length];
            int[] validations = new int[Names.Length];
            List<ExportTargetJob> jobs = CreateJobs();
            for (int index = 0; index < jobs.Count; index++)
            {
                int current = index;
                ExportTargetJob job = jobs[index];
                if (index != failedIndex || hadPrevious) WriteMarker(job.OutputDirectory, "old");
                if (index == failedIndex && phase == "publication")
                    File.WriteAllText(job.OutputDirectory, "occupied-old-file");
                Func<string> build = job.Build;
                job.Build = () =>
                {
                    builds[current]++;
                    if (current == failedIndex && phase == "build") throw new IOException("injected-build");
                    return build();
                };
                job.Validate = target =>
                {
                    validations[current]++;
                    Assert.Equal(job.OutputDirectory, Directories(target)[current]);
                    Assert.Single(Directories(target).Where(path => path != null));
                    Assert.Equal("new", ReadMarker(job.OutputDirectory));
                    target.Reports.Add("report-" + current);
                    target.Warnings.Add("warning-" + current);
                    if (current == failedIndex && phase == "validate")
                        throw new IOException("injected-validate");
                };
            }

            V2ExportResult result = IndependentTargetExport.Run(outputRoot, jobs);

            Assert.Equal(Names.Length, result.Targets.Count);
            Assert.Equal(3, result.Targets.Count(target => target.Succeeded));
            for (int index = 0; index < jobs.Count; index++)
            {
                bool succeeded = index != failedIndex;
                ExportTargetResult target = result.Targets[index];
                Assert.Equal(Names[index], target.TargetName);
                Assert.Equal(jobs[index].OutputDirectory, target.OutputDirectory);
                Assert.Equal(succeeded, target.Succeeded);
                Assert.Equal(!succeeded && hadPrevious, target.PreviousOutputRetained);
                Assert.Equal(succeeded ? jobs[index].OutputDirectory : null, Directories(result)[index]);
                Assert.Equal(1, builds[index]);
                Assert.Equal(succeeded || phase == "validate" ? 1 : 0, validations[index]);
                Assert.Equal(succeeded, result.Reports.Contains("report-" + index));
                Assert.Equal(succeeded, result.Warnings.Contains("warning-" + index));
                if (succeeded)
                {
                    Assert.Equal("new", ReadMarker(jobs[index].OutputDirectory));
                    Assert.Equal(String.Empty, target.ErrorMessage);
                }
            }
            Assert.NotEmpty(result.Targets[failedIndex].ErrorMessage);
            if (phase != "publication")
                Assert.Contains("injected-" + phase, result.Targets[failedIndex].ErrorMessage);
            AssertFailedDestination(jobs[failedIndex].OutputDirectory, phase, hadPrevious);
            Assert.Empty(AtomicDirectoryPublisher.RecoverInterruptedPublications(outputRoot));
            AssertFailedDestination(jobs[failedIndex].OutputDirectory, phase, hadPrevious);
            foreach (ExportTargetJob job in jobs.Where((job, index) => index != failedIndex))
                Assert.Equal("new", ReadMarker(job.OutputDirectory));
        }

        [Fact]
        public void ValidationRunsAfterSingleTargetBeginWhileRootIsLocked()
        {
            List<ExportTargetJob> jobs = CreateJobs();
            foreach (ExportTargetJob job in jobs) WriteMarker(job.OutputDirectory, "old");
            int validationCount = 0;
            for (int index = 0; index < jobs.Count; index++)
            {
                int current = index;
                jobs[index].Validate = target =>
                {
                    Assert.Single(Directories(target).Where(path => path != null));
                    for (int sibling = 0; sibling < jobs.Count; sibling++)
                        Assert.Equal(sibling <= current ? "new" : "old", ReadMarker(jobs[sibling].OutputDirectory));
                    Assert.Throws<IOException>(() => AtomicDirectoryPublisher.RecoverInterruptedPublications(outputRoot));
                    validationCount++;
                };
            }

            V2ExportResult result = IndependentTargetExport.Run(outputRoot, jobs);

            Assert.Equal(4, validationCount);
            Assert.All(result.Targets, target => Assert.True(target.Succeeded, target.ErrorMessage));
            Assert.Empty(AtomicDirectoryPublisher.RecoverInterruptedPublications(outputRoot));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AllFailuresProduceNoCurrentDirectoriesOrReports(bool hadPrevious)
        {
            List<ExportTargetJob> jobs = CreateJobs();
            foreach (ExportTargetJob job in jobs)
            {
                if (hadPrevious) WriteMarker(job.OutputDirectory, "old");
                job.Validate = target =>
                {
                    target.Reports.Add("must-not-escape");
                    target.Warnings.Add("must-not-escape");
                    throw new IOException("all-fail");
                };
            }

            V2ExportResult result = IndependentTargetExport.Run(outputRoot, jobs);

            Assert.Equal(4, result.Targets.Count);
            Assert.All(result.Targets, target =>
            {
                Assert.False(target.Succeeded);
                Assert.Equal(hadPrevious, target.PreviousOutputRetained);
                Assert.Contains("all-fail", target.ErrorMessage);
                AssertFailedDestination(target.OutputDirectory, "validate", hadPrevious);
            });
            Assert.All(Directories(result), path => Assert.Null(path));
            Assert.Empty(result.Reports);
            Assert.DoesNotContain("must-not-escape", result.Warnings);
            Assert.Empty(AtomicDirectoryPublisher.RecoverInterruptedPublications(outputRoot));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NoSelectedJobsAreRejectedWithoutTouchingOldOutput(bool nullJobs)
        {
            string oldDirectory = ScopedPath("output", "unselected");
            WriteMarker(oldDirectory, "old");

            Assert.Throws<ArgumentException>(() => IndependentTargetExport.Run(outputRoot,
                nullJobs ? null : new List<ExportTargetJob>()));

            Assert.Equal("old", ReadMarker(oldDirectory));
            Assert.Empty(Directory.GetFiles(outputRoot));
        }

        [Theory]
        [InlineData("duplicate")]
        [InlineData("nested")]
        [InlineData("parent")]
        [InlineData("root")]
        [InlineData("escape")]
        [InlineData("prefix-sibling")]
        public void InvalidDestinationsAreRejectedBeforeAnyBuilder(string kind)
        {
            List<ExportTargetJob> jobs = CreateJobs();
            string oldDirectory = jobs[0].OutputDirectory;
            WriteMarker(oldDirectory, "old");
            switch (kind)
            {
                case "duplicate": jobs[1].OutputDirectory = oldDirectory; break;
                case "nested": jobs[1].OutputDirectory = Path.Combine(oldDirectory, "child"); break;
                case "parent":
                    jobs[0].OutputDirectory = Path.Combine(oldDirectory, "child");
                    jobs[1].OutputDirectory = oldDirectory;
                    break;
                case "root": jobs[1].OutputDirectory = outputRoot; break;
                case "escape": jobs[1].OutputDirectory = Path.Combine(outputRoot, "..", "outside"); break;
                case "prefix-sibling": jobs[1].OutputDirectory = ScopedPath("output-sibling", "target"); break;
            }
            int builds = 0;
            foreach (ExportTargetJob job in jobs) job.Build = () => { builds++; return null; };

            Assert.Throws<IOException>(() => IndependentTargetExport.Run(outputRoot, jobs));

            Assert.Equal(0, builds);
            Assert.Equal("old", ReadMarker(oldDirectory));
            Assert.Empty(Directory.GetFiles(outputRoot));
            Assert.False(Directory.Exists(ScopedPath("outside")));
            Assert.False(Directory.Exists(ScopedPath("output-sibling")));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void UnselectedOldTargetsRemainUntouchedAndAreNotCurrent(int selected)
        {
            List<ExportTargetJob> jobs = CreateJobs();
            foreach (ExportTargetJob job in jobs) WriteMarker(job.OutputDirectory, "old");

            V2ExportResult result = IndependentTargetExport.Run(outputRoot, new[] { jobs[selected] });

            Assert.Single(result.Targets);
            Assert.True(result.Targets[0].Succeeded);
            for (int index = 0; index < jobs.Count; index++)
            {
                Assert.Equal(index == selected ? "new" : "old", ReadMarker(jobs[index].OutputDirectory));
                Assert.Equal(index == selected ? jobs[index].OutputDirectory : null, Directories(result)[index]);
            }
        }

        public static IEnumerable<object[]> CancellationCases()
        {
            for (int index = 0; index < Names.Length; index++)
                foreach (bool inValidation in new[] { false, true })
                    foreach (bool hadPrevious in new[] { false, true })
                        yield return new object[] { index, inValidation, hadPrevious };
        }

        [Theory]
        [MemberData(nameof(CancellationCases))]
        public void CancellationRollsBackCurrentStopsLaterAndPreservesEarlierCommits(
            int cancelledIndex, bool inValidation, bool hadPrevious)
        {
            List<ExportTargetJob> jobs = CreateJobs();
            int[] builds = new int[4];
            int[] validations = new int[4];
            OperationCanceledException cancellation = new OperationCanceledException("cancel-current");
            for (int index = 0; index < jobs.Count; index++)
            {
                int current = index;
                ExportTargetJob job = jobs[index];
                if (index != cancelledIndex || hadPrevious) WriteMarker(job.OutputDirectory, "old");
                Func<string> build = job.Build;
                job.Build = () =>
                {
                    builds[current]++;
                    if (current == cancelledIndex && !inValidation) throw cancellation;
                    return build();
                };
                job.Validate = target =>
                {
                    validations[current]++;
                    Assert.Equal("new", ReadMarker(job.OutputDirectory));
                    if (current == cancelledIndex) throw cancellation;
                };
            }

            V2ExportResult interrupted = null;
            Assert.Same(cancellation, Assert.Throws<OperationCanceledException>(
                () => IndependentTargetExport.Run(outputRoot, jobs, partial => interrupted = partial)));

            Assert.NotNull(interrupted);
            Assert.Equal(4, interrupted.Targets.Count);
            Assert.Equal(cancelledIndex, interrupted.Targets.Count(target => target.Succeeded));
            Assert.Empty(AtomicDirectoryPublisher.RecoverInterruptedPublications(outputRoot));
            for (int index = 0; index < jobs.Count; index++)
            {
                ExportTargetResult target = interrupted.Targets[index];
                Assert.Equal(Names[index], target.TargetName);
                Assert.Equal(jobs[index].OutputDirectory, target.OutputDirectory);
                Assert.Equal(index < cancelledIndex, target.Succeeded);
                Assert.Equal(index == cancelledIndex && hadPrevious, target.PreviousOutputRetained);
                Assert.Equal(index < cancelledIndex ? jobs[index].OutputDirectory : null, Directories(interrupted)[index]);
                if (index < cancelledIndex)
                    Assert.Equal(String.Empty, target.ErrorMessage);
                else if (index == cancelledIndex)
                {
                    Assert.Contains("cancel-current", target.ErrorMessage);
                    Assert.DoesNotContain("Not attempted", target.ErrorMessage);
                }
                else
                    Assert.Contains("Not attempted", target.ErrorMessage);
                Assert.Equal(index <= cancelledIndex ? 1 : 0, builds[index]);
                Assert.Equal(index < cancelledIndex || (index == cancelledIndex && inValidation) ? 1 : 0,
                    validations[index]);
                if (index == cancelledIndex)
                    AssertFailedDestination(jobs[index].OutputDirectory, "validate", hadPrevious);
                else
                    Assert.Equal(index < cancelledIndex ? "new" : "old", ReadMarker(jobs[index].OutputDirectory));
            }
        }

        [Fact]
        public void InterruptedPublishedJournalWithMissingPreviousNeverClaimsOldOutputWasRetained()
        {
            List<ExportTargetJob> jobs = CreateJobs();
            string destination = jobs[0].OutputDirectory;
            string transactionId = Guid.NewGuid().ToString("N");
            string previous = destination + ".previous-transaction-" + transactionId + "-00";
            string interruptedStaging = ScopedPath("output", ".sw2urdf-targets-interrupted", "ros1");
            string journalPath = ScopedPath("output", ".sw2urdf-publication-" + transactionId + ".json");
            WriteMarker(destination, "unconfirmed-new-output");
            DirectoryPublicationJournal journal = new DirectoryPublicationJournal
            {
                SchemaVersion = 1,
                TransactionId = transactionId,
                PublicationRoot = outputRoot,
                CreatedUtc = DateTime.UtcNow.ToString("o"),
                Phase = "published",
                Requests = new List<DirectoryPublishRequest>
                {
                    new DirectoryPublishRequest
                    {
                        Label = Names[0],
                        StagingDirectory = interruptedStaging,
                        DestinationDirectory = destination,
                        PreviousDirectory = previous,
                        HadPreviousDirectory = true,
                        Published = true,
                        PublicationState = "published"
                    }
                }
            };
            File.WriteAllText(journalPath, JsonConvert.SerializeObject(journal));
            Assert.False(Directory.Exists(previous));
            Assert.False(Directory.Exists(interruptedStaging));
            string staging = jobs[0].Build();

            InvalidDataException recovery = Assert.Throws<InvalidDataException>(() => AtomicDirectoryPublisher.Begin(
                new[] { new DirectoryPublishRequest
                {
                    Label = Names[0], StagingDirectory = staging, DestinationDirectory = destination
                } }, outputRoot));

            Assert.True(Assert.IsType<bool>(recovery.Data["directoryPublishRecovery"]));
            Assert.Contains("Cannot restore the previous output", recovery.Message);
            int validations = 0;
            jobs[0].Validate = target => validations++;
            V2ExportResult result = IndependentTargetExport.Run(outputRoot, new[] { jobs[0] });

            ExportTargetResult failure = Assert.Single(result.Targets);
            Assert.False(failure.Succeeded);
            Assert.False(failure.PreviousOutputRetained);
            Assert.Contains("Recovery required", failure.ErrorMessage);
            Assert.Contains("Cannot restore the previous output", failure.ErrorMessage);
            Assert.Equal(0, validations);
            Assert.All(Directories(result), path => Assert.Null(path));
            Assert.Empty(result.Reports);
            Assert.Equal("unconfirmed-new-output", ReadMarker(destination));
            Assert.True(File.Exists(journalPath));
            Assert.False(Directory.Exists(previous));
            Assert.False(Directory.Exists(interruptedStaging));
            string report = ExportHelper.BuildIndependentExportReport(
                new URDFPackage("test_robot", outputRoot), result, TimeSpan.Zero);
            Assert.Contains("Status: FAIL", report);
            Assert.Contains("Recovery required", report);
            Assert.DoesNotContain("Previous output retained", report);
            Assert.DoesNotContain("[Output directory]", report);
            bool lockAcquired = false;
            AtomicDirectoryPublisher.WithOutputRootLock(outputRoot, () => lockAcquired = true);
            Assert.True(lockAcquired);
        }

        [Theory]
        [InlineData(false, "PARTIAL", 3)]
        [InlineData(true, "FAIL", 0)]
        public void RootReportCountsOnlyCurrentSuccessAndExplicitlyLabelsRetainedOldOutput(
            bool allFail, string status, int successCount)
        {
            List<ExportTargetJob> jobs = CreateJobs();
            for (int index = 0; index < jobs.Count; index++)
            {
                WriteMarker(jobs[index].OutputDirectory, "old");
                if (allFail || index == 1)
                    jobs[index].Validate = target => { throw new IOException("report-test-failure"); };
            }
            V2ExportResult result = IndependentTargetExport.Run(outputRoot, jobs);
            URDFPackage package = new URDFPackage("test_robot", outputRoot);

            string report = ExportHelper.BuildIndependentExportReport(package, result, TimeSpan.FromSeconds(7));

            Assert.Contains("Status: " + status, report);
            Assert.DoesNotContain("Status: PASS", report);
            Assert.Contains("Succeeded: " + successCount + "; failed: " + (4 - successCount), report);
            foreach (ExportTargetResult target in result.Targets)
            {
                string section = report.Split(new[] { "## " + target.TargetName + Environment.NewLine },
                    StringSplitOptions.None)[1].Split(new[] { "\n## " }, StringSplitOptions.None)[0];
                if (target.Succeeded)
                {
                    Assert.Contains("Result: SUCCESS", section);
                    Assert.Contains("[Output directory]", section);
                    Assert.DoesNotContain("Previous output retained", section);
                }
                else
                {
                    Assert.Contains("Result: FAILED", section);
                    Assert.Contains("Previous output retained; NOT updated by this export.", section);
                    Assert.Contains("report-test-failure", section);
                    Assert.DoesNotContain("[Output directory]", section);
                    Assert.DoesNotContain("[Validation report]", section);
                    Assert.Equal("old", ReadMarker(target.OutputDirectory));
                }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AuxiliaryReportWriteFailureCannotDiscardCommittedOutputs(bool lockedOldReport)
        {
            List<ExportTargetJob> jobs = CreateJobs();
            foreach (ExportTargetJob job in jobs) WriteMarker(job.OutputDirectory, "old");
            V2ExportResult result = IndependentTargetExport.Run(outputRoot, jobs);
            URDFPackage package = new URDFPackage("test_robot", outputRoot);
            string[] beforeDirectories = Directories(result);
            ExportTargetResult[] beforeTargets = result.Targets.ToArray();
            if (lockedOldReport)
            {
                File.WriteAllText(package.WindowsExportReportFile, "old-report");
                using (FileStream occupied = new FileStream(package.WindowsExportReportFile,
                    FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    ExportHelper.TryWriteIndependentExportReport(package, result, TimeSpan.Zero);
                Assert.Equal("old-report", File.ReadAllText(package.WindowsExportReportFile));
            }
            else
            {
                Directory.CreateDirectory(package.WindowsExportReportFile);
                WriteMarker(package.WindowsExportReportFile, "report-path-occupied");
                ExportHelper.TryWriteIndependentExportReport(package, result, TimeSpan.Zero);
                Assert.Equal("report-path-occupied", ReadMarker(package.WindowsExportReportFile));
            }

            Assert.Equal(beforeDirectories, Directories(result));
            Assert.Equal(beforeTargets, result.Targets.ToArray());
            Assert.Equal(4, result.Targets.Count(target => target.Succeeded));
            Assert.Contains(result.Warnings, warning => warning.Contains("summary report could not be updated"));
            Assert.Empty(AtomicDirectoryPublisher.RecoverInterruptedPublications(outputRoot));
            foreach (ExportTargetJob job in jobs) Assert.Equal("new", ReadMarker(job.OutputDirectory));
        }

        [Fact]
        public void RealBridgeRos2ValidationFailureRestoresOldRos2AndPreservesRos1Checksums()
        {
            URDFPackage source = CreateSourcePackage(false);
            URDFPackage output = new URDFPackage("test_robot", outputRoot);
            WriteMarker(output.WindowsPackageDirectory, "old-ros1");
            WriteMarker(output.WindowsRos2PackageDirectory, "old-ros2");
            List<string> callbacks = new List<string>();
            Dictionary<string, string> committedRos1 = null;

            V2ExportResult result = V2ExportBridge.Export(source, output, SourceUrdf(source),
                CreateLegacyRobot(), new ExportHelper.MeshExportRecord[0], RosOptions(), (target, selected) =>
                {
                    if (selected.ExportRos1Legacy)
                    {
                        callbacks.Add("ROS 1");
                        File.WriteAllText(Path.Combine(target.Ros1Directory, "config", "callback-report.txt"),
                            "validated-ros1");
                    }
                    else
                    {
                        callbacks.Add("ROS 2");
                        Assert.True(selected.ExportRos2);
                        Assert.True(File.Exists(Path.Combine(target.Ros2Directory, "package.xml")));
                        Assert.False(File.Exists(Path.Combine(target.Ros2Directory, "marker.txt")));
                        AssertRos1FilesAndChecksums(output.WindowsPackageDirectory);
                        committedRos1 = Snapshot(output.WindowsPackageDirectory);
                        throw new IOException("real-ros2-validation-failure");
                    }
                });

            Assert.Equal(new[] { "ROS 1", "ROS 2" }, callbacks.ToArray());
            AssertRos1OnlySucceeded(result, output);
            Assert.Contains("real-ros2-validation-failure", result.Targets[1].ErrorMessage);
            Assert.True(result.Targets[1].PreviousOutputRetained);
            Assert.Equal("old-ros2", ReadMarker(output.WindowsRos2PackageDirectory));
            Assert.Single(Directory.GetFiles(output.WindowsRos2PackageDirectory, "*", SearchOption.AllDirectories));
            Assert.NotNull(committedRos1);
            Assert.Equal(committedRos1.OrderBy(pair => pair.Key).ToArray(),
                Snapshot(output.WindowsPackageDirectory).OrderBy(pair => pair.Key).ToArray());
            Assert.Equal("validated-ros1", File.ReadAllText(
                Path.Combine(output.WindowsPackageDirectory, "config", "callback-report.txt")));
            AssertRos1FilesAndChecksums(output.WindowsPackageDirectory);
            Assert.Empty(AtomicDirectoryPublisher.RecoverInterruptedPublications(outputRoot));
        }

        [Fact]
        public void RealBridgeMalformedRos2ProfileFailsOnlyRos2BeforeItsValidation()
        {
            URDFPackage source = CreateSourcePackage(false);
            URDFPackage output = new URDFPackage("test_robot", outputRoot);
            WriteMarker(output.WindowsRos2PackageDirectory, "old-ros2");
            ExportTargetOptions options = RosOptions();
            options.Ros2ControlProfileFile = ScopedPath("malformed-profile.json");
            File.WriteAllText(options.Ros2ControlProfileFile, "{ definitely not JSON");
            int ros1Callbacks = 0;
            int ros2Callbacks = 0;

            V2ExportResult result = V2ExportBridge.Export(source, output, SourceUrdf(source),
                CreateLegacyRobot(), new ExportHelper.MeshExportRecord[0], options, (target, selected) =>
                {
                    if (selected.ExportRos1Legacy) ros1Callbacks++;
                    if (selected.ExportRos2) ros2Callbacks++;
                });

            Assert.Equal(1, ros1Callbacks);
            Assert.Equal(0, ros2Callbacks);
            AssertRos1OnlySucceeded(result, output);
            Assert.NotEmpty(result.Targets[1].ErrorMessage);
            Assert.True(result.Targets[1].PreviousOutputRetained);
            Assert.Equal("old-ros2", ReadMarker(output.WindowsRos2PackageDirectory));
            Assert.Single(Directory.GetFiles(output.WindowsRos2PackageDirectory, "*", SearchOption.AllDirectories));
            AssertRos1FilesAndChecksums(output.WindowsPackageDirectory);
            Assert.Empty(AtomicDirectoryPublisher.RecoverInterruptedPublications(outputRoot));
        }

        [Fact]
        public void RealBridgeInvalidCommonUrdfTreeThrowsBeforeCallbacksOrNewOutputs()
        {
            URDFPackage source = CreateSourcePackage(true);
            URDFPackage output = new URDFPackage("test_robot", ScopedPath("invalid-delivery"));
            int callbacks = 0;

            Assert.Throws<InvalidDataException>(() => V2ExportBridge.Export(source, output, SourceUrdf(source),
                CreateLegacyRobot(), new ExportHelper.MeshExportRecord[0], RosOptions(),
                (target, selected) => callbacks++));

            Assert.Equal(0, callbacks);
            Assert.False(Directory.Exists(output.WindowsExportRootDirectory));
            Assert.False(Directory.Exists(output.WindowsPackageDirectory));
            Assert.False(Directory.Exists(output.WindowsRos2PackageDirectory));
        }

        [Theory]
        [InlineData("robot_", "robot.urdf", "robot")]
        [InlineData("con", "con.urdf", "con_item")]
        public void CoreTargetNamesNormalizeTrailingSeparatorsAndReservedMjcfNames(
            string name, string urdfFileName, string mjcfDirectory)
        {
            Assert.Equal(urdfFileName, RosPackageExporter.GetRobotUrdfFileName(name));
            Assert.Equal(mjcfDirectory, MjcfAssetExporter.GetRobotDirectoryName(name));
        }

        [Fact]
        public void RealBridgeNormalizedRobotFileNamesPassBothTargetReportValidators()
        {
            // A punctuation-only source name becomes robot_, whose emitted filename is robot.urdf.
            URDFPackage source = CreateSourcePackage(false, "_");
            URDFPackage output = new URDFPackage("_", outputRoot);
            Assert.Equal("robot_", source.RobotName);
            Assert.Equal("robot_", output.RobotName);
            File.WriteAllText(Path.Combine(source.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\nbase_link,PASS\n");
            MethodInfo validate = typeof(ExportHelper).GetMethod("ValidateAndWriteTargetReport",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(validate);
            List<string> validated = new List<string>();

            V2ExportResult result = V2ExportBridge.Export(source, output, SourceUrdf(source),
                CreateLegacyRobot(), new ExportHelper.MeshExportRecord[0], RosOptions(), (target, selected) =>
                {
                    string directory = selected.ExportRos1Legacy ? target.Ros1Directory : target.Ros2Directory;
                    Assert.True(File.Exists(Path.Combine(directory, "urdf", "robot.urdf")));
                    Assert.False(File.Exists(Path.Combine(directory, "urdf", "robot_.urdf")));
                    validate.Invoke(null, new object[]
                    {
                        output, target, selected, null, false, MeshExportFormat.STL, TimeSpan.Zero
                    });
                    validated.Add(selected.ExportRos1Legacy ? "ROS 1" : "ROS 2");
                });

            Assert.Equal(new[] { "ROS 1", "ROS 2" }, validated.ToArray());
            Assert.Equal(2, result.Targets.Count);
            Assert.All(result.Targets, target => Assert.True(target.Succeeded, target.ErrorMessage));
            Assert.Equal(2, result.Reports.Count);
            foreach (ExportTargetResult target in result.Targets)
            {
                string report = File.ReadAllText(Path.Combine(target.OutputDirectory, "config", "export_report.md"));
                Assert.Contains(report, result.Reports);
                Assert.Contains("Selected targets: " + target.TargetName, report);
                Assert.Contains("robot.urdf", report);
                Assert.DoesNotContain("Status: FAIL", report);
                Assert.DoesNotContain("FAIL:", report);
                AssertPackageChecksums(target.OutputDirectory);
            }
            Assert.Empty(AtomicDirectoryPublisher.RecoverInterruptedPublications(outputRoot));
        }

        private URDFPackage CreateSourcePackage(bool invalidTree, string name = "test_robot")
        {
            URDFPackage source = new URDFPackage(name, ScopedPath("source"));
            source.CreateDirectories();
            string link = "<link name='base_link'><inertial><origin xyz='0 0 0' rpy='0 0 0'/>" +
                "<mass value='1'/><inertia ixx='1' ixy='0' ixz='0' iyy='1' iyz='0' izz='1'/>" +
                "</inertial><visual><geometry><box size='1 1 1'/></geometry></visual>" +
                "<collision><geometry><box size='1 1 1'/></geometry></collision></link>";
            File.WriteAllText(SourceUrdf(source), "<robot name='" + source.RobotName + "'>" + link +
                (invalidTree ? link.Replace("base_link", "disconnected_link") : String.Empty) + "</robot>");
            return source;
        }

        private static string SourceUrdf(URDFPackage source)
        {
            return Path.Combine(source.WindowsRobotsDirectory, "test_robot.urdf");
        }

        private static SW2URDF.URDF.Robot CreateLegacyRobot()
        {
            SW2URDF.URDF.Robot robot = new SW2URDF.URDF.Robot();
            robot.SetBaseLink(new SW2URDF.URDF.Link { Name = "base_link" });
            return robot;
        }

        private static ExportTargetOptions RosOptions()
        {
            ExportTargetOptions options = ExportTargetOptions.RecommendedDefaults("test_robot");
            options.ExportRos1Legacy = true;
            options.ExportRos2 = true;
            options.ExportUsdAsset = false;
            options.ExportMjcfAsset = false;
            return options;
        }

        private static void AssertRos1OnlySucceeded(V2ExportResult result, URDFPackage output)
        {
            Assert.Equal(2, result.Targets.Count);
            Assert.Equal("ROS 1", result.Targets[0].TargetName);
            Assert.True(result.Targets[0].Succeeded, result.Targets[0].ErrorMessage);
            Assert.Equal("ROS 2", result.Targets[1].TargetName);
            Assert.False(result.Targets[1].Succeeded);
            Assert.Equal(Path.GetFullPath(output.WindowsPackageDirectory).TrimEnd('\\', '/'), result.Ros1Directory);
            Assert.Null(result.Ros2Directory);
            Assert.Null(result.UsdDirectory);
            Assert.Null(result.MjcfDirectory);
        }

        private void AssertRos1FilesAndChecksums(string directory)
        {
            AssertScoped(directory);
            Assert.True(File.Exists(Path.Combine(directory, "package.xml")));
            Assert.True(File.Exists(Path.Combine(directory, "CMakeLists.txt")));
            Assert.True(File.Exists(Path.Combine(directory, "launch", "display.launch")));
            string urdf = File.ReadAllText(Path.Combine(directory, "urdf", "test_robot.urdf"));
            Assert.Contains("base_link", urdf);
            Assert.Contains("<box", urdf);
            AssertPackageChecksums(directory);
        }

        private void AssertPackageChecksums(string directory)
        {
            AssertScoped(directory);
            string[] checksums = File.ReadAllLines(Path.Combine(directory, RobotBundleLayout.ChecksumsFile));
            Assert.NotEmpty(checksums);
            HashSet<string> checkedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in checksums)
            {
                Assert.True(line.Length > 66, line);
                Assert.Equal("  ", line.Substring(64, 2));
                string path = Path.GetFullPath(Path.Combine(directory, line.Substring(66).Replace('/', Path.DirectorySeparatorChar)));
                AssertScoped(path);
                Assert.True(path.StartsWith(Path.GetFullPath(directory).TrimEnd('\\', '/') +
                    Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                Assert.True(checkedFiles.Add(path), path);
                Assert.Equal(line.Substring(0, 64), HashFile(path));
            }
            string checksumPath = Path.GetFullPath(Path.Combine(directory, RobotBundleLayout.ChecksumsFile));
            string[] payload = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath).Where(path => !String.Equals(path, checksumPath, StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.Equal(payload.Length, checkedFiles.Count);
            Assert.All(payload, path => Assert.Contains(path, checkedFiles));
        }

        private static Dictionary<string, string> Snapshot(string directory)
        {
            return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, HashFile, StringComparer.OrdinalIgnoreCase);
        }

        private static string HashFile(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", String.Empty).ToLowerInvariant();
        }

        private List<ExportTargetJob> CreateJobs()
        {
            return Names.Select((name, index) => new ExportTargetJob
            {
                Name = name,
                OutputDirectory = ScopedPath("output", "target-" + index),
                Build = () =>
                {
                    string staging = ScopedPath("output", "staging-" + index);
                    WriteMarker(staging, "new");
                    return staging;
                }
            }).ToList();
        }

        private static string[] Directories(V2ExportResult result)
        {
            return new[] { result.Ros1Directory, result.Ros2Directory, result.UsdDirectory, result.MjcfDirectory };
        }

        private static void AssertFailedDestination(string directory, string phase, bool hadPrevious)
        {
            if (phase == "publication")
            {
                Assert.False(Directory.Exists(directory));
                Assert.Equal("occupied-old-file", File.ReadAllText(directory));
            }
            else if (hadPrevious)
                Assert.Equal("old", ReadMarker(directory));
            else
            {
                Assert.False(Directory.Exists(directory));
                Assert.False(File.Exists(directory));
            }
        }

        private void WriteMarker(string directory, string content)
        {
            AssertScoped(directory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "marker.txt"), content);
        }

        private static string ReadMarker(string directory)
        {
            return File.ReadAllText(Path.Combine(directory, "marker.txt"));
        }

        private string ScopedPath(params string[] parts)
        {
            string path = Path.GetFullPath(Path.Combine(new[] { temporaryDirectory }.Concat(parts).ToArray()));
            AssertScoped(path);
            return path;
        }

        private void AssertScoped(string path)
        {
            string root = Path.GetFullPath(temporaryDirectory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            Assert.True(Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase), path);
        }

        public void Dispose()
        {
            string root = Path.GetFullPath(temporaryDirectory);
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            Assert.True(root.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase), root);
            string name = Path.GetFileName(root);
            const string prefix = "sw2urdf-independent-";
            Assert.True(name.StartsWith(prefix, StringComparison.Ordinal));
            Guid identifier;
            Assert.True(Guid.TryParseExact(name.Substring(prefix.Length), "N", out identifier));
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
