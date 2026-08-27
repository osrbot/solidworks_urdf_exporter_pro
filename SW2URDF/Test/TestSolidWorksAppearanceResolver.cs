using Moq;
using SolidWorks.Interop.sldworks;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;
using SwTexture = SolidWorks.Interop.sldworks.Texture;

namespace SW2URDF.Test
{
    public class TestSolidWorksAppearanceResolver
    {
        [Fact]
        public void BuiltInMaterialPresetMapsNameToIndependentRgbaCopy()
        {
            Assert.True(MaterialAppearancePresets.TryGet("green", out double[] first));
            Assert.Equal(new[] { 0.05, 0.60, 0.10, 1.0 }, first);

            first[0] = 1.0;
            Assert.True(MaterialAppearancePresets.TryGet("GREEN", out double[] second));
            Assert.Equal(0.05, second[0], 12);
        }

        [Fact]
        public void CustomMaterialIdDoesNotInventAnAppearancePreset()
        {
            Assert.False(MaterialAppearancePresets.TryGet("my_robot_finish", out _));
        }

        [Fact]
        public void ParsesSolidWorksRgbAndTransparency()
        {
            bool parsed = SolidWorksAppearanceResolver.TryParseMaterialValues(
                Values(0.2, 0.4, 0.6, 0.25),
                out double[] rgba);

            Assert.True(parsed);
            Assert.Equal(new[] { 0.2, 0.4, 0.6, 0.75 }, rgba);
        }

        [Fact]
        public void RejectsMissingMalformedAndUnsetMaterialValues()
        {
            Assert.False(SolidWorksAppearanceResolver.TryParseMaterialValues(null, out _));
            Assert.False(SolidWorksAppearanceResolver.TryParseMaterialValues(new double[8], out _));
            Assert.False(SolidWorksAppearanceResolver.TryParseMaterialValues(
                new[] { -1.0, -1.0, -1.0, -1.0, -1.0, -1.0, -1.0, -1.0, -1.0 },
                out _));
        }

        [Fact]
        public void ComponentAppearanceWinsOverEarlierComponentsDocumentAppearance()
        {
            Mock<ModelDoc2> firstDocument = new Mock<ModelDoc2>();
            firstDocument.SetupGet(model => model.MaterialPropertyValues)
                .Returns(Values(1.0, 0.0, 0.0, 0.0));

            Mock<Component2> firstComponent = ComponentWithNoExplicitAppearance();
            firstComponent.Setup(component => component.GetModelDoc2()).Returns(firstDocument.Object);

            Mock<Component2> secondComponent = new Mock<Component2>();
            secondComponent.SetupGet(component => component.MaterialPropertyValues)
                .Returns(Values(0.0, 0.0, 1.0, 0.2));

            Link link = LinkWithComponents(firstComponent.Object, secondComponent.Object);

            Assert.True(SolidWorksAppearanceResolver.Resolve(link));
            Assert.Equal(new[] { 0.0, 0.0, 1.0, 0.8 }, link.Visual.Material.Color.GetColor());
        }

        [Fact]
        public void FallsBackToDocumentAppearance()
        {
            Mock<ModelDoc2> document = new Mock<ModelDoc2>();
            document.SetupGet(model => model.MaterialPropertyValues)
                .Returns(Values(0.1, 0.3, 0.5, 0.4));
            Mock<Component2> component = ComponentWithNoExplicitAppearance();
            component.Setup(item => item.GetModelDoc2()).Returns(document.Object);

            Link link = LinkWithComponents(component.Object);

            Assert.True(SolidWorksAppearanceResolver.Resolve(link));
            Assert.Equal(new[] { 0.1, 0.3, 0.5, 0.6 }, link.Visual.Material.Color.GetColor());
        }

        [Fact]
        public void SkipsComFailuresAndUnresolvedComponents()
        {
            Mock<Component2> broken = new Mock<Component2>();
            broken.SetupGet(component => component.MaterialPropertyValues)
                .Throws(new COMException("unresolved"));
            broken.SetupGet(component => component.ReferencedConfiguration)
                .Throws(new COMException("unresolved"));
            broken.Setup(component => component.GetModelMaterialPropertyValues(It.IsAny<string>()))
                .Throws(new COMException("unresolved"));
            broken.Setup(component => component.GetModelDoc2())
                .Throws(new COMException("unresolved"));

            Mock<Component2> valid = new Mock<Component2>();
            valid.SetupGet(component => component.MaterialPropertyValues)
                .Returns(Values(0.7, 0.6, 0.5, 0.0));

            Link link = LinkWithComponents(null, broken.Object, valid.Object);

            Assert.True(SolidWorksAppearanceResolver.Resolve(link));
            Assert.Equal(new[] { 0.7, 0.6, 0.5, 1.0 }, link.Visual.Material.Color.GetColor());
        }

        [Fact]
        public void TextureMaterialNamesDoNotBecomeBitmapPaths()
        {
            Mock<SwTexture> texture = new Mock<SwTexture>();
            texture.SetupGet(value => value.MaterialName)
                .Returns(@"C:\materials\Brushed Aluminum.p2m");
            Mock<Component2> component = ComponentWithNoExplicitAppearance();
            component.Setup(value => value.GetTexture(It.IsAny<string>())).Returns(texture.Object);

            Link link = LinkWithComponents(component.Object);
            link.Name = "arm link";

            Assert.True(SolidWorksAppearanceResolver.Resolve(link));
            Assert.Equal("material_Brushed_Aluminum", link.Visual.Material.Name);
            Assert.Equal(string.Empty, link.Visual.Material.Texture.wFilename);
        }

