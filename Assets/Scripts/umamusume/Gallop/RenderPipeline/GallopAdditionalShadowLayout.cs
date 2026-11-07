using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Gallop.RenderPipeline
{
    // Fixed native helpers 0x51fc4e0, 0x51fb7e0, 0x51faec0, 0x51fbee0.
    // These operate on requested sizes, not the host URP shadow-resolution tiers.
    public static class GallopAdditionalShadowLayout
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Request
        {
            public int visibleIndex, sliceIndex, requestedResolution;
            [MarshalAs(UnmanagedType.U1)] public bool soft;
            [MarshalAs(UnmanagedType.U1)] public bool point;
            public ushort padding;
            public int offsetX, offsetY, allocatedResolution;
        }

        public static int SliceBudget(int maxVisible) => maxVisible == 256 ? 545 : maxVisible;
        public static int SliceCount(LightType type) => type == LightType.Spot ? 1 : type == LightType.Point ? 6 : 0;
        public static int Resolution(LightType type, int mode, int memoryMB)
        {
            int size = type == LightType.Spot ? 4096 : type == LightType.Directional ? 8192 : type == LightType.Point ? 2048 : 0;
            size /= mode == 0 ? 4 : mode == 1 ? 2 : 1;
            int cap = type == LightType.Spot ? 1024 : type == LightType.Directional ? 2048 : type == LightType.Point ? 512 : int.MaxValue;
            if (memoryMB >= 512) cap = unchecked(cap * 2);
            return System.Math.Min(size, cap);
        }
        public static int Resolution(Light light)
        {
            int mode = (int)light.shadowResolution;
            if (mode == -1) mode = (int)QualitySettings.shadowResolution;
            // Native VisibleLight overload only dispatches enum 0/1/2.
            return mode >= 0 && mode <= 2 ? Resolution(light.type, mode, SystemInfo.graphicsMemorySize) : 0;
        }
        static bool Before(Request a, Request b, float[] distances)
        {
            if (a.requestedResolution != b.requestedResolution) return a.requestedResolution > b.requestedResolution;
            if (a.soft != b.soft) return !a.soft;
            if (a.point != b.point) return !a.point;
            float da = distances[a.visibleIndex], db = distances[b.visibleIndex];
            if (da < db) return true;
            if (da != db) return false; // NaN is unordered: preserve insertion order.
            if (a.visibleIndex != b.visibleIndex) return a.visibleIndex < b.visibleIndex;
            return a.sliceIndex < b.sliceIndex;
        }
        public static void Sort(Request[] requests, float[] distances, int start, int end)
        {
            for (int i = start + 1; i < end; i++)
            {
                var item = requests[i]; int j = i - 1;
                // Original compares all the way to zero, even for start > 0.
                for (; j >= 0 && Before(item, requests[j], distances); j--) requests[j + 1] = requests[j];
                requests[j + 1] = item;
            }
        }
        public static int EstimateScale(Request[] requests, int count, int edge)
        {
            long area = 0, capacity = unchecked(edge * edge);
            for (int i = 0; i < count; i++) area += unchecked(requests[i].requestedResolution * requests[i].requestedResolution);
            int scale = 1;
            while (area > (long)scale * scale * capacity) scale = checked(scale * 2);
            return scale;
        }
        public static int Pack(Request[] requests, int count, int edge, int scale,
            List<RectInt> free, ref bool rescaled, ref bool tooSmall)
        {
            while (true)
            {
                free.Clear(); free.Add(new RectInt(0, 0, edge, edge)); bool retry = false;
                if (scale > 1) rescaled = true;
                for (int i = 0; i < count; i++)
                {
                    var request = requests[i]; int size = request.requestedResolution / scale;
                    if (size < (request.soft ? 16 : 8)) { tooSmall = true; break; }
                    int index = free.FindIndex(r => r.width >= size);
                    if (index < 0) { scale = checked(scale * 2); retry = true; break; }
                    var area = free[index]; free.RemoveAt(index);
                    request.offsetX = area.x; request.offsetY = area.y; request.allocatedResolution = size; requests[i] = request;
                    int remaining = count - i - 1, inserted = 0;
                    for (int y = area.y; y <= area.yMax - size && inserted < remaining; y += size)
                        for (int x = area.x; x <= area.xMax - size && inserted < remaining; x += size)
                        {
                            if (x == area.x && y == area.y) continue;
                            free.Insert(index + inserted++, new RectInt(x, y, size, size));
                        }
                }
                if (!retry) return scale;
            }
        }
        public static int SelectCount(Request[] requests, int requestCount, int maxVisible, int edge, out int scale)
        {
            int count = System.Math.Min(requestCount, SliceBudget(maxVisible)); scale = 1;
            while (count > 0)
            {
                scale = EstimateScale(requests, count, edge);
                var last = requests[count - 1];
                if (last.requestedResolution >= scale * (last.soft ? 16 : 8)) break;
                count -= last.point ? 6 : 1;
            }
            return count;
        }
    }
}
