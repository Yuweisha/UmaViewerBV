using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // Managed reconstruction of the SetupMiniChara 0x4e68360 selection loop for
    // the exercised type-2 path (type 0/1 branches remain open).
    public sealed class LiveMiniSelectionSetting
    {
        public int Type;
        public int TableIndex;
        public int Index1;
        public int Index2;
        public int Extra;
    }

    public sealed class LiveMiniSelectionMayu
    {
        public int Index;
        public int[] Parameter0 = System.Array.Empty<int>();
        public int[] Parameter1 = System.Array.Empty<int>();
    }

    public sealed class LiveMiniSelectionCandidate
    {
        public int Index;
    }

    public sealed class LiveMiniSelectionResource
    {
        public int Max;
    }

    public interface ILiveMiniSelectionContext
    {
        IReadOnlyList<LiveMiniSelectionCandidate> Candidates { get; }
        IReadOnlyList<LiveMiniSelectionMayu> MayuTable { get; }
        bool IsValid(int value);
        LiveMiniSelectionResource GetResource(int index);
        object Build(int edi, int esi, int ebx);
    }

    public sealed class LiveMiniSelectionLoop
    {
        public readonly List<object> Selected = new List<object>();
        public readonly List<string> Events = new List<string>();

        public void Run(IReadOnlyList<LiveMiniSelectionSetting> settings, ILiveMiniSelectionContext context)
        {
            Selected.Clear();
            for (int i = 0; i < settings.Count; i++)
            {
                var setting = settings[i];
                int edi = -1, esi = -1, ebx = -1;
                Events.Add("GetSetting(" + i + ")");
                switch (setting.Type)
                {
                    case 1:
                    {
                        int index = setting.TableIndex - 1;
                        if (context.MayuTable == null || index < 0 || index >= context.MayuTable.Count) continue;
                        var mayu = context.MayuTable[index];
                        if (mayu.Parameter0.Length == 0 || mayu.Parameter1.Length == 0) continue;
                        edi = mayu.Index; esi = mayu.Parameter0[0]; ebx = mayu.Parameter1[0];
                        if (setting.Index1 > -1) edi = setting.Index1;
                        if (setting.Index2 > -1) esi = setting.Index2;
                        if (setting.Extra >= 0) ebx = setting.Extra;
                        break;
                    }
                    case 2:
                        edi = setting.Index1; esi = setting.Index2; ebx = setting.Extra;
                        break;
                    default:
                        // type 0 keeps edi/esi/ebx at -1 and still runs the
                        // validity check like the original body.
                        break;
                }
                if (edi == 1)
                {
                    if (context.Candidates == null || context.Candidates.Count <= 0) continue;
                    var candidate = context.Candidates[i % context.Candidates.Count];
                    edi = candidate.Index;
                }
                Events.Add("Validity(" + edi + ")");
                if (!context.IsValid(edi)) continue;
                if (esi <= -1) continue;
                var resource = context.GetResource(esi);
                Events.Add("Resource(" + esi + ")");
                if (resource == null) continue;
                if (ebx < 0) ebx = 0;
                else if (resource.Max > 1 && ebx >= resource.Max) ebx %= resource.Max;
                Events.Add("Build(" + edi + "," + esi + "," + ebx + ")");
                Selected.Add(context.Build(edi, esi, ebx));
                Events.Add("ListAdd");
            }
        }
    }
}
