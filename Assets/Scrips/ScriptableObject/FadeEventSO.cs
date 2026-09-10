using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(menuName ="Event/FadeEventSO")]
public class FadeEventSO : ScriptableObject
{
    public UnityAction<Color, float, bool> OnEventRaised;// 屏幕淡入淡出事件

    /// <summary>当前是否有尚未完成的淡入/淡出。</summary>
    public bool IsTransitioning { get; private set; }

    /// <summary>
    /// 屏幕逐渐变黑（淡入）
    /// </summary>
    /// <param name="duration">过渡时长（秒）</param>
    public void FadeIn(float duration)
    {
        RaiseEvent(Color.black, duration, true);
    }
    /// <summary>
    /// 屏幕逐渐变透明（淡出）
    /// </summary>
    /// <param name="duration">过渡时长（秒）</param>
    public void FadeOut(float duration)
    {
        RaiseEvent(Color.clear, duration, false);
    }

    public void RaiseEvent(Color target, float duration,bool fadeIn)
    {
        IsTransitioning = true;
        if (OnEventRaised == null)
        {
            IsTransitioning = false;
            return;
        }

        OnEventRaised.Invoke(target, duration, fadeIn);
    }

    public void NotifyCompleted()
    {
        IsTransitioning = false;
    }

    /// <summary>等到 FadeCanvas 真正落到目标色，避免 WaitForSeconds 比最后一帧更早结束。</summary>
    public IEnumerator WaitUntilCompleted()
    {
        if (!IsTransitioning)
            yield break;

        float elapsed = 0f;
        const float timeout = 8f;
        while (IsTransitioning && elapsed < timeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        IsTransitioning = false;
    }
}
