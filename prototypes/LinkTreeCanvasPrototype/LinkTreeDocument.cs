using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LinkTreeCanvasPrototype
{
    public sealed class LinkTreeNode
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
        public string Name { get; set; }
        public string JointName { get; set; }
        public string JointType { get; set; }
        public double X { get; set; }
        public double Y { get; set; }

        public LinkTreeNode Clone()
        {
            return (LinkTreeNode)MemberwiseClone();
        }
    }

    public sealed class LinkTreeDocument
    {
        private static readonly Regex RosNamePattern = new Regex("^[A-Za-z_][A-Za-z0-9_]*$");

        public List<LinkTreeNode> Nodes { get; private set; }

        public LinkTreeDocument()
        {
            Nodes = new List<LinkTreeNode>();
        }

        public LinkTreeNode Root
        {
            get { return Nodes.SingleOrDefault(node => !node.ParentId.HasValue); }
        }

        public IEnumerable<LinkTreeNode> ChildrenOf(Guid parentId)
        {
            return Nodes.Where(node => node.ParentId == parentId);
        }

        public LinkTreeNode Find(Guid id)
        {
            return Nodes.SingleOrDefault(node => node.Id == id);
        }

        public bool IsDescendant(Guid candidateId, Guid ancestorId)
        {
            LinkTreeNode current = Find(candidateId);
            while (current != null && current.ParentId.HasValue)
            {
                if (current.ParentId.Value == ancestorId)
                {
                    return true;
                }
                current = Find(current.ParentId.Value);
            }
            return false;
        }

        public LinkTreeDocument Clone()
        {
            LinkTreeDocument clone = new LinkTreeDocument();
            clone.Nodes.AddRange(Nodes.Select(node => node.Clone()));
            return clone;
        }

        public static string ValidateRosName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Link 名称不能为空。";
            }
            if (!RosNamePattern.IsMatch(value))
            {
                return "名称只能包含字母、数字和下划线，且不能以数字开头。";
            }
            return null;
        }

        public static LinkTreeDocument CreateSample()
        {
            LinkTreeDocument document = new LinkTreeDocument();
            LinkTreeNode root = NewNode("base_link", null, 90, 330);
            LinkTreeNode chassis = NewNode("chassis_link", root.Id, 390, 250);
            LinkTreeNode lidar = NewNode("lidar_link", chassis.Id, 710, 115);
            LinkTreeNode imu = NewNode("imu_link", chassis.Id, 710, 250);
            LinkTreeNode leftWheel = NewNode("left_wheel_link", chassis.Id, 710, 385);
            leftWheel.JointType = "continuous";
            LinkTreeNode rightWheel = NewNode("right_wheel_link", chassis.Id, 710, 520);
            rightWheel.JointType = "continuous";
            document.Nodes.AddRange(new[] { root, chassis, lidar, imu, leftWheel, rightWheel });
            return document;
        }

        public static LinkTreeNode NewNode(string name, Guid? parentId, double x, double y)
        {
            return new LinkTreeNode
            {
                Id = Guid.NewGuid(),
                ParentId = parentId,
                Name = name,
                JointName = parentId.HasValue ? name.Replace("_link", "") + "_joint" : string.Empty,
                JointType = parentId.HasValue ? "fixed" : string.Empty,
                X = x,
                Y = y
            };
        }
    }
}
