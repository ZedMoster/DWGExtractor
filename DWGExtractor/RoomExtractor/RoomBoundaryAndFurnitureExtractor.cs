using ACadSharp;
using ACadSharp.Blocks;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using Newtonsoft.Json;
using RoomBoundaryExtractor.Models;
using Arc = ACadSharp.Entities.Arc;
using Line = ACadSharp.Entities.Line;
using Point = RoomBoundaryExtractor.Models.Point;

namespace RoomBoundaryExtractor.RoomExtractor
{
    public class RoomBoundaryAndFurnitureExtractor
    {
        public static string ExtractRoomBoundaryAndFurniture(string dwgFilePath)
        {
            try
            {
                // 加载DWG文件
                CadDocument doc = DwgReader.Read(dwgFilePath);
                var list = doc.Entities.ToList();
                var roomData = new RoomData();

                // 提取墙边界
                ExtractBlocksSplitWallAndFurniture(doc, roomData);

                // 转换为JSON
                return JsonConvert.SerializeObject(roomData, Formatting.Indented);
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(
                    new { error = $"Failed to extract data: {ex.Message}" }
                );
            }
        }

        private static void ExtractBlocksSplitWallAndFurniture(CadDocument doc, RoomData roomData)
        {
            var inserts = doc.Entities.OfType<Insert>().ToList();

            // 找到不可动建筑图层的 Insert 作为“坐标基准”
            var baseInsert = inserts.FirstOrDefault(i =>
                i.Layer?.Name?.Contains("不可动建筑图层") == true
            );

            if (baseInsert == null)
                throw new Exception("未找到任何“不可动建筑图层”图块，无法建立坐标基准。");
            Console.WriteLine("“不可动建筑图层”图块，无法建立坐标基准:" + baseInsert.InsertPoint);

            // 遍历所有 Insert 实例
            foreach (var insert in inserts)
            {
                var block = insert.Block;
                if (block == null)
                    continue;

                var layerName = insert.Layer?.Name ?? string.Empty;
                var name = block.Name ?? "Unnamed";

                if (layerName.Contains("不可动建筑图层"))
                {
                    // 提取构成房间边界的图元
                    foreach (var entity in block.Entities)
                    {
                        var geo = ConvertEntityToGeometry(entity);
                        if (geo != null)
                            roomData.Boundary.Add(geo);
                    }
                }
                else
                {
                    // 家具图块：位置和属性统一转换成“房间坐标系”下
                    var position = TransformByBase(insert.InsertPoint, baseInsert);

                    var furniture = new Furniture
                    {
                        Name = name,
                        Position = position,
                        Rotation = Math.Round(insert.Rotation, 2),
                        Scale = new Point
                        {
                            X = Math.Round(insert.XScale, 2),
                            Y = Math.Round(insert.YScale, 2),
                        },
                        Geometry = new List<EntityGeometry>(),
                    };

                    // 提取图块内所有几何体
                    foreach (var entity in block.Entities)
                    {
                        var geo = ConvertEntityToGeometry(entity);
                        if (geo != null)
                            furniture.Geometry.Add(geo);
                    }
                    roomData.Furniture.Add(furniture);
                }
            }
        }

        private static EntityGeometry? ConvertEntityToGeometry(Entity entity)
        {
            if (entity is Line line)
            {
                return new EntityGeometry
                {
                    Type = EntityCategory.Line,
                    Points = new List<Point>
                    {
                        new Point { X = line.StartPoint.X, Y = line.StartPoint.Y },
                        new Point { X = line.EndPoint.X, Y = line.EndPoint.Y },
                    },
                };
            }
            else if (entity is Arc arc)
            {
                return new EntityGeometry
                {
                    Type = EntityCategory.Arc,
                    Points = new List<Point>
                    {
                        new Point { X = arc.Center.X, Y = arc.Center.Y },
                    },
                    Radius = arc.Radius,
                    StartAngle = arc.StartAngle,
                    EndAngle = arc.EndAngle,
                };
            }
            else if (entity is Circle circle)
            {
                return new EntityGeometry
                {
                    Type = EntityCategory.Circle,
                    Points = new List<Point>
                    {
                        new Point { X = circle.Center.X, Y = circle.Center.Y },
                    },
                    Radius = circle.Radius,
                };
            }

            // 你可以继续支持更多类型，比如 Polyline 等
            return null;
        }

        private static Point ComputeArcPoint(Arc arc, double angleDeg, Insert baseInsert)
        {
            double angleRad = angleDeg * Math.PI / 180.0;

            double x = arc.Center.X + arc.Radius * Math.Cos(angleRad);
            double y = arc.Center.Y + arc.Radius * Math.Sin(angleRad);

            return TransformByBase(new XYZ(x, y, arc.Center.Z), baseInsert);
        }

        private static Point TransformByBase(XYZ pt, Insert baseInsert)
        {
            double rad = baseInsert.Rotation * Math.PI / 180.0;

            // 缩放
            double x = pt.X * baseInsert.XScale;
            double y = pt.Y * baseInsert.YScale;

            // 旋转
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double rx = x * cos - y * sin;
            double ry = x * sin + y * cos;

            // 平移
            rx += baseInsert.InsertPoint.X;
            ry += baseInsert.InsertPoint.Y;

            return new Point { X = Math.Round(rx, 2), Y = Math.Round(ry, 2) };
        }
    }

