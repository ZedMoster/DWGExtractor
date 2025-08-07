namespace RoomBoundaryExtractor.Models
{
    // 定义JSON输出结构
    public class RoomData
    {
        public List<EntityGeometry> Boundary { get; set; } = [];

        public List<Furniture> Furniture { get; set; } = [];
    }

    public class Point
    {
        public double X { get; set; } = 0;
        public double Y { get; set; } = 0;
    }

    public class Furniture
    {
        public string? Name { get; set; }
        public Point? Scale { get; set; }
        public Point? Position { get; set; }
        public double Rotation { get; set; }

        /// <summary>
        /// 包括这个图块所有的线条等信息,用于重建它
        /// </summary>
        public List<EntityGeometry> Geometry { get; set; } = [];
    }

    public enum EntityCategory
    {
        Line,
        Arc,
        Circle,
        Polyline,
    }

    public class EntityGeometry
    {
        public EntityCategory Type { get; set; } = EntityCategory.Line;
        public List<Point> Points { get; set; } = []; // 对于圆和弧，保存圆心和端点
        public double? Radius { get; set; } // 圆或弧的半径
        public double? StartAngle { get; set; } // 弧线起始角
        public double? EndAngle { get; set; } // 弧线终止角
    }
}
