using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    public partial class CameraData
    {
        // Original Initialize 0x51c8ab0: four Default parameters and four -1 IDs.
        readonly CustomRenderPass.Parameter[] _customRenderParameters = {
            CustomRenderPass.Parameter.Default(), CustomRenderPass.Parameter.Default(),
            CustomRenderPass.Parameter.Default(), CustomRenderPass.Parameter.Default()
        };
        readonly int[] _customRenderParameterReserveIdArray = { -1, -1, -1, -1 };
        // Native fallback is the zero-initialized inline field, not Default().
        CustomRenderPass.Parameter _dummyParameter;

        public ref CustomRenderPass.Parameter GetCustomRenderParameter(out int outReserveId)
        {
            outReserveId = -1;
            for (int i = 0; i < _customRenderParameterReserveIdArray.Length; i++)
            {
                if (_customRenderParameterReserveIdArray[i] >= 0) continue;
                _customRenderParameterReserveIdArray[i] = i;
                _customRenderParameters[i].SetReserveId(i);
                outReserveId = i;
                return ref _customRenderParameters[i];
            }
            return ref _dummyParameter;
        }

        public bool ReleaseCustomRenderParameter(ref int refReserveId)
        {
            int id = refReserveId;
            refReserveId = -1;
            if ((uint)id > 3) return false;
            _customRenderParameterReserveIdArray[id] = -1;
            _customRenderParameters[id] = CustomRenderPass.Parameter.Default();
            return true;
        }

        public bool ReleaseCustomRenderParameter(ref CustomRenderPass.Parameter parameter)
        {
            for (int i = 0; i < _customRenderParameterReserveIdArray.Length; i++)
            {
                int id = _customRenderParameterReserveIdArray[i];
                if (id < 0 || id != parameter.ReserveId) continue;
                ReleaseCustomRenderParameter(ref id);
                parameter.SetReserveId(-1);
                return true;
            }
            return false;
        }

        public void AddRenderPassesCustomRenderPass(ScriptableRenderer renderer, CustomRenderPass[] passes)
        {
            for (int i = 0; i < passes.Length; i++)
                if (passes[i].Setup(this, ref _customRenderParameters[i], renderer))
                    renderer.EnqueuePass(passes[i]);
        }
    }
}