        [Fact]
        public void ExistingSolidWorksTextureImageIsLoadedAutomatically()
        {
            string texturePath = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-texture-" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(texturePath, new byte[] { 1, 2, 3 });
            try
            {
                Mock<SwTexture> texture = new Mock<SwTexture>();
                texture.SetupGet(value => value.MaterialName).Returns(texturePath);
                Mock<Component2> component = ComponentWithNoExplicitAppearance();
                component.Setup(value => value.GetTexture(It.IsAny<string>()))
                    .Returns(texture.Object);

                Link link = LinkWithComponents(component.Object);

                Assert.True(SolidWorksAppearanceResolver.Resolve(link));
                Assert.Equal(Path.GetFullPath(texturePath),
                    link.Visual.Material.Texture.wFilename);
            }
            finally
            {
                File.Delete(texturePath);
            }
        }

        [Fact]
        public void LinkNameProvidesStableMaterialNameWithoutTexture()
        {
            Link link = LinkWithComponents();
            link.Name = "left arm/link";

            Assert.False(SolidWorksAppearanceResolver.Resolve(link));
            Assert.Equal("material_left_arm_link", link.Visual.Material.Name);
            Assert.Equal(string.Empty, link.Visual.Material.Texture.wFilename);
        }

        [Fact]
        public void ResolveIfUnsetPreservesUserMaterialOverride()
        {
            Mock<Component2> component = new Mock<Component2>();
            component.SetupGet(value => value.MaterialPropertyValues)
                .Returns(Values(1.0, 0.0, 0.0, 0.0));
            Link link = LinkWithComponents(component.Object);
            link.Visual.Material.Name = "user_material";
            link.Visual.Material.Color.SetColor(new[] { 0.1, 0.2, 0.3, 0.4 });

            Assert.False(SolidWorksAppearanceResolver.ResolveIfUnset(link));
            Assert.Equal("user_material", link.Visual.Material.Name);
            Assert.Equal(
                new[] { 0.1, 0.2, 0.3, 0.4 },
                link.Visual.Material.Color.GetColor());
        }

        [Fact]
        public void ResolveIfUnsetPreservesUserColorWithoutMaterialName()
        {
            Mock<Component2> component = new Mock<Component2>();
            component.SetupGet(value => value.MaterialPropertyValues)
                .Returns(Values(1.0, 0.0, 0.0, 0.0));
            Link link = LinkWithComponents(component.Object);
            link.Visual.Material.Color.SetColor(new[] { 0.1, 0.2, 0.3, 0.4 });

            Assert.False(SolidWorksAppearanceResolver.ResolveIfUnset(link));
            Assert.Equal(new[] { 0.1, 0.2, 0.3, 0.4 },
                link.Visual.Material.Color.GetColor());
        }

        [Fact]
        public void ResolveIfUnsetPreservesUserTextureWithoutMaterialName()
        {
            Link link = LinkWithComponents();
            link.Visual.Material.Texture.wFilename = @"C:\textures\user.png";

            Assert.False(SolidWorksAppearanceResolver.ResolveIfUnset(link));
            Assert.Equal(@"C:\textures\user.png", link.Visual.Material.Texture.wFilename);
        }

        [Fact]
        public void ResolveIfUnsetRefreshesAutomaticallyResolvedAppearance()
        {
            Mock<Component2> component = new Mock<Component2>();
            component.SetupGet(value => value.MaterialPropertyValues)
                .Returns(Values(1.0, 0.0, 0.0, 0.0));
            Link link = LinkWithComponents(component.Object);

            Assert.True(SolidWorksAppearanceResolver.Resolve(link));
            Assert.True(link.Visual.Material.AppearanceAutomaticallyResolved);

            component.SetupGet(value => value.MaterialPropertyValues)
                .Returns(Values(0.0, 0.0, 1.0, 0.0));
            Assert.True(SolidWorksAppearanceResolver.ResolveIfUnset(link));
            Assert.Equal(new[] { 0.0, 0.0, 1.0, 1.0 },
                link.Visual.Material.Color.GetColor());
        }

        private static Mock<Component2> ComponentWithNoExplicitAppearance()
        {
            Mock<Component2> component = new Mock<Component2>();
            component.SetupGet(value => value.MaterialPropertyValues).Returns((object)null);
            component.SetupGet(value => value.ReferencedConfiguration).Returns("Default");
            component.Setup(value => value.GetModelMaterialPropertyValues(It.IsAny<string>()))
                .Returns((object)null);
            return component;
        }

        private static Link LinkWithComponents(params Component2[] components)
        {
            Link link = new Link();
            link.Name = "test_link";
            link.SWComponents.AddRange(components);
            return link;
        }

        private static double[] Values(double red, double green, double blue, double transparency)
        {
            return new[]
            {
                red, green, blue,
                0.5, 0.5, 0.5, 0.5,
                transparency,
                0.0
            };
        }
    }
}
