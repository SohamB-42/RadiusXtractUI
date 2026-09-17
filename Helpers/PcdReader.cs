using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace RadiusXtractUI.Helpers;

public static class PcdReader
{
    public static List<Vector3> Read(string filePath)
    {
        var points = new List<Vector3>();

        if (!File.Exists(filePath))
            return points;

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var reader = new StreamReader(fs, System.Text.Encoding.ASCII, false, 4096, true);

        string line;
        bool isBinary = false;
        int pointCount = 0;

        string[] fields = null;
        int[] sizes = null;
        int[] counts = null;
        
        // Parse header
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("FIELDS", StringComparison.OrdinalIgnoreCase))
            {
                fields = line.Substring(7).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            }
            else if (line.StartsWith("SIZE", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Substring(5).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                sizes = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++) int.TryParse(parts[i], out sizes[i]);
            }
            else if (line.StartsWith("COUNT", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Substring(6).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                counts = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++) int.TryParse(parts[i], out counts[i]);
            }
            else if (line.StartsWith("POINTS", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    int.TryParse(parts[1], out pointCount);
                }
            }
            else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    isBinary = parts[1].Equals("binary", StringComparison.OrdinalIgnoreCase);
                }
                break; // End of header
            }
        }

        if (pointCount == 0 || fields == null || sizes == null) return points;
        
        if (counts == null)
        {
            counts = new int[fields.Length];
            Array.Fill(counts, 1);
        }

        // Find offsets for x, y, z
        int xIdx = -1, yIdx = -1, zIdx = -1;
        int pointSize = 0;
        int[] byteOffsets = new int[fields.Length];

        for (int i = 0; i < fields.Length; i++)
        {
            byteOffsets[i] = pointSize;
            pointSize += sizes[i] * counts[i];

            if (fields[i].Equals("x", StringComparison.OrdinalIgnoreCase)) xIdx = i;
            if (fields[i].Equals("y", StringComparison.OrdinalIgnoreCase)) yIdx = i;
            if (fields[i].Equals("z", StringComparison.OrdinalIgnoreCase)) zIdx = i;
        }

        if (xIdx == -1 || yIdx == -1 || zIdx == -1) return points; // No valid coordinates

        if (isBinary)
        {
            // reader.ReadLine() might consume extra bytes due to buffering, so we need to be careful.
            // Since we created StreamReader with leaveOpen: true, we can get the actual stream.
            // However, StreamReader reads in chunks, so its internal buffer is ahead of fs.Position.
            // Actually, a simpler way is to just read the header manually byte by byte, but since we used StreamReader:
            // Let's reopen or seek correctly. In C#, stream position after StreamReader is often wrong.
            // A quick fix is to read the header again byte by byte or just use a new FileStream and skip the header lines.
            
            // Re-read header to get exact byte position
        }

        // Let's close the reader and find the exact DATA line position in a raw stream.
        reader.Dispose();
        fs.Position = 0;
        
        using var rawReader = new BinaryReader(fs);
        // Find "DATA ascii" or "DATA binary"
        string dataLineStart = "DATA ";
        int matchIdx = 0;
        while (fs.Position < fs.Length)
        {
            char c = (char)rawReader.ReadByte();
            if (c == dataLineStart[matchIdx])
            {
                matchIdx++;
                if (matchIdx == dataLineStart.Length)
                {
                    // Read until newline
                    while (fs.Position < fs.Length)
                    {
                        char n = (char)rawReader.ReadByte();
                        if (n == '\n') break;
                    }
                    break;
                }
            }
            else
            {
                matchIdx = 0;
            }
        }

        if (isBinary)
        {
            byte[] pointBuffer = new byte[pointSize];
            for (int i = 0; i < pointCount; i++)
            {
                if (fs.Position + pointSize > fs.Length) break;
                
                int read = fs.Read(pointBuffer, 0, pointSize);
                if (read < pointSize) break;

                float x = BitConverter.ToSingle(pointBuffer, byteOffsets[xIdx]);
                float y = BitConverter.ToSingle(pointBuffer, byteOffsets[yIdx]);
                float z = BitConverter.ToSingle(pointBuffer, byteOffsets[zIdx]);

                points.Add(new Vector3(x, y, z));
            }
        }
        else
        {
            using var asciiReader = new StreamReader(fs);
            for (int i = 0; i < pointCount; i++)
            {
                line = asciiReader.ReadLine();
                if (line == null) break;

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > Math.Max(xIdx, Math.Max(yIdx, zIdx)))
                {
                    if (float.TryParse(parts[xIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(parts[yIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                        float.TryParse(parts[zIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    {
                        points.Add(new Vector3(x, y, z));
                    }
                }
            }
        }

        return points;
    }
}
