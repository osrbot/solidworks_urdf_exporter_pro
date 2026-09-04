using MathNet.Numerics.LinearAlgebra;
using Moq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace SW2URDF.Test
{
    public class TestSolidWorksEffectiveMass
    {
        [Fact]
        public void SelectedPartUsesEffectiveSiValuesAndSeparateComAndInertiaObjects()
        {
            var fixture = new Fixture();
            Component2 part = fixture.Component("part-1", 4.25).Object;
            fixture.Override("part-1", true, true, true);

            MassPropertySnapshot snapshot = fixture.Read(part);

            Assert.Equal(4.25, snapshot.Mass);
            Assert.Equal(new[] { 0.12, -0.23, 0.34 }, snapshot.CenterOfMass);
            Assert.Equal(Fixture.Moment, snapshot.Moment);
            Assert.True(snapshot.HasMassOverride);
            Assert.True(snapshot.HasCenterOfMassOverride);
            Assert.True(snapshot.HasInertiaOverride);
            Assert.Equal(3, fixture.Properties.Count); // shared metadata, COM, inertia
            Assert.Equal(new[] { part }, fixture.LastScope);
            fixture.Extension.Verify(value => value.CreateMassProperty(), Times.Never);
            fixture.Assembly.Verify(value => value.ClearSelection2(It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public void ParentAndChildSelectionsKeepWholeSubassemblyOverrideAtomicInEitherOrder()
        {
            foreach (bool childFirst in new[] { false, true })
            {
                var fixture = new Fixture();
                var parent = fixture.Component("gearbox-1", 12.0);
                var child = fixture.Component("gearbox-1/shaft-1", 2.0, parent.Object);
                parent.Setup(value => value.GetChildren()).Returns(new object[] { child.Object });
                fixture.Override("gearbox-1", true, true, true);
                Component2[] selection = childFirst
                    ? new[] { child.Object, parent.Object, child.Object }
                    : new[] { parent.Object, child.Object, parent.Object };

                MassPropertySnapshot result = fixture.Read(selection);

                Assert.Equal(12.0, result.Mass);
                Assert.Equal(new[] { parent.Object }, fixture.LastScope);
                Assert.True(result.HasInertiaOverride);
            }
        }

        [Fact]
        public void DuplicateOccurrenceIsCountedOnceButSiblingInstancesAreNotCollapsed()
        {
            var fixture = new Fixture();
            var first = fixture.Component("part-1", 2.0);
            var second = fixture.Component("part-10", 3.0);

            Assert.Equal(5.0, fixture.Read(first.Object, second.Object, first.Object).Mass);
            Assert.Equal(2, fixture.LastScope.Length);
        }

        [Fact]
        public void MultipleComponentsAggregateOverrideFlagsWithSingletonMetadataQueries()
        {
            var fixture = new Fixture();
            var first = fixture.Component("a-1", 2.0);
            var second = fixture.Component("b-1", 3.0);
            fixture.Override("a-1", true, false, false);
            fixture.Override("b-1", false, true, true);

            MassPropertySnapshot result = fixture.Read(first.Object, second.Object);

            Assert.Equal(5.0, result.Mass);
            Assert.True(result.HasMassOverride);
            Assert.True(result.HasCenterOfMassOverride);
            Assert.True(result.HasInertiaOverride);
        }

        [Fact]
        public void MetadataTraversalUsesOneObjectAndNoPerComponentRecalculation()
        {
            var fixture = new Fixture();
            Component2[] components = Enumerable.Range(1, 50)
                .Select(index => fixture.Component("part-" + index, 1.0).Object).ToArray();

            Assert.Equal(50.0, fixture.Read(components).Mass);

            Assert.Equal(3, fixture.Properties.Count);
            fixture.Properties[0].Verify(value => value.Recalculate(), Times.Never);
            fixture.Properties[0].Verify(value => value.GetOverrideOptions(), Times.Exactly(51));
            fixture.Properties[1].Verify(value => value.Recalculate(), Times.Once);
            fixture.Properties[2].Verify(value => value.Recalculate(), Times.Once);
        }

        [Fact]
        public void WholeSubassemblyIncludesDescendantOverrideMetadataWithoutFlatteningSelection()
        {
            var fixture = new Fixture();
            var parent = fixture.Component("module-1", 7.0);
            var child = fixture.Component("module-1/motor-1", 5.0, parent.Object);
            parent.Setup(value => value.GetChildren()).Returns(new object[] { child.Object });
            fixture.Override(child.Object.Name2, false, true, false);

            MassPropertySnapshot result = fixture.Read(parent.Object);

            Assert.Equal(7.0, result.Mass);
            Assert.True(result.HasCenterOfMassOverride);
            Assert.False(result.HasMassOverride);
            Assert.False(result.HasInertiaOverride);
            Assert.Equal(new[] { parent.Object }, fixture.LastScope);
        }

        [Fact]
        public void DescendantOnlySelectionRejectsAnyWholeSubassemblyOverride()
        {
            for (int flag = 0; flag < 3; flag++)
            {
                var fixture = new Fixture();
                var parent = fixture.Component("module-1", 20.0);
                var child = fixture.Component("module-1/motor-1", 2.0, parent.Object);
                fixture.Override(parent.Object.Name2, flag == 0, flag == 1, flag == 2);

                var error = Assert.Throws<InvalidOperationException>(() => fixture.Read(child.Object));

                Assert.Contains("module-1", error.Message);
                Assert.Contains("cannot be distributed", error.Message);
                Assert.Equal(0, fixture.NumericReads);
            }
        }

        [Fact]
        public void WholeAssemblyOverrideCannotBeCopiedIntoEachLink()
        {
            for (int flag = 0; flag < 3; flag++)
            {
                var fixture = new Fixture();
                var part = fixture.Component("part-1", 2.0);
                fixture.Override(string.Empty, flag == 0, flag == 1, flag == 2);

                var error = Assert.Throws<InvalidOperationException>(() => fixture.Read(part.Object));

                Assert.Contains("whole-assembly", error.Message);
                Assert.Contains("cannot be distributed", error.Message);
                Assert.Equal(0, fixture.NumericReads);
            }
        }

        [Fact]
        public void CurrentReferencedConfigurationIsUsedWithoutOpeningOrSwitchingPartDocument()
        {
            var fixture = new Fixture();
            var part = fixture.Component("part-1", 3.0);
            part.SetupGet(value => value.ReferencedConfiguration).Returns("Measured variant");
            fixture.EffectiveMass = components => components.Single().ReferencedConfiguration == "Measured variant" ? 9.0 : 3.0;

            Assert.Equal(9.0, fixture.Read(part.Object).Mass);
            part.Verify(value => value.GetModelDoc2(), Times.Never);
            part.VerifySet(value => value.ReferencedConfiguration = It.IsAny<string>(), Times.Never);
            fixture.Assembly.Verify(value => value.ShowConfiguration2(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void ConfigurationChangeDuringReadIsRejected()
        {
            foreach (bool changeAssembly in new[] { false, true })
            {
                var fixture = new Fixture();
                var part = fixture.Component("part-1", 3.0);
                fixture.EffectiveMass = components =>
                {
                    if (changeAssembly) fixture.Configuration.SetupGet(value => value.Name).Returns("Other");
                    else part.SetupGet(value => value.ReferencedConfiguration).Returns("Other");
                    return 3.0;
                };

                Assert.Contains("configuration changed", Assert.Throws<InvalidOperationException>(
                    () => fixture.Read(part.Object)).Message);
            }
        }

        [Fact]
        public void EmptyAndRootSelectionsCannotBecomeWholeAssemblyReads()
        {
            var fixture = new Fixture();
            Assert.Throws<ArgumentException>(() => fixture.Read());
            Assert.Throws<ArgumentException>(() => fixture.Read(new Component2[] { null }));
            var root = fixture.Component("root", 100.0);
            root.Setup(value => value.IsRoot()).Returns(true);
            Assert.Throws<ArgumentException>(() => fixture.Read(root.Object));
            Assert.Empty(fixture.Properties);
        }

        [Fact]
        public void ExplicitNullReadsWholePartOrAssemblyAndRetainsDocumentOverrides()
        {
            foreach (int type in new[] { (int)swDocumentTypes_e.swDocPART, (int)swDocumentTypes_e.swDocASSEMBLY })
            {
                var fixture = new Fixture { AllowWholeDocument = true };
                fixture.Assembly.Setup(value => value.GetType()).Returns(type);
                fixture.Override(string.Empty, true, true, true);

                MassPropertySnapshot result = fixture.Read((Component2[])null);

                Assert.Equal(123.0, result.Mass);
                Assert.True(result.HasMassOverride);
                Assert.True(result.HasCenterOfMassOverride);
                Assert.True(result.HasInertiaOverride);
                Assert.Empty(fixture.LastScope);
            }
        }

        [Fact]
        public void ExplicitWholeAssemblyReadInspectsCurrentRootForDescendantOverrides()
        {
            var fixture = new Fixture { AllowWholeDocument = true };
            var root = new Mock<Component2>(MockBehavior.Strict);
            var child = fixture.Component("part-1", 3.0);
            fixture.Override("part-1", false, false, true);
            root.Setup(value => value.GetChildren()).Returns(new object[] { child.Object });
            fixture.Configuration.Setup(value => value.GetRootComponent3(false)).Returns(root.Object);

            MassPropertySnapshot result = fixture.Read((Component2[])null);

            Assert.Equal(123.0, result.Mass);
            Assert.False(result.HasMassOverride);
            Assert.True(result.HasInertiaOverride);
            fixture.Configuration.Verify(value => value.GetRootComponent3(false), Times.Once);
            Assert.Empty(fixture.LastScope);
        }

        [Fact]
        public void SuppressedLightweightAndUnresolvedExplicitSelectionsFail()
        {
            foreach (int state in new[] { 0, 1, 4, 5 })
            {
                var fixture = new Fixture();
                var part = fixture.Component("part-1", 3.0);
                part.Setup(value => value.GetSuppression2()).Returns(state);
                Assert.Contains("Resolve it", Assert.Throws<InvalidOperationException>(() => fixture.Read(part.Object)).Message);
                Assert.Empty(fixture.Properties);
            }
        }

        [Fact]
        public void SuppressedDescendantsAreNotReadAsEffectiveComponents()
        {
            var fixture = new Fixture();
            var parent = fixture.Component("module-1", 3.0);
            var suppressed = fixture.Component("module-1/suppressed-1", 100.0, parent.Object);
            suppressed.Setup(value => value.GetSuppression2()).Returns((int)swComponentSuppressionState_e.swComponentSuppressed);
            parent.Setup(value => value.GetChildren()).Returns(new object[] { suppressed.Object });
            fixture.Override(suppressed.Object.Name2, true, true, true);

            Assert.False(fixture.Read(parent.Object).HasMassOverride);
        }

        [Fact]
        public void MissingNewApiNeverFallsBackToBodyBasedMassProperties()
        {
            var fixture = new Fixture();
            var part = fixture.Component("part-1", 3.0);
            fixture.Extension.Setup(value => value.CreateMassProperty2()).Returns((object)null);

            Assert.Contains("CreateMassProperty2", Assert.Throws<InvalidOperationException>(() => fixture.Read(part.Object)).Message);
            fixture.Extension.Verify(value => value.CreateMassProperty(), Times.Never);
        }

        [Fact]
        public void FailedRecalculationOrMissingOverrideMetadataNeverProducesSnapshot()
        {
            foreach (bool failRecalculate in new[] { false, true })
            {
                var fixture = new Fixture();
                var part = fixture.Component("part-1", 3.0);
                fixture.ConfigureProperty = (property, index) =>
                {
                    if (failRecalculate) property.Setup(value => value.Recalculate()).Returns(false);
                    else property.Setup(value => value.GetOverrideOptions()).Returns((object)null);
                };

                string message = Assert.Throws<InvalidOperationException>(() => fixture.Read(part.Object)).Message;
                Assert.Contains(failRecalculate ? "Recalculate failed" : "GetOverrideOptions", message);
                Assert.Equal(0, fixture.NumericReads);
            }
        }

        [Fact]
        public void RejectedOrContaminatedSelectionCannotLeakAssemblyMass()
        {
            foreach (bool retainedPreselection in new[] { false, true })
            {
                var fixture = new Fixture();
                var selected = fixture.Component("part-1", 3.0);
                var unrelated = fixture.Component("other-1", 200.0);
                fixture.ConfigureProperty = (property, index) =>
                {
                    if (retainedPreselection || index > 0)
                        property.SetupGet(value => value.SelectedItems).Returns(retainedPreselection
                            ? new object[] { unrelated.Object } : null);
                };

                Assert.Contains("SelectedItems", Assert.Throws<InvalidOperationException>(
                    () => fixture.Read(selected.Object)).Message);
                Assert.Equal(0, fixture.NumericReads);
            }
        }

        [Fact]
        public void SameNameWithWrongReferencedConfigurationIsRejected()
        {
            var fixture = new Fixture();
            var selected = fixture.Component("part-1", 3.0);
            var wrongConfiguration = new Mock<Component2>(MockBehavior.Strict);
            wrongConfiguration.SetupGet(value => value.Name2).Returns("part-1");
            wrongConfiguration.SetupGet(value => value.ReferencedConfiguration).Returns("Wrong configuration");
            fixture.ConfigureProperty = (property, index) =>
            {
                if (index == 1)
                    property.SetupGet(value => value.SelectedItems).Returns(new object[] { wrongConfiguration.Object });
            };

            Assert.Contains("referenced configuration", Assert.Throws<InvalidOperationException>(
                () => fixture.Read(selected.Object)).Message);
            Assert.Equal(0, fixture.NumericReads);
        }

        [Fact]
        public void ComFailurePreservesDiagnosticAndDoesNotFallback()
        {
            var fixture = new Fixture();
            var part = fixture.Component("part-1", 3.0);
            var cause = new COMException("mock COM read failure", unchecked((int)0x80004005));
            fixture.ConfigureProperty = (property, index) => property.Setup(value => value.Recalculate()).Throws(cause);

            var error = Assert.Throws<InvalidOperationException>(() => fixture.Read(part.Object));

            Assert.Same(cause, error.InnerException);
            Assert.Contains("80004005", error.Message);
            Assert.Contains("No body-only fallback", error.Message);
        }

        [Fact]
        public void InvalidNumericResultsAreRejectedWithoutDensityCorrections()
        {
            for (int invalid = 0; invalid < 5; invalid++)
            {
                var fixture = new Fixture();
                var part = fixture.Component("part-1", 3.0);
                fixture.ConfigureProperty = (property, index) =>
                {
                    if (invalid == 0) property.SetupGet(value => value.Mass).Returns(double.NaN);
                    if (invalid == 1) property.SetupGet(value => value.Mass).Returns(0.0);
                    if (invalid == 2) property.SetupGet(value => value.CenterOfMass).Returns(new double[2]);
                    if (invalid == 3) property.SetupGet(value => value.CenterOfMass).Returns(new[] { 0.0, double.PositiveInfinity, 0.0 });
                    if (invalid == 4) property.Setup(value => value.GetMomentOfInertia(It.IsAny<int>())).Returns((object)null);
                };

                Assert.Throws<InvalidOperationException>(() => fixture.Read(part.Object));
            }
        }

        [Fact]
        public void FrameConversionPreservesOverrideFlagsAndUnchangedMomentConvention()
        {
            var source = new MassPropertySnapshot(2.0, new[] { 1.0, 2.0, 3.0 }, Fixture.Moment, true, false, true);
            Matrix<double> frame = Matrix<double>.Build.DenseIdentity(4);
            frame[0, 3] = 0.5;

            MassPropertySnapshot converted = MassPropertyFrameConverter.Convert(source, Matrix<double>.Build.DenseIdentity(4), frame);

            Assert.True(converted.HasMassOverride);
            Assert.False(converted.HasCenterOfMassOverride);
            Assert.True(converted.HasInertiaOverride);
            Assert.Equal(Fixture.Moment, converted.Moment);
            Assert.Equal(0.5, converted.CenterOfMass[0]);
            var legacy = new MassPropertySnapshot(2.0, new double[3], Fixture.Moment);
            Assert.False(legacy.HasMassOverride);
            Assert.False(legacy.HasCenterOfMassOverride);
            Assert.False(legacy.HasInertiaOverride);
        }

        private sealed class Fixture
        {
            public static readonly double[] Moment = { 0.12, -0.01, 0.02, -0.01, 0.18, 0.03, 0.02, 0.03, 0.21 };
            public readonly Mock<ModelDoc2> Assembly = new Mock<ModelDoc2>(MockBehavior.Strict);
            public readonly Mock<ModelDocExtension> Extension = new Mock<ModelDocExtension>(MockBehavior.Strict);
            public readonly Mock<Configuration> Configuration = new Mock<Configuration>();
            public readonly List<Mock<IMassProperty2>> Properties = new List<Mock<IMassProperty2>>();
            public Action<Mock<IMassProperty2>, int> ConfigureProperty;
            public Func<Component2[], double> EffectiveMass;
            public Component2[] LastScope;
            public int NumericReads;
            public bool AllowWholeDocument;
            private readonly Dictionary<string, double> masses = new Dictionary<string, double>();
            private readonly Dictionary<string, IMassPropertyOverrideOptions> overrides = new Dictionary<string, IMassPropertyOverrideOptions>();

            public Fixture()
            {
                Configuration.SetupGet(value => value.Name).Returns("Export configuration");
                var manager = new Mock<ConfigurationManager>();
                manager.SetupGet(value => value.ActiveConfiguration).Returns(Configuration.Object);
                Assembly.Setup(value => value.GetType()).Returns((int)swDocumentTypes_e.swDocASSEMBLY);
                Assembly.SetupGet(value => value.ConfigurationManager).Returns(manager.Object);
                Assembly.SetupGet(value => value.Extension).Returns(Extension.Object);
                Extension.Setup(value => value.CreateMassProperty2()).Returns(() => CreateProperty().Object);
                EffectiveMass = components => components.Sum(component => masses[component.Name2]);
            }

            public Mock<Component2> Component(string name, double mass, Component2 parent = null)
            {
                var component = new Mock<Component2>(MockBehavior.Strict);
                component.SetupGet(value => value.Name2).Returns(name);
                component.SetupGet(value => value.ReferencedConfiguration).Returns("Referenced configuration");
                component.Setup(value => value.GetParent()).Returns(parent);
                component.Setup(value => value.GetChildren()).Returns((object)null);
                component.Setup(value => value.IsRoot()).Returns(false);
                component.Setup(value => value.GetSuppression2()).Returns((int)swComponentSuppressionState_e.swComponentFullyResolved);
                masses.Add(name, mass);
                return component;
            }

            public void Override(string name, bool mass, bool center, bool inertia)
            {
                var options = new Mock<IMassPropertyOverrideOptions>(MockBehavior.Strict);
                options.SetupGet(value => value.OverrideMass).Returns(mass);
                options.SetupGet(value => value.OverrideCenterOfMass).Returns(center);
                options.SetupGet(value => value.OverrideMomentsOfInertia).Returns(inertia);
                overrides[name] = options.Object;
            }

            public MassPropertySnapshot Read(params Component2[] components)
            {
                return SolidWorksMassPropertyReader.Read(Assembly.Object, components);
            }

            private Mock<IMassProperty2> CreateProperty()
            {
                var property = new Mock<IMassProperty2>(MockBehavior.Strict);
                Component2[] scope = new Component2[0];
                bool readCenter = false;
                bool recalculated = false;
                property.SetupProperty(value => value.UseSystemUnits, false);
                property.SetupProperty(value => value.IncludeHiddenBodiesOrComponents, false);
                property.SetupSet(value => value.SelectedItems = It.IsAny<object>()).Callback<object>(value =>
                {
                    scope = value == null ? new Component2[0] : ((Array)value).Cast<Component2>().ToArray();
                    LastScope = scope;
                });
                property.SetupGet(value => value.SelectedItems).Returns(() => scope.Cast<object>().ToArray());
                property.Setup(value => value.Recalculate()).Returns(() =>
                {
                    Assert.True(property.Object.UseSystemUnits);
                    Assert.True(property.Object.IncludeHiddenBodiesOrComponents);
                    recalculated = true;
                    return true;
                });
                property.Setup(value => value.GetOverrideOptions()).Returns(() =>
                {
                    Assert.False(recalculated);
                    Assert.True(scope.Length <= 1);
                    string name = scope.Length == 0 ? string.Empty : scope[0].Name2;
                    if (!overrides.ContainsKey(name)) Override(name, false, false, false);
                    return overrides[name];
                });
                property.SetupGet(value => value.CenterOfMass).Returns(() =>
                {
                    Assert.True(AllowWholeDocument || scope.Length > 0);
                    Assert.True(recalculated);
                    readCenter = true;
                    NumericReads++;
                    return new[] { 0.12, -0.23, 0.34 };
                });
                property.SetupGet(value => value.Mass).Returns(() =>
                {
                    Assert.True(AllowWholeDocument || scope.Length > 0);
                    Assert.True(recalculated);
                    NumericReads++;
                    return scope.Length == 0 ? 123.0 : EffectiveMass(scope);
                });
                property.Setup(value => value.GetMomentOfInertia((int)swMassPropertyMoment_e.swMassPropertyMomentAboutCenterOfMass)).Returns(() =>
                {
                    Assert.True(AllowWholeDocument || scope.Length > 0);
                    Assert.True(recalculated);
                    Assert.False(readCenter);
                    NumericReads++;
                    return Moment;
                });
                if (ConfigureProperty != null) ConfigureProperty(property, Properties.Count);
                Properties.Add(property);
                return property;
            }
        }
    }
}
