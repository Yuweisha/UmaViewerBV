using UnityEngine;
using UnityEngine.UI;

namespace Gallop.Live
{
    // Bind an existing image; creation/resource selection belongs to the original
    // live UI installer. Color access remains a host Unity boundary.
    public sealed class UnityLiveFadeSurface:ILiveFadeSurface
    {
        readonly RawImage _image;
        public UnityLiveFadeSurface(RawImage image){_image=image;}
        public bool IsAlive=>_image!=null;
        public Color Color{get=>_image.color;set=>_image.color=value;}
    }
}
