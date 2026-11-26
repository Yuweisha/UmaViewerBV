using UnityEngine;

namespace Gallop.CutIn
{
    // Asset references on CutIn background prefabs.
    public sealed class CutInBgModel : MonoBehaviour
    {
        [SerializeField] private GameObject[] _audienceObjectArray;
        public AnimationClip[] AudienceAnimationArray;

        public GameObject GetAudiencePrefab(int index) =>
            _audienceObjectArray != null && index >= 0 && index < _audienceObjectArray.Length
                ? _audienceObjectArray[index] : null;
    }
}
