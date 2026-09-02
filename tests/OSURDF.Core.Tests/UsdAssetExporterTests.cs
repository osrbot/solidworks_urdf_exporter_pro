using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OSURDF.Core.Bundle;
using OSURDF.Core.Export;
using OSURDF.Core.Model;
using OSURDF.Core.Urdf;
using Xunit;

namespace OSURDF.Core.Tests;

public sealed class UsdAssetExporterTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "osurdf-usd-exporter-tests-" + Guid.NewGuid().ToString("N"));

    public UsdAssetExporterTests()
    {
        Directory.CreateDirectory(root);
    }

    [Fact]
    public void ExportRequiresReopenedOpenUsdEvidenceAndReturnsPublishedFiles()
    {
        string bundle = BuildBundle();
        string runtime = Touch("python.exe");
        string adapter = Touch("osurdf_usd_adapter.py");
        string output = Path.Combine(root, "usd");
        FakeRunner runner = new FakeRunner(reopened: true);

        UsdAssetExportResult result = new UsdAssetExporter(runner).Export(
            new UsdAssetExportOptions
            {
                BundleDirectory = bundle,
                OutputDirectory = output,
                PythonExecutable = runtime,
                AdapterScript = adapter,
                Overwrite = true
            });

        Assert.Equal(output, result.OutputDirectory);
        Assert.Equal("0.26.8", result.OpenUsdVersion);
        Assert.True(File.Exists(result.UsdFile));
        Assert.True(File.Exists(result.NameMapFile));
        Assert.True(File.Exists(result.ReportFile));
        Assert.True(runner.Invocation!.Overwrite);
    }

    [Fact]
    public void ExportRejectsAReportThatDidNotReopenTheStage()
    {
        string bundle = BuildBundle();
        FakeRunner runner = new FakeRunner(reopened: false);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new UsdAssetExporter(runner).Export(new UsdAssetExportOptions
            {
                BundleDirectory = bundle,
                OutputDirectory = Path.Combine(root, "usd-invalid"),
                PythonExecutable = Touch("invalid-python.exe"),
                AdapterScript = Touch("invalid-adapter.py"),
                Overwrite = true
            }));

        Assert.Contains("reopened", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportAcceptsRetainedPreviousDirectoryDiagnostic()
    {
        string bundle = BuildBundle();
        string retained = Path.Combine(root, "usd.previous-retained");
        FakeRunner runner = new FakeRunner(reopened: true, retainedPreviousDirectory: retained);

        UsdAssetExportResult result = new UsdAssetExporter(runner).Export(
            new UsdAssetExportOptions
            {
                BundleDirectory = bundle,
                OutputDirectory = Path.Combine(root, "usd-retained"),
                PythonExecutable = Touch("retained-python.exe"),
                AdapterScript = Touch("retained-adapter.py"),
                Overwrite = true
            });

        JObject report = JObject.Parse(File.ReadAllText(result.ReportFile, Encoding.UTF8));
        Assert.Equal(retained, report.Value<string>("retainedPreviousDirectory"));
        Assert.Equal(Path.GetFullPath(retained), result.RetainedPreviousDirectory);
    }

    [Fact]
    public void ExportUsesFixedArtifactsWhenReportedPathsAreEncodingDamaged()
    {
        string bundle = BuildBundle();
        string output = Path.Combine(root, "中文 输出目录", "usd");
        FakeRunner runner = new FakeRunner(
            reopened: true,
            damageReportedPaths: true);

        UsdAssetExportResult result = new UsdAssetExporter(runner).Export(
            new UsdAssetExportOptions
            {
                BundleDirectory = bundle,
                OutputDirectory = output,
                PythonExecutable = Touch("unicode-python.exe"),
                AdapterScript = Touch("unicode-adapter.py"),
                Overwrite = true
            });

        Assert.Equal(Path.Combine(output, "robot.usd"), result.UsdFile);
        Assert.Equal(Path.Combine(output, "name_map.json"), result.NameMapFile);
        Assert.Equal(Path.Combine(output, "export_report.json"), result.ReportFile);
        Assert.All(
            new[] { result.UsdFile, result.NameMapFile, result.ReportFile },
            path => Assert.True(File.Exists(path), path));
    }

    private string BuildBundle()
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "minimal_robot.urdf");
        RobotDocument robot = UrdfCodec.Read(fixture);
        return new RobotBundleBuilder().Build(robot, new BundleBuildOptions
        {
            SourceUrdfPath = fixture,
            OutputDirectory = Path.Combine(root, "bundle.osurdf"),
            Overwrite = true
        }).OutputDirectory;
    }

    private string Touch(string name)
    {
        string path = Path.Combine(root, name);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(false));
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeRunner : IUsdAdapterRunner
    {
        private readonly bool reopened;
        private readonly string? retainedPreviousDirectory;
        private readonly bool damageReportedPaths;

        public FakeRunner(
            bool reopened,
            string? retainedPreviousDirectory = null,
            bool damageReportedPaths = false)
        {
            this.reopened = reopened;
            this.retainedPreviousDirectory = retainedPreviousDirectory;
            this.damageReportedPaths = damageReportedPaths;
        }

        public UsdAdapterInvocation? Invocation { get; private set; }

        public UsdAdapterRunResult Run(UsdAdapterInvocation invocation)
        {
            Invocation = invocation;
            Directory.CreateDirectory(invocation.OutputDirectory);
            string usd = Path.Combine(invocation.OutputDirectory, "robot.usd");
            string nameMap = Path.Combine(invocation.OutputDirectory, "name_map.json");
            string report = Path.Combine(invocation.OutputDirectory, "export_report.json");
            File.WriteAllText(usd, "#usda 1.0", new UTF8Encoding(false));
            File.WriteAllText(nameMap, "{}", new UTF8Encoding(false));
            File.WriteAllText(
                report,
                JsonConvert.SerializeObject(new
                {
                    ok = true,
                    retainedPreviousDirectory,
                    validation = new
                    {
                        ok = true,
                        stageReopened = reopened,
                        openUsdVersion = "0.26.8"
                    }
                }),
                new UTF8Encoding(false));
            string reportedUsd = damageReportedPaths
                ? Path.Combine(invocation.OutputDirectory, "损坏", "robot.usd")
                : usd;
            string reportedNameMap = damageReportedPaths
                ? Path.Combine(invocation.OutputDirectory, "损坏", "name_map.json")
                : nameMap;
            string reportedReport = damageReportedPaths
                ? Path.Combine(invocation.OutputDirectory, "损坏", "export_report.json")
                : report;
            return new UsdAdapterRunResult
            {
                ExitCode = 0,
                StandardOutput = JsonConvert.SerializeObject(new
                {
                    ok = true,
                    outputDirectory = invocation.OutputDirectory,
                    usd = reportedUsd,
                    nameMap = reportedNameMap,
                    report = reportedReport,
                    retainedPreviousDirectory
                }),
                StandardError = string.Empty
            };
        }
    }
}
