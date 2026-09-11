using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 开始菜单空闲超时后全屏循环播放预告片：淡入开播，任意操作淡出回菜单。
/// </summary>
public class StartMenuIdleTrailer : MonoBehaviour
{
    const float StickThreshold = 0.5f;
    const float MouseMoveSqr = 0.25f;

    enum Phase { IdleWatch, FadeToTrailer, Playing, FadeToMenu }

    [SerializeField] float idleSeconds = 60f;
    [SerializeField] float fadeDuration = 0.5f;
    [SerializeField] GameObject startMenu;
    [SerializeField] GameObject characterSelect;
    [SerializeField] GameObject overlay;
    [SerializeField] VideoPlayer videoPlayer;
    [SerializeField] RawImage videoImage;
    [SerializeField] Image fadeImage;
    [SerializeField] VideoClip clip;

    Phase phase = Phase.IdleWatch;
    float idleTimer;
    Coroutine transition;
    RenderTexture targetTexture;
    StartMenuUI startMenuUI;
    AudioSource videoAudio;
    AudioListener tempListener;
    bool addedTempListener;

    void Awake()
    {
        if (startMenu == null)
        {
            var t = transform.Find("StartMenu");
            if (t != null)
                startMenu = t.gameObject;
        }

        if (characterSelect == null)
        {
            var t = transform.Find("CharacterSelect");
            if (t != null)
                characterSelect = t.gameObject;
        }

        startMenuUI = startMenu != null ? startMenu.GetComponent<StartMenuUI>() : null;
        EnsureOverlay();
        ConfigureVideo();
        HideOverlayImmediate();
    }

    void OnEnable()
    {
        ResetIdle();
    }

    void OnDisable()
    {
        bool wasShowingTrailer = phase != Phase.IdleWatch;
        StopTransition();
        StopVideo();
        ReleaseUnityListener();

        var loader = FindFirstObjectByType<SceneLoader>();
        RestoreFmodDevice();
        if (wasShowingTrailer && (loader == null || !loader.IsLoading))
            RestartMenuBgm();

        if (startMenuUI != null)
            startMenuUI.enabled = true;
        HideOverlayImmediate();
        phase = Phase.IdleWatch;
    }

    void OnDestroy()
    {
        ReleaseUnityListener();
        if (videoPlayer != null)
            videoPlayer.targetTexture = null;
        if (videoImage != null)
            videoImage.texture = null;
        if (targetTexture != null)
        {
            targetTexture.Release();
            Destroy(targetTexture);
            targetTexture = null;
        }
    }

    void Update()
    {
        bool input = HasAnyInput();

        switch (phase)
        {
            case Phase.IdleWatch:
                if (!CanWatchIdle())
                {
                    idleTimer = 0f;
                    return;
                }

                if (input)
                {
                    idleTimer = 0f;
                    return;
                }

                idleTimer += Time.unscaledDeltaTime;
                if (idleTimer >= idleSeconds)
                    BeginPlay();
                break;

            case Phase.FadeToTrailer:
            case Phase.Playing:
                if (input)
                    BeginReturn();
                break;
        }
    }

    bool CanWatchIdle()
    {
        if (startMenu == null || !startMenu.activeSelf)
            return false;
        if (characterSelect != null && characterSelect.activeSelf)
            return false;

        var loader = FindFirstObjectByType<SceneLoader>();
        return loader == null || !loader.IsLoading;
    }

    void BeginPlay()
    {
        if (transition != null || clip == null)
            return;

        transition = StartCoroutine(PlayRoutine());
    }

    void BeginReturn()
    {
        if (phase == Phase.FadeToMenu)
            return;

        StopTransition();
        transition = StartCoroutine(ReturnRoutine());
    }

    IEnumerator PlayRoutine()
    {
        phase = Phase.FadeToTrailer;
        if (startMenuUI != null)
            startMenuUI.enabled = false;

        ShowOverlay();
        SetFade(0f);
        yield return FadeTo(1f);
        if (phase != Phase.FadeToTrailer)
            yield break;

        YieldFmodDevice();
        EnsureUnityListener();
        yield return null;
        yield return StartVideo();
        if (phase != Phase.FadeToTrailer)
            yield break;

        phase = Phase.Playing;
        yield return FadeTo(0f);
        transition = null;
    }

