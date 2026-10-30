using RatchetPs2.Games.UYA.Occlusion;
using RatchetPs2.Core.Occlusion;
using System.Numerics;
using RatchetPs2.Core.Shrubs;
using RatchetPs2.Core.Ties;

internal static class UyaOcclusionSurfaceTests
{
    public static void Run()
    {
        var vertices = Enumerable.Range(0, 3).Select(i => new TieLogicalVertex
        {
            LodIndex = 0,
            PacketIndex = 0,
            PacketStripIndex = 0,
            StripIndex = 0,
            IndexInStrip = i,
            LogicalVertexIndex = i,
            VuAddress = i,
            Token = 0,
            MappingKind = TieLogicalVertexMappingKind.PrimaryRowAddress,
            AddressRow = null,
            VertexRow = null,
            DecodedVertex = new TiePacketDecodedVertex
            {
                Index = i,
                SourceIndex = i,
                Kind = TiePacketDecodedVertexKind.Dinky,
                Offset = 0,
                Bytes = [],
                SourceRowIndex = 0,
                SourceRow = null,
                X = (short)(i == 1 ? 1 : 0),
                Y = (short)(i == 2 ? 1 : 0),
                Z = 0,
                GsPacketWriteOffset = 0,
                S = (ushort)(i == 0 ? 32768 : i == 1 ? 4096 : 0),
                T = (ushort)(i == 0 ? 4096 : i == 1 ? 8192 : 0),
                Q = 4096,
                SecondaryGsPacketWriteOffset = 0
            }
        }).ToArray();
        var strips = Enumerable.Range(0, 2).Select(i => new TieTriangleStrip
        {
            LodIndex = 0,
            PacketIndex = 0,
            PacketStripIndex = i,
            StripIndex = i,
            LogicalVertexStartIndex = 0,
            LogicalVertexCount = 3,
            TriangleStartIndex = i,
            TriangleCount = 1,
            VuAddress = 0,
            Flags = 0,
            ShaderIndex = i,
            Tokens = [],
            LogicalVertices = vertices
        }).ToArray();
        var tie = new TieClass
        {
            ByteLength = 0,
            Header = new TieClassHeader { PacketTableOffsets = [], PacketCounts = [], CacheSizes = [], RgbaRemapOffsets = [], Lods = [], UnknownOffsets78 = [], Scale = 1024 },
            Shaders = [new() { Index = 0, Offset = 0, ClampU = true, Bytes = [] }, new() { Index = 1, Offset = 0, Bytes = [] }],
            LodTopologies = [new()
            {
                LodIndex=0,LogicalVertexCount=3,PacketVertexRowCount=0,PrimaryAddressMappedLogicalVertexCount=3,
                SecondaryAddressMappedLogicalVertexCount=0,UnresolvedLogicalVertexCount=0,StripCount=2,TriangleCount=2,
                LogicalVertices=vertices,Strips=strips,Triangles=[new(0,0,0,0,1,2),new(0,1,0,0,1,2)]
            }]
        };
        var faces = TieSurfaceMeshExtractor.ExtractTextured(tie, 0);
        Check(faces.Count == 2 && faces[0].MaterialIndex == 0 && faces[1].MaterialIndex == 1, "tie material stays per face");
        Check(faces[0].UvA == new Vector2(-8, 1) && faces[1].UvA == new Vector2(8, 1)
            && faces[0].ClampU && !faces[1].ClampU, "tie signed clamp UVs and repeated tile phase");
        Check(faces[0].B == Vector3.UnitX && faces[0].C == Vector3.UnitY, "tie surface stays in native space");
        var shrub = new ShrubClass
        {
            ByteLength = 0,
            Header = new ShrubClassHeader { BoundingSphere = Vector4.Zero, Scale = 1024 },
            Packets = [new() { PacketIndex=0,Entry=new(0,0),Header=new(2,2,6,0),Primitives=[
                new ShrubTexturePrimitive(0,1,[]),Primitive(),new ShrubTexturePrimitive(0,0,[]),Primitive()] }]
        };
        var shrubs = ShrubSurfaceMeshExtractor.ExtractTextured(shrub);
        Check(shrubs.Count == 2 && shrubs[0].MaterialIndex == 1 && shrubs[1].MaterialIndex == 0, "shrub material switches retained");
        Check(shrubs[0].UvA == new Vector2(.25f, .75f) && shrubs[0].B == Vector3.UnitX, "shrub Q12 UV and native positions");
    }
    private static ShrubVertexPrimitive Primitive() => new(0, ShrubGeometryType.TriangleList,
        [new(0, 0, 0, 1024, 3072, 0, 0), new(1, 0, 0, 4096, 0, 0, 0), new(0, 1, 0, 0, 4096, 0, 0)]);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
