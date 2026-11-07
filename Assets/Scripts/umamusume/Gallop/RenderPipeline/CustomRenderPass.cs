using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Fixed GameAssembly 0x5208a30/0x5207ea0/0x5208140/0x52084d0.
    // URP14 owns physical targets, execution and stable render-event sorting.
    public sealed class CustomRenderPass : ScriptableRenderPass, IDisposable
    {
        public delegate void ConfigureCallback(CustomRenderPass pass, CommandBuffer cmd, RenderTextureDescriptor descriptor);
        public delegate void CameraCleanupCallback(CustomRenderPass pass, CommandBuffer cmd);
        public delegate void ExecuteCallback(CustomRenderPass pass, ScriptableRenderContext context, ref RenderingData data);

        public struct Parameter
        {
            public bool IsEnable;
            public RenderPassEvent RenderPassEvent;
            public ConfigureCallback OnConfigure;
            public CameraCleanupCallback OnCameraCleanup;
            public ExecuteCallback OnExecute;
            public bool IsNoRendering;
            private int _reserveId;
            public int ReserveId => _reserveId;
            public void SetReserveId(int value) => _reserveId = value;
            public void SetPassName(string _) { } // Native shared RET body.
            public static Parameter Default() => new Parameter { _reserveId = -1 };
        }

        ScriptableRenderer _renderer;
        CameraData _cameraData;
        Parameter _parameter;
        public CameraData CameraData => _cameraData;

        public CustomRenderPass()
        {
            profilingSampler = new ProfilingSampler("CustomRenderPass");
            renderPassEvent = 0;
        }

        public bool Setup(CameraData cameraData, ref Parameter parameter, ScriptableRenderer renderer)
        {
            _cameraData = null;
            _renderer = null;
            if (!parameter.IsEnable) return false;
            renderPassEvent = parameter.RenderPassEvent;
            _parameter = parameter;
            _cameraData = cameraData;
            _renderer = renderer;
            return true;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureClear(ClearFlag.None, Color.black);
            if (_renderer != null && !_parameter.IsNoRendering)
            {
#pragma warning disable CS0618
                ConfigureTarget(_renderer.cameraColorTarget, _renderer.cameraDepthTarget);
#pragma warning restore CS0618
            }
            if (_parameter.OnConfigure != null) _parameter.OnConfigure(this, cmd, cameraTextureDescriptor);
            else base.Configure(cmd, cameraTextureDescriptor);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            _parameter.OnExecute?.Invoke(this, context, ref renderingData);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (_parameter.OnCameraCleanup != null) _parameter.OnCameraCleanup(this, cmd);
            else base.OnCameraCleanup(cmd);
        }

        public void Dispose()
        {
            _parameter.IsEnable = false;
            _parameter.OnCameraCleanup = null;
            _parameter.OnConfigure = null;
            _parameter.OnExecute = null;
        }
    }
}
