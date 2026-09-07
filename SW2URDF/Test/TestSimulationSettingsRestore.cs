using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using OSURDF.Core.Model;
using SW2URDF.UI;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public class TestSimulationSettingsRestore
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TargetDamageRestoresOtherTargetAndValidatesOnlyWhenSelected(bool usdDamage)
        {
            var root = SavedRoot(usdDamage);
            string original = root.SimulationSettingsJson;
            var options = ExportTargetOptions.RecommendedDefaults("robot");
            ExportTargetOptions.RestoreSimulationSettings(root, options);

            Assert.Equal(original, root.SimulationSettingsJson);
            Assert.Equal("floating", options.Simulation.BaseMode);
            Assert.Equal("velocity", Assert.Single(options.Simulation.JointDrives).Mode);
            if (usdDamage)
            {
                Assert.NotEmpty(options.UsdSimulationRestoreError);
                Assert.Null(options.MjcfSimulationRestoreError);
                Assert.Null(options.UsdSimulation);
                Assert.Equal(3.0, Assert.Single(options.Simulation.Mjcf.JointDrives).Damping);
            }
            else
            {
                Assert.Null(options.UsdSimulationRestoreError);
                Assert.NotEmpty(options.MjcfSimulationRestoreError);
                Assert.Null(options.Simulation.Mjcf);
                Assert.Equal(2.0, Assert.Single(options.UsdSimulation.JointDrives).Damping);
            }
            string code = usdDamage ? "USD_SIMULATION_RESTORE" : "MJCF_SIMULATION_RESTORE";
            Assert.Equal(code, Assert.Single(options.ValidateFindings()).Code);
            options.ExportUsdAsset = !usdDamage;
            options.ExportMjcfAsset = usdDamage;
            Assert.Empty(options.ValidateFindings());
            options.ExportUsdAsset = options.ExportMjcfAsset = false;
            Assert.Empty(options.ValidateFindings());
            options.ExportUsdAsset = options.ExportMjcfAsset = true;
            Assert.Equal(code, Assert.Single(options.ValidateFindings()).Code);
        }

        [Fact]
        public void BothTargetErrorsAreRetainedWithoutLosingCommonIntent()
        {
            var root = SavedRoot(true);
            JObject json = JObject.Parse(root.SimulationSettingsJson);
            json["simulation"]["mjcf"] = new JArray();
            root.SimulationSettingsJson = json.ToString();
            var options = ExportTargetOptions.RecommendedDefaults("robot");
            ExportTargetOptions.RestoreSimulationSettings(root, options);
            Assert.NotEmpty(options.UsdSimulationRestoreError);
            Assert.NotEmpty(options.MjcfSimulationRestoreError);
            Assert.Equal("floating", options.Simulation.BaseMode);
            Assert.Equal(2, options.ValidateFindings().Count);

            root.SimulationSettingsJson = ValidEnvelope().ToString();
            ExportTargetOptions.RestoreSimulationSettings(root, options);
            Assert.Equal(2, options.ValidateFindings().Count);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void FormCapturesTargetErrorUntilExplicitApply(bool usdDamage)
        {
            using (var form = CreateForm(SavedRoot(usdDamage)))
            {
                string original = Root(form).SimulationSettingsJson;
                var restored = ExportTargetOptions.RecommendedDefaults("robot");
                string error;
                Assert.True(form.TryRestoreSimulationSettings(restored, out error));
                Assert.Null(error);
                Assert.False(form.IsSimulationExportBlocked(true));
                form.Exporter.ExportTargets = restored;
                SetTarget(form, "modernUsdAssetCheckBox", !usdDamage);
                SetTarget(form, "modernMjcfAssetCheckBox", usdDamage);
                Assert.DoesNotContain(Capture(form).ValidateFindings(), IsRestoreError);
                SetTarget(form, "modernUsdAssetCheckBox", true);
                SetTarget(form, "modernMjcfAssetCheckBox", true);
                Assert.Single(Capture(form).ValidateFindings().Where(IsRestoreError));
                Assert.Equal(original, Root(form).SimulationSettingsJson);

                // A later successful read must not act as explicit repair confirmation.
                Root(form).SimulationSettingsJson = ValidEnvelope().ToString();
                Assert.True(form.TryRestoreSimulationSettings(new ExportTargetOptions(), out error));
                Assert.Single(Capture(form).ValidateFindings().Where(IsRestoreError));
                form.ApplySimulationSettings(new UsdSimulationProfile(), new SimulationProfile());
                Assert.DoesNotContain(Capture(form).ValidateFindings(), IsRestoreError);
                Assert.Null(form.Exporter.ExportTargets.UsdSimulationRestoreError);
                Assert.Null(form.Exporter.ExportTargets.MjcfSimulationRestoreError);
                Assert.NotNull(form.Exporter.ExportTargets.UsdSimulation);
                Assert.NotNull(form.Exporter.ExportTargets.Simulation.Mjcf);
                Assert.False(form.IsSimulationExportBlocked(true));
            }
        }

        [Theory]
        [InlineData("{broken")]
        [InlineData("{\"version\":1,\"simulation\":[],\"usdSimulation\":{}}")]
        [InlineData("{\"version\":1,\"simulation\":{\"jointDrives\":\"broken\"},\"usdSimulation\":{}}")]
        public void WholeJsonAndCommonDamageKeepGlobalProtection(string json)
        {
            using (var form = CreateForm(new SW2URDF.URDF.Link { SimulationSettingsJson = json }))
            {
                string error;
                Assert.False(form.TryRestoreSimulationSettings(new ExportTargetOptions(), out error));
                Assert.NotEmpty(error);
                Assert.Equal(json, Root(form).SimulationSettingsJson);
                SetTarget(form, "modernUsdAssetCheckBox", true);
                SetTarget(form, "modernMjcfAssetCheckBox", true);
                Assert.True(form.IsSimulationExportBlocked(true));
                Assert.False(form.IsSimulationExportBlocked(false));
                SetTarget(form, "modernUsdAssetCheckBox", false);
                SetTarget(form, "modernMjcfAssetCheckBox", false);
                Assert.False(form.IsSimulationExportBlocked(true));
            }
        }

        private static bool IsRestoreError(ExportTargetValidationFinding finding) =>
            finding.Code == "USD_SIMULATION_RESTORE" || finding.Code == "MJCF_SIMULATION_RESTORE";

        private static JObject ValidEnvelope() => JObject.Parse(
            "{\"version\":1,\"simulation\":{\"baseMode\":\"floating\",\"jointDrives\":[{\"joint\":\"wheel\",\"mode\":\"velocity\"}]," +
            "\"mjcf\":{\"jointDrives\":[{\"joint\":\"wheel\",\"damping\":3}]}}," +
            "\"usdSimulation\":{\"jointDrives\":[{\"joint\":\"wheel\",\"mode\":\"velocity\",\"damping\":2}]}}");

        private static SW2URDF.URDF.Link SavedRoot(bool usdDamage)
        {
            JObject json = ValidEnvelope();
            JToken target = usdDamage ? json["usdSimulation"] : json["simulation"]["mjcf"];
            target["jointDrives"][0]["damping"] = "broken";
            return new SW2URDF.URDF.Link { SimulationSettingsJson = json.ToString() };
        }

        private static AssemblyExportForm CreateForm(SW2URDF.URDF.Link root)
        {
            var form = (AssemblyExportForm)Activator.CreateInstance(typeof(AssemblyExportForm), true);
            form.Exporter = (ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));
            // The real exporter leaves its legacy Links field uninitialized.
            typeof(AssemblyExportForm).GetField("BaseNode", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(form, new SW2URDF.URDF.LinkNode(root));
            return form;
        }

        private static SW2URDF.URDF.Link Root(AssemblyExportForm form) =>
            ((SW2URDF.URDF.LinkNode)typeof(AssemblyExportForm).GetField("BaseNode",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form)).Link;

        [Fact]
        public void ApplyAndRestoreUseEditableRootWithoutLegacyExporterLinks()
        {
            var root = new SW2URDF.URDF.Link();
            using (var form = CreateForm(root))
            {
                Assert.Null(form.Exporter.Links);
                form.ApplySimulationSettings(new UsdSimulationProfile(),
                    new SimulationProfile { BaseMode = "floating" });
                Assert.NotEmpty(root.SimulationSettingsJson);
                var restored = new ExportTargetOptions();
                string error;
                Assert.True(form.TryRestoreSimulationSettings(restored, out error));
                Assert.Null(error);
                Assert.Equal("floating", restored.Simulation.BaseMode);
            }
        }

        private static ExportTargetOptions Capture(AssemblyExportForm form) =>
            (ExportTargetOptions)typeof(AssemblyExportForm).GetMethod("CaptureExportTargetOptions",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);

        private static void SetTarget(AssemblyExportForm form, string name, bool enabled) =>
            ((CheckBox)typeof(AssemblyExportForm).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form)).Checked = enabled;
    }
}
