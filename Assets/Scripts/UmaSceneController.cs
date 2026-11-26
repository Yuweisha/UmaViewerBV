using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UmaSceneController:MonoBehaviour
{
    public static UmaSceneController instance;
    public GameObject CavansPrefab;
    public GameObject CavansInstance;

    public GameObject LoadingProgressPanel;
    public Slider LoadingProgressSlider;
    public TextMeshProUGUI LoadingProgressText;

    private void Awake()
    {
        if (instance)
        {
            DestroyImmediate(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(this);
    }

    private void Start()
    {
        UmaAssetManager.OnLoadProgressChange += LoadingProgressChange;
    }

    public static void LoadScene(string name, Action OnSceneloaded = null, Action OnPrevSceneUnloaded = null, Func<bool> isReady = null)
    {
        instance.StartCoroutine(instance.LoadLiveSceneAsync(name, OnSceneloaded, OnPrevSceneUnloaded, isReady));
    }

    public IEnumerator ShowLoadingScreen()
    {
        ClearLoadingScreen();
        CavansInstance = Instantiate(CavansPrefab, transform);
        var animation = CavansInstance.GetComponent<Animation>();
        animation.Play("SceneTransition_s");
        yield return new WaitUntil(() => !animation.isPlaying);
    }

    public IEnumerator HideLoadingScreen()
    {
        LoadingProgressChange(-1, -1);
        if (!CavansInstance)
            yield break;
        var animation = CavansInstance.GetComponent<Animation>();
        animation.Play("SceneTransition_e");
        yield return new WaitUntil(() => !animation.isPlaying);
        ClearLoadingScreen();
    }

    public void ClearLoadingScreen()
    {
        LoadingProgressChange(-1, -1);
        if (CavansInstance)
        {
            CavansInstance.SetActive(false);
            Destroy(CavansInstance);
        }
        CavansInstance = null;
    }

    IEnumerator LoadLiveSceneAsync(string sceneName, Action OnSceneloaded, Action OnPrevSceneUnloaded, Func<bool> isReady)
    {

        yield return ShowLoadingScreen();

        // Set the current Scene to be able to unload it later
        Scene currentScene = SceneManager.GetActiveScene();

        // The Application loads the Scene in the background at the same time as the current Scene.
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

        // Wait until the last operation fully loads to return anything
        yield return new WaitUntil(()=> asyncLoad.isDone);

        OnSceneloaded?.Invoke();

        // Unload the previous Scene
        AsyncOperation asyncUnLoad = SceneManager.UnloadSceneAsync(currentScene);
        yield return new WaitUntil(() => asyncUnLoad.isDone);

        SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));
        OnPrevSceneUnloaded?.Invoke();
        if (isReady != null) yield return new WaitUntil(() => isReady());

        yield return HideLoadingScreen();
    }

    public void LoadingProgressChange(int curren, int target, string message = null)
    {
        if(curren == -1)
        {
            LoadingProgressPanel.SetActive(false);
        }
        else if (target > 0)
        {
            LoadingProgressPanel.SetActive(true);
            LoadingProgressSlider.value = (float)curren / target;
            if (string.IsNullOrEmpty(message))
            {
                LoadingProgressText.text = $"Loading...({curren}/{target})";
            }
            else
            {
                LoadingProgressText.text = $"{message}({curren}/{target})";
            }
        }
    }
}

