using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 选人界面单个角色槽：灰色未选中，选中后亮起并播放 Idle。
/// </summary>
public class CharacterSelectSlot : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    public PlayerCharacterSO character;

    static readonly Color DimColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    CharacterSelectUI owner;
    Image[] images = System.Array.Empty<Image>();
    Animator[] animators = System.Array.Empty<Animator>();
    bool cached;
    bool highlighted;

    public void BindOwner(CharacterSelectUI selectUI)
    {
        owner = selectUI;
    }

    public void SetHighlighted(bool value)
    {
        EnsureCached();
        highlighted = value;
        ApplyVisuals();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        owner?.HighlightSlot(this);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            return;

        owner?.HighlightSlot(this);
        owner?.ConfirmSelection();
    }

    void Awake()
    {
        EnsureCached();
    }

    void EnsureCached()
    {
        if (cached)
            return;

        images = GetComponentsInChildren<Image>(true);
        animators = GetComponentsInChildren<Animator>(true);
        InstallPointerRelays();
        cached = true;
    }

    void InstallPointerRelays()
    {
        var graphics = GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            var graphic = graphics[i];
            if (graphic == null || graphic.gameObject == gameObject)
                continue;

            var relay = graphic.GetComponent<CharacterSelectPointerRelay>();
            if (relay == null)
                relay = graphic.gameObject.AddComponent<CharacterSelectPointerRelay>();
            relay.slot = this;
        }
    }

    void ApplyVisuals()
    {
        for (int i = 0; i < images.Length; i++)
        {
            var image = images[i];
            if (image == null)
                continue;

            float alpha = image.color.a;
            Color tint = highlighted ? Color.white : DimColor;
            tint.a = alpha;
            image.color = tint;
        }

        for (int i = 0; i < animators.Length; i++)
        {
            var animator = animators[i];
            if (animator == null || animator.runtimeAnimatorController == null)
                continue;

            if (highlighted)
            {
                animator.enabled = true;
                animator.Play(0, 0, 0f);
                animator.Update(0f);
            }
            else
            {
                animator.Play(0, 0, 0f);
                animator.Update(0f);
                animator.enabled = false;
            }
        }
    }
}

/// <summary>
/// 子 Graphic 收到指针事件后转发给所属槽位。
/// </summary>
public class CharacterSelectPointerRelay : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    public CharacterSelectSlot slot;

    public void OnPointerEnter(PointerEventData eventData)
    {
        slot?.OnPointerEnter(eventData);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        slot?.OnPointerClick(eventData);
    }
}