    IEnumerator ReturnRoutine()
    {
        phase = Phase.FadeToMenu;
        ShowOverlay();
        yield return FadeTo(1f);

        StopVideo();
        ReleaseUnityListener();
        yield return null;
        yield return null;
        RestoreFmodDevice();
        RestartMenuBgm();

        if (startMenuUI != null)
            startMenuUI.enabled = true;

        yield return FadeTo(0f);
        HideOverlayImmediate();
        ResetIdle();
        phase = Phase.IdleWatch;
        transition = null;
    }

    IEnumerator StartVideo()
    {
        if (videoPlayer == null || clip == null)
            yield break;

        ConfigureVideo();
        videoPlayer.Stop();
        videoPlayer.clip = clip;
        BindVideoAudio();
        videoPlayer.Prepare();

        float waited = 0f;
        while (!videoPlayer.isPrepared && waited < 5f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        BindVideoAudio();
        videoPlayer.Play();
    }

    void StopVideo()
    {
        if (videoPlayer == null)
            return;

        videoPlayer.Stop();
    }

    void EnsureUnityListener()
    {
        if (FindFirstObjectByType<AudioListener>() != null)
            return;

        var cam = Camera.main;
        if (cam == null)
            cam = FindFirstObjectByType<Camera>();
        if (cam == null)
            return;

        tempListener = cam.GetComponent<AudioListener>();
        if (tempListener == null)
        {
            tempListener = cam.gameObject.AddComponent<AudioListener>();
            addedTempListener = true;
            return;
        }

        tempListener.enabled = true;
        addedTempListener = false;
    }

    void ReleaseUnityListener()
    {
        if (addedTempListener && tempListener != null)
            Destroy(tempListener);

        addedTempListener = false;
        tempListener = null;
    }

    BgmManager FindBgm()
    {
        var loader = FindFirstObjectByType<SceneLoader>();
        if (loader != null && loader.bgmManager != null)
            return loader.bgmManager;
        return FindFirstObjectByType<BgmManager>();
    }

    void YieldFmodDevice()
    {
        FindBgm()?.YieldAudioDevice();
    }

    void RestoreFmodDevice()
    {
        FindBgm()?.RestoreAudioDevice();
    }

    void RestartMenuBgm()
    {
        var loader = FindFirstObjectByType<SceneLoader>();
        var bgm = FindBgm();
        if (bgm == null)
            return;

        bgm.RestartForScene(loader != null ? loader.menuScene : null);
    }

    void ShowOverlay()
    {
        if (overlay != null && !overlay.activeSelf)
            overlay.SetActive(true);
        if (fadeImage != null)
            fadeImage.raycastTarget = true;
    }

    void HideOverlayImmediate()
    {
        SetFade(0f);
        if (fadeImage != null)
            fadeImage.raycastTarget = false;
        if (overlay != null)
            overlay.SetActive(false);
    }

    IEnumerator FadeTo(float alpha)
    {
        if (fadeImage == null)
            yield break;

        Color start = fadeImage.color;
        Color target = new Color(0f, 0f, 0f, alpha);
        fadeImage.raycastTarget = true;

        if (fadeDuration <= 0f)
        {
            fadeImage.color = target;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeImage.color = Color.Lerp(start, target, Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }

        fadeImage.color = target;
    }

    void SetFade(float alpha)
    {
        if (fadeImage == null)
            return;

        Color c = fadeImage.color;
        c.r = 0f;
        c.g = 0f;
        c.b = 0f;
        c.a = alpha;
        fadeImage.color = c;
    }

    void ResetIdle()
    {
        idleTimer = 0f;
    }

    void StopTransition()
    {
        if (transition == null)
            return;

        StopCoroutine(transition);
        transition = null;
    }

    bool HasAnyInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            return true;

        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.delta.ReadValue().sqrMagnitude >= MouseMoveSqr)
                return true;
            if (mouse.scroll.ReadValue().sqrMagnitude > 0.01f)
                return true;
            if (mouse.leftButton.wasPressedThisFrame
                || mouse.rightButton.wasPressedThisFrame
                || mouse.middleButton.wasPressedThisFrame)
                return true;
        }

