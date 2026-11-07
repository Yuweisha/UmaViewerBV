using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Gallop.Live
{
    public partial class Director
    {
        readonly List<LiveMiniCharacter> _miniCharacters=new List<LiveMiniCharacter>();
        public IReadOnlyList<LiveMiniCharacter> MiniCharacters=>_miniCharacters;
        public IEnumerator InitializeMiniCharacters(IList<LiveCharacterLoadData> characters)
        {
            var settings=LiveSettingsResourceMap.Load(live.MusicId);
            if(settings.MiniCharacters.Count==0)yield break;
            var input=characters.Select(c=>new LiveMiniSelection.CharacterInput{
                CharaId=c.CharaEntry.Id,DressIds=new[]{c.MasterDressId},DressColorIds=new[]{0}}).ToArray();
            var selected=new List<LiveMiniSelection.MiniCharacterInfo>();
            LiveMiniSelection.Apply(settings.MiniCharacters,input,input.Length,
                ()=>UmaViewerMain.Instance.Characters.Where(c=>!c.IsMob).OrderBy(_=>UnityEngine.Random.value).Select(c=>c.Id).ToArray(),
                dress=>{var row=UmaDatabaseController.Instance.ReadDressData(dress);return row==null?(int?)null:Convert.ToInt32(row["color_num"]);},selected);
            if(selected.Count==0)yield break;
            // Original load iterator4e6fd70 creates this independent model root;
            // Director._miniCharaCameraTransform is a different field.
            MiniCharaRootTransform=new GameObject("MiniCharaModelRoot").transform;
            MiniCharaRootTransform.SetParent(transform,false);
            CreateMiniCharaCamera(MainRenderCamera.targetTexture);
            InitializeMiniConstraintOwners(selected.Count);
            foreach(var info in selected)
            {
                var root=new GameObject("Live Mini "+_miniCharacters.Count);
                root.transform.SetParent(MiniCharaRootTransform,false);
                var model=UmaViewerBuilder.Instance.LoadLiveMiniUma(info,root.transform);
                // MergeModel destroys duplicate source bones at the frame boundary.
                yield return null;
                var player=root.AddComponent<LiveMiniCharacter>();
                player.Initialize(model);
                InstallMiniConstraintOwner(_miniCharacters.Count,root,player,player.Animation);
                _miniCharacters.Add(player);
            }
            _liveTimelineControl.MiniTransformPlayerResolver=index=>index>=0&&index<_miniCharacters.Count?_miniCharacters[index]:null;
            _liveTimelineControl.MiniAnimationPlayerResolver=index=>index>=0&&index<_miniCharacters.Count?_miniCharacters[index].Animation:null;
            _liveTimelineControl.MiniColorPlayerResolver=index=>index>=0&&index<_miniCharacters.Count?_miniCharacters[index].Color:null;
        }
        void EvaluateMiniCharacters()
        {
            foreach(var player in _miniCharacters)
                player.Evaluate(_liveTimeController!=null?_liveTimeController.DeltaTimePauseReset:Time.deltaTime);
        }
        void ReleaseMiniCharacters()
        {
            if(_liveTimelineControl!=null)
            {
                _liveTimelineControl.MiniTransformPlayerResolver=null;
                _liveTimelineControl.MiniAnimationPlayerResolver=null;
                _liveTimelineControl.MiniColorPlayerResolver=null;
            }
            _miniCharacters.Clear();
            ReleaseMiniConstraintOwners();
        }
    }
}
