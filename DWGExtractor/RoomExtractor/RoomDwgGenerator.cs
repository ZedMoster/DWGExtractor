using ACadSharp;
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
            if (doc.Layers.Contains("家具"))
                furnitureLayer = doc.Layers["家具"];
            else
            {
                furnitureLayer = new Layer("家具");
                defaultLayer.Color = new Color(43, 145, 175);
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

            foreach (var furniture in data.Furniture)
            {
                // 加载导出的 DWG 块
                var doc2 = DwgReader.Read(furniture.Name);

                // 只取第一个块（或按 Name 匹配）
                var block = doc2.BlockRecords.FirstOrDefault();
                if (block == null)
                    continue;

                // 插入块
                var insert = new Insert(block)
                {
                    InsertPoint = new XYZ(furniture.Position.X, furniture.Position.Y, 0),
                    Rotation = furniture.Rotation,
                    XScale = furniture.Scale.X,
                    YScale = furniture.Scale.Y,
                    ZScale = 1,
                };

                // 添加到目标文档
                doc.Entities.Add(insert);
            }

            // 写入DWG
            using var writer = new DwgWriter(outputDwgPath, doc);
            writer.Write();

            Console.WriteLine($"图纸已保存: {outputDwgPath}");
        }
    }
}
