// The preview SolidCraft draws (solidcraft-cli snapshot) has the viewport's background, a vertical
// gradient, and the snapshot command has no option to leave it out; only the path-traced
// `render` does, at seconds to minutes per picture. The FreeCAD add-on uploads its preview with a
// transparent background (saveImage "Transparent"), so the item looks the same in the web app
// whichever program it came from, on a light or a dark theme.
//
// So the companion takes the background out itself. The gradient changes only from row to row,
// so each row's background colour is read from its first and last pixel; the background is what
// matches it and is connected to the picture's edge (a flood fill), so a face of the model that
// happens to have the background's colour inside the outline stays. Pixels at the outline, where
// the renderer blended model and background, get a partial alpha and their colour un-blended, so
// the edge has no halo of the old background.
//
// The PNG reading and writing is the minimum for what SolidCraft writes: 8-bit RGBA or RGB, not
// interlaced. Anything else is left as it is.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace EasyPDM.Companion;

static class TransparentPreview
{
    // How far (per channel) a pixel may differ from its row's background and still be background.
    const int Tolerance = 6;
    // Beyond this distance from the background an outline pixel is fully the model's.
    const int Blend = 48;

    /// Rewrites `path` with a transparent background; false (file untouched) when it cannot.
    public static bool Apply(string path)
    {
        try
        {
            if (!TryRead(File.ReadAllBytes(path), out var width, out var height, out var rgba))
                return false;
            if (!RemoveBackground(width, height, rgba))
                return false;
            File.WriteAllBytes(path, Write(width, height, rgba));
            return true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"  preview kept with its background: {e.Message}");
            return false;
        }
    }

    public static bool RemoveBackground(int width, int height, byte[] rgba)
    {
        if (width < 3 || height < 3) return false;
        var background = new (int R, int G, int B)[height];
        for (var y = 0; y < height; y++)
        {
            int l = y * width * 4, r = (y * width + width - 1) * 4;
            // Both ends of the row must agree: a picture whose model reaches the edge, or whose
            // background is not a vertical gradient, is left alone rather than cut wrongly.
            if (Math.Abs(rgba[l] - rgba[r]) > Tolerance || Math.Abs(rgba[l + 1] - rgba[r + 1]) > Tolerance
                || Math.Abs(rgba[l + 2] - rgba[r + 2]) > Tolerance)
                return false;
            background[y] = (rgba[l], rgba[l + 1], rgba[l + 2]);
        }

        int Distance(int i, int y)
        {
            var (R, G, B) = background[y];
            return Math.Max(Math.Abs(rgba[i] - R), Math.Max(Math.Abs(rgba[i + 1] - G), Math.Abs(rgba[i + 2] - B)));
        }

        var cleared = new bool[width * height];
        var queue = new Queue<int>();
        void Seed(int x, int y)
        {
            var p = y * width + x;
            if (!cleared[p] && Distance(p * 4, y) <= Tolerance) { cleared[p] = true; queue.Enqueue(p); }
        }
        for (var x = 0; x < width; x++) { Seed(x, 0); Seed(x, height - 1); }
        for (var y = 0; y < height; y++) { Seed(0, y); Seed(width - 1, y); }
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            int x = p % width, y = p / width;
            if (x > 0) Seed(x - 1, y);
            if (x < width - 1) Seed(x + 1, y);
            if (y > 0) Seed(x, y - 1);
            if (y < height - 1) Seed(x, y + 1);
        }

        for (var p = 0; p < cleared.Length; p++)
        {
            var i = p * 4;
            if (cleared[p]) { rgba[i] = rgba[i + 1] = rgba[i + 2] = rgba[i + 3] = 0; continue; }
            int x = p % width, y = p / width;
            var atOutline = (x > 0 && cleared[p - 1]) || (x < width - 1 && cleared[p + 1])
                || (y > 0 && cleared[p - width]) || (y < height - 1 && cleared[p + width]);
            if (!atOutline) continue;
            var d = Distance(i, y);
            if (d >= Blend) continue;
            // The renderer drew c = a*model + (1-a)*background; recover a and the model's colour.
            var a = (double)d / Blend;
            var (R, G, B) = background[y];
            rgba[i] = Unblend(rgba[i], R, a);
            rgba[i + 1] = Unblend(rgba[i + 1], G, a);
            rgba[i + 2] = Unblend(rgba[i + 2], B, a);
            rgba[i + 3] = (byte)Math.Round(rgba[i + 3] * a);
        }
        return true;
    }

    static byte Unblend(byte c, int background, double a) =>
        (byte)Math.Clamp(Math.Round((c - (1 - a) * background) / a), 0, 255);

    // --- PNG, 8-bit RGB/RGBA, not interlaced ----------------------------------------------------

    static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool TryRead(byte[] png, out int width, out int height, out byte[] rgba)
    {
        width = height = 0;
        rgba = [];
        if (png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(Signature)) return false;
        var idat = new MemoryStream();
        int colourType = -1, pos = 8;
        while (pos + 12 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos));
            var type = Encoding.ASCII.GetString(png, pos + 4, 4);
            var body = png.AsSpan(pos + 8, length);
            if (type == "IHDR")
            {
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(body);
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
                if (body[8] != 8 || body[12] != 0) return false;  // 8 bits, no interlace
                colourType = body[9];
                if (colourType is not (2 or 6)) return false;
                if ((long)width * height > 40_000_000) return false;
            }
            else if (type == "IDAT") idat.Write(body);
            else if (type == "IEND") break;
            pos += 12 + length;
        }
        if (colourType < 0) return false;
        var channels = colourType == 6 ? 4 : 3;
        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            z.ReadExactly(raw);

        rgba = new byte[width * height * 4];
        var previous = new byte[stride];
        var line = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            Array.Copy(raw, y * (stride + 1) + 1, line, 0, stride);
            for (var x = 0; x < stride; x++)
            {
                int a = x >= channels ? line[x - channels] : 0, b = previous[x], c = x >= channels ? previous[x - channels] : 0;
                line[x] = (byte)(line[x] + filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) >> 1,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException("unknown PNG filter"),
                });
            }
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 4;
                rgba[o] = line[x * channels];
                rgba[o + 1] = line[x * channels + 1];
                rgba[o + 2] = line[x * channels + 2];
                rgba[o + 3] = channels == 4 ? line[x * channels + 3] : (byte)255;
            }
            (previous, line) = (line, previous);
        }
        return true;
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    public static byte[] Write(int width, int height, byte[] rgba)
    {
        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++)
            Array.Copy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);  // filter 0
        var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw);

        var output = new MemoryStream();
        output.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8; header[9] = 6;  // 8-bit RGBA
        Chunk(output, "IHDR", header);
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    static void Chunk(Stream output, string type, byte[] body)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)body.Length);
        output.Write(number);
        var typed = Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
        output.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typed));
        output.Write(number);
    }

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc32(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
