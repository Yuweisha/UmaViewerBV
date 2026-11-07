using UnityEngine;
namespace Gallop.Live
{
    // Serialized identity and fields from the source stage prefabs.
    // Native Start 0x5072000 / UpdateRenderer 0x5072110.
    public sealed class RendererController:MonoBehaviour
    {
        [SerializeField] int _lightmapIndex=-1;
        [SerializeField] Vector4 _lightmapScaleOffset=new Vector4(1,1,0,0);
        [SerializeField] Vector4 _realtimeLightmapScaleOffset=new Vector4(1,1,0,0);
        [SerializeField] int _sortingOrder;
        void Start()=>UpdateRenderer(GetComponent<Renderer>());
        public void UpdateRenderer(Renderer renderer)
        {
            if(renderer==null)return;
            renderer.lightmapIndex=_lightmapIndex;
            renderer.lightmapScaleOffset=_lightmapScaleOffset;
            renderer.realtimeLightmapScaleOffset=_realtimeLightmapScaleOffset;
            renderer.sortingOrder=_sortingOrder;
        }
    }
}
