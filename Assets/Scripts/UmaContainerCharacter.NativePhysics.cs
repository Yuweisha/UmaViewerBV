using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gallop;
using UnityEngine;

public enum CharacterPhysicsBackend { CySpringNative = 0, DynamicBone = 1 }

public partial class UmaContainerCharacter
{
    // Selected when the body is loaded. Changing Config requires reloading the character.
    public CharacterPhysicsBackend ActivePhysicsBackend { get; private set; }
    private CySpringController _cySpringController;
    private SkirtController _skirtController;
    private CySpringOwner _cySpringOwner;
    private bool _cySpringLoaded;

    private sealed class UmaViewerCySpringOwner : CySpringOwner
    {
        private readonly UmaContainerCharacter _chara;

        public UmaViewerCySpringOwner(UmaContainerCharacter chara)
            : base(chara!=null?chara.LiveClothAssets:null)
        {
            _chara = chara;
        }

        public override bool IsCharaModel()
        {
            return true;
        }

        public override int GetCharaID()
        {
            return _chara != null && _chara.CharaEntry != null ? _chara.CharaEntry.Id : 0;
        }

        public override int GetDressID()
        {
            return 0;
        }

        public override int GetHeadID()
        {
            return 0;
        }

        public override float GetCySpringCorrectScale(bool isHead)
        {
            if(_chara!=null&&_chara.IsLive)return isHead?_chara.BodyScale:1f;
            if (_chara == null || _chara.BodyScale <= 0.0f)
                return 1.0f;

            return _chara.BodyScale;
        }

        public override float GetBodyScale()
        {
            if(_chara!=null&&_chara.IsLive)return _chara.BodyScale;
            if (_chara == null || _chara.BodyScale <= 0.0f)
                return 1.0f;

            return _chara.BodyScale;
        }

        public override float GetTotalScale()
        {
            if(_chara!=null&&_chara.IsLive)return _chara.LiveTotalScale;
            if (_chara == null || _chara.BodyScale <= 0.0f)
                return 1.0f;

            return _chara.BodyScale;
        }
    }

    private void LoadNativePhysics()
    {
        if (IsMini)
            return;

        if(IsLive){LoadLiveNativePhysics();return;}

        if (PhysicsContainer == null)
        {
            Debug.LogWarning("[UmaContainerCharacter] PhysicsContainer is null.");
            return;
        }

        cySpringDataContainers = new List<CySpringDataContainer>(
            PhysicsContainer.GetComponentsInChildren<CySpringDataContainer>(true)
        );

        if (cySpringDataContainers == null || cySpringDataContainers.Count == 0)
        {
            Debug.LogWarning("[UmaContainerCharacter] No CySpringDataContainer found.");
            return;
        }

        Dictionary<string, Transform> transformCacheDic =
            BuildCySpringTransformCache(transform, out HashSet<string> duplicateBoneNames);

        if (duplicateBoneNames.Count > 0)
        {
            Debug.LogWarning(
                $"[UmaContainerCharacter] Duplicate bone names detected before CySpring bind: {duplicateBoneNames.Count}\n" +
                $"{string.Join("\n", duplicateBoneNames.Take(30))}"
            );
        }

        Transform hip = FindCySpringTransform(transform, "Hip");

        if (hip == null && UpBodyBone != null)
            hip = UpBodyBone.transform;

        if (hip == null)
            hip = transform.Find("Position/Hip");

        if (hip == null)
        {
            Debug.LogWarning("[UmaContainerCharacter] Hip bone not found. CySpring will use character root as fallback.");
            hip = transform;
        }

        var slots = SplitCySpringContainers(cySpringDataContainers);


        _cySpringOwner = new UmaViewerCySpringOwner(this);
        _cySpringController = CySpringController.AddController(gameObject, hip, _cySpringOwner);

        if (_cySpringController == null)
        {
            Debug.LogError("[UmaContainerCharacter] Failed to create CySpringController.");
            _cySpringLoaded = false;
            return;
        }

        _cySpringController.LoadFromDataContainers(
            transformCacheDic,
            slots.head,
            slots.body,
            slots.bust,
            slots.tail,
            null,
            null,
            null,
            BodyScale,
            BodyScale,
            BodyScale
        );

        _cySpringController.Reset();
        _cySpringLoaded = true;
        _cySpringController.SetEnableAllCySpringBones(EnablePhysics);

        LinkSkirtControllerToCySpring();

        Debug.Log(
            "[UmaContainerCharacter] CySpring loaded. " +
            $"head={(slots.head ? slots.head.name : "null")}, " +
            $"body={(slots.body ? slots.body.name : "null")}, " +
            $"bust={(slots.bust ? slots.bust.name : "null")}, " +
            $"tail={(slots.tail ? slots.tail.name : "null")}"
        );
    }

