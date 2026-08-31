using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SW2URDF.Test
{
    [Collection("Requires SW Test Collection")]
    public sealed class TestDeepReferenceGeometryIntegration : SW2URDFTest
    {
        private const string UnicodeFrameName = "\u5750\u6807\u7cfb1";
        private const string UnicodeAxisName = "\u53c2\u8003\u8f741";

        public TestDeepReferenceGeometryIntegration(SWTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public void PersistentReferencesResolveDeepUnicodeGeometryAcrossReopen()
        {
            Assert.True(string.Equals(
                System.Environment.GetEnvironmentVariable(
                    "SW2URDF_RUN_DEEP_REFERENCE_TESTS"),
                "1",
                StringComparison.Ordinal),
                "Set SW2URDF_RUN_DEEP_REFERENCE_TESTS=1 and provide the disposable fixture path.");

            string assemblyPath = System.Environment.GetEnvironmentVariable(
                "SW2URDF_TEST_DEEP_REFERENCE_ASSEMBLY");
            Assert.True(
                !string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath),
                "SW2URDF_TEST_DEEP_REFERENCE_ASSEMBLY must name the disposable nested assembly.");
            string disposableRoot = Path.Combine(
                Path.GetFullPath(Path.GetTempPath()),
                "sw2urdf-deep-reference-");
            Assert.True(
                Path.GetFullPath(assemblyPath).StartsWith(
                    disposableRoot,
                    StringComparison.OrdinalIgnoreCase),
                "The mutating Live API test only accepts a generated disposable fixture under the system temp directory.");

            ModelDoc2 model = OpenFixture(assemblyPath);
            PrepareDistinctTopLevelTransforms(model);
            CadFeatureReference rootFrameReference =
                CreateRootDocumentFrameReference(model);
            ReferenceGeometryCatalog catalog = new ReferenceGeometryCatalog(model);
            ReferenceGeometryEntry[] frames = catalog.CoordinateSystems
                .Where(entry => entry.DisplayName == UnicodeFrameName)
                .ToArray();
            ReferenceGeometryEntry[] axes = catalog.Axes
                .Where(entry => entry.DisplayName == UnicodeAxisName)
                .ToArray();

            Assert.True(frames.Length >= 2, "Expected the two same-name frame instances.");
            Assert.True(axes.Length >= 2, "Expected the two same-name axis instances.");
            Assert.Equal(
                frames.Length,
                frames.Select(entry => entry.Reference.IdentityKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.Equal(
                axes.Length,
                axes.Select(entry => entry.Reference.IdentityKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count());

            ReferenceGeometryEntry deepFrame = frames
                .OrderByDescending(entry => PathDepth(entry.ComponentPath))
                .First();
            ReferenceGeometryEntry shallowFrame = frames
                .OrderBy(entry => PathDepth(entry.ComponentPath))
                .First();
            ReferenceGeometryEntry deepAxis = axes.Single(entry =>
                entry.ComponentPath == deepFrame.ComponentPath);
            ReferenceGeometryEntry shallowAxis = axes.Single(entry =>
                entry.ComponentPath == shallowFrame.ComponentPath);

            Assert.True(
                PathDepth(deepFrame.ComponentPath) >= 5,
                "The selected reference must belong to the fifth-level leaf component.");
            Assert.NotEqual(deepFrame.DisplayLabel, shallowFrame.DisplayLabel);
            Assert.NotEqual(
                deepFrame.Reference.IdentityKey,
                shallowFrame.Reference.IdentityKey);

            CadFeatureReference deepFrameReference = deepFrame.Reference.Clone();
            CadFeatureReference shallowFrameReference = shallowFrame.Reference.Clone();
            CadFeatureReference deepAxisReference = deepAxis.Reference.Clone();
            CadFeatureReference shallowAxisReference = shallowAxis.Reference.Clone();
            VerifyResolvedGeometry(
                model,
                deepFrameReference,
                shallowFrameReference,
                deepAxisReference,
                shallowAxisReference);
            CreateInterruptedPreparedRecoverySlot(model);
            SaveFixture(
                model,
                "Saving the fixture with an interrupted prepared slot failed.");

            model = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.True(SwApp.CloseAllDocuments(true));
            Assert.Null(SwApp.ActiveDoc);
            model = OpenFixture(assemblyPath);
            AssertPreparedConfigurationIsIgnored(model);
            VerifyConfigurationPersistence(
                model,
                rootFrameReference,
                deepFrameReference,
                deepAxisReference);

            model = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.True(SwApp.CloseAllDocuments(true));
            Assert.Null(SwApp.ActiveDoc);
            model = OpenFixture(assemblyPath);
            VerifyRootDocumentReference(model, rootFrameReference);
            VerifyResolvedGeometry(
                model,
                deepFrameReference,
                shallowFrameReference,
                deepAxisReference,
                shallowAxisReference);
            AssertPersistedConfiguration(
                model,
                "deep_reference_root_v2",
                rootFrameReference,
                deepFrameReference,
                deepAxisReference);
            AssertNoRecoveryConfigurationAttribute(model);
        }

        private void VerifyConfigurationPersistence(
            ModelDoc2 model,
            CadFeatureReference rootFrameReference,
            CadFeatureReference deepFrameReference,
            CadFeatureReference deepAxisReference)
        {
            LinkNode first = CreateConfigurationTree(
                "deep_reference_root_v1",
                rootFrameReference,
                deepFrameReference,
                deepAxisReference);
            ConfigurationSaveResult firstSave =
                ConfigurationSerialization.SaveConfigTreeXML(
                    SwApp,
                    model,
                    first,
                    false);
            Assert.Equal(
                ConfigurationSaveStatus.Saved,
                firstSave.Status);
            AssertPersistedConfiguration(
                model,
                "deep_reference_root_v1",
                rootFrameReference,
                deepFrameReference,
                deepAxisReference);
            AssertNoRecoveryConfigurationAttribute(model);

            LinkNode second = CreateConfigurationTree(
                "deep_reference_root_v2",
                rootFrameReference,
                deepFrameReference,
                deepAxisReference);
            ConfigurationSaveResult secondSave =
                ConfigurationSerialization.SaveConfigTreeXML(
                    SwApp,
                    model,
                    second,
                    true);
            Assert.Equal(
                ConfigurationSaveStatus.Saved,
                secondSave.Status);
            AssertPersistedConfiguration(
                model,
                "deep_reference_root_v2",
                rootFrameReference,
                deepFrameReference,
                deepAxisReference);
            AssertNoRecoveryConfigurationAttribute(model);

            SaveFixture(
                model,
                "Saving the fixture with its second committed configuration failed.");
        }

        private static void AssertPreparedConfigurationIsIgnored(ModelDoc2 model)
        {
            SolidWorks.Interop.sldworks.Attribute recovery =
                FindRecoveryConfigurationAttribute(model);
            Assert.True(
                recovery != null,
                "The prepared recovery slot must survive Save3 and a document reopen.");
            Assert.True(
                ConfigurationSerialization.IsPreparedConfigurationSlot(recovery),
                "The reopened recovery slot must retain its complete schema and revision=0 marker.");

            LinkNode interruptedLoad =
                ConfigurationSerialization.LoadBaseNodeFromModel(
                    model,
                    out string interruptedLoadError);
            Assert.Null(interruptedLoad);
            Assert.Equal(string.Empty, interruptedLoadError);
        }

        private static void SaveFixture(ModelDoc2 model, string failureMessage)
        {
            int errors = 0;
            int warnings = 0;
            Assert.True(
                model.Save3(
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    ref errors,
                    ref warnings),
                failureMessage + " Errors=" + errors + ", warnings=" + warnings);
            Assert.Equal(0, errors);
        }

        private void CreateInterruptedPreparedRecoverySlot(ModelDoc2 model)
        {
            FieldInfo field = typeof(ConfigurationSerialization).GetField(
                "UrdfConfigurationRecoveryAttributeName",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);
            string recoveryName = (string)field.GetRawConstantValue();
            SolidWorks.Interop.sldworks.Attribute recovery =
                ConfigurationSerialization.CreateNewConfigurationAttribute(
                    SwApp,
                    model,
                    recoveryName);

            Assert.NotNull(recovery);
            Assert.True(
                ConfigurationSerialization.IsPreparedConfigurationSlot(recovery),
                "A newly created slot must remain uncommitted until its final revision write.");
        }

        private static LinkNode CreateConfigurationTree(
            string rootName,
            CadFeatureReference rootFrameReference,
            CadFeatureReference deepFrameReference,
            CadFeatureReference deepAxisReference)
        {
            LinkNode root = new LinkNode
            {
                IsBaseNode = true,
                Name = rootName,
                Text = rootName
            };
            root.Link.Name = rootName;
            root.Link.FrameReference = rootFrameReference.Clone();

            const string childName = "unicode_deep_reference_link";
            LinkNode child = new LinkNode
            {
                IsBaseNode = false,
                Name = childName,
                Text = childName
            };
            child.Link.Name = childName;
            child.Link.FrameReference = deepFrameReference.Clone();
            child.Link.Joint.Name = "unicode_deep_reference_joint";
            child.Link.Joint.Type = "continuous";
            child.Link.Joint.AxisReference = deepAxisReference.Clone();
            root.Nodes.Add(child);
            return root;
        }

        private static void AssertPersistedConfiguration(
            ModelDoc2 model,
            string expectedRootName,
            CadFeatureReference rootFrameReference,
            CadFeatureReference deepFrameReference,
            CadFeatureReference deepAxisReference)
        {
            LinkNode restored = ConfigurationSerialization.LoadBaseNodeFromModel(
                model,
                out string errorMessage);
            Assert.True(
                string.IsNullOrWhiteSpace(errorMessage),
                errorMessage);
            Assert.NotNull(restored);
            Assert.Equal(expectedRootName, restored.Link.Name);
            Assert.Equal(rootFrameReference, restored.Link.FrameReference);
            Assert.Equal(
                ReferenceGeometryOwnerScope.RootDocument,
                restored.Link.FrameReference.OwnerScope);

            LinkNode child = Assert.IsType<LinkNode>(
                Assert.Single(restored.Nodes.Cast<LinkNode>()));
            Assert.Equal("unicode_deep_reference_link", child.Link.Name);
            Assert.Equal(deepFrameReference, child.Link.FrameReference);
            Assert.Equal(
                ReferenceGeometryOwnerScope.ComponentInstance,
                child.Link.FrameReference.OwnerScope);
            Assert.Equal(deepAxisReference, child.Link.Joint.AxisReference);
            Assert.Equal(
                ReferenceGeometryOwnerScope.ComponentInstance,
                child.Link.Joint.AxisReference.OwnerScope);
        }

        private CadFeatureReference CreateRootDocumentFrameReference(ModelDoc2 model)
        {
            ExportHelper exporter = new ExportHelper(SwApp);
            MethodInfo createBaseReference = typeof(ExportHelper).GetMethod(
                "CreateBaseRefOrigin",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(createBaseReference);

            CadFeatureReference reference = Assert.IsType<CadFeatureReference>(
                createBaseReference.Invoke(exporter, new object[] { true }));
            VerifyRootDocumentReference(model, reference);
            return reference;
        }

        private static void VerifyRootDocumentReference(
            ModelDoc2 model,
            CadFeatureReference reference)
        {
            Assert.Equal(
                ReferenceGeometryOwnerScope.RootDocument,
                reference.OwnerScope);
            Assert.True(reference.FeaturePersistentId.Length > 0);
            Assert.True(
                reference.ComponentPersistentId == null ||
                reference.ComponentPersistentId.Length == 0);

            ReferenceGeometryResolver resolver =
                new ReferenceGeometryResolver(model);
            ReferenceGeometryResolution resolution = resolver.Resolve(reference);
            Assert.True(resolution.IsResolved, resolution.Message);
            Assert.Null(resolution.Geometry.Component);
            Assert.Equal(
                model.GetPathName(),
                resolution.Geometry.OwnerModel.GetPathName(),
                ignoreCase: true);
            Assert.NotNull(
                resolver.ResolveCoordinateSystemTransform(
                    reference,
                    out resolution));
            Assert.True(resolution.IsResolved, resolution.Message);
        }

        private static void AssertNoRecoveryConfigurationAttribute(ModelDoc2 model)
        {
            Assert.True(
                FindRecoveryConfigurationAttribute(model) == null,
                "The validated recovery configuration instance must be deleted after canonical commit.");
        }

        private static SolidWorks.Interop.sldworks.Attribute
            FindRecoveryConfigurationAttribute(ModelDoc2 model)
        {
            const string recoveryAttributeName =
                "URDF Export Configuration (v2 recovery)";
            FeatureManager featureManager = model.FeatureManager;
            Feature[] features = (featureManager.GetFeatures(true) as object[] ??
                    new object[0])
                .OfType<Feature>()
                .ToArray();
            return features.Select(feature =>
            {
                SolidWorks.Interop.sldworks.Attribute attribute =
                    feature.GetSpecificFeature2() as
                        SolidWorks.Interop.sldworks.Attribute;
                return attribute != null &&
                    (string.Equals(
                         feature.Name,
                         recoveryAttributeName,
                         StringComparison.Ordinal) ||
                     string.Equals(
                         attribute.GetName(),
                         recoveryAttributeName,
                         StringComparison.Ordinal))
                    ? attribute
                    : null;
            }).FirstOrDefault(attribute => attribute != null);
        }

        private ModelDoc2 OpenFixture(string assemblyPath)
        {
            Assert.True(SwApp.CloseAllDocuments(true));
            int errors = 0;
            int warnings = 0;
            ModelDoc2 model = (ModelDoc2)SwApp.OpenDoc6(
                assemblyPath,
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                string.Empty,
                ref errors,
                ref warnings);
            Assert.NotNull(model);
            Assert.Equal(0, errors);

            AssemblyDoc assembly = model as AssemblyDoc;
            Assert.NotNull(assembly);
            int resolveStatus = assembly.ResolveAllLightWeightComponents(true);
            Assert.True(
                resolveStatus == (int)swComponentResolveStatus_e.swResolveOk,
                "Nested fixture components could not be fully resolved. Status: " +
                resolveStatus + ", warnings: " + warnings);
            return model;
        }

        private void PrepareDistinctTopLevelTransforms(ModelDoc2 model)
        {
            AssemblyDoc assembly = model as AssemblyDoc;
            Assert.NotNull(assembly);
            Component2[] components = (assembly.GetComponents(true) as object[] ??
                    new object[0])
                .OfType<Component2>()
                .ToArray();
            Component2 nestedBranch = components.Single(component =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(component.GetPathName()),
                    "level_4",
                    StringComparison.OrdinalIgnoreCase));
            Component2 shallowBranch = components.Single(component =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(component.GetPathName()),
                    "unicode_leaf",
                    StringComparison.OrdinalIgnoreCase));

            SetComponentTransform(
                nestedBranch,
                CreateZRotationTransform(Math.PI * 31.0 / 180.0, 0.12, -0.08, 0.05));
            SetComponentTransform(
                shallowBranch,
                CreateZRotationTransform(Math.PI * -23.0 / 180.0, 0.31, 0.07, -0.09));
            model.ForceRebuild3(false);

            MathTransform deepTransform = nestedBranch.GetTotalTransform(false);
            MathTransform shallowTransform = shallowBranch.GetTotalTransform(false);
            Assert.NotNull(deepTransform);
            Assert.NotNull(shallowTransform);
            Assert.True(
                Math.Abs(((double[])deepTransform.ArrayData)[1]) > 0.1,
                "The nested fixture branch rotation was not applied.");
            Assert.True(
                Math.Abs(((double[])shallowTransform.ArrayData)[1]) > 0.1,
                "The shallow fixture branch rotation was not applied.");

            int errors = 0;
            int warnings = 0;
            Assert.True(
                model.Save3(
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    ref errors,
                    ref warnings),
                "Saving the transformed nested fixture failed. Errors=" + errors +
                ", warnings=" + warnings);
            Assert.Equal(0, errors);
        }

        private MathTransform CreateZRotationTransform(
            double angle,
            double x,
            double y,
            double z)
        {
            double cosine = Math.Cos(angle);
            double sine = Math.Sin(angle);
            MathUtility mathUtility = (MathUtility)SwApp.GetMathUtility();
            return (MathTransform)mathUtility.CreateTransform(new[]
            {
                cosine, sine, 0.0,
                -sine, cosine, 0.0,
                0.0, 0.0, 1.0,
                x, y, z,
                1.0,
                0.0, 0.0, 0.0
            });
        }

        private static void SetComponentTransform(
            Component2 component,
            MathTransform transform)
        {
            Assert.NotNull(component);
            Assert.NotNull(transform);
            if (!component.SetTransformAndSolve2(transform))
            {
                component.Transform2 = transform;
            }
        }

        private void VerifyResolvedGeometry(
            ModelDoc2 model,
            CadFeatureReference deepFrameReference,
            CadFeatureReference shallowFrameReference,
            CadFeatureReference deepAxisReference,
            CadFeatureReference shallowAxisReference)
        {
            ReferenceGeometryResolver resolver = new ReferenceGeometryResolver(model);
            ReferenceGeometryResolution deepResolution = resolver.Resolve(
                deepFrameReference);
            ReferenceGeometryResolution shallowResolution = resolver.Resolve(
                shallowFrameReference);
            ReferenceGeometryResolution axisResolution = resolver.Resolve(
                deepAxisReference);
            ReferenceGeometryResolution shallowAxisResolution = resolver.Resolve(
                shallowAxisReference);

            Assert.True(deepResolution.IsResolved, deepResolution.Message);
            Assert.True(shallowResolution.IsResolved, shallowResolution.Message);
            Assert.True(axisResolution.IsResolved, axisResolution.Message);
            Assert.True(shallowAxisResolution.IsResolved, shallowAxisResolution.Message);
            Assert.Equal(UnicodeFrameName, deepResolution.Geometry.Feature.Name);
            Assert.Equal(UnicodeFrameName, shallowResolution.Geometry.Feature.Name);
            Assert.Equal(UnicodeAxisName, axisResolution.Geometry.Feature.Name);
            Assert.NotEqual(
                deepResolution.Geometry.Component.Name2,
                shallowResolution.Geometry.Component.Name2);

            CadFeatureReference configurationRenamedReference =
                CadFeatureReference.ExplicitComponent(
                    ReferenceGeometryKind.CoordinateSystem,
                    deepFrameReference.ComponentPersistentId,
                    deepFrameReference.FeaturePersistentId,
                    "renamed_\u914d\u7f6e");
            Assert.Equal(deepFrameReference, configurationRenamedReference);
            ReferenceGeometryResolution configurationRenamedResolution =
                resolver.Resolve(configurationRenamedReference);
            Assert.True(
                configurationRenamedResolution.IsResolved,
                configurationRenamedResolution.Message);
            Assert.Equal(
                deepResolution.Geometry.Component.Name2,
                configurationRenamedResolution.Geometry.Component.Name2);

            VerifyCombinedTransformMatchesSolidWorksSequence(
                resolver,
                deepFrameReference,
                deepResolution.Geometry);

            ExportHelper exporter = new ExportHelper(SwApp);
            VerifyDeepReferenceConsumers(
                exporter,
                deepFrameReference,
                deepResolution.Geometry.Component);
            double[] deepAxis = VerifyAxisMatchesSolidWorksTransform(
                exporter,
                deepAxisReference,
                axisResolution.Geometry);
            double[] shallowAxis = VerifyAxisMatchesSolidWorksTransform(
                exporter,
                shallowAxisReference,
                shallowAxisResolution.Geometry);
            double axisDifference = deepAxis.Zip(
                shallowAxis,
                (left, right) => Math.Abs(left - right)).Sum();
            Assert.True(
                axisDifference > 1e-4,
                string.Format(
                    "The differently transformed same-name axis instances must remain distinct. " +
                    "deepPath={0}; shallowPath={1}; deepAxis=[{2}]; shallowAxis=[{3}]; " +
                    "deepTotal=[{4}]; shallowTotal=[{5}]",
                    axisResolution.Geometry.Component.Name2,
                    shallowAxisResolution.Geometry.Component.Name2,
                    FormatArray(deepAxis),
                    FormatArray(shallowAxis),
                    FormatTransform(axisResolution.Geometry.Component.GetTotalTransform(false)),
                    FormatTransform(shallowAxisResolution.Geometry.Component.GetTotalTransform(false))));
        }

        private static void VerifyDeepReferenceConsumers(
            ExportHelper exporter,
            CadFeatureReference frameReference,
            Component2 component)
        {
            Link link = new Link
            {
                Name = "deep_reference_link",
                FrameReference = frameReference.Clone()
            };
            link.SWComponents.Add(component);

            exporter.ComputeInertialProperties(link);
            Assert.True(link.Inertial.Mass.Value > 0);
            Assert.All(
                link.Inertial.Origin.GetXYZ(),
                value => Assert.True(!double.IsNaN(value) && !double.IsInfinity(value)));
            Assert.True(link.Inertial.Inertia.Ixx > 0);
            Assert.True(link.Inertial.Inertia.Iyy > 0);
            Assert.True(link.Inertial.Inertia.Izz > 0);

            ExportHelper.LinkLocalBoundingBox bounds =
                exporter.CreateLinkLocalBoundingBox(link);
            Assert.True(bounds.IsUsable);
        }

        private void VerifyCombinedTransformMatchesSolidWorksSequence(
            ReferenceGeometryResolver resolver,
            CadFeatureReference frameReference,
            ResolvedReferenceGeometry geometry)
        {
            MathTransform combined = resolver.ResolveCoordinateSystemTransform(
                frameReference,
                out ReferenceGeometryResolution resolution);
            Assert.True(resolution.IsResolved, resolution.Message);
            Assert.NotNull(combined);

            Feature ownerFeature = ResolveOwnerFeature(
                geometry,
                frameReference);
            CoordinateSystemFeatureData definition =
                ownerFeature.GetDefinition() as CoordinateSystemFeatureData;
            Assert.NotNull(definition);
            MathTransform local = definition.Transform;
            MathTransform component = geometry.Component.GetTotalTransform(false);
            Assert.NotNull(local);
            Assert.NotNull(component);

            MathUtility mathUtility = (MathUtility)SwApp.GetMathUtility();
            double[][] sourcePoints =
            {
                new[] { 0.0, 0.0, 0.0 },
                new[] { 0.019, -0.027, 0.041 }
            };
            foreach (double[] source in sourcePoints)
            {
                MathPoint sourcePoint = (MathPoint)mathUtility.CreatePoint(source);
                MathPoint localPoint = (MathPoint)sourcePoint.MultiplyTransform(local);
                MathPoint expectedPoint = (MathPoint)localPoint.MultiplyTransform(component);
                MathPoint actualPoint = (MathPoint)sourcePoint.MultiplyTransform(combined);
                double[] expected = (double[])expectedPoint.ArrayData;
                double[] actual = (double[])actualPoint.ArrayData;
                for (int axis = 0; axis < 3; axis++)
                {
                    Assert.True(
                        Math.Abs(expected[axis] - actual[axis]) <= 1e-10,
                        string.Format(
                            "Combined transform mismatch on axis {0}: expected={1:G17}, actual={2:G17}",
                            axis,
                            expected[axis],
                            actual[axis]));
                }
            }
        }

        private double[] VerifyAxisMatchesSolidWorksTransform(
            ExportHelper exporter,
            CadFeatureReference axisReference,
            ResolvedReferenceGeometry geometry)
        {
            Feature ownerFeature = ResolveOwnerFeature(
                geometry,
                axisReference);
            RefAxis axisFeature = ownerFeature.GetSpecificFeature2() as RefAxis;
            Assert.NotNull(axisFeature);
            double[] parameters = axisFeature.GetRefAxisParams() as double[];
            Assert.NotNull(parameters);
            Assert.True(parameters.Length >= 6);
            double[] local =
            {
                parameters[3] - parameters[0],
                parameters[4] - parameters[1],
                parameters[5] - parameters[2]
            };
            double localLength = Math.Sqrt(local.Sum(value => value * value));
            Assert.True(localLength > 1e-12);
            local = local.Select(value => value / localLength).ToArray();

            MathTransform componentTransform = geometry.Component.GetTotalTransform(false);
            Assert.NotNull(componentTransform);
            MathUtility mathUtility = (MathUtility)SwApp.GetMathUtility();
            MathVector localVector = (MathVector)mathUtility.CreateVector(local);
            MathVector expectedVector = (MathVector)localVector.MultiplyTransform(
                componentTransform);
            double[] expected = (double[])expectedVector.ArrayData;
            double expectedLength = Math.Sqrt(expected.Sum(value => value * value));
            expected = expected.Select(value => value / expectedLength).ToArray();

            double[] actual = exporter.EstimateAxis(axisReference);
            Assert.Equal(3, actual.Length);
            for (int index = 0; index < 3; index++)
            {
                Assert.True(
                    Math.Abs(expected[index] - actual[index]) <= 1e-10,
                    string.Format(
                        "Axis transform mismatch at {0}: expected={1:G17}, actual={2:G17}",
                        index,
                        expected[index],
                        actual[index]));
            }
            return actual;
        }

        private static Feature ResolveOwnerFeature(
            ResolvedReferenceGeometry geometry,
            CadFeatureReference reference)
        {
            ModelDocExtension extension = geometry.OwnerModel.Extension;
            Assert.NotNull(extension);
            Feature feature = extension.GetObjectByPersistReference3(
                reference.FeaturePersistentId,
                out int state) as Feature;
            Assert.Equal(
                (int)swPersistReferencedObjectStates_e.swPersistReferencedObject_Ok,
                state);
            Assert.NotNull(feature);
            return feature;
        }

        private static int PathDepth(string componentPath)
        {
            return (componentPath ?? string.Empty)
                .Split(
                    new[] { '/', '\\' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Length;
        }

        private static string FormatTransform(MathTransform transform)
        {
            return transform == null
                ? "null"
                : FormatArray(transform.ArrayData as double[]);
        }

        private static string FormatArray(double[] values)
        {
            return values == null
                ? "null"
                : string.Join(",", values.Select(value => value.ToString("G17")));
        }
    }
}
