using RoomBoundaryExtractor.RoomExtractor;

namespace RoomBoundaryExtractor
{
    class Program
    {
        static void Main(string[] args)
        {
            string dwgFilePath = @"C:\git\DWGExtractor\提取户型1.dwg";
            string jsonOutput = RoomBoundaryAndFurniture.ExtractRoomBoundaryAndFurniture(
                dwgFilePath
            );
            Console.WriteLine(jsonOutput);
            System.IO.File.WriteAllText("room_boundary_and_furniture.json", jsonOutput);

            RoomDwgGenerator.CreateDwgFromRoomData("room_boundary_and_furniture.json", "test.dwg");
            Console.WriteLine("重建完成:test.dwg");
        }
    }
}
