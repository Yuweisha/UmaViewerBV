using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Live 标签下 Gacha 分页的内容：育成卡 CutIn 演出。
///
/// 演出本身是实时演算，资源在 3d/motion/cutin/card/ 下：
///   body/crd{cardId}_001/anm_cti_crd{cardId}_001_01..NN   身体动作（多段）
///   camera/..._NN_cam   摄像机      facial/..._NN_face   表情
///   position/..._NN_pos  站位        bg/..._bg            背景
///
/// UmaContainerCharacter.LoadAnimation 已经认识 "_cti_crd"：加载第 01 段时会自动把后续段
/// 和 position/camera 动作一起预加载，所以这里只要把第 01 段交给它即可。
/// </summary>
public partial class UmaViewerUI
{
    private const string CutinCardMotionRoot = "3d/motion/cutin/card/body/";
    private ScrollRect _gachaList;
    private GameObject _gachaStage;
    private string _gachaStagePath;

    /// <summary>把育成卡 CutIn 演出填进 Gacha 分页。</summary>
    public void LoadGachaPanels()
    {
        _gachaList = FindGachaListScrollRect();

        if (_gachaList == null)
        {
            Debug.LogWarning("[Gacha] 没找到 Gacha List 下的 ScrollRect，分页保持空白");
            return;
        }

        for (int i = _gachaList.content.childCount - 1; i >= 0; i--)
            Destroy(_gachaList.content.GetChild(i).gameObject);

        var cards = new List<UmaDatabaseEntry>();
        var seen = new HashSet<int>();

        foreach (var entry in Main.AbMotions)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Name))
                continue;

            if (!entry.Name.StartsWith(CutinCardMotionRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!entry.Name.EndsWith("_01", StringComparison.OrdinalIgnoreCase))
                continue;

            int slash = entry.Name.LastIndexOf('/');
            string file = slash >= 0 ? entry.Name.Substring(slash + 1) : entry.Name;

            // anm_cti_crd{cardId}_001_01
            string[] parts = file.Split('_');

            if (parts.Length < 5)
                continue;

            string cardPart = parts[2];               // crd100401

            if (!cardPart.StartsWith("crd", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!int.TryParse(cardPart.Substring(3), out int cardId))
                continue;

            if (!seen.Add(cardId))
                continue;

            cards.Add(entry);
        }

        cards.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        Debug.Log($"[Gacha] 找到 {cards.Count} 张带 CutIn 的育成卡");

        var pageEntries = new List<PageManager.Entry>();

        foreach (var clip in cards)
        {
            int slash = clip.Name.LastIndexOf('/');
            string file = slash >= 0 ? clip.Name.Substring(slash + 1) : clip.Name;
            string cardPart = file.Split('_')[2];      // crd100401
            int cardId = int.Parse(cardPart.Substring(3));

            var entry = new PageManager.Entry
            {
                Name = cardId.ToString(),
                FontSize = 20,
            };

            var captured = clip;
            entry.OnClick = container =>
            {
                HighlightChildImage(_gachaList.content, container);
                PlayGachaCutin(cardId, captured);
            };

            pageEntries.Add(entry);
        }

        // Gacha 分页没有自己的 PageManager 引用，直接铺到 content 里
        foreach (var pageEntry in pageEntries)
        {
            var container = Instantiate(UmaContainerPrefab, _gachaList.content).GetComponent<UmaUIContainer>();
            container.Name = pageEntry.Name;
            container.Button.onClick.AddListener(() => pageEntry.OnClick(container));
        }
    }

    /// <summary>加载卡对应的角色并播放它的 CutIn 第 01 段。</summary>
    private void PlayGachaCutin(int cardId, UmaDatabaseEntry clip)
    {
        int charaId = cardId / 100;         // crd100401 -> chara 1004
        int bgId = 0;

        // card_data 给出权威的 chara_id / bg_id（bg_id 决定 CutIn 用哪套舞台）
        var cards = UmaDatabaseController.Instance != null
            ? UmaDatabaseController.Instance.CardData
            : null;

        if (cards != null)
        {
            var row = cards.Find(r => Convert.ToInt32(r["id"]) == cardId);

            if (row != null)
            {
                charaId = Convert.ToInt32(row["chara_id"]);
                bgId = Convert.ToInt32(row["bg_id"]);
            }
        }

        string stagePath = $"3d/env/cutin/cutin{charaId}_{bgId:00}/pfb_env_cutin{charaId}_{bgId:00}_00";

        CharaEntry chara = Main.Characters != null
            ? Main.Characters.FirstOrDefault(c => c.Id == charaId)
            : null;

        if (chara == null)
        {
            ShowMessage($"找不到角色 {charaId}（卡 {cardId}）", UIMessageType.Warning);
            return;
        }

        CostumeEntry costume = Main.Costumes != null
            ? Main.Costumes.FirstOrDefault(c => c.CharaId == charaId)
            : null;

        string costumeId = costume != null ? costume.Id : string.Empty;
        Debug.Log($"[Gacha] 卡 {cardId} -> 角色 {charaId} 服装 {(string.IsNullOrEmpty(costumeId) ? "<默认>" : costumeId)} 动作 {clip.Name}");

        var load = new List<UmaDatabaseEntry>();
        load.AddRange(Main.AbChara.Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath)
                                           && a.Name.Contains(charaId.ToString())));
        load.AddRange(Main.AbChara.Where(a => a.Name.StartsWith(UmaDatabaseController.HeadPath)
                                           && a.Name.Contains(charaId.ToString())));
        load.Add(clip);

        if (Main.AbList.TryGetValue("3d/animator/drivenkeylocator", out var locator))
            load.Add(locator);

        UmaDatabaseEntry stageEntry = null;

        if (Main.AbList.TryGetValue(stagePath, out stageEntry) && stageEntry != null)
        {
            load.Add(stageEntry);
            Debug.Log($"[Gacha] 舞台: {stagePath}");
        }
        else
        {
            Debug.LogWarning($"[Gacha] 找不到舞台 {stagePath}（chara={charaId} bg={bgId}），只放角色");
        }

        Builder.UnloadUma();
        UmaAssetManager.PreLoadAndRun(load, delegate
        {
            StartCoroutine(Builder.LoadUma(chara, costumeId, false, CurrentHeadCostumeId));

            // LoadUma 是协程，等容器出现再把动作塞给它
            StartCoroutine(PlayWhenReady(clip, stagePath));
        });
    }

    private System.Collections.IEnumerator PlayWhenReady(UmaDatabaseEntry clip, string stagePath)
    {
        SpawnStage(stagePath);

        for (int i = 0; i < 240; i++)
        {
            var container = Builder.CurrentUMAContainer;

            if (container != null)
            {
                container.LoadAnimation(clip);
                LoadedAnimation();
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning("[Gacha] 角色加载超时，未能播放 " + clip.Name);
    }

    /// <summary>换上 CutIn 的舞台 prefab，换卡时销毁旧的。</summary>
    private void SpawnStage(string stagePath)
    {
        if (string.IsNullOrEmpty(stagePath))
            return;

        if (_gachaStage != null && _gachaStagePath == stagePath)
            return;

        if (_gachaStage != null)
        {
            Destroy(_gachaStage);
            _gachaStage = null;
        }

        try
        {
            _gachaStage = Builder.LoadAssetPath(stagePath, null);
            _gachaStagePath = stagePath;
        }
        catch (Exception error)
        {
            Debug.LogWarning($"[Gacha] 舞台实例化失败 {stagePath}: {error.Message}");
            _gachaStagePath = null;
        }
    }

    /// <summary>Gacha List 面板下的 ScrollRect（场景里没有对应的 public 字段）。</summary>
    private ScrollRect FindGachaListScrollRect()
    {
        if (_gachaList != null)
            return _gachaList;

        foreach (var rect in GetComponentsInChildren<ScrollRect>(true))
        {
            Transform t = rect.transform;

            for (int depth = 0; depth < 4 && t != null; depth++)
            {
                if (t.name.IndexOf("Gacha", StringComparison.OrdinalIgnoreCase) >= 0)
                    return rect;

                t = t.parent;
            }
        }

        return null;
    }
}
