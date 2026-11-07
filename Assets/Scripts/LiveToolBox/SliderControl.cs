using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SliderControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public bool is_Touched;
    public bool is_Outed;
    public Button PauseButton;
    public bool IsUserPaused { get; private set; }

    private void Awake()
    {
        // Space owns playback state; pointer events only seek.
        if(PauseButton!=null)PauseButton.gameObject.SetActive(false);
    }

    public void TogglePause()
    {
        var director=Gallop.Live.Director.instance;
        if(director==null||!director._isLiveSetup||director.IsOfflineRendering)return;
        if(director.IsPlaybackPaused)
        {
            director.SetPlaybackPaused(false);
            IsUserPaused=false;
        }
        else IsUserPaused=!IsUserPaused;
        is_Touched=IsUserPaused;
        is_Outed=!IsUserPaused;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        is_Outed=false;
        is_Touched=true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if(!is_Touched)return;
        is_Touched=IsUserPaused;
        is_Outed=true;
    }
}
