using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(menuName ="Event/FadeEventSO")]
public class FadeEventSO : ScriptableObject
{
    public UnityAction<Color, float, bool> OnEventRaised;// 屏幕淡入淡出事件
    
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
        OnEventRaised?.Invoke(target, duration, fadeIn);
    }
}
