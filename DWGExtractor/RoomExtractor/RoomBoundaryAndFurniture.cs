using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using Newtonsoft.Json;
using RoomBoundaryExtractor.Models;
using Arc = ACadSharp.Entities.Arc;
using Line = ACadSharp.Entities.Line;
using Point = RoomBoundaryExtractor.Models.Point;

namespace RoomBoundaryExtractor.RoomExtractor
{
    public class RoomBoundaryAndFurniture
    {
        public static string ExtractRoomBoundaryAndFurniture(string dwgFilePath)
        {
            try
            {
                var doc = DwgReader.Read(dwgFilePath);
                var roomData = new RoomData();

                ExtractBlocksSplitWallAndFurniture(doc, roomData);

                return JsonConvert.SerializeObject(roomData, Formatting.Indented);
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = $"提取失败: {ex.Message}" });
            }
        }

        private static void ExtractBlocksSplitWallAndFurniture(CadDocument doc, RoomData roomData)
        {
            var inserts = doc.Entities.OfType<Insert>().ToList();

            var baseInsert = inserts.FirstOrDefault(i =>
                i.Layer?.Name?.Contains("不可动建筑图层") == true
            );

            if (baseInsert == null)
                throw new Exception("未找到“不可动建筑图层”图块 Insert，无法建立坐标基准。");

            foreach (var insert in inserts)
            {
                var block = insert.Block;
                if (block == null)
                    continue;

                string name = block.Name ?? "Unnamed";
                string layerName = insert.Layer?.Name ?? string.Empty;

                if (layerName.Contains("不可动建筑图层"))
                {
                    foreach (var entity in block.Entities)
                    {
                        var geo = ConvertEntityToGeometryWithTransform(entity, insert, baseInsert);
                        if (geo != null)
                            roomData.Boundary.Add(geo);
                    }
                }
                else
                {
                    var furniture = new Furniture
                    {
                        Name = name,
                        Position = new Point
                        {
                            X = Math.Round(insert.InsertPoint.X - baseInsert.InsertPoint.X, 2),
                            Y = Math.Round(insert.InsertPoint.Y - baseInsert.InsertPoint.Y, 2),
                        },
                        Rotation = Math.Round(insert.Rotation, 2),
                        Scale = new Point
                        {
                            X = Math.Round(insert.XScale, 2),
                            Y = Math.Round(insert.YScale, 2),
                        },
                    };

                    foreach (var entity in block.Entities)
                    {
                        var geo = ConvertEntityToGeometryWithTransform(entity, insert, baseInsert);
                        if (geo != null)
                            furniture.Geometry.Add(geo);
                    }

                    roomData.Furniture.Add(furniture);
                }
            }
        }

        private static EntityGeometry? ConvertEntityToGeometryWithTransform(
            Entity entity,
            Insert insert,
            Insert baseInsert
        )
        {
            if (entity is Line line)
            {
                var p1 = TransformPoint(line.StartPoint, insert, baseInsert);
                var p2 = TransformPoint(line.EndPoint, insert, baseInsert);

                return new EntityGeometry
                {
                    Type = EntityCategory.Line,
                    Points = new List<Point>
                    {
                        new Point { X = Math.Round(p1.X, 2), Y = Math.Round(p1.Y, 2) },
                        new Point { X = Math.Round(p2.X, 2), Y = Math.Round(p2.Y, 2) },
                    },
                };
            }
            else if (entity is Circle circle)
            {
                var center = TransformPoint(circle.Center, insert, baseInsert);

                return new EntityGeometry
                {
                    Type = EntityCategory.Circle,
                    Points = new List<Point>
                    {
                        new Point { X = Math.Round(center.X, 2), Y = Math.Round(center.Y, 2) },
                    },
                    Radius = Math.Round(circle.Radius * insert.XScale, 2),
                };
            }
            else if (entity is Arc arc)
            {
                var center = TransformPoint(arc.Center, insert, baseInsert);

                return new EntityGeometry
                {
                    Type = EntityCategory.Arc,
                    Points = new List<Point>
                    {
                        new Point { X = Math.Round(center.X, 2), Y = Math.Round(center.Y, 2) },
                    },
                    Radius = Math.Round(arc.Radius * insert.XScale, 2),
                    StartAngle = arc.StartAngle + insert.Rotation,
                    EndAngle = arc.EndAngle + insert.Rotation,
                };
            }

            return null;
        }

        private static Point TransformPoint(XYZ pt, Insert insert, Insert baseInsert)
        {
            double x = pt.X * insert.XScale;
            double y = pt.Y * insert.YScale;

            double rad = insert.Rotation;
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);

            double rx = x * cos - y * sin;
            double ry = x * sin + y * cos;

            rx += insert.InsertPoint.X;
            ry += insert.InsertPoint.Y;

            rx -= baseInsert.InsertPoint.X;
            ry -= baseInsert.InsertPoint.Y;

            return new Point { X = Math.Round(rx, 2), Y = Math.Round(ry, 2) };
        }
    }
}
