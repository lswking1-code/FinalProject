using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 挂在 PlayerIcon 上，根据 SceneLoader 当前选择的角色切换头像。
/// </summary>
public class PlayerIconUI : MonoBehaviour
{
    [SerializeField] Image iconImage;

    SceneLoader sceneLoader;
    Sprite fallbackSprite;

    void Awake()
    {
        if (iconImage == null)
            iconImage = GetComponent<Image>();

        if (iconImage != null)
            fallbackSprite = iconImage.sprite;
    }

    void OnEnable()
    {
        BindLoader();
        Apply(sceneLoader != null ? sceneLoader.selectedCharacter : null);
    }

    void OnDisable()
    {
        UnbindLoader();
    }

    void LateUpdate()
    {
        if (sceneLoader != null)
            return;

        BindLoader();
        if (sceneLoader != null)
            Apply(sceneLoader.selectedCharacter);
    }

    void BindLoader()
    {
        if (sceneLoader != null)
            return;

        sceneLoader = FindFirstObjectByType<SceneLoader>();
        if (sceneLoader != null)
            sceneLoader.SelectedCharacterChanged += Apply;
    }

    void UnbindLoader()
    {
        if (sceneLoader == null)
            return;

        sceneLoader.SelectedCharacterChanged -= Apply;
        sceneLoader = null;
    }

    void Apply(PlayerCharacterSO character)
    {
        if (iconImage == null)
            return;

        var sprite = character != null ? character.icon : null;
        iconImage.sprite = sprite != null ? sprite : fallbackSprite;
    }
}

