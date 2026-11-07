using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public enum LiveFacialPartGroup { Eye, Eyebrow, Mouth }

    // A non-allocating view: authored arrays remain unchanged, and facial-id
    // fallback keeps the native float weight (including half-weight mouths).
    public readonly struct LiveFacialParts
    {
        readonly FacialPartsData[] authored;
        readonly int id;
        readonly float weight;
        readonly int maxId;
        readonly bool fromId;
        internal LiveFacialParts(FacialPartsData[] data, int max)
        { authored = data; id = 0; weight = 0; maxId = max; fromId = false; }
        internal LiveFacialParts(int part, float value)
        { authored = null; id = part; weight = value; maxId = 0; fromId = true; }
        public int Count => fromId ? 1 : authored == null ? 0 : authored.Length;
        public FaceParts this[int index]
        {
            get
            {
                if (fromId)
                {
                    if (index != 0) throw new IndexOutOfRangeException();
                    return new FaceParts { _faceParts = id, _weight = weight };
                }
                var part = authored[index];
                if (part.FacialPartsId < 0 || part.FacialPartsId > maxId)
                    throw new KeyNotFoundException("Original facial part ID: " + part.FacialPartsId);
                // Original 0x4e922b0 multiplies by float32 0.01, and does NOT
                // multiply explicit parts by the key's overall weight.
                return new FaceParts { _faceParts = part.FacialPartsId, _weight = part.WeightPer * 0.01f };
            }
        }
    }

    public static class LiveFacialPartsResolver
    {
        public static bool IsActive(LiveTimelineKey key) => key != null && (((int)key.attribute & 65536) == 0);

        // Native GetFacePartsSetFromFacialId 0x4e8fcd0 reads the compact
        // LiveTimelineDefine.FacialPartsSet table, not MasterFaceTypeData.
        public static bool TryGetFacePartsSet(int facialId, int weightPer, out FacePartsSet parts)
        {
            parts = null;
            if (!LiveFacialIdTable.TryGet(facialId, out var entry)) return false;
            float scale = Mathf.Clamp01(weightPer * 0.01f);
            FaceParts[] One(int id, float weight) => new[] { new FaceParts { _faceParts = id, _weight = weight } };
            parts = new FacePartsSet {
                _eyeR = One(entry[4], scale), _eyeL = One(entry[3], scale),
                _eyebrowR = One(entry[2], scale), _eyebrowL = One(entry[1], scale),
                _mouth = One(entry[5], BitConverter.Int32BitsToSingle(entry[6]) * scale)
            };
            return true;
        }

        public static bool TryResolve(LiveTimelineKey key, LiveFacialPartGroup group, bool left, out LiveFacialParts parts)
        {
            parts = default;
            // Ordinary AutoLip's parent gates by character and mouth ownership;
            // it does not call IsNop before converting its current key.
            if (key==null || (!(key is LiveTimelineKeyLipSyncData)&&!IsActive(key))) return false;
            int facialId, weight;
            FacialPartsData[] data;
            if (key is LiveTimelineKeyFacialEyeData eye)
            { facialId = eye.facialId; weight = eye.weight; data = left ? eye.facialPartsDataArrayL : eye.facialPartsDataArrayR; }
            else if (key is LiveTimelineKeyFacialEyebrowData brow)
            { facialId = brow.facialId; weight = brow.weight; data = left ? brow.facialPartsDataArrayL : brow.facialPartsDataArrayR; }
            else if (key is LiveTimelineKeyFacialMouthData mouth)
            { facialId = mouth.facialId; weight = mouth.weight; data = mouth.facialPartsDataArray; }
            else if (key is LiveTimelineKeyLipSyncData lip&&group==LiveFacialPartGroup.Mouth)
            { facialId = lip.facialId; weight = lip.weight; data = lip.facialPartsDataArray; }
            else if (key is LiveTimelineKeyFacialFaceData face && group != LiveFacialPartGroup.Mouth)
            { facialId = face.facialId; weight = face.weight; data = null; }
            else throw new ArgumentException("Expected an eye, eyebrow or mouth timeline key", nameof(key));

            // UpdateEye calls 0x4f06590 (0x40000). Mouth/eyebrow call
            // 0x4f065b0 (0x20000); these gates are deliberately different.
            int explicitMask = group == LiveFacialPartGroup.Eye ? 262144 : 131072;
            if (!(key is LiveTimelineKeyFacialFaceData) && ((int)key.attribute & explicitMask) != 0)
            {
                parts = new LiveFacialParts(data, group == LiveFacialPartGroup.Eye ? 28 : group == LiveFacialPartGroup.Eyebrow ? 24 : 56);
                return true;
            }
            if (!LiveFacialIdTable.TryGet(facialId, out var entry)) return false;
            int index = group == LiveFacialPartGroup.Eye ? (left ? 3 : 4) : group == LiveFacialPartGroup.Eyebrow ? (left ? 1 : 2) : 5;
            float scale = Mathf.Clamp01(weight * 0.01f);
            if (group == LiveFacialPartGroup.Mouth) scale = BitConverter.Int32BitsToSingle(entry[6]) * scale;
            parts = new LiveFacialParts(entry[index], scale);
            return true;
        }
    }
}
