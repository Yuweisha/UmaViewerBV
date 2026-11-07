using System;
using System.Collections.Generic;
using System.Linq;
using AnimateToUnity;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    [Serializable]
    public sealed class LiveFlashRaceInfo
    {
        public int RaceInstanceId, Weather = 1, RaceType, DateYear, ChampionsMeetingResourceId;
        public string[] CharacterNameArray, TrainerNameArray, CharacterNameArrayForChampionsText, TrainerNameArrayForChampionsText;
        public Texture GoalCaptureTexture;
    }

    public sealed class LiveChampionsTextController : LiveFlashController
    {
        public LiveFlashRaceInfo RaceInfo = new LiveFlashRaceInfo();
        AnMotionInstance firstCharaTrainerMotion, raceTitleWinMotion;
        string raceTitleWinLabel;
        public int ContextTextureCount { get; private set; }

        public override void Initialize(string resourcePath)
        {
            if (RaceInfo.ChampionsMeetingResourceId <= 0)
                throw new InvalidOperationException("Champions Flash requires the race resource ID supplied by Live load settings.");
            base.Initialize(resourcePath);
            raceTitleWinLabel = "in" + RaceInfo.ChampionsMeetingResourceId.ToString("D2");
            firstCharaTrainerMotion = FlashPlayer.Root.FindMotion("MOT_mc_1st_chara_trainer");
            raceTitleWinMotion = FlashPlayer.Root.FindMotion("MOT_mc_race_title_win");
            if (raceTitleWinMotion == null || !raceTitleWinMotion.Parameter._labelParamList.Any(x => x._name == raceTitleWinLabel))
                throw new InvalidOperationException("The installed Champions animation does not support race label " + raceTitleWinLabel + ".");
            var trainers = Enumerable.Range(1, 9).Select(x => $"{x}位のトレーナー名").ToArray();
            var characters = Enumerable.Range(1, 9).Select(x => $"{x}位のウマ娘").ToArray();
            var trainerNames = RaceInfo.TrainerNameArrayForChampionsText?.Length > 0 ? RaceInfo.TrainerNameArrayForChampionsText : RaceInfo.TrainerNameArray;
            var characterNames = RaceInfo.CharacterNameArrayForChampionsText?.Length > 0 ? RaceInfo.CharacterNameArrayForChampionsText : RaceInfo.CharacterNameArray;
            if (trainerNames != null)
                Array.Copy(trainerNames, trainers, System.Math.Min(trainerNames.Length, trainers.Length));
            if (characterNames != null)
                Array.Copy(characterNames, characters, System.Math.Min(characterNames.Length, characters.Length));
            for (int rank = 1; rank <= 9; rank++)
            {
                SetNames(rank, trainers[rank - 1], characters[rank - 1], false);
                if (rank == 1)
                    SetNames(rank, trainers[rank - 1], characters[rank - 1], true);
            }

            ReplaceImage("PLN_dum_text_champions_title", $"Live/Image/Champions/tex_championslive_racename_{RaceInfo.ChampionsMeetingResourceId:D2}");
            ReplaceImage("PLN_dum_text_years_01", $"Live/Image/Champions/tex_championslive_year_{RaceInfo.DateYear - 2022:D2}");
            var year = LoadOnView<Texture2D>($"Live/Image/Champions/tex_championslive_year_l_{RaceInfo.DateYear - 2022:D2}");
            if (year != null)
            {
                ContextTextureCount++;
                FlashPlayer.SetTexture("PLN_dum_text_years_02", year, 0, true, false, null);
                FlashPlayer.SetTexture("PLN_dum_text_years_02_15", year, 0, true, false, null);
            }
        }

        void ReplaceImage(string name, string path)
        {
            var texture = LoadOnView<Texture2D>(path);
            if (texture != null)
            {
                ContextTextureCount++;
                FlashPlayer.SetTexture(name, texture, 0, true, false, null);
            }
        }

        static Color ColorFrom(string html)
        {
            ColorUtility.TryParseHtmlString(html, out var color);
            return color;
        }

        void SetNames(int rank, string trainer, string character, bool mirror)
        {
            SetName(rank, "trainer", trainer, mirror ? "_22" : "");
            SetName(rank, "chara", character, mirror ? "_17" : "");
        }

        void SetName(int rank, string kind, string value, string suffix)
        {
            var root = FlashPlayer.GetObj($"OBJ_txt_{kind}_name_target{rank:D2}{suffix}");
            if (root == null)
                return;
            for (int shadow = 0; shadow < 2; shadow++)
            {
                string target = $"TXT_txt_{kind}_name_target{rank:D2}_{shadow:D2}";
                var node = FlashPlayer.Root.FindObject(target, root.transform);
                if (node?.Text == null)
                    continue;
                node.Text.SetText(value);
                string start = shadow == 0 ? "#FFFFFF" : rank == 1 ? "#CD00EB" : "#2468F3";
                string end = shadow == 0 ? (rank == 1 ? "#F3C3FF" : "#C5CDFF") : (rank == 1 ? "#060E62" : "#5A24F3");
                node.Text.SetGradationColors(ColorFrom(start), ColorFrom(end));
            }
        }

        protected override void AlterUpdatePlayer()
        {
            // Native A2U performs its second/render update independently of the
            // controller's frozen clock. Flush the host's gradient uniforms only
            // after all manual parent/child seeks, without advancing animation.
            foreach (var node in FlashPlayer.Root.Objects)
                node.Text?.UpdateGradation();
        }

        public void PlayIn(float currentTime, LiveTimelineKeyFlashPlayerData key)
        {
            if (!IsInitialized)
                return;

            UpdateFlashSe("in", key);
            FlashActionPlayer.Play("in", currentTime);
            SetLabelTimeAndPlay(raceTitleWinMotion, "in_racetitle_win", "in_rank_1_out", raceTitleWinLabel, currentTime);
            SetLabelTimeAndPlay(firstCharaTrainerMotion, "in_rank_1", "in_rank_1_out", "in", currentTime);
        }

        void SetLabelTimeAndPlay(AnMotionInstance target, string triggerLabel, string stopLabel, string playLabel, float time)
        {
            if (target == null)
                return;
            var labels = FlashPlayer.Root.RootMotion.Parameter._labelParamList;
            var trigger = labels.FirstOrDefault(x => x._name == triggerLabel);
            if (trigger == null)
                return;
            var stop = labels.FirstOrDefault(x => x._name == stopLabel);
            var targetLabel = target.Parameter._labelParamList.FirstOrDefault(x => x._name == playLabel);
            if (targetLabel == null)
            {
                if (target.Clock.State == 0)
                    target.Clock.State = 1;
                return;
            }

            float end = stop != null ? LiveTimelineControl.MonitorFloat32(stop._timeRange.x - FlashPlayer.Root.Clock.SourceFrameTime) : trigger._timeRange.y;
            if (time >= trigger._timeRange.x && time < end)
                FlashActionPlayer.Play(playLabel, LiveTimelineControl.MonitorFloat32(time - trigger._timeRange.x), target.Transform.name);
            else if (target.Clock.State == 0)
                target.Clock.State = 1;
        }

        public override void PlayLabel(string label, float time, object key = null)
        {
            if (label == "in")
                PlayIn(time, key as LiveTimelineKeyFlashPlayerData);
            else
                base.PlayLabel(label, time, key);
        }
    }
}
