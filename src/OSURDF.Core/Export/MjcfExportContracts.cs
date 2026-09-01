using System;
using System.Collections.Generic;

namespace OSURDF.Core.Export
{
    public sealed class MjcfExportOptions
    {
        public string BundleDirectory { get; set; }

        public string OutputDirectory { get; set; }

        public bool Overwrite { get; set; }

        public IMjcfCompilerValidator CompilerValidator { get; set; }
    }

    public sealed class MjcfExportResult
    {
        public string OutputDirectory { get; set; }

        public string RobotXmlPath { get; set; }

        public string SceneXmlPath { get; set; }

        public string NameMapPath { get; set; }

        public string ExportReportPath { get; set; }

        public string StructuralGenerationStatus { get; set; }

        public string OfficialCompilationStatus { get; set; }

        public string RetainedPreviousDirectory { get; set; }
    }

    public interface IMjcfCompilerValidator
    {
        MjcfCompilerValidationResult Validate(MjcfCompilerValidationRequest request);
    }

    public sealed class MjcfCompilerValidationRequest
    {
        public string WorkingDirectory { get; set; }

        public IReadOnlyList<string> ModelPaths { get; set; } = Array.Empty<string>();
    }

    public sealed class MjcfCompilerValidationResult
    {
        public bool Succeeded { get; set; }

        public string Validator { get; set; }

        public string MuJoCoVersion { get; set; }

        public string Message { get; set; }
    }
}
