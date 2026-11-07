using System.Collections.Generic;
using Gallop.Live.Cutt;
using Gallop.RenderPipeline;
using UnityEngine;

namespace Gallop.Live
{
    // Reconstructed Gallop.Model.Component.RendererHolder.OnInitialize 0x51b9910.
    // The original component collects renderers under its ModelControllerBehaviour
    // owner, instantiates one material array per renderer, keeps the first renderer
    // per FNV name hash, records attachment indices by the "M_Attach" substring and
    // classifies each renderer through CG3D.RendererHolder.PARTS_NAME_DIC.
    public sealed class LiveRendererHolder
    {
        public enum PartsType
        {
            None = 0,
            Head = 1,
            Body = 2,
        }

        // Original Gallop.StaticVariableDefine.CG3D.RendererHolder..cctor 0x588f990.
        // Insertion order is the original Add order; M_Head/M_Hair/... map to Head,
        // M_Body/M_Tail map to Body.
        public static readonly Dictionary<string, PartsType> PARTS_NAME_DIC = new Dictionary<string, PartsType>
        {
            { "M_Head", PartsType.Head },
            { "M_Hair", PartsType.Head },
            { "M_Face", PartsType.Head },
            { "M_Cheek", PartsType.Head },
            { "M_Mayu", PartsType.Head },
            { "M_Line", PartsType.Head },
            { "M_Tear", PartsType.Head },
            { "M_Body", PartsType.Body },
            { "M_Tail", PartsType.Body },
            { "M_Eye", PartsType.Head },
            { "M_Mouth", PartsType.Head },
        };

        public Renderer[] RendererArray { get; private set; }
        public Material[][] MaterialArray { get; private set; }
        public Dictionary<int, Renderer> RendererDictionary { get; private set; }
        public PartsType[] PartsTypeArray { get; private set; }
        public List<int> AttachmentIndexList { get; } = new List<int>();

        // Original OnInitialize receives the owner through ModelComponentBase._owner
        // (0x18). The owner is a Component and the renderer query is
        // GetComponentsInChildren<Renderer>(true).
        public void OnInitialize(Component owner)
        {
            Renderer[] renderers = owner.GetComponentsInChildren<Renderer>(true);
            RendererArray = renderers;
            MaterialArray = new Material[renderers.Length][];
            RendererDictionary = new Dictionary<int, Renderer>(renderers.Length);
            PartsTypeArray = new PartsType[renderers.Length];
            AttachmentIndexList.Clear();

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                MaterialArray[i] = RenderUtils.GetMaterialArray(renderer);

                string name = renderer.name;
                int hash = FNVHash.Generate(name);
                if (!RendererDictionary.ContainsKey(hash))
                    RendererDictionary.Add(hash, renderer);

                if (name.Contains("M_Attach"))
                    AttachmentIndexList.Add(i);

                PartsTypeArray[i] = PartsType.None;
                foreach (KeyValuePair<string, PartsType> pair in PARTS_NAME_DIC)
                {
                    if (name.Contains(pair.Key))
                    {
                        PartsTypeArray[i] = pair.Value;
                        break;
                    }
                }
            }
        }

        // Original getters 0x51b9460 and 0x51b9490.
        public Material[] GetMaterialArray(int index) => MaterialArray[index];

        public Material GetMaterial(int index)
        {
            Material[] materials = MaterialArray[index];
            if (materials == null || materials.Length == 0)
                return null;
            return materials[0];
        }

        // Original 0x51b8f00: the dictionary is only a null guard; the search
        // walks the renderer array and compares renderer.gameObject identity.
        public int FindIndex(GameObject gameObject)
        {
            if (RendererDictionary == null)
                return -1;
            for (int i = 0; i < RendererArray.Length; i++)
            {
                if (RendererArray[i].gameObject == gameObject)
                    return i;
            }
            return -1;
        }

        // Original 0x51b8fe0: walks the renderer array and compares names.
        public int FindIndex(string nodeName)
        {
            if (RendererArray == null)
                return -1;
            for (int i = 0; i < RendererArray.Length; i++)
            {
                if (RendererArray[i].name == nodeName)
                    return i;
            }
            return -1;
        }

        // Original 0x51b92e0.
        public Renderer FindRenderer(GameObject rendererObject)
        {
            if (RendererArray == null)
                return null;
            int index = FindIndex(rendererObject);
            return index < 0 ? null : RendererArray[index];
        }

        // Original 0x51b9330: dictionary lookup by precomputed hash.
        public Renderer FindRenderer(int hash)
        {
            if (RendererDictionary == null)
                return null;
            return RendererDictionary.TryGetValue(hash, out Renderer renderer) ? renderer : null;
        }

        // Original 0x51b93c0: hash the name, then dictionary lookup.
        public Renderer FindRenderer(string nodeName)
            => FindRenderer(FNVHash.Generate(nodeName));

        // Original 0x51b91a0: linear search over every material array.
        public Material FindMaterial(string name)
        {
            if (MaterialArray == null)
                return null;
            foreach (Material[] materials in MaterialArray)
            {
                foreach (Material material in materials)
                {
                    if (material.name == name)
                        return material;
                }
            }
            return null;
        }

        // Original 0x51b9270: find the renderer, then return its first material.
        public Material FindMaterial(GameObject rendererObject)
        {
            if (RendererArray == null)
                return null;
            int index = FindIndex(rendererObject);
            if (index < 0)
                return null;
            Material[] materials = MaterialArray[index];
            if (materials == null)
                return null;
            return materials.Length == 0 ? null : materials[0];
        }

        // Original 0x51b9080: append every material whose name matches.
        public void FindMaterialAll(string name, List<Material> buffer)
        {
            if (MaterialArray == null || buffer == null)
                return;
            foreach (Material[] materials in MaterialArray)
            {
                foreach (Material material in materials)
                {
                    if (material.name == name)
                        buffer.Add(material);
                }
            }
        }

        // Original 0x51b94e0: out-of-range or missing array returns None.
        public PartsType GetPartsType(int index)
        {
            if (PartsTypeArray == null || index < 0 || index >= PartsTypeArray.Length)
                return PartsType.None;
            return PartsTypeArray[index];
        }
    }
}
