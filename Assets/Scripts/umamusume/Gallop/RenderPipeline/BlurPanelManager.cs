using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Render-consumer contract for an ORIGINAL BlurPanel owner. This does not
    // create a panel, canvas, geometry, animation control or inferred hierarchy.
    // The game's Graphic/child-canvas lifecycle is a separate pending owner.
    public interface IBlurPanelRenderSource
    {
        GameObject GameObject { get; }
        Canvas SelfCanvas { get; }
        bool Enabled { get; }
        float Blur { get; }
        float Strength { get; }
        Color Color { get; }
        void SetupCanvas();
    }

    public sealed class BlurPanelManager
    {
        sealed class Entry { public IBlurPanelRenderSource Source; public short LayerValue; }
        readonly List<Entry> panels = new List<Entry>();
        static readonly int Temporary = Shader.PropertyToID("BlurPanelManager_RT0");
        public int PanelNum => panels.Count;
        public void Add(IBlurPanelRenderSource source)
        {
            if (panels.Exists(p => ReferenceEquals(p.Source, source))) return;
            panels.Add(new Entry { Source = source });
        }
        public void Remove(IBlurPanelRenderSource source)
        {
            int i = panels.FindIndex(p => ReferenceEquals(p.Source, source));
            if (i >= 0) panels.RemoveAt(i);
        }
        public void Release() => panels.Clear();
        public void SetupRendering()
        {
            foreach (var entry in panels)
            {
                entry.Source.SetupCanvas();
                entry.LayerValue = unchecked((short)SortingLayer.GetLayerValueFromID(entry.Source.SelfCanvas.sortingLayerID));
            }
            panels.Sort((a, b) => Compare(a.Source.SelfCanvas.sortingLayerID, a.LayerValue, a.Source.SelfCanvas.sortingOrder,
                b.Source.SelfCanvas.sortingLayerID, b.LayerValue, b.Source.SelfCanvas.sortingOrder));
        }
        // Native 0x51df600 deliberately returns -1 on equal keys as well.
        public static int Compare(int idA, short valueA, int orderA, int idB, short valueB, int orderB) =>
            idA != idB ? (valueA > valueB ? 1 : -1) : (orderA > orderB ? 1 : -1);

        internal bool OnBlurRendering(ScriptableRenderContext context, ref RenderingData data, CommandBuffer cmd,
            int layerId, short layerValue, GallopUiPass pass, Material material)
        {
            bool handled = false;
            var camera = data.cameraData.camera;
            foreach (var entry in panels)
            {
                var p = entry.Source;
                if (!p.GameObject.activeInHierarchy || p.SelfCanvas.sortingLayerID != layerId) continue;
                // Native 0x51c7f10: matching disabled/zero-strength panels still
                // own the layer and draw its transparent objects, without blur.
                handled = true;
                if (p.Strength > 0 && p.Enabled)
                {
                    var descriptor = data.cameraData.cameraTargetDescriptor;
                    var attachment = pass.colorAttachmentHandle.nameID;
                    cmd.GetTemporaryRT(Temporary, descriptor.width, descriptor.height);
                    material.SetFloat(ShaderManager.GetPropertyId(ShaderManager.PropertyId._BlurWidth), p.Blur);
                    material.SetFloat(ShaderManager.GetPropertyId(ShaderManager.PropertyId._BlurStrength), p.Strength);
                    material.SetColor(ShaderManager.GetPropertyId(ShaderManager.PropertyId._Color), p.Color);
                    cmd.Blit(attachment, Temporary, material, 0);
                    cmd.Blit(Temporary, attachment, material, 1);
                    cmd.ReleaseTemporaryRT(Temporary);
                    context.ExecuteCommandBuffer(cmd); cmd.Clear();
                    // Raw camera projection, exactly as in the native manager.
                    cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
                    cmd.SetRenderTarget(data.cameraData.renderer.cameraColorTargetHandle.nameID,
                        data.cameraData.renderer.cameraDepthTargetHandle.nameID);
                    context.ExecuteCommandBuffer(cmd); cmd.Clear();
                }
                pass.OnRendering(context, ref data, false, new SortingLayerRange(layerValue, layerValue));
            }
            return handled;
        }
    }
}
