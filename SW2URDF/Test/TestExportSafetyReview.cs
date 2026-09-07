using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Moq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public class TestExportSafetyReview
    {
        [Fact]
        public void FailedDiagnosticsSurviveStagingCleanupWithoutCopyingMeshes()
        {
            string root = Path.Combine(Path.GetTempPath(), "sw2urdf-diagnostics-" + Guid.NewGuid().ToString("N"));
            string staging = Path.Combine(root, "staging");
            string config = Path.Combine(staging, "ROS1", "test", "config");
            Directory.CreateDirectory(config);
            try
            {
                File.WriteAllText(Path.Combine(config, "inertial_validation.csv"), "link,status\nbase,FAIL");
                File.WriteAllText(Path.Combine(config, "mesh_manifest.csv"), "mesh,status\nbase,FAIL");
                File.WriteAllText(Path.Combine(staging, "large.STL"), "not a diagnostic");
                string retained = ExportHelper.PreserveFailedExportDiagnostics(staging, Path.Combine(root, "logs"));
                Directory.Delete(staging, true);
                Assert.Equal(2, Directory.GetFiles(retained, "*", SearchOption.AllDirectories).Length);
                Assert.Contains("base,FAIL", File.ReadAllText(Path.Combine(retained, "ROS1", "test", "config", "inertial_validation.csv")));
                Assert.Empty(Directory.GetFiles(retained, "*.STL", SearchOption.AllDirectories));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void NoDiagnosticFilesDoesNotCreateAnEmptyArchive()
        {
            string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Assert.Null(ExportHelper.PreserveFailedExportDiagnostics(missing, missing + "-logs"));
            Assert.False(Directory.Exists(missing + "-logs"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DocumentCoordinatesRestoreIndependentlyToTheCapturedDocument(bool failModern)
        {
            var app = new Mock<ISldWorks>();
            var model = new Mock<ModelDoc2>();
            var extension = new Mock<ModelDocExtension>();
            int modern = (int)swUserPreferenceStringValue_e.swExportOutputCoordinateSystem;
            int legacy = (int)swUserPreferenceStringValue_e.swFileSaveAsCoordinateSystem;
            var values = new Dictionary<int, string> { { modern, "chosen_frame" }, { legacy, "legacy_frame" } };
            app.Setup(x => x.GetUserPreferenceStringValue(modern)).Returns(() => values[modern]);
            app.Setup(x => x.SetUserPreferenceStringValue(modern, It.IsAny<string>()))
                .Returns<int, string>((id, value) =>
                {
                    if (failModern) throw new InvalidOperationException("restore rejected");
                    values[id] = value;
                    return true;
                });
            model.SetupGet(x => x.Extension).Returns(extension.Object);
            model.Setup(x => x.GetCurrentCoordinateSystemName()).Returns(() => values[legacy]);
            extension.Setup(x => x.GetUserPreferenceString(legacy, It.IsAny<int>()))
                .Returns<int, int>((id, option) => values[id]);
            extension.Setup(x => x.SetUserPreferenceString(legacy, It.IsAny<int>(), It.IsAny<string>()))
                .Returns<int, int, string>((id, option, value) =>
                {
                    values[id] = value;
                    return true;
                });
            var helper = (ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));
            helper.iSwApp = app.Object;
            helper.ActiveSWModel = model.Object;
            helper.SaveUserPreferences();
            values[modern] = values[legacy] = String.Empty;
            helper.ActiveSWModel = new Mock<ModelDoc2>(MockBehavior.Strict).Object;
            if (failModern)
                Assert.Throws<AggregateException>(() => helper.ResetUserPreferences());
            else
            {
                helper.ResetUserPreferences();
                Assert.Equal("chosen_frame", values[modern]);
            }
            Assert.Equal("legacy_frame", values[legacy]);
            extension.Verify(x => x.SetUserPreferenceString(modern, It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void MeshCoordinatePreparationDoesNotReadOrApplyStlTolerances()
        {
            var app = new Mock<ISldWorks>(MockBehavior.Strict);
            var model = new Mock<ModelDoc2>(MockBehavior.Strict);
            var extension = new Mock<ModelDocExtension>(MockBehavior.Strict);
            int modern = (int)swUserPreferenceStringValue_e.swExportOutputCoordinateSystem;
            int legacy = (int)swUserPreferenceStringValue_e.swFileSaveAsCoordinateSystem;
            app.Setup(x => x.SetUserPreferenceStringValue(modern, String.Empty)).Returns(true);
            app.Setup(x => x.GetUserPreferenceStringValue(modern)).Returns(String.Empty);
            model.SetupGet(x => x.Extension).Returns(extension.Object);
            model.Setup(x => x.GetCurrentCoordinateSystemName()).Returns(String.Empty);
            extension.Setup(x => x.SetUserPreferenceString(legacy, It.IsAny<int>(), String.Empty)).Returns(true);
            extension.Setup(x => x.GetUserPreferenceString(legacy, It.IsAny<int>())).Returns(String.Empty);
            var helper = (ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));
            helper.iSwApp = app.Object;
            helper.ResetMeshExportCoordinateSystem(model.Object);
            app.VerifyAll();
            model.VerifyAll();
            extension.VerifyAll();
        }

        [Theory]
        [InlineData(false, true, "base_link", "", "", true)]
        [InlineData(false, false, "base_link", "", "", true)]
        [InlineData(true, true, "", "", "", true)]
        [InlineData(false, false, "base_link", "base_link", "base_link", false)]
        [InlineData(true, true, "", "base_link", "base_link", false)]
        [InlineData(false, true, "base_link", "", "base_link", false)]
        [InlineData(false, true, "base_link", "base_link", "", false)]
        [InlineData(false, true, "base_link", "", null, false)]
        [InlineData(false, true, "base_link", null, "", true)]
        [InlineData(false, false, "base_link", null, "", false)]
        public void MeshCoordinatesRequireTheEffectiveDocumentFrame(
            bool accepted, bool documentAccepted, string global, string document, string current, bool valid)
        {
            var app = new Mock<ISldWorks>(MockBehavior.Strict);
            var model = new Mock<ModelDoc2>(MockBehavior.Strict);
            var extension = new Mock<ModelDocExtension>(MockBehavior.Strict);
            int modern = (int)swUserPreferenceStringValue_e.swExportOutputCoordinateSystem;
            int legacy = (int)swUserPreferenceStringValue_e.swFileSaveAsCoordinateSystem;
            app.Setup(x => x.SetUserPreferenceStringValue(modern, String.Empty)).Returns(accepted);
            app.Setup(x => x.GetUserPreferenceStringValue(modern)).Returns(global);
            model.SetupGet(x => x.Extension).Returns(extension.Object);
            model.Setup(x => x.GetCurrentCoordinateSystemName()).Returns(current);
            extension.Setup(x => x.SetUserPreferenceString(legacy, 0, String.Empty)).Returns(documentAccepted);
            extension.Setup(x => x.GetUserPreferenceString(legacy, 0)).Returns(document);
            var helper = (ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));
            helper.iSwApp = app.Object;
            if (valid)
                helper.ResetMeshExportCoordinateSystem(model.Object);
            else
            {
                var error = Assert.Throws<InvalidOperationException>(() => helper.ResetMeshExportCoordinateSystem(model.Object));
                Assert.Contains("prepare", error.Message);
                Assert.Contains("global=", error.Message);
                Assert.Contains("document=", error.Message);
                Assert.Contains("current=", error.Message);
            }
            app.VerifyAll();
            model.VerifyAll();
            extension.VerifyAll();
        }

        [Fact]
        public void UnchangedGlobalCoordinateRestoresDespiteFalseSetterResult()
        {
            var app = new Mock<ISldWorks>();
            var model = new Mock<ModelDoc2>();
            var extension = new Mock<ModelDocExtension>();
            int modern = (int)swUserPreferenceStringValue_e.swExportOutputCoordinateSystem;
            int legacy = (int)swUserPreferenceStringValue_e.swFileSaveAsCoordinateSystem;
            string document = "base_link";
            app.Setup(x => x.GetUserPreferenceStringValue(modern)).Returns("base_link");
            app.Setup(x => x.SetUserPreferenceStringValue(modern, It.IsAny<string>())).Returns(false);
            model.SetupGet(x => x.Extension).Returns(extension.Object);
            model.Setup(x => x.GetCurrentCoordinateSystemName()).Returns(() => document);
            extension.Setup(x => x.GetUserPreferenceString(legacy, 0)).Returns(() => document);
            extension.Setup(x => x.SetUserPreferenceString(legacy, 0, It.IsAny<string>()))
                .Returns<int, int, string>((id, option, value) => { document = value; return true; });
            var helper = (ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));
            helper.iSwApp = app.Object;
            helper.ActiveSWModel = model.Object;
            helper.SaveUserPreferences();
            document = String.Empty;
            helper.ResetUserPreferences();
            Assert.Equal("base_link", document);
            app.Verify(x => x.SetUserPreferenceStringValue(modern, It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedCoordinateReadbackStillRestoresTheCapturedDocument(bool failCurrent)
        {
            var app = new Mock<ISldWorks>();
            var model = new Mock<ModelDoc2>();
            var extension = new Mock<ModelDocExtension>();
            int modern = (int)swUserPreferenceStringValue_e.swExportOutputCoordinateSystem;
            int legacy = (int)swUserPreferenceStringValue_e.swFileSaveAsCoordinateSystem;
            string document = "base_link";
            bool fail = false;
            var error = new System.Runtime.InteropServices.COMException("readback failed");
            app.Setup(x => x.GetUserPreferenceStringValue(modern)).Returns("base_link");
            app.Setup(x => x.SetUserPreferenceStringValue(modern, It.IsAny<string>())).Returns(false);
            model.SetupGet(x => x.Extension).Returns(extension.Object);
            model.Setup(x => x.GetCurrentCoordinateSystemName()).Returns(() =>
            {
                if (fail && failCurrent) throw error;
                return document;
            });
            extension.Setup(x => x.GetUserPreferenceString(legacy, 0)).Returns(() =>
            {
                if (fail && !failCurrent) throw error;
                return document;
            });
            extension.Setup(x => x.SetUserPreferenceString(legacy, 0, It.IsAny<string>()))
                .Returns<int, int, string>((id, option, value) => { document = value; return true; });
            var helper = (ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));
            helper.iSwApp = app.Object;
            helper.ActiveSWModel = model.Object;
            helper.SaveUserPreferences();
            fail = true;
            Assert.Same(error, Assert.Throws<System.Runtime.InteropServices.COMException>(() =>
                helper.ResetMeshExportCoordinateSystem(model.Object)));
            Assert.Equal(String.Empty, document);
            fail = false;
            helper.ActiveSWModel = new Mock<ModelDoc2>(MockBehavior.Strict).Object;
            helper.ResetUserPreferences();
            Assert.Equal("base_link", document);
        }

        [Fact]
        public void RestoringPreferenceTextAloneDoesNotProveTheEffectiveFrameWasRestored()
        {
            var app = new Mock<ISldWorks>();
            var model = new Mock<ModelDoc2>();
            var extension = new Mock<ModelDocExtension>();
            int modern = (int)swUserPreferenceStringValue_e.swExportOutputCoordinateSystem;
            int legacy = (int)swUserPreferenceStringValue_e.swFileSaveAsCoordinateSystem;
            string current = "base_link";
            app.Setup(x => x.GetUserPreferenceStringValue(modern)).Returns("base_link");
            model.SetupGet(x => x.Extension).Returns(extension.Object);
            model.Setup(x => x.GetCurrentCoordinateSystemName()).Returns(() => current);
            extension.Setup(x => x.GetUserPreferenceString(legacy, 0)).Returns("base_link");
            var helper = (ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));
            helper.iSwApp = app.Object;
            helper.ActiveSWModel = model.Object;
            helper.SaveUserPreferences();
            current = String.Empty;
            var error = Assert.Throws<AggregateException>(() => helper.ResetUserPreferences());
            Assert.Contains(error.InnerExceptions, e => e.Message.Contains("effective document") && e.Message.Contains("base_link"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AdjustedStlSettingsAreReportedAndUsedEvenWhenSetterClaimsSuccess(bool accepted)
        {
            var app = SettingsApp((int)swSTLQuality_e.swSTLQuality_Custom, 0.027, 0.52, accepted);
            var requested = ExportHelper.CreateStlMeshSettings(false, 1.0);
            string warning;
            var actual = ExportHelper.ApplyStlMeshSettings(app.Object, requested, out warning);
            Assert.NotNull(warning);
            Assert.Equal(0.027, actual.Deviation);
            Assert.Equal(0.52, actual.AngleTolerance);
            var stats = ExportHelper.CreateStlExportStats(new Link(), actual, settings => 20);
            Assert.Equal(actual.Deviation, stats.Deviation.Value);
            Assert.Equal(actual.AngleTolerance, stats.AngleTolerance.Value);
        }

        [Fact]
        public void AcceptedMatchingSettingsDoNotWarn()
        {
            var requested = ExportHelper.CreateStlMeshSettings(false, 0.5);
            var app = SettingsApp((int)swSTLQuality_e.swSTLQuality_Custom, requested.Deviation, requested.AngleTolerance, true);
            string warning;
            ExportHelper.ApplyStlMeshSettings(app.Object, requested, out warning);
            Assert.Null(warning);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(Double.NaN)]
        [InlineData(Double.PositiveInfinity)]
        public void InvalidReadbackStopsBeforeMeshExport(double deviation)
        {
            var app = SettingsApp((int)swSTLQuality_e.swSTLQuality_Custom, deviation, 0.5, true);
            Assert.Throws<InvalidOperationException>(() =>
                ExportHelper.ApplyStlMeshSettings(app.Object, ExportHelper.CreateStlMeshSettings(false, 0.5), out _));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(42)]
        public void UnreducedMeshEstimatesOnceUsingEffectiveSettings(int triangles)
        {
            var settings = ExportHelper.CreateStlMeshSettings(false, 0);
            settings.Deviation = 0.0006;
            int calls = 0;
            var stats = ExportHelper.CreateStlExportStats(new Link(), settings, actual =>
            {
                Assert.Same(settings, actual);
                calls++;
                return triangles;
            });
            Assert.Equal(1, calls);
            Assert.Equal(triangles > 0 ? (int?)triangles : null, stats.EstimatedTriangles);
        }

        [Fact]
        public void ReducedMeshKeepsDistinctBaselineEstimate()
        {
            var calls = new List<double>();
            var stats = ExportHelper.CreateStlExportStats(new Link(), ExportHelper.CreateStlMeshSettings(false, 0.5), settings =>
            {
                calls.Add(settings.ReductionRatio);
                return settings.ReductionRatio == 0 ? 100 : 50;
            });
            Assert.Equal(new[] { 0.0, 0.5 }, calls);
            Assert.Equal(100, stats.BaselineEstimatedTriangles);
            Assert.Equal(50, stats.EstimatedTriangles);
        }

        private static Mock<ISldWorks> SettingsApp(int quality, double deviation, double angle, bool accepted)
        {
            var app = new Mock<ISldWorks>(MockBehavior.Strict);
            app.Setup(x => x.SetUserPreferenceIntegerValue(It.IsAny<int>(), It.IsAny<int>())).Returns(accepted);
            app.Setup(x => x.SetUserPreferenceDoubleValue(It.IsAny<int>(), It.IsAny<double>())).Returns(accepted);
            app.Setup(x => x.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality)).Returns(quality);
            app.Setup(x => x.GetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swSTLDeviation)).Returns(deviation);
            app.Setup(x => x.GetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swSTLAngleTolerance)).Returns(angle);
            return app;
        }
    }
}