    private Dictionary<string, Transform> BuildCySpringTransformCache(Transform root, out HashSet<string> duplicateBoneNames)
    {
        Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        duplicateBoneNames = new HashSet<string>();

        if (root == null)
            return bones;

        Transform[] allBones = root.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < allBones.Length; i++)
        {
            Transform bone = allBones[i];

            if (bone == null || string.IsNullOrEmpty(bone.name))
                continue;

            if (!bones.ContainsKey(bone.name))
            {
                bones.Add(bone.name, bone);
            }
            else
            {
                duplicateBoneNames.Add(bone.name);
            }
        }

        return bones;
    }

    private Transform FindCySpringTransform(Transform root, string boneName)
    {
        if (root == null || string.IsNullOrEmpty(boneName))
            return null;

        Transform[] allBones = root.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < allBones.Length; i++)
        {
            if (allBones[i] != null && allBones[i].name == boneName)
                return allBones[i];
        }

        return null;
    }

    private void LinkSkirtControllerToCySpring()
    {
        if (_cySpringController == null || _skirtController == null)
            return;

    _cySpringController.CreateLinkSkirtController(_skirtController);
    }

    private void LoadSkirtController(UmaDatabaseEntry bodyEntry)
    {
        // Live defers exact category skirt selection until head/tail loading
        // has supplied its complete identity. Preview retains its own path.
        if(IsLive)return;
        if (IsMini || Body == null || bodyEntry == null)
            return;

        SkirtParamDataAsset skirtParam = LoadBestSkirtParamAsset(bodyEntry);
        if (skirtParam == null)
        {
            Debug.LogWarning($"[UmaContainerCharacter] No SkirtParamDataAsset found for {bodyEntry.Name}.");
            return;
        }

        _skirtController = GetComponent<SkirtController>();
        if (_skirtController == null)
            _skirtController = gameObject.AddComponent<SkirtController>();

        _skirtController.CharaId = CharaEntry != null ? CharaEntry.Id : 0;
        _skirtController.ModelScale = BodyScale > 0f ? BodyScale : 1f;
        _skirtController.Create(skirtParam);
        _skirtController.IsEnableSkirt = EnablePhysics;

        if (_cySpringController != null)
            LinkSkirtControllerToCySpring();

        Debug.Log(
            $"[UmaContainerCharacter] SkirtController loaded param={skirtParam.name}, " +
            $"charaId={_skirtController.CharaId}, body={bodyEntry.Name}");
    }

    private SkirtParamDataAsset LoadBestSkirtParamAsset(UmaDatabaseEntry bodyEntry)
    {
        List<UmaDatabaseEntry> candidates = FindSkirtParamEntries(bodyEntry).ToList();
        if (candidates.Count == 0)
            return null;

        int charaId = CharaEntry != null ? CharaEntry.Id : 0;
        SkirtParamDataAsset best = null;
        int bestScore = -1;
        bool bestHasCharaOverride = false;

        for (int i = 0; i < candidates.Count; i++)
        {
            UmaDatabaseEntry entry = candidates[i];
            if (entry == null)
                continue;

            AssetBundle bundle = UmaAssetManager.LoadAssetBundle(entry, false, true);
            if (bundle == null)
                continue;

            SkirtParamDataAsset asset = LoadSkirtParamAssetFromBundle(bundle, entry);
            if (asset == null)
            {
                Debug.LogWarning($"[UmaContainerCharacter] Skirt AB has no SkirtParamDataAsset: {entry.Name}");
                continue;
            }

            int score = ScoreSkirtParamAsset(asset);
            bool hasCharaOverride = charaId > 0 && asset.GetOverrideData(charaId) != null;

            if (score > bestScore || (score == bestScore && hasCharaOverride && !bestHasCharaOverride))
            {
                best = asset;
                bestScore = score;
                bestHasCharaOverride = hasCharaOverride;
            }

            Debug.Log(
                $"[UmaContainerCharacter] Skirt candidate {asset.name}: " +
                $"score={score}, charaOverride={hasCharaOverride}, entry={entry.Name}");
        }

        return best;
    }

    private IEnumerable<UmaDatabaseEntry> FindSkirtParamEntries(UmaDatabaseEntry bodyEntry)
    {
        if (Main == null || Main.AbChara == null || bodyEntry == null)
            yield break;

        HashSet<string> bodyTokens = GetSkirtBodyTokens(bodyEntry);
        if (bodyTokens.Count == 0)
            yield break;

        foreach (UmaDatabaseEntry entry in Main.AbChara)
        {
            if (entry == null || !entry.IsAssetBundle)
                continue;

            string assetName = Path.GetFileName(entry.Name);
            if (string.IsNullOrEmpty(assetName))
                continue;

            string lower = assetName.ToLowerInvariant();
            if (!lower.Contains("_skirt"))
                continue;

            foreach (string bodyToken in bodyTokens)
            {
                string expectedPrefix = "ast_" + bodyToken.ToLowerInvariant() + "_skirt";
                if (lower.StartsWith(expectedPrefix))
                {
                    yield return entry;
                    break;
                }
            }
        }
    }

    private HashSet<string> GetSkirtBodyTokens(UmaDatabaseEntry bodyEntry)
    {
        HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string prefabName = Path.GetFileName(bodyEntry.Name);
        if (!string.IsNullOrEmpty(prefabName) && prefabName.StartsWith("pfb_", StringComparison.OrdinalIgnoreCase))
        {
            string token = prefabName.Substring(4);
            AddBodyTokenVariants(result, token);
        }

        string directoryName = Path.GetFileName(Path.GetDirectoryName(bodyEntry.Name));
        AddBodyTokenVariants(result, directoryName);

        return result;
    }

    private void AddBodyTokenVariants(HashSet<string> result, string token)
    {
        if (result == null || string.IsNullOrEmpty(token))
            return;

        if (!token.StartsWith("bdy", StringComparison.OrdinalIgnoreCase))
            return;

        result.Add(token);

        string[] parts = token.Split('_');
        if (parts.Length >= 2)
            result.Add(parts[0] + "_" + parts[1]);
    }

    private SkirtParamDataAsset LoadSkirtParamAssetFromBundle(AssetBundle bundle, UmaDatabaseEntry entry)
    {
        if (bundle == null)
            return null;

        string assetName = entry != null ? Path.GetFileName(entry.Name) : null;
        if (!string.IsNullOrEmpty(assetName) && bundle.Contains(assetName))
        {
            SkirtParamDataAsset direct = bundle.LoadAsset<SkirtParamDataAsset>(assetName);
            if (direct != null)
                return direct;
        }

        SkirtParamDataAsset[] allAssets = bundle.LoadAllAssets<SkirtParamDataAsset>();
        return allAssets != null && allAssets.Length > 0 ? allAssets[0] : null;
    }

    private int ScoreSkirtParamAsset(SkirtParamDataAsset asset)
    {
        if (asset == null || asset._skirtBone == null || Body == null)
            return 0;

        int score = 0;
        Transform root = Body.transform;

        for (int i = 0; i < asset._skirtBone.Count; i++)
        {
            SkirtParamDataElement element = asset._skirtBone[i];
            if (element == null)
                continue;

            if (FindCySpringTransform(root, element._rootBoneName) != null &&
                FindCySpringTransform(root, element._childBoneName) != null)
            {
                score++;
            }
        }

        return score;
    }

    private (
        CySpringDataContainer head,
        CySpringDataContainer body,
        CySpringDataContainer bust,
        CySpringDataContainer tail
    ) SplitCySpringContainers(List<CySpringDataContainer> containers)
    {
        CySpringDataContainer head = null;
        CySpringDataContainer body = null;
        CySpringDataContainer bust = null;
        CySpringDataContainer tail = null;

        if (containers == null)
            return (head, body, bust, tail);

        for (int i = 0; i < containers.Count; i++)
        {
            CySpringDataContainer container = containers[i];

            if (container == null)
                continue;

            // Collision-only fallback data is not a spring slot.
            if (container.springParam == null || container.springParam.Count == 0)
                continue;

            int kind = GuessCySpringContainerKind(container);

            switch (kind)
            {
                case 0:
                    if (head == null)
                        head = container;
                    else
                        Debug.LogWarning($"[UmaContainerCharacter] Extra head CySpringDataContainer ignored: {container.name}");
                    break;

                case 1:
                    if (body == null)
                        body = container;
                    else
                        Debug.LogWarning($"[UmaContainerCharacter] Extra body CySpringDataContainer ignored: {container.name}");
                    break;

                case 2:
                    if (bust == null)
                        bust = container;
                    else
                        Debug.LogWarning($"[UmaContainerCharacter] Extra bust CySpringDataContainer ignored: {container.name}");
                    break;

                case 3:
                    if (tail == null)
                        tail = container;
                    else
                        Debug.LogWarning($"[UmaContainerCharacter] Extra tail CySpringDataContainer ignored: {container.name}");
                    break;
            }
        }

        if (body == null && containers.Count == 1)
            body = containers[0];

        return (head, body, bust, tail);
    }

    private int GuessCySpringContainerKind(CySpringDataContainer container)
    {
        if (container == null)
            return 1;

        string assetName = container.name.ToLowerInvariant();
        if (assetName.Contains("bust")) return 2;
        if (assetName.StartsWith("pfb_tail")) return 3;
        if (assetName.StartsWith("pfb_chr") || assetName.StartsWith("pfb_mchr")) return 0;
        if (assetName.StartsWith("pfb_bdy") || assetName.StartsWith("pfb_mbdy")) return 1;

        string path = GetTransformPath(container.transform).ToLowerInvariant();

        if (path.Contains("tail"))
            return 3;

        if (path.Contains("bust") || path.Contains("breast") || path.Contains("mune"))
            return 2;

        if (path.Contains("head") || path.Contains("hair") || path.Contains("ear"))
            return 0;

        if (path.Contains("body") || path.Contains("skirt"))
            return 1;

        if (container.springParam != null)
        {
            for (int i = 0; i < container.springParam.Count; i++)
            {
                CySpringParamDataElement element = container.springParam[i];

                if (element == null)
                    continue;

                int kind = GuessCySpringBoneKind(element._boneName);

                if (kind >= 0)
                    return kind;

                if (element._childElements == null)
                    continue;

                for (int j = 0; j < element._childElements.Count; j++)
                {
                    CySpringParamDataChildElement child = element._childElements[j];

                    if (child == null)
                        continue;

                    kind = GuessCySpringBoneKind(child._boneName);

                    if (kind >= 0)
                        return kind;
                }
            }
        }

        return 1;
    }

    private int GuessCySpringBoneKind(string boneName)
    {
        if (string.IsNullOrEmpty(boneName))
            return -1;

        string name = boneName.ToLowerInvariant();

        if (name.Contains("tail"))
            return 3;

        if (name.Contains("bust") || name.Contains("breast") || name.Contains("mune"))
            return 2;

        if (name.Contains("head") || name.Contains("hair") || name.Contains("ear"))
            return 0;

        if (name.Contains("skirt") || name.Contains("cloth") || name.Contains("body"))
            return 1;

        return -1;
    }

    private string GetTransformPath(Transform t)
    {
        if (t == null)
            return string.Empty;

        string path = t.name;
        Transform parent = t.parent;

        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

}
