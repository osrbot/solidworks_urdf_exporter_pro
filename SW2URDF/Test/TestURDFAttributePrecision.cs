using SW2URDF.URDF;
using System;
using System.IO;
using System.Xml;
using Xunit;

namespace SW2URDF.Test
{
    public class TestURDFAttributePrecision
    {
        [Fact]
        public void TestWriteURDFKeepsDoublePrecision()
        {
            const double expected = 3.998552042E-4;
            URDFAttribute attribute = new URDFAttribute("ixx", true, expected);

            XmlDocument document = LoadAttributeXml(attribute);
            string text = document.DocumentElement.GetAttribute("ixx");
            double actual = Double.Parse(
                text,
                URDFAttribute.URDFNumberStyle,
                URDFAttribute.URDFNumberFormat);

            Assert.Equal(expected, actual);
            Assert.NotEqual("0.00039986", text);
            Assert.True(text.Length > "0.00039986".Length);
        }

        [Fact]
        public void TestWriteURDFKeepsDoubleArrayPrecision()
        {
            const double first = 3.998552042E-4;
            const double second = -1.130620548E-8;
            URDFAttribute attribute = new URDFAttribute("xyz", true, new[] { first, second });

            XmlDocument document = LoadAttributeXml(attribute);
            string[] fields = document.DocumentElement.GetAttribute("xyz").Split(' ');

            Assert.Equal(2, fields.Length);
            Assert.Equal(first, Double.Parse(
                fields[0],
                URDFAttribute.URDFNumberStyle,
                URDFAttribute.URDFNumberFormat));
            Assert.Equal(second, Double.Parse(
                fields[1],
                URDFAttribute.URDFNumberStyle,
                URDFAttribute.URDFNumberFormat));
            Assert.NotEqual("0.00039986", fields[0]);
        }

        private static XmlDocument LoadAttributeXml(URDFAttribute attribute)
        {
            XmlWriterSettings settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true
            };
            StringWriter stringWriter = new StringWriter();
            using (XmlWriter writer = XmlWriter.Create(stringWriter, settings))
            {
                writer.WriteStartElement("test");
                attribute.WriteURDF(writer);
                writer.WriteEndElement();
            }

            XmlDocument document = new XmlDocument();
            document.LoadXml(stringWriter.ToString());
            return document;
        }
    }
}
