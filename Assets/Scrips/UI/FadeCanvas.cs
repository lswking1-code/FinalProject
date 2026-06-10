using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class FadeCanvas : MonoBehaviour
{
    [Header("事件监听")]
    public FadeEventSO fadeEvent;

    public Image fadeImage;

    private Coroutine fadeCoroutine;

    private void Awake()
    {
        UpdateRaycastBlocking();
    }

    private void OnEnable()
    {
        fadeEvent.OnEventRaised += OnFadeEvent;
    }

    private void OnDisable()
    {
        fadeEvent.OnEventRaised -= OnFadeEvent;
    }

    private void OnFadeEvent(Color target, float duration, bool fadeIn)
    {
        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);

        if (fadeImage != null && target.a > 0.01f)
            fadeImage.raycastTarget = true;

        fadeCoroutine = StartCoroutine(FadeRoutine(target, duration));
    }

    private IEnumerator FadeRoutine(Color target, float duration)
    {
        if (fadeImage == null)
            yield break;

        Color start = fadeImage.color;
        float elapsed = 0f;

        if (duration <= 0f)
        {
            fadeImage.color = target;
            yield break;
        }

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            fadeImage.color = Color.Lerp(start, target, elapsed / duration);
            yield return null;
        }

        fadeImage.color = target;
        UpdateRaycastBlocking();
    }

    private void UpdateRaycastBlocking()
    {
        if (fadeImage == null)
            return;

        // 透明时关闭射线检测，避免挡住下层 UI 按钮
        fadeImage.raycastTarget = fadeImage.color.a > 0.01f;
    }
}
