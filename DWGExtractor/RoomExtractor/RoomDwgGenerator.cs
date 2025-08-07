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

namespace RoomBoundaryExtractor.RoomExtractor
{
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
                    Layer = furnitureLayer,
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
