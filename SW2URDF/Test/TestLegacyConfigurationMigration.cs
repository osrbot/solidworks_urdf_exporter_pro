using Moq;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Xunit;
using Feature = SolidWorks.Interop.sldworks.Feature;
using FeatureManager = SolidWorks.Interop.sldworks.FeatureManager;
using ModelDoc2 = SolidWorks.Interop.sldworks.ModelDoc2;
using SwAttribute = SolidWorks.Interop.sldworks.Attribute;
using SwParameter = SolidWorks.Interop.sldworks.Parameter;

namespace SW2URDF.Test
{
    public class TestLegacyConfigurationMigration
    {
        private const string RecoverySlotName = "URDF Export Configuration (v2 recovery)";

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void PreparedSlotsWithLegacyOfferReviewedMigration(bool canonical, bool recovery)
        {
            string payload = SerializeLegacy(CreateTree());
            var slots = PreparedSlots(canonical, recovery);
            slots.Add(new ReadOnlyConfigurationSlot("URDF Export Configuration (v1.5)", payload, 1.5, null));
            ModelDoc2 model = ConfigurationModel(slots);

            LinkNode loaded = ConfigurationSerialization.LoadBaseNodeFromModel(model, out string error);
            Assert.Null(loaded);
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.True(ConfigurationSerialization.TryReadLegacyConfiguration(model, out string data, out double version));
            Assert.Equal(payload, data);
            Assert.Equal(1.5, version);
            var migration = new LegacyConfigurationMigration(data, version, new ReferenceGeometryEntry[0]);
            Assert.True(migration.IsResolved);
            Assert.Equal(2, migration.LinkCount);
            Assert.Equal("base_link", migration.CreateReviewedTree().Link.Name);
            foreach (var slot in slots)
                slot.VerifyReadOnly();
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void DamagedCommittedSlotPreventsLegacyFallback(bool damagedRecovery, bool addPreparedPeer)
        {
            string damagedName = damagedRecovery
                ? RecoverySlotName
                : ConfigurationSerialization.UrdfConfigurationSwAttributeName;
            var slots = new List<ReadOnlyConfigurationSlot>
            {
                new ReadOnlyConfigurationSlot(damagedName, "<broken>", 2.0, 7.0),
                new ReadOnlyConfigurationSlot("URDF Export Configuration (v1.5)", SerializeLegacy(CreateTree()), 1.5, null)
            };
            if (addPreparedPeer)
                slots.AddRange(PreparedSlots(damagedRecovery, !damagedRecovery));
            ModelDoc2 model = ConfigurationModel(slots);

            Assert.Null(ConfigurationSerialization.LoadBaseNodeFromModel(model, out string error));
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.False(ConfigurationSerialization.TryReadLegacyConfiguration(model, out string data, out double version));
            Assert.Equal(string.Empty, data);
            Assert.Equal(0.0, version);
            foreach (var slot in slots)
                slot.VerifyReadOnly();
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void PreparedSlotsWithoutLegacyDoNotOfferMigration(bool canonical, bool recovery)
        {
            var slots = PreparedSlots(canonical, recovery);
            ModelDoc2 model = ConfigurationModel(slots);

            Assert.Null(ConfigurationSerialization.LoadBaseNodeFromModel(model, out string error));
            Assert.Equal(string.Empty, error);
            Assert.False(ConfigurationSerialization.TryReadLegacyConfiguration(model, out string data, out double version));
            Assert.Equal(string.Empty, data);
            Assert.Equal(0.0, version);
            foreach (var slot in slots)
                slot.VerifyReadOnly();
        }

        private static List<ReadOnlyConfigurationSlot> PreparedSlots(bool canonical, bool recovery)
        {
            var slots = new List<ReadOnlyConfigurationSlot>();
            if (canonical)
                slots.Add(new ReadOnlyConfigurationSlot(
                    ConfigurationSerialization.UrdfConfigurationSwAttributeName, string.Empty, 2.0, 0.0));
            if (recovery)
                slots.Add(new ReadOnlyConfigurationSlot(
                    RecoverySlotName, string.Empty, 2.0, 0.0));
            return slots;
        }

        private static ModelDoc2 ConfigurationModel(IEnumerable<ReadOnlyConfigurationSlot> slots)
        {
            var manager = new Mock<FeatureManager>(MockBehavior.Strict);
            manager.Setup(item => item.GetFeatures(true))
                .Returns(slots.Select(slot => (object)slot.Feature.Object).ToArray());
            var model = new Mock<ModelDoc2>(MockBehavior.Strict);
            model.Setup(item => item.FeatureManager).Returns(manager.Object);
            return model.Object;
        }

        private sealed class ReadOnlyConfigurationSlot
        {
            private readonly Mock<SwAttribute> attribute = new Mock<SwAttribute>(MockBehavior.Strict);
            private readonly List<Mock<SwParameter>> parameters = new List<Mock<SwParameter>>();
            internal readonly Mock<Feature> Feature = new Mock<Feature>(MockBehavior.Strict);

            internal ReadOnlyConfigurationSlot(string name, string payload, double version, double? revision)
            {
                attribute.Setup(item => item.GetName()).Returns(name);
                attribute.Setup(item => item.GetParameter(It.IsAny<string>())).Returns((SwParameter)null);
                AddParameter("data", payload, 0);
                AddParameter("name", "config1", 0);
                AddParameter("date", "2026-09-04T00:00:00.0000000+00:00", 0);
                AddParameter("exporterVersion", string.Empty, version);
                if (revision.HasValue)
                    AddParameter("revision", string.Empty, revision.Value);
                Feature.Setup(item => item.GetTypeName2()).Returns("Attribute");
                Feature.SetupGet(item => item.Name).Returns(name);
                Feature.Setup(item => item.GetSpecificFeature2()).Returns(attribute.Object);
            }

            private void AddParameter(string name, string text, double number)
            {
                var parameter = new Mock<SwParameter>(MockBehavior.Strict);
                parameter.Setup(item => item.GetStringValue()).Returns(text);
                parameter.Setup(item => item.GetDoubleValue()).Returns(number);
                attribute.Setup(item => item.GetParameter(name)).Returns(parameter.Object);
                parameters.Add(parameter);
            }

            internal void VerifyReadOnly()
            {
                attribute.Verify(item => item.Delete(It.IsAny<bool>()), Times.Never());
                foreach (var parameter in parameters)
                {
                    parameter.Verify(item => item.SetStringValue2(
                        It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never());
                    parameter.Verify(item => item.SetDoubleValue2(
                        It.IsAny<double>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never());
                }
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void OriginSetElementCopiesCustomizationAndIndependentCoordinates(bool customized)
        {
            var source = new Origin(false) { isCustomized = customized };
            source.SetXYZ(new double[] { 0.125, -0.25, 0.5 });
            source.SetRPY(new double[] { -0.75, 1.25, -1.5 });
            var target = new Origin(false) { isCustomized = !customized };

            target.SetElement(source);

            Assert.Equal(customized, target.isCustomized);
            Assert.Equal(source.GetXYZ(), target.GetXYZ());
            Assert.Equal(source.GetRPY(), target.GetRPY());
            target.X = 99;
            target.Yaw = 88;
            target.isCustomized = !customized;
            Assert.Equal(0.125, source.X);
            Assert.Equal(-1.5, source.Yaw);
            Assert.Equal(customized, source.isCustomized);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LinkClonePreservesAllOriginCustomizationWithoutAliasing(bool customized)
        {
            Link root = CreateTree();
            root.Children[0].AdditionalCollisions.Add(new Collision());
            var sourceOrigins = new[]
            {
                root.Joint.Origin,
                root.Children[0].Joint.Origin,
                root.Children[0].Inertial.Origin,
                root.Children[0].Visual.Origin,
                root.Children[0].Collision.Origin,
                root.Children[0].AdditionalCollisions[0].Origin
            };
            for (int index = 0; index < sourceOrigins.Length; index++)
            {
                sourceOrigins[index].isCustomized = customized;
                sourceOrigins[index].SetXYZ(new double[] { index + 0.125, -0.25, 0.5 });
                sourceOrigins[index].SetRPY(new double[] { -0.75, 1.25, index + 1.5 });
            }

            Link cloned = root.Clone();
            var clonedOrigins = new[]
            {
                cloned.Joint.Origin,
                cloned.Children[0].Joint.Origin,
                cloned.Children[0].Inertial.Origin,
                cloned.Children[0].Visual.Origin,
                cloned.Children[0].Collision.Origin,
                cloned.Children[0].AdditionalCollisions[0].Origin
            };
            for (int index = 0; index < sourceOrigins.Length; index++)
            {
                Origin source = sourceOrigins[index];
                Origin copy = clonedOrigins[index];
                Assert.NotSame(source, copy);
                Assert.Equal(customized, copy.isCustomized);
                Assert.Equal(source.GetXYZ(), copy.GetXYZ());
                Assert.Equal(source.GetRPY(), copy.GetRPY());
                copy.X = 99;
                copy.Yaw = 88;
                copy.isCustomized = !customized;
                Assert.Equal(index + 0.125, source.X);
                Assert.Equal(index + 1.5, source.Yaw);
                Assert.Equal(customized, source.isCustomized);
            }
        }

        [Fact]
        public void ReviewedTreeAndVersionTwoRoundTripPreserveLegacyData()
        {
            Link original = CreateTree();
            original.Joint.LegacyCoordinateSystemName = " root frame";
            original.Children[0].Joint.LegacyCoordinateSystemName = "wheel frame";
            original.Children[0].Joint.LegacyAxisName = " wheel axis";
            var rootFrame = Entry(" root frame", ReferenceGeometryKind.CoordinateSystem, 1);
            var wheelFrame = Entry("wheel frame", ReferenceGeometryKind.CoordinateSystem, 2);
            var wheelAxis = Entry(" wheel axis", ReferenceGeometryKind.Axis, 3);
            string legacy = SerializeLegacy(original);
            Assert.Contains("CoordinateSystemName", legacy);
            Assert.Contains("AxisName", legacy);
            Assert.DoesNotContain("FrameReference", legacy);
            Assert.DoesNotContain("AxisReference", legacy);

            var migration = new LegacyConfigurationMigration(
                legacy, 1.5, new[] { rootFrame, wheelFrame, wheelAxis });
            Assert.Equal(2, migration.LinkCount);
            Assert.Equal(3, migration.References.Count);
            Assert.True(migration.IsResolved);
            LinkNode reviewed = migration.CreateReviewedTree();
            Assert.True(reviewed.NeedsSaving);
            AssertPreserved(original, reviewed);
            Assert.Equal(rootFrame.Reference, reviewed.Link.FrameReference);
            var wheel = (LinkNode)reviewed.Nodes[0];
            Assert.Equal(wheelFrame.Reference, wheel.Link.FrameReference);
            Assert.Equal(wheelAxis.Reference, wheel.Link.Joint.AxisReference);
            Assert.Null(wheel.Link.Joint.LegacyCoordinateSystemName);
            Assert.Null(wheel.Link.Joint.LegacyAxisName);

            string versionTwo = ConfigurationSerialization.SerializeDraftPayload(reviewed);
            Assert.False(string.IsNullOrWhiteSpace(versionTwo));
            Assert.DoesNotContain("CoordinateSystemName", versionTwo);
            Assert.DoesNotContain("AxisName", versionTwo);
            LinkNode restored = ConfigurationSerialization.DeserializeDraftPayload(versionTwo);
            Assert.NotNull(restored);
            AssertPreserved(original, restored);
            Assert.Equal(rootFrame.Reference, restored.Link.FrameReference);
            Assert.Equal(wheelFrame.Reference, ((LinkNode)restored.Nodes[0]).Link.FrameReference);
            Assert.Equal(wheelAxis.Reference, ((LinkNode)restored.Nodes[0]).Link.Joint.AxisReference);
        }

        [Fact]
        public void RootJointNormalizationDoesNotClearMigratedRootFrame()
        {
            Link root = CreateTree();
            root.Joint.Name = "hidden_joint";
            root.Joint.Type = "continuous";
            root.Joint.LegacyCoordinateSystemName = "root";
            root.Joint.LegacyAxisName = "axis";
            var frame = Entry("root", ReferenceGeometryKind.CoordinateSystem, 1);
            var axis = Entry("axis", ReferenceGeometryKind.Axis, 2);
            var migration = new LegacyConfigurationMigration(
                SerializeLegacy(root), 1.5, new[] { frame, axis });

            LinkNode reviewed = migration.CreateReviewedTree();
            Assert.True(reviewed.IsBaseNode);
            Assert.Equal(string.Empty, reviewed.Link.Joint.Name);
            Assert.Equal(string.Empty, reviewed.Link.Joint.Type);
            Assert.Equal(frame.Reference, reviewed.Link.FrameReference);
            Assert.Equal(ReferenceSelectionMode.None, reviewed.Link.Joint.AxisReference.Mode);
            Assert.Equal("wheel_joint", ((LinkNode)reviewed.Nodes[0]).Link.Joint.Name);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void EmptyLegacyAxisPreservesNumericDirectionAsNone(string axisName)
        {
            Link root = CreateTree();
            root.Children[0].Joint.LegacyAxisName = axisName;
            var migration = new LegacyConfigurationMigration(
                SerializeLegacy(root), 1.5, new ReferenceGeometryEntry[0]);
            Assert.True(migration.IsResolved);
            Assert.Empty(migration.References);
            LinkNode reviewed = migration.CreateReviewedTree();
            var child = (LinkNode)reviewed.Nodes[0];
            Assert.Equal(CadFeatureReference.None(ReferenceGeometryKind.Axis), child.Link.Joint.AxisReference);
            Assert.Equal(new double[] { 0, -1, 0 }, child.Link.Joint.Axis.GetXYZ());
            LinkNode restored = ConfigurationSerialization.DeserializeDraftPayload(
                ConfigurationSerialization.SerializeDraftPayload(reviewed));
            Assert.NotNull(restored);
            var restoredChild = (LinkNode)restored.Nodes[0];
            Assert.Equal(ReferenceSelectionMode.None, restoredChild.Link.Joint.AxisReference.Mode);
            Assert.Equal(child.Link.Joint.Axis.GetXYZ(), restoredChild.Link.Joint.Axis.GetXYZ());
        }

        [Theory]
        [InlineData("  frame ")]
        [InlineData(" ")]
        public void RootNamesPreserveWhitespaceAndIgnoreComponentNamesakes(string name)
        {
            Link root = CreateTree();
            root.Joint.LegacyCoordinateSystemName = name;
            var exact = Entry(name, ReferenceGeometryKind.CoordinateSystem, 1);
            var trimmed = Entry(name.Trim(), ReferenceGeometryKind.CoordinateSystem, 2);
            var component = Entry(name, ReferenceGeometryKind.CoordinateSystem, 3, "assembly-1/part-1");
            var migration = new LegacyConfigurationMigration(
                SerializeLegacy(root), 1.5, new[] { trimmed, component, exact });
            LegacyReferenceSelection selection = Assert.Single(migration.References);
            Assert.Equal(name, selection.LegacyName);
            Assert.Equal("base_link", selection.LinkName);
            Assert.Same(exact, selection.Selected);
            Assert.True(migration.IsResolved);
        }

        [Fact]
        public void MissingNameRemainsUnresolvedUntilExplicitSelection()
        {
            Link root = CreateTree();
            root.Joint.LegacyCoordinateSystemName = "Missing";
            var alternative = Entry("missing", ReferenceGeometryKind.CoordinateSystem, 1);
            var migration = new LegacyConfigurationMigration(
                SerializeLegacy(root), 1.5, new[] { alternative });
            var selection = Assert.Single(migration.References);
            Assert.Null(selection.Selected);
            Assert.False(migration.IsResolved);
            Assert.Throws<InvalidOperationException>(() => migration.CreateReviewedTree());
            Assert.Contains(alternative, selection.Choices);
            selection.Selected = alternative;
            Assert.True(migration.IsResolved);
            Assert.Equal(alternative.Reference, migration.CreateReviewedTree().Link.FrameReference);
        }

        [Fact]
        public void DuplicateNamesRequireExplicitResolutionAndWrongKindIsNotAChoice()
        {
            Link root = CreateTree();
            root.Children[0].Joint.LegacyAxisName = "axis";
            var first = Entry("axis", ReferenceGeometryKind.Axis, 1);
            var second = Entry("axis", ReferenceGeometryKind.Axis, 2);
            var wrongKind = Entry("axis", ReferenceGeometryKind.CoordinateSystem, 3);
            var migration = new LegacyConfigurationMigration(
                SerializeLegacy(root), 1.5, new[] { first, wrongKind, second });
            var selection = Assert.Single(migration.References);
            Assert.Equal(ReferenceGeometryKind.Axis, selection.Kind);
            Assert.Null(selection.Selected);
            Assert.Equal(2, selection.Choices.Count);
            Assert.DoesNotContain(wrongKind, selection.Choices);
            selection.Selected = wrongKind;
            Assert.False(migration.IsResolved);
            Assert.Throws<InvalidOperationException>(() => migration.CreateReviewedTree());
            selection.Selected = second;
            Assert.True(migration.IsResolved);
            Assert.Equal(second.Reference,
                ((LinkNode)migration.CreateReviewedTree().Nodes[0]).Link.Joint.AxisReference);
        }

        [Fact]
        public void SameNameOfWrongKindDoesNotResolveReference()
        {
            Link root = CreateTree();
            root.Joint.LegacyCoordinateSystemName = "frame";
            var migration = new LegacyConfigurationMigration(SerializeLegacy(root), 1.5,
                new[] { Entry("frame", ReferenceGeometryKind.Axis, 1) });
            Assert.Empty(Assert.Single(migration.References).Choices);
            Assert.False(migration.IsResolved);
            Assert.Throws<InvalidOperationException>(() => migration.CreateReviewedTree());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void QualifiedNameMatchesExactComponentPathNotBasename(bool includeExact)
        {
            Link root = CreateTree();
            root.Children[0].Joint.LegacyCoordinateSystemName = " frame <assembly-1/part-1>";
            var exact = Entry("frame", ReferenceGeometryKind.CoordinateSystem, 1, "assembly-1/part-1");
            var entries = new[]
            {
                Entry("frame", ReferenceGeometryKind.CoordinateSystem, 2, "part-1"),
                Entry("frame", ReferenceGeometryKind.CoordinateSystem, 3, "other-1/part-1"),
                Entry("frame", ReferenceGeometryKind.CoordinateSystem, 4, "Assembly-1/part-1"),
                Entry("frame", ReferenceGeometryKind.CoordinateSystem, 5)
            }.ToList();
            if (includeExact)
                entries.Add(exact);
            var migration = new LegacyConfigurationMigration(SerializeLegacy(root), 1.5, entries);
            var selection = Assert.Single(migration.References);
            Assert.Equal(includeExact, migration.IsResolved);
            if (includeExact)
            {
                Assert.Same(exact, selection.Selected);
                Assert.Equal(exact.Reference,
                    ((LinkNode)migration.CreateReviewedTree().Nodes[0]).Link.FrameReference);
            }
            else
            {
                Assert.Null(selection.Selected);
                Assert.Throws<InvalidOperationException>(() => migration.CreateReviewedTree());
            }
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(1.4)]
        [InlineData(1.6)]
        [InlineData(2.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void OnlyVersionOnePointFiveIsAccepted(double version)
        {
            Assert.Throws<SerializationException>(() => new LegacyConfigurationMigration(
                SerializeLegacy(CreateTree()), version, new ReferenceGeometryEntry[0]));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("<Link>")]
        [InlineData("<wrong />")]
        [InlineData("<!DOCTYPE Link [<!ENTITY value 'blocked'>]><Link>&value;</Link>")]
        public void MalformedOrEmptyPayloadIsRejected(string payload)
        {
            Exception error = Record.Exception(() => new LegacyConfigurationMigration(
                payload, 1.5, new ReferenceGeometryEntry[0]));
            Assert.NotNull(error);
            Assert.True(error is SerializationException || error is XmlException, error.ToString());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DuplicateLinkNamesOrBrokenParentRelationshipAreRejected(bool duplicateName)
        {
            Link root = CreateTree();
            if (duplicateName)
                root.Children[0].Name = root.Name;
            else
                root.Children[0].Parent = null;
            Assert.Throws<SerializationException>(() => new LegacyConfigurationMigration(
                SerializeLegacy(root), 1.5, new ReferenceGeometryEntry[0]));
        }

        private static ReferenceGeometryEntry Entry(
            string name, ReferenceGeometryKind kind, byte id, string componentPath = null)
        {
            CadFeatureReference reference = componentPath == null
                ? CadFeatureReference.ExplicitRoot(kind, new byte[] { id })
                : CadFeatureReference.ExplicitComponent(kind, new byte[] { id, 100 },
                    new byte[] { id }, "default");
            return new ReferenceGeometryEntry(reference, name, componentPath ?? string.Empty);
        }

        private static Link CreateTree()
        {
            var root = new Link { Name = "base_link" };
            root.SWComponentPIDs.Add(new byte[] { 0, 128, 255 });
            root.SWMainComponentPID = new byte[] { 0, 128, 255 };
            var child = new Link(root) { Name = "wheel_link", STLQualityFine = true,
                MeshReductionRatio = 0.35, CollisionMeshStrategy = CollisionMeshStrategy.BoxPrimitive };
            child.SWComponentPIDs.Add(new byte[] { 4, 0, 254 });
            child.SWComponentPIDs.Add(new byte[] { 5, 128, 253 });
            child.SWMainComponentPID = new byte[] { 5, 128, 253 };
            child.Joint.Name = "wheel_joint";
            child.Joint.Type = "revolute";
            child.Joint.Axis.SetXYZ(new double[] { 0, -1, 0 });
            child.Joint.Origin.X = 0.123456789;
            child.Joint.Origin.Yaw = -0.75;
            child.Joint.Origin.isCustomized = true;
            child.Joint.Limit.Lower = -1.25;
            child.Joint.Limit.Upper = 2.5;
            child.Joint.Limit.Effort = 12;
            child.Joint.Limit.Velocity = 3;
            child.Inertial.Mass.Value = 4.125;
            child.Inertial.Inertia.Ixx = 0.0023456789;
            root.Children.Add(child);
            return root;
        }

        private static void AssertPreserved(Link original, LinkNode actual)
        {
            Assert.Equal(original.Name, actual.Link.Name);
            Assert.Equal(original.SWMainComponentPID, actual.Link.SWMainComponentPID);
            Assert.Equal(original.SWComponentPIDs[0], actual.Link.SWComponentPIDs[0]);
            Assert.Single(actual.Nodes.Cast<LinkNode>());
            Link expected = original.Children[0];
            Link child = ((LinkNode)actual.Nodes[0]).Link;
            Assert.Equal(expected.Name, child.Name);
            Assert.Equal(expected.SWMainComponentPID, child.SWMainComponentPID);
            Assert.Equal(expected.SWComponentPIDs.Count, child.SWComponentPIDs.Count);
            for (int index = 0; index < expected.SWComponentPIDs.Count; index++)
                Assert.Equal(expected.SWComponentPIDs[index], child.SWComponentPIDs[index]);
            Assert.Equal(expected.STLQualityFine, child.STLQualityFine);
            Assert.Equal(expected.MeshReductionRatio, child.MeshReductionRatio);
            Assert.Equal(expected.CollisionMeshStrategy, child.CollisionMeshStrategy);
            Assert.Equal(expected.Joint.Name, child.Joint.Name);
            Assert.Equal(expected.Joint.Type, child.Joint.Type);
            Assert.Equal(expected.Joint.Axis.GetXYZ(), child.Joint.Axis.GetXYZ());
            Assert.Equal(expected.Joint.Origin.X, child.Joint.Origin.X);
            Assert.Equal(expected.Joint.Origin.Yaw, child.Joint.Origin.Yaw);
            Assert.Equal(expected.Joint.Origin.isCustomized, child.Joint.Origin.isCustomized);
            Assert.Equal(expected.Joint.Limit.Lower, child.Joint.Limit.Lower);
            Assert.Equal(expected.Joint.Limit.Upper, child.Joint.Limit.Upper);
            Assert.Equal(expected.Joint.Limit.Effort, child.Joint.Limit.Effort);
            Assert.Equal(expected.Joint.Limit.Velocity, child.Joint.Limit.Velocity);
            Assert.Equal(expected.Inertial.Mass.Value, child.Inertial.Mass.Value);
            Assert.Equal(expected.Inertial.Inertia.Ixx, child.Inertial.Inertia.Ixx);
        }

        private static string SerializeLegacy(Link root)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractSerializer(typeof(Link)).WriteObject(stream, root);
                var document = XDocument.Parse(Encoding.UTF8.GetString(stream.ToArray()),
                    LoadOptions.PreserveWhitespace);
                // Retain serializer-generated object IDs and backreferences, removing only newer members.
                string[] newerMembers = { "FrameReference", "AxisReference", "ConfigurationSource",
                    "ConfigurationEvidence", "ConfigurationUserConfirmed" };
                document.Descendants().Where(element => newerMembers.Contains(element.Name.LocalName))
                    .ToList().ForEach(element => element.Remove());
                return document.ToString(SaveOptions.DisableFormatting);
            }
        }
    }
}
