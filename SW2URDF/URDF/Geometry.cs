using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.Serialization;

namespace SW2URDF.URDF
{
    //The geometry element of the visual and collision elements
    [DataContract(IsReference = true, Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class Geometry : URDFElement
    {
        [DataMember]
        public readonly Mesh Mesh;

        [DataMember]
        public readonly Box Box;

        [DataMember]
        public readonly Cylinder Cylinder;

        [DataMember]
        public readonly Sphere Sphere;

        public Geometry() : base("geometry", true)
        {
            Mesh = new Mesh();
            Box = new Box();
            Cylinder = new Cylinder();
            Sphere = new Sphere();

            ChildElements.Add(Mesh);
            ChildElements.Add(Box);
            ChildElements.Add(Cylinder);
            ChildElements.Add(Sphere);
        }

        public void UseMesh(string filename)
        {
            Mesh.Filename = filename;
            Box.Clear();
            Cylinder.Clear();
            Sphere.Clear();
        }

        public void UseBox(double width, double depth, double height)
        {
            Mesh.Clear();
            Box.Size = new[] { width, depth, height };
            Cylinder.Clear();
            Sphere.Clear();
        }

        public void UseCylinder(double radius, double length)
        {
            Mesh.Clear();
            Box.Clear();
            Cylinder.Radius = radius;
            Cylinder.Length = length;
            Sphere.Clear();
        }

        public void UseSphere(double radius)
        {
            Mesh.Clear();
            Box.Clear();
            Cylinder.Clear();
            Sphere.Radius = radius;
        }
    }

    [DataContract(IsReference = true, Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class Box : URDFElement
    {
        [DataMember]
        private readonly URDFAttribute SizeAttribute;

        public double[] Size
        {
            get => (double[])SizeAttribute.Value;
            set => SizeAttribute.Value = value;
        }

        public Box() : base("box", false)
        {
            SizeAttribute = new URDFAttribute("size", true, null);
            Attributes.Add(SizeAttribute);
        }

        public void Clear()
        {
            SizeAttribute.Value = null;
        }

        public override void AppendToCSVDictionary(List<string> context, OrderedDictionary dictionary)
        {
            if (!ElementContainsData())
            {
                return;
            }

            double[] size = Size;
            string prefix = String.Join(".", new List<string>(context) { GetType().Name });
            dictionary.Add(prefix + ".size.x", size[0]);
            dictionary.Add(prefix + ".size.y", size[1]);
            dictionary.Add(prefix + ".size.z", size[2]);
        }
    }

    [DataContract(IsReference = true, Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class Cylinder : URDFElement
    {
        [DataMember]
        private readonly URDFAttribute RadiusAttribute;

        [DataMember]
        private readonly URDFAttribute LengthAttribute;

        public double Radius
        {
            get => (double)RadiusAttribute.Value;
            set => RadiusAttribute.Value = value;
        }

        public double Length
        {
            get => (double)LengthAttribute.Value;
            set => LengthAttribute.Value = value;
        }

        public Cylinder() : base("cylinder", false)
        {
            RadiusAttribute = new URDFAttribute("radius", true, null);
            LengthAttribute = new URDFAttribute("length", true, null);
            Attributes.Add(RadiusAttribute);
            Attributes.Add(LengthAttribute);
        }

        public void Clear()
        {
            RadiusAttribute.Value = null;
            LengthAttribute.Value = null;
        }

        public override void AppendToCSVDictionary(List<string> context, OrderedDictionary dictionary)
        {
            if (!ElementContainsData())
            {
                return;
            }

            string prefix = String.Join(".", new List<string>(context) { GetType().Name });
            dictionary.Add(prefix + ".radius", Radius);
            dictionary.Add(prefix + ".length", Length);
        }
    }

    [DataContract(IsReference = true, Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class Sphere : URDFElement
    {
        [DataMember]
        private readonly URDFAttribute RadiusAttribute;

        public double Radius
        {
            get => (double)RadiusAttribute.Value;
            set => RadiusAttribute.Value = value;
        }

        public Sphere() : base("sphere", false)
        {
            RadiusAttribute = new URDFAttribute("radius", true, null);
            Attributes.Add(RadiusAttribute);
        }

        public void Clear()
        {
            RadiusAttribute.Value = null;
        }

        public override void AppendToCSVDictionary(List<string> context, OrderedDictionary dictionary)
        {
            if (!ElementContainsData())
            {
                return;
            }

            string prefix = String.Join(".", new List<string>(context) { GetType().Name });
            dictionary.Add(prefix + ".radius", Radius);
        }
    }
}
