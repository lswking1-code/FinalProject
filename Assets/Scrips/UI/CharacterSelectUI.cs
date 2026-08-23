using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 主菜单选人界面：方向/鼠标高亮角色，Jump 或左键确认后开局。
/// </summary>
public class CharacterSelectUI : MonoBehaviour
{
    const float NavigateThreshold = 0.5f;
    const float ConfirmLockDuration = 0.15f;

    public CharacterSelectSlot[] slots;
    public MenuActions menuActions;

    InputSystem_Actions actions;
    int currentIndex;
    bool navigateLocked;
    bool confirmed;
    float confirmReadyTime;

    void Awake()
    {
        if (menuActions == null)
            menuActions = GetComponentInParent<MenuActions>();

        if (slots == null || slots.Length == 0)
        {
            slots = GetComponentsInChildren<CharacterSelectSlot>(true);
            System.Array.Sort(slots, (a, b) =>
            {
                float ax = a != null ? a.transform.position.x : 0f;
                float bx = b != null ? b.transform.position.x : 0f;
                return ax.CompareTo(bx);
            });
        }

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                slots[i].BindOwner(this);
        }
    }

    void OnEnable()
    {
        confirmed = false;
        navigateLocked = false;
        confirmReadyTime = Time.unscaledTime + ConfirmLockDuration;

        actions ??= new InputSystem_Actions();
        actions.Player.Enable();
        actions.UI.Enable();

        currentIndex = -1;
        HighlightIndex(0);
        TryHighlightUnderPointer();
    }

    void OnDisable()
    {
        if (actions == null)
            return;

        actions.Player.Disable();
        actions.UI.Disable();
    }

    void OnDestroy()
    {
        actions?.Dispose();
        actions = null;
    }

    void Update()
    {
        if (confirmed || actions == null)
            return;

        HandleNavigate();
        HandleMouseHover();
        HandleConfirm();
        HandleCancel();
    }

    public void HighlightSlot(CharacterSelectSlot slot)
    {
        if (confirmed || slot == null || slots == null)
            return;

        int index = System.Array.IndexOf(slots, slot);
        if (index < 0)
            return;

        HighlightIndex(index);
    }

    public void ConfirmSelection()
    {
        if (confirmed || Time.unscaledTime < confirmReadyTime)
            return;

        if (slots == null || slots.Length == 0)
            return;

        currentIndex = Mathf.Clamp(currentIndex, 0, slots.Length - 1);
        var slot = slots[currentIndex];
        if (slot == null || slot.character == null)
        {
            Debug.LogWarning("CharacterSelectUI: 当前槽位未绑定 PlayerCharacterSO。");
            return;
        }

        confirmed = true;
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            InputPromptDeviceTracker.Remember(Mouse.current);
        else
            RememberLastMenuDevice();

        var loader = FindFirstObjectByType<SceneLoader>();
        loader?.SelectCharacter(slot.character);
        menuActions?.ConfirmNewGame();
    }

    void HandleNavigate()
    {
        float moveX = actions.Player.Move.ReadValue<Vector2>().x;
        float navX = actions.UI.Navigate.ReadValue<Vector2>().x;
        float x = Mathf.Abs(moveX) >= Mathf.Abs(navX) ? moveX : navX;

        if (Mathf.Abs(x) > NavigateThreshold)
        {
            if (navigateLocked)
                return;

            navigateLocked = true;
            RememberLastMenuDevice();
            HighlightIndex(currentIndex + (x > 0f ? 1 : -1));
            return;
        }

        navigateLocked = false;
    }

    void HandleMouseHover()
    {
        if (Mouse.current == null || Mouse.current.delta.ReadValue().sqrMagnitude < 0.01f)
            return;

        InputPromptDeviceTracker.Remember(Mouse.current);
        TryHighlightUnderPointer();
    }

    void HandleConfirm()
    {
        if (actions.Player.Jump.WasPressedThisFrame())
        {
            InputPromptDeviceTracker.RememberFromAction(actions.Player.Jump);
            ConfirmSelection();
        }
    }

    void HandleCancel()
    {
        bool cancel = actions.UI.Cancel.WasPressedThisFrame();
        var keyboard = Keyboard.current;
        if (!cancel && keyboard != null)
            cancel = keyboard.escapeKey.wasPressedThisFrame;

        if (!cancel)
            return;

        menuActions?.BackToStartMenu();
    }

    void RememberLastMenuDevice()
    {
        if (actions.Player.Jump.activeControl != null)
        {
            InputPromptDeviceTracker.RememberFromAction(actions.Player.Jump);
            return;
        }

        if (actions.Player.Move.activeControl != null)
        {
            InputPromptDeviceTracker.RememberFromAction(actions.Player.Move);
            return;
        }

        if (actions.UI.Navigate.activeControl != null)
        {
            InputPromptDeviceTracker.RememberFromAction(actions.UI.Navigate);
            return;
        }

        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            InputPromptDeviceTracker.Remember(Mouse.current);
    }

    void HighlightIndex(int index)
    {
        if (slots == null || slots.Length == 0)
            return;

        int count = slots.Length;
        index = ((index % count) + count) % count;
        if (index == currentIndex)
            return;

        currentIndex = index;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                slots[i].SetHighlighted(i == currentIndex);
        }
    }

    void TryHighlightUnderPointer()
    {
        if (EventSystem.current == null)
            return;

        Vector2 pointerPos = Mouse.current != null
            ? Mouse.current.position.ReadValue()
            : Vector2.zero;

        var pointer = new PointerEventData(EventSystem.current) { position = pointerPos };
        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, results);

        for (int i = 0; i < results.Count; i++)
        {
            var slot = results[i].gameObject.GetComponentInParent<CharacterSelectSlot>();
            if (slot == null)
                continue;

            HighlightSlot(slot);
            return;
        }
    }
}
