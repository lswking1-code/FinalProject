using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 选人界面单个角色槽：灰色未选中，选中后亮起并播放 Idle。
/// </summary>
public class CharacterSelectSlot : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    public PlayerCharacterSO character;

    [Header("Description")]
    [TextArea]
    [Tooltip("未勾选 Use English 时显示的中文描述")]
    public string descriptionChinese;

    [TextArea]
    [Tooltip("勾选 CharacterSelect 的 Use English 后显示的英文描述")]
    public string descriptionEnglish;

    static readonly Color DimColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    CharacterSelectUI owner;
    Image[] images = System.Array.Empty<Image>();
    Animator[] animators = System.Array.Empty<Animator>();
    GameObject description;
    TextMeshProUGUI descriptionText;
    bool cached;
    bool highlighted;

    public void BindOwner(CharacterSelectUI selectUI)
    {
        owner = selectUI;
        ApplyDescriptionLanguage();
    }

    public void SetHighlighted(bool value)
    {
        EnsureCached();
        highlighted = value;
        ApplyVisuals();
        ApplyDescriptionLanguage();
    }

    public void ApplyDescriptionLanguage()
    {
        CacheDescription();
        if (descriptionText == null)
            return;

        string text = UseEnglish() ? descriptionEnglish : descriptionChinese;
        if (string.IsNullOrEmpty(text))
            text = UseEnglish() ? descriptionChinese : descriptionEnglish;
        descriptionText.text = text ?? string.Empty;
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
        ApplyDescriptionLanguage();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ApplyDescriptionLanguage();
    }
#endif

    bool UseEnglish()
    {
        if (owner == null)
            owner = GetComponentInParent<CharacterSelectUI>(true);
        return owner != null && owner.useEnglish;
    }

    void EnsureCached()
    {
        if (cached)
            return;

        images = GetComponentsInChildren<Image>(true);
        animators = GetComponentsInChildren<Animator>(true);
        CacheDescription();
        if (description != null && Application.isPlaying)
            description.SetActive(false);
        if (Application.isPlaying)
            InstallPointerRelays();
        cached = true;
    }

    void CacheDescription()
    {
        if (description == null)
        {
            var child = transform.Find("Description");
            description = child != null ? child.gameObject : null;
        }

        if (descriptionText == null && description != null)
            descriptionText = description.GetComponent<TextMeshProUGUI>();
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

        if (description != null && description.activeSelf != highlighted)
            description.SetActive(highlighted);
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
