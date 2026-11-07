using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // CharacterObject.SetScaleFactor 4e7a300 and the loaded LiveModel setter
    // chain 575d700 -> 52a9b90/52ab020/52a9ba0. Resource/prop ownership is
    // separate; each registered sink represents one actual loaded model.
    public sealed class LiveCharacterScaleDispatcher
    {
        public readonly List<LiveModelScaleSink> Models = new List<LiveModelScaleSink>();
        public float Factor { get; private set; } = 1f;

        public LiveModelScaleSink AddModel(UmaContainerCharacter owner, LiveRendererHolder rendererHolder)
        {
            var model = new LiveModelScaleSink(owner);
            model.BindBillboards(rendererHolder);
            Models.Add(model);
            owner.LiveScaleModel = model;
            return model;
        }

        public void SetFactor(float value)
        {
            // Original IsFloatEqualLight compares the OLD value against the
            // rounded closed interval [new-epsilon,new+epsilon].
            float min = LiveTimelineControl.MonitorFloat32(value - 1e-6f), max = LiveTimelineControl.MonitorFloat32(value + 1e-6f);
            if (Factor >= min && Factor <= max)
                return;
            Factor = value;
            for (int i = 0; i < Models.Count; i++)
                Models[i].Apply(value);
        }
    }

    public sealed class LiveModelScaleSink
    {

        public readonly UmaContainerCharacter Owner;
        public readonly LiveFormationPositionAddState PositionAdd = new LiveFormationPositionAddState();
        public readonly List<Material> BodyBillboardMaterials = new List<Material>();
        public readonly List<Material> HairBillboardMaterials = new List<Material>();
        public float Factor { get; private set; } = 1f;
        public float TotalScale => LiveTimelineControl.MonitorFloat32(Factor * Owner.BodyScale);
        public float HeadScale { get; private set; }

        public bool ScaleAdjustEnabled = true;
        public bool UpdateCySpringNodesAndCollision;
        public LiveModelScaleSink(UmaContainerCharacter owner)
        {
            Owner = owner;
            HeadScale = owner.Position != null ? owner.BodyScale : 1f;
        }

        public void BindBillboards(LiveRendererHolder holder)
        {
            BodyBillboardMaterials.Clear();
            HairBillboardMaterials.Clear();
            if (holder?.RendererArray == null)
                return;
            // PartsHolder.InitializeMaterialFromRenderer 5166910: body takes
            // priority, then face, then hair. Body billboard is exact; hair
            // billboard uses IndexOf after excluding hair alpha/prop names.
            for (int i = 0; i < holder.RendererArray.Length; i++)
            {
                string name = holder.RendererArray[i].name;
                if (name.IndexOf("M_Body", StringComparison.Ordinal) >= 0)
                {
                    if (name == "M_Body_Billboard")
                        BodyBillboardMaterials.Add(holder.GetMaterial(i));
                }
                else if (name.IndexOf("M_Face", StringComparison.Ordinal) >= 0)
                    continue;
                else if (name.IndexOf("M_Hair", StringComparison.Ordinal) >= 0 && name.IndexOf("M_Hair_Alpha", StringComparison.Ordinal) < 0 && name.IndexOf("M_Hair_Prop", StringComparison.Ordinal) < 0 && name.IndexOf("M_Hair_Billboard", StringComparison.Ordinal) >= 0)
                    HairBillboardMaterials.Add(holder.GetMaterial(i));
            }
        }

        public void Apply(float requestedFactor)
        {
            float rawTotal = LiveTimelineControl.MonitorFloat32(requestedFactor * Owner.BodyScale);
            Factor = LiveTimelineControl.MonitorFloat32(rawTotal / Owner.BodyScale);
            float total = TotalScale;
            if (Owner.Position != null)
            {
                Owner.Position.localScale = new Vector3(total, total, total);

                if (ScaleAdjustEnabled)
                    HeadScale = total;
            }

            Owner.ApplyLiveModelScalePhysics(total, UpdateCySpringNodesAndCollision);
            int id = ShaderManager.GetPropertyId(ShaderManager.PropertyId._BillboardScale);
            ApplyMaterials(BodyBillboardMaterials, id, 0);
            ApplyMaterials(HairBillboardMaterials, id, 1);
            Owner.FaceDrivenKeyTarget?.LiveEyeTrace?.SetScale(total);
        }

        void ApplyMaterials(List<Material> materials, int id, int group)
        {
            foreach (var material in materials)
            {
                if (material == null)
                    throw new NullReferenceException();
                if (material.HasProperty(id))
                {
                    material.SetFloat(id, Factor);

                }
            }
        }
    }
}