        var pad = Gamepad.current;
        if (pad != null)
        {
            if (pad.leftStick.ReadValue().magnitude > StickThreshold
                || pad.rightStick.ReadValue().magnitude > StickThreshold)
                return true;
            if (pad.dpad.ReadValue().sqrMagnitude > 0.25f)
                return true;
            if (pad.buttonSouth.wasPressedThisFrame
                || pad.buttonNorth.wasPressedThisFrame
                || pad.buttonWest.wasPressedThisFrame
                || pad.buttonEast.wasPressedThisFrame
                || pad.startButton.wasPressedThisFrame
                || pad.selectButton.wasPressedThisFrame
                || pad.leftShoulder.wasPressedThisFrame
                || pad.rightShoulder.wasPressedThisFrame
                || pad.leftTrigger.wasPressedThisFrame
                || pad.rightTrigger.wasPressedThisFrame
                || pad.leftStickButton.wasPressedThisFrame
                || pad.rightStickButton.wasPressedThisFrame)
                return true;
        }

        return false;
    }

    void EnsureOverlay()
    {
        if (overlay == null)
        {
            var existing = transform.Find("IdleTrailerOverlay");
            overlay = existing != null ? existing.gameObject : CreateOverlay();
        }

        if (videoImage == null && overlay != null)
        {
            var video = overlay.transform.Find("Video");
            if (video != null)
                videoImage = video.GetComponent<RawImage>();
        }

        if (fadeImage == null && overlay != null)
        {
            var fade = overlay.transform.Find("Fade");
            if (fade != null)
                fadeImage = fade.GetComponent<Image>();
        }

        if (videoPlayer == null && overlay != null)
            videoPlayer = overlay.GetComponent<VideoPlayer>();
        if (videoPlayer == null && overlay != null)
            videoPlayer = overlay.AddComponent<VideoPlayer>();

        BindVideoAudio();
    }

    void BindVideoAudio()
    {
        if (videoPlayer == null)
            return;

        if (videoAudio == null && overlay != null)
            videoAudio = overlay.GetComponent<AudioSource>();
        if (videoAudio == null && overlay != null)
            videoAudio = overlay.AddComponent<AudioSource>();
        if (videoAudio == null)
            return;

        videoAudio.playOnAwake = false;
        videoAudio.loop = false;
        videoAudio.spatialBlend = 0f;
        videoAudio.volume = 1f;
        videoAudio.mute = false;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        videoPlayer.controlledAudioTrackCount = 1;
        videoPlayer.EnableAudioTrack(0, true);
        videoPlayer.SetTargetAudioSource(0, videoAudio);
    }

    GameObject CreateOverlay()
    {
        var root = CreateUiObject("IdleTrailerOverlay", transform);
        StretchFull(root.GetComponent<RectTransform>());
        root.transform.SetAsLastSibling();

        var backdrop = CreateUiObject("Backdrop", root.transform);
        StretchFull(backdrop.GetComponent<RectTransform>());
        var backdropImage = backdrop.AddComponent<Image>();
        backdropImage.color = Color.black;
        backdropImage.raycastTarget = true;

        var video = CreateUiObject("Video", root.transform);
        StretchFull(video.GetComponent<RectTransform>());
        videoImage = video.AddComponent<RawImage>();
        videoImage.color = Color.white;
        videoImage.raycastTarget = false;
        var fitter = video.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 16f / 9f;

        var fade = CreateUiObject("Fade", root.transform);
        StretchFull(fade.GetComponent<RectTransform>());
        fadeImage = fade.AddComponent<Image>();
        fadeImage.color = Color.clear;
        fadeImage.raycastTarget = false;

        return root;
    }

    void ConfigureVideo()
    {
        if (videoPlayer == null)
            return;

        if (clip != null)
            videoPlayer.clip = clip;

        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = true;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        videoPlayer.skipOnDrop = true;
        videoPlayer.waitForFirstFrame = true;
        BindVideoAudio();

        EnsureRenderTexture();
        videoPlayer.targetTexture = targetTexture;
        if (videoImage != null)
            videoImage.texture = targetTexture;

        if (clip != null && videoImage != null)
        {
            var fitter = videoImage.GetComponent<AspectRatioFitter>();
            if (fitter != null && clip.height > 0)
                fitter.aspectRatio = clip.width / (float)clip.height;
        }
    }

    void EnsureRenderTexture()
    {
        int width = clip != null && clip.width > 0 ? (int)clip.width : 1920;
        int height = clip != null && clip.height > 0 ? (int)clip.height : 1080;
        if (targetTexture != null
            && targetTexture.width == width
            && targetTexture.height == height)
            return;

        if (targetTexture != null)
        {
            targetTexture.Release();
            Destroy(targetTexture);
        }

        targetTexture = new RenderTexture(width, height, 0)
        {
            name = "IdleTrailerRT",
        };
        targetTexture.Create();
    }

    static GameObject CreateUiObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
