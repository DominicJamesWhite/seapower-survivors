using System;
using System.Collections.Generic;
using System.IO;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Land and water from the game's terrain files, for the main menu, where
/// <see cref="Sea.IsWater"/> has no loaded terrain and calls everywhere deep.
/// Heights are little-endian shorts in metres: a chunk of 2400 by 2400 at 1/120
/// of a degree (half a mile) for each 20 degree square, and lowres.raw for the
/// world at 1/20 of a degree if a chunk cannot be read. Quick Start's Topo class
/// showed that these can be read this way.
/// </summary>
static class Chart
{
    const int ChunkDim = 2400, ChunkBytes = ChunkDim * ChunkDim * 2;
    const int LowresW = 7200, LowresH = 3600;
    const int CachedChunks = 3;

    static readonly Dictionary<string, byte[]> chunks = new Dictionary<string, byte[]>();
    static readonly List<string> order = new List<string>();
    static byte[] lowres;
    static bool lowresTried;

    static string DataFolder
    {
        get
        {
            string user = Path.Combine(Application.streamingAssetsPath, "user", "terrain", "data");
            return File.Exists(Path.Combine(user, "y1x01.lz4")) || File.Exists(Path.Combine(user, "lowres.raw"))
                ? user
                : Path.Combine(Application.streamingAssetsPath, "original", "terrain", "data");
        }
    }

    /// <summary>Deep enough for a ship, by the same depth as <see cref="Sea"/>. With no terrain to read at all, it calls everywhere water and logs why.</summary>
    internal static bool IsWater(GeoPosition at)
    {
        var height = Height(at);
        return height == null || height < -Sea.MinDepthM;
    }

    static short? Height(GeoPosition at)
    {
        var chunk = ChunkFor(at);
        if (chunk != null)
        {
            int x = Mathf.Clamp((int)((at._longitude + 180.0) * 120.0), 0, 43199);
            int y = Mathf.Clamp((int)((90.0 - at._latitude) * 120.0), 0, 21599);
            return BitConverter.ToInt16(chunk, 2 * (y % ChunkDim * ChunkDim + x % ChunkDim));
        }
        var world = Lowres();
        if (world == null) return null;
        int wx = Mathf.Clamp((int)((at._longitude + 180.0) * 20.0), 0, LowresW - 1);
        int wy = Mathf.Clamp((int)((90.0 - at._latitude) * 20.0), 0, LowresH - 1);
        return BitConverter.ToInt16(world, 2 * (wy * LowresW + wx));
    }

    static byte[] ChunkFor(GeoPosition at)
    {
        string name = Utils.longLatToDEMChunk(at);
        if (chunks.TryGetValue(name, out var chunk)) return chunk;
        try
        {
            string path = Path.Combine(DataFolder, name + ".lz4");
            if (File.Exists(path))
            {
                chunk = LZ4.decompressBuffer(File.ReadAllBytes(path));
                if (chunk != null && chunk.Length != ChunkBytes)
                {
                    Entry.Log.LogWarning($"Terrain chunk {name} is {chunk.Length} bytes, not {ChunkBytes}: using the coarse map there");
                    chunk = null;
                }
            }
        }
        catch (Exception e)
        {
            Entry.Log.LogWarning($"Could not read terrain chunk {name}, using the coarse map there: {e.Message}");
            chunk = null;
        }
        chunks[name] = chunk;
        order.Add(name);
        if (order.Count > CachedChunks)
        {
            chunks.Remove(order[0]);
            order.RemoveAt(0);
        }
        return chunk;
    }

    static byte[] Lowres()
    {
        if (lowresTried) return lowres;
        lowresTried = true;
        try
        {
            var data = File.ReadAllBytes(Path.Combine(DataFolder, "lowres.raw"));
            if (data.Length == LowresW * LowresH * 2) lowres = data;
            else Entry.Log.LogWarning($"lowres.raw is {data.Length} bytes: no coarse map");
        }
        catch (Exception e)
        {
            Entry.Log.LogWarning($"Could not read lowres.raw, so stations will not be checked for land: {e.Message}");
        }
        return lowres;
    }
}
