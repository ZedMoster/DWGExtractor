using RoomBoundaryExtractor.RoomExtractor;

namespace RoomBoundaryExtractor
{
    class Program
    {
        static void Main(string[] args)
        {
            string dwgFilePath = @"C:\Users\zed\source\repos\提取户型1.dwg";
            string jsonOutput = RoomBoundaryAndFurnitureExtractor.ExtractRoomBoundaryAndFurniture(
                dwgFilePath
            );
            Console.WriteLine(jsonOutput);

            // 保存JSON到文件
            System.IO.File.WriteAllText("room_boundary_and_furniture.json", jsonOutput);

            RoomDwgGenerator.CreateDwgFromRoomData("room_boundary_and_furniture.json", "test.dwg");
            Console.WriteLine("重建完成:test.dwg");
        }
    }
}
