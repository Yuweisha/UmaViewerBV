using System;
using UnityEngine;

namespace Gallop.Live
{
    public interface ILiveBlackBoardGlyphTarget<T> where T : class
    { T Renderer { get; } T Material { get; } }
    public interface ILiveBlackBoardGlyphServices<T> where T : class
    {
        bool ObjectEquals(T a, T b);
        T GetCurrentCulture(); void TouchCompareInfo(T culture);
        T NewGameObject(); T GetTransform(T obj, bool component);
        void SetParent(T transform, T parent); int GetLayer(int index); void SetLayer(T obj, int layer);
        string Substring(string value, int start, int length); int IndexOf(string value, string part);
        string FormatSpriteName(int index); T GetSprite(T atlas, string name);
        T AddImageCommon(T obj); void EnsureImageType(T image);
        void SetSprite(T image, T sprite); void SetNativeSize(T image); T GetRectTransform(T image);
        void SetLocalScale(T transform, Vector3 value); void SetLocalPosition(T transform, Vector3 value);
        Vector3 GetLocalPosition(T transform); void Render(T camera);
        T NewTexture(int width, int height, int format, bool mipChain);
        T GetActiveRT(); void SetActiveRT(T rt); void ReadPixels(T texture, Rect rect, int x, int y);
        void Apply(T texture); void Discard(T rt); int GetTargetCount(T targets);
        ILiveBlackBoardGlyphTarget<T> GetTarget(T targets, int index);
        int GetPropertyId(int index); void SetTexture(T material, int property, T texture);
        void SetSharedMaterial(T renderer, T material); void SetActive(T obj, bool active);
    }
    // Full native CreateNameTexture (4e62d40). String/culture, sprite/image,
    // rendering and engine properties are explicit services.
    public sealed class LiveBlackBoardGlyphControl<T> where T : class
    {
        public const string Characters = "ーアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲンガギグゲゴザジズゼゾダヂヅデドバビブベボパピプペポァィゥェォッャュョヮヴ";
        public const string SmallCharacters = "ァィゥェォッャュョヮ";
        readonly ILiveBlackBoardGlyphServices<T> source;
        public LiveBlackBoardResources<T> Resources;
        public Vector3 TextScale = new Vector3(.9f,.9f,1f);
        public LiveBlackBoardGlyphControl(ILiveBlackBoardGlyphServices<T> source) { this.source = source; }
        static T Require(T value) => value ?? throw new NullReferenceException();
        T Image(int index) => Require(Resources.Images[index]);
        public T CreateNameTexture(string name)
        {
            if (source.ObjectEquals(Resources.Atlas, null)) return null;
            int length = name.Length;
            T[] objects = new T[length];
            Resources.Images = new T[length];
            source.TouchCompareInfo(Require(source.GetCurrentCulture()));
            float y = 0;
            for (int i=0;i<length;i++)
            {
                objects[i] = source.NewGameObject();
                T transform = source.GetTransform(Require(objects[i]), false);
                T parent = source.GetTransform(Require(Resources.Canvas), true);
                source.SetParent(Require(transform), parent);
                T objForLayer = objects[i];
                int layer = source.GetLayer(11);
                source.SetLayer(Require(objForLayer), layer);
                string character = source.Substring(name, i, 1);
                bool small = source.IndexOf(SmallCharacters, character) >= 0;
                float step = small ? 25f : 28f, x = small ? 2f : 0f;
                int index = source.IndexOf(Characters, character);
                T atlas = Resources.Atlas;
                string spriteName = source.FormatSpriteName(index);
                T sprite = source.GetSprite(Require(atlas), spriteName);
                T[] images = Resources.Images;
                T image = source.AddImageCommon(Require(objects[i]));
                if (images == null) throw new NullReferenceException();
                if (image != null) source.EnsureImageType(image);
                images[i] = image;
                source.SetSprite(Image(i), sprite);
                source.SetNativeSize(Image(i));
                T rect = source.GetRectTransform(Image(i));
                source.SetLocalScale(Require(rect), TextScale);
                rect = source.GetRectTransform(Image(i));
                source.SetLocalPosition(Require(rect), new Vector3(x, y, 0));
                y = y - step;
            }
            for (int i=0;i<length;i++)
            {
                T rect = source.GetRectTransform(Image(i));
                Vector3 p = source.GetLocalPosition(Require(rect));
                p.y = p.y - (-114f);
                rect = source.GetRectTransform(Image(i));
                source.SetLocalPosition(Require(rect), p);
            }
            source.Render(Require(Resources.Camera));
            T texture = source.NewTexture(32,256,5,false);
            T previousRT = source.GetActiveRT();
            source.SetActiveRT(Resources.RenderTexture);
            source.ReadPixels(Require(texture),new Rect(0,0,32,256),0,0);
            source.Apply(texture);
            source.SetActiveRT(previousRT);
            source.Discard(Require(Resources.RenderTexture));
            int count = source.GetTargetCount(Require(Resources.Targets));
            for (int i=0;i<count;i++)
            {
                var target = source.GetTarget(Require(Resources.Targets),i);
                if (target == null) throw new NullReferenceException();
                T material = target.Material;
                int property = source.GetPropertyId(42);
                source.SetTexture(Require(material),property,Resources.RenderTexture);
                source.SetSharedMaterial(Require(target.Renderer),target.Material);
            }
            source.SetActive(Require(Resources.CanvasObject),false);
            source.SetActive(Require(Resources.CameraObject),false);
            return texture;
        }
    }
}