    public static class RoomDwgGenerator
    {
        public static void CreateDwgFromRoomData(string jsonPath, string outputDwgPath)
        {
            var json = File.ReadAllText(jsonPath);
            var data = JsonConvert.DeserializeObject<RoomData>(json);
            if (data == null)
            {
                Console.WriteLine("JSON数据无效");
                return;
            }

            var doc = new CadDocument();

            // 创建默认图层
            Layer defaultLayer;
            if (doc.Layers.Contains("固定墙"))
                defaultLayer = doc.Layers["固定墙"];
            else
            {
                defaultLayer = new Layer("固定墙");
                defaultLayer.Color = new Color(255, 0, 0);
                doc.Layers.Add(defaultLayer);
            }

            // 创建家具专用图层
            Layer furnitureLayer;
            if (doc.Layers.Contains("Furniture"))
                furnitureLayer = doc.Layers["Furniture"];
            else
            {
                furnitureLayer = new Layer("Furniture");
                defaultLayer.Color = new Color(0, 120, 120);
                doc.Layers.Add(furnitureLayer);
            }

            // 创建房间边界（连接线）
            if (data.Boundary.Count > 0)
            {
                foreach (var geo in data.Boundary)
                {
                    switch (geo.Type)
                    {
                        case EntityCategory.Line when geo.Points.Count == 2:
                            var g1 = geo.Points[0];
                            var g2 = geo.Points[1];
                            var line = new Line
                            {
                                StartPoint = new XYZ(g1.X, g1.Y, 0),
                                EndPoint = new XYZ(g2.X, g2.Y, 0),
                                Layer = defaultLayer,
                            };
                            doc.Entities.Add(line);
                            break;

                        case EntityCategory.Arc:
                            if (
                                geo.Points.Count >= 1
                                && geo.Radius.HasValue
                                && geo.StartAngle.HasValue
                                && geo.EndAngle.HasValue
                            )
                            {
                                var center = geo.Points[0];
                                var arc = new Arc
                                {
                                    Center = new XYZ(center.X, center.Y, 0),
                                    Radius = geo.Radius.Value,
                                    StartAngle = geo.StartAngle.Value,
                                    EndAngle = geo.EndAngle.Value,
                                    Layer = defaultLayer,
                                };
                                doc.Entities.Add(arc);
                            }
                            break;

                        case EntityCategory.Circle:
                            if (geo.Points.Count >= 1 && geo.Radius.HasValue)
                            {
                                var center = geo.Points[0];
                                var circle = new Circle
                                {
                                    Center = new XYZ(center.X, center.Y, 0),
                                    Radius = geo.Radius.Value,
                                    Layer = defaultLayer,
                                };

                                doc.Entities.Add(circle);
                            }
                            break;
                    }
                }
            }

            // 记录已经添加过的块定义，Key是块名，Value是BlockRecord
            var addedBlocks = new Dictionary<string, BlockRecord>();

            foreach (var f in data.Furniture)
            {
                string blockName = f.Name ?? "Furniture";

                // 若未添加过，则添加块定义
                if (!addedBlocks.ContainsKey(blockName))
                {
                    var blockRecord = new BlockRecord(blockName);
                    var block = new Block(blockRecord);

                    if (f.Geometry != null)
                    {
                        foreach (var geo in f.Geometry)
                        {
                            switch (geo.Type)
                            {
                                case EntityCategory.Line when geo.Points.Count == 2:
                                    var g1 = geo.Points[0];
                                    var g2 = geo.Points[1];
                                    var line = new Line
                                    {
                                        StartPoint = new XYZ(g1.X, g1.Y, 0),
                                        EndPoint = new XYZ(g2.X, g2.Y, 0),
                                        Layer = furnitureLayer,
                                    };
                                    blockRecord.Entities.Add(line);
                                    break;

                                case EntityCategory.Arc:
                                    if (
                                        geo.Points.Count >= 1
                                        && geo.Radius.HasValue
                                        && geo.StartAngle.HasValue
                                        && geo.EndAngle.HasValue
                                    )
                                    {
                                        var center = geo.Points[0];
                                        var arc = new Arc
                                        {
                                            Center = new XYZ(center.X, center.Y, 0),
                                            Radius = geo.Radius.Value,
                                            StartAngle = geo.StartAngle.Value,
                                            EndAngle = geo.EndAngle.Value,
                                            Layer = furnitureLayer,
                                        };
                                        blockRecord.Entities.Add(arc);
                                    }
                                    break;

                                case EntityCategory.Circle:
                                    if (geo.Points.Count >= 1 && geo.Radius.HasValue)
                                    {
                                        var center = geo.Points[0];
                                        var circle = new Circle
                                        {
                                            Center = new XYZ(center.X, center.Y, 0),
                                            Radius = geo.Radius.Value,
                                            Layer = furnitureLayer,
                                        };
                                        blockRecord.Entities.Add(circle);
                                    }
                                    break;
                            }
                        }
                    }

                    // 添加块定义和块实体到文档
                    doc.BlockRecords.Add(blockRecord);
                    addedBlocks[blockName] = blockRecord;
                }

                // 用BlockRecord创建Insert
                var insert = new Insert(addedBlocks[blockName])
                {
                    InsertPoint = new XYZ(f.Position?.X ?? 0, f.Position?.Y ?? 0, 0),
                    XScale = f.Scale?.X ?? 1,
                    YScale = f.Scale?.Y ?? 1,
                    Rotation = f.Rotation,
                };

                doc.Entities.Add(insert);
            }

            // 写入DWG
            using var writer = new DwgWriter(outputDwgPath, doc);
            writer.Write();

            Console.WriteLine($"图纸已保存: {outputDwgPath}");
        }
    }
}
