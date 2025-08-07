using ACadSharp;
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
                    // TODO 优化为导出块到本地,然后创建的时候直接再入本地的块插入,不这样重新创建,容易失败
                    var file = ExportBlockToDwg(
                        block,
                        @"C:\git\DWGExtractor\DWGExtractor\bin\Debug\net8.0"
                    );

                    var furniture = new Furniture
                    {
                        Name = file,
                        Position = TransformPoint(insert.InsertPoint, insert, baseInsert),
                        Rotation = Math.Round(insert.Rotation, 2),
                        Scale = new Point
                        {
                            X = Math.Round(insert.XScale, 2),
                            Y = Math.Round(insert.YScale, 2),
                        },
                    };

                    //foreach (var entity in block.Entities)
                    //{
                    //    var geo = ConvertEntityToGeometryWithTransform(entity, insert, baseInsert);
                    //    if (geo != null)
                    //        furniture.Geometry.Add(geo);
                    //}

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
            else if (entity is LwPolyline lw)
            {
                var points = lw
                    .Vertices.Select(v =>
                    {
                        var pt = TransformPoint(
                            new XYZ
                            {
                                X = Math.Round(v.Location.X, 2),
                                Y = Math.Round(v.Location.Y, 2),
                                Z = 0,
                            },
                            insert,
                            baseInsert
                        );
                        return new Point { X = Math.Round(pt.X, 2), Y = Math.Round(pt.Y, 2) };
                    })
                    .ToList();

                return new EntityGeometry { Type = EntityCategory.Polyline, Points = points };
            }
            else if (entity is Insert insert2)
            {
                // 嵌套Insert：递归处理
                foreach (var item in insert2.Block.Entities)
                {
                    return ConvertEntityToGeometryWithTransform(item, insert2, baseInsert);
                }
            }
            Console.WriteLine(entity);
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

        private static string ExportBlockToDwg(BlockRecord originalBlockRecord, string folderPath)
        {
            if (originalBlockRecord == null || originalBlockRecord.Entities.Count == 0)
                return string.Empty;

            var blockDoc = new CadDocument();
            var clonedBlockRecord = originalBlockRecord.Clone() as BlockRecord;
            foreach (var entity in originalBlockRecord.Entities)
            {
                if (entity.Clone() is Entity cloned)
                {
                    clonedBlockRecord.Entities.Add(cloned);
                }
            }

            // 添加克隆的 BlockRecord
            blockDoc.BlockRecords.Add(clonedBlockRecord);

            // 创建 Insert 实体（插入克隆后的 BlockRecord）
            var insert = new Insert(clonedBlockRecord) { InsertPoint = XYZ.Zero };
            blockDoc.Entities.Add(insert);

            // 确保文件名合法
            string safeName = string.Concat(
                originalBlockRecord.Name.Split(Path.GetInvalidFileNameChars())
            );
            string dwgPath = Path.Combine(folderPath, $"{safeName}.dwg");

            // 写入 DWG 文件
            using var writer = new DwgWriter(dwgPath, blockDoc);
            writer.Write();

            return dwgPath;
        }
    }
}
