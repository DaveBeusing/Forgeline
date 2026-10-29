using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ForgeLine.Rendering.Benchmarks;

internal static class ProceduralCaptureImage
{
    internal static void Write(string path,int width,int height,ReadOnlySpan<byte> rgba)
    {
        if(rgba.Length!=checked(width*height*4))throw new ArgumentException("Invalid capture dimensions.");
        using var file=new FileStream(path,FileMode.CreateNew);
        file.Write(new byte[]{137,80,78,71,13,10,26,10});
        Span<byte> header=stackalloc byte[13];header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header,width);BinaryPrimitives.WriteInt32BigEndian(header[4..],height);header[8]=8;header[9]=6;
        Chunk(file,"IHDR",header);
        using var bytes=new MemoryStream();
        using(var compressed=new ZLibStream(bytes,CompressionLevel.Fastest,true))
            for(int y=0;y<height;y++){compressed.WriteByte(0);compressed.Write(rgba.Slice(y*width*4,width*4));}
        Chunk(file,"IDAT",bytes.ToArray());Chunk(file,"IEND",[]);
    }
    private static void Chunk(Stream output,string type,ReadOnlySpan<byte> data)
    {
        Span<byte> length=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(length,data.Length);output.Write(length);
        byte[] tag=Encoding.ASCII.GetBytes(type);output.Write(tag);output.Write(data);
        uint crc=uint.MaxValue;foreach(byte value in tag)Step(value);foreach(byte value in data)Step(value);
        BinaryPrimitives.WriteUInt32BigEndian(length,~crc);output.Write(length);
        void Step(byte value){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0 ? (crc>>1)^0xedb88320u : crc>>1;}
    }
}
