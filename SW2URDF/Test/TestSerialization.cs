using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SW2URDF.Test
{
    public class TestSerialization
    {
        [Fact]
        public void VersionTwoRoundTripPreservesPersistentReferencesAndUnicodeConfiguration()
        {
            LinkNode root = new LinkNode { IsBaseNode = true };
            root.Link.Name = "base_link";
            root.Link.FrameReference = CadFeatureReference.ExplicitComponent(
                ReferenceGeometryKind.CoordinateSystem,
                new byte[] { 1, 2, 3 },
                new byte[] { 4, 5, 6 },
                "中文配置");

            LinkNode child = new LinkNode();
            child.Link.Name = "wheel_link";
            child.Link.Joint.Name = "wheel_joint";
            child.Link.Joint.Type = "continuous";
            child.Link.FrameReference = CadFeatureReference.ExplicitComponent(
                ReferenceGeometryKind.CoordinateSystem,
                new byte[] { 7, 8 },
                new byte[] { 9, 10 },
                "默认");
            child.Link.Joint.AxisReference = CadFeatureReference.ExplicitComponent(
                ReferenceGeometryKind.Axis,
                new byte[] { 7, 8 },
                new byte[] { 11, 12 },
                "默认");
            root.Nodes.Add(child);

            string payload = ConfigurationSerialization.SerializeDraftPayload(root);
            LinkNode restored = ConfigurationSerialization.DeserializeDraftPayload(payload);

            Assert.NotNull(restored);
            Assert.Equal(root.Link.FrameReference, restored.Link.FrameReference);
            Assert.Equal(
                ReferenceGeometryOwnerScope.ComponentInstance,
                restored.Link.FrameReference.OwnerScope);
            LinkNode restoredChild = (LinkNode)restored.Nodes[0];
            Assert.Equal(child.Link.FrameReference, restoredChild.Link.FrameReference);
            Assert.Equal(child.Link.Joint.AxisReference, restoredChild.Link.Joint.AxisReference);
            Assert.Contains("中文配置", payload);
        }

        [Fact]
        public void ExplicitReferenceOwnerScopeEnforcesPersistentIdShape()
        {
            CadFeatureReference root = CadFeatureReference.ExplicitRoot(
                ReferenceGeometryKind.CoordinateSystem,
                new byte[] { 1 });
            CadFeatureReference component =
                CadFeatureReference.ExplicitComponent(
                    ReferenceGeometryKind.CoordinateSystem,
                    new byte[] { 2 },
                    new byte[] { 1 },
                    "default");

            Assert.Equal(
                ReferenceGeometryOwnerScope.RootDocument,
                root.OwnerScope);
            Assert.Equal(
                ReferenceGeometryOwnerScope.ComponentInstance,
                component.OwnerScope);
            Assert.NotEqual(root.IdentityKey, component.IdentityKey);
            Assert.Throws<ArgumentException>(() =>
                CadFeatureReference.ExplicitComponent(
                    ReferenceGeometryKind.Axis,
                    null,
                    new byte[] { 1 },
                    "default"));
        }

        [Fact]
        public void PersistentIdentityDoesNotStoreReferenceGeometryDisplayNames()
        {
            CadFeatureReference reference = CadFeatureReference.ExplicitComponent(
                ReferenceGeometryKind.CoordinateSystem,
                new byte[] { 1 },
                new byte[] { 2 },
                "配置");

            Assert.DoesNotContain("坐标系1", reference.IdentityKey);
            Assert.DoesNotContain("Coordinate System1", reference.IdentityKey);
        }

        [Fact]
        public void PersistentIdArraysCannotMutateReferenceIdentity()
        {
            CadFeatureReference reference = CadFeatureReference.ExplicitComponent(
                ReferenceGeometryKind.Axis,
                new byte[] { 1, 2 },
                new byte[] { 3, 4 },
                "default");
            string identity = reference.IdentityKey;

            reference.ComponentPersistentId[0] = 99;
            reference.FeaturePersistentId[0] = 98;

            Assert.Equal(identity, reference.IdentityKey);
            Assert.Equal((byte)1, reference.ComponentPersistentId[0]);
            Assert.Equal((byte)3, reference.FeaturePersistentId[0]);
        }

        [Fact]
        public void ReferencedConfigurationIsMetadataRatherThanReferenceIdentity()
        {
            CadFeatureReference original = CadFeatureReference.ExplicitComponent(
                ReferenceGeometryKind.Axis,
                new byte[] { 1, 2 },
                new byte[] { 3, 4 },
                "old_configuration");
            CadFeatureReference renamedConfiguration = CadFeatureReference.ExplicitComponent(
                ReferenceGeometryKind.Axis,
                new byte[] { 1, 2 },
                new byte[] { 3, 4 },
                "renamed_configuration");

            Assert.Equal(original, renamedConfiguration);
            Assert.Equal(original.GetHashCode(), renamedConfiguration.GetHashCode());
            Assert.Equal(original.IdentityKey, renamedConfiguration.IdentityKey);
        }

        [Theory]
        [InlineData("FrameReference")]
        [InlineData("AxisReference")]
        public void VersionTwoPayloadRejectsMissingPersistentReferenceField(
            string elementName)
        {
            LinkNode root = new LinkNode { IsBaseNode = true };
            root.Link.Name = "base_link";
            string payload = ConfigurationSerialization.SerializeDraftPayload(root);
            XDocument document = XDocument.Parse(payload);
            document.Descendants()
                .First(element => element.Name.LocalName == elementName)
                .Remove();

            LinkNode restored = ConfigurationSerialization.DeserializeDraftPayload(
                document.ToString(SaveOptions.DisableFormatting));

            Assert.Null(restored);
        }

        [Theory]
        [InlineData("OwnerScope")]
        [InlineData("ComponentPersistentId")]
        public void VersionTwoPayloadRejectsIncompleteComponentReference(
            string elementName)
        {
            LinkNode root = new LinkNode { IsBaseNode = true };
            root.Link.Name = "base_link";
            root.Link.FrameReference =
                CadFeatureReference.ExplicitComponent(
                    ReferenceGeometryKind.CoordinateSystem,
                    new byte[] { 1 },
                    new byte[] { 2 },
                    "default");
            string payload =
                ConfigurationSerialization.SerializeDraftPayload(root);
            XDocument document = XDocument.Parse(payload);
            document.Descendants()
                .First(element => element.Name.LocalName == elementName)
                .Remove();

            LinkNode restored =
                ConfigurationSerialization.DeserializeDraftPayload(
                    document.ToString(SaveOptions.DisableFormatting));

            Assert.Null(restored);
        }

        [Fact]
        public void VersionTwoSerializationRejectsReferenceWithWrongGeometryKind()
        {
            LinkNode root = new LinkNode { IsBaseNode = true };
            root.Link.Name = "base_link";
            root.Link.FrameReference = CadFeatureReference.Automatic(
                ReferenceGeometryKind.Axis);

            Assert.Equal(
                string.Empty,
                ConfigurationSerialization.SerializeDraftPayload(root));
        }
    }

    [Collection("Requires SW Test Collection")]
    public class TestLegacySerializationBoundary : SW2URDFTest
    {
        public TestLegacySerializationBoundary(SWTestFixture fixture) : base(fixture)
        {
        }

        [Theory]
        [InlineData("3_DOF_ARM")]
        [InlineData("4_WHEELER")]
        public void NameBasedConfigurationRequiresExplicitRecreation(string modelName)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);

            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(
                doc,
                out string errorMessage);

            Assert.Null(baseNode);
            Assert.False(String.IsNullOrWhiteSpace(errorMessage));
            Assert.Contains("name-based", errorMessage);
            Assert.True(SwApp.CloseAllDocuments(true));
        }
    }
}
