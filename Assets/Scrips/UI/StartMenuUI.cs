using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 主菜单开始界面：方向/鼠标高亮按钮，Jump 或左键确认。
/// </summary>
public class StartMenuUI : MonoBehaviour
{
    const float NavigateThreshold = 0.5f;
    const float ConfirmLockDuration = 0.15f;
    static readonly Color DimColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    public Button[] buttons;

    InputSystem_Actions actions;
    Graphic[][] buttonGraphics;
    Color[][] originalColors;
    int currentIndex = -1;
    bool navigateLocked;
    float confirmReadyTime;

    void Awake()
    {
        if (buttons == null || buttons.Length == 0)
            CollectButtons();

        CacheVisuals();
        DisableAutomaticNavigation();
    }

    void OnEnable()
    {
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
        if (actions == null)
            return;

        HandleNavigate();
        HandleMouseHover();
        HandleConfirm();
    }

    public void HighlightButton(Button button)
    {
        if (button == null || buttons == null)
            return;

        int index = System.Array.IndexOf(buttons, button);
        if (index < 0)
            return;

        HighlightIndex(index);
    }

    public void ConfirmSelection()
    {
        if (Time.unscaledTime < confirmReadyTime)
            return;

        if (buttons == null || buttons.Length == 0)
            return;

        currentIndex = Mathf.Clamp(currentIndex, 0, buttons.Length - 1);
        var button = buttons[currentIndex];
        if (button == null || !button.interactable)
            return;

        button.onClick.Invoke();
    }

    void CollectButtons()
    {
        buttons = GetComponentsInChildren<Button>(true);
        System.Array.Sort(buttons, (a, b) =>
        {
            Vector3 ap = a != null ? a.transform.position : Vector3.zero;
            Vector3 bp = b != null ? b.transform.position : Vector3.zero;
            int byY = bp.y.CompareTo(ap.y);
            return byY != 0 ? byY : ap.x.CompareTo(bp.x);
        });
    }

    void CacheVisuals()
    {
        if (buttons == null)
            return;

        buttonGraphics = new Graphic[buttons.Length][];
        originalColors = new Color[buttons.Length][];

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null)
            {
                buttonGraphics[i] = System.Array.Empty<Graphic>();
                originalColors[i] = System.Array.Empty<Color>();
                continue;
            }

            var graphics = buttons[i].GetComponentsInChildren<Graphic>(true);
            buttonGraphics[i] = graphics;
            originalColors[i] = new Color[graphics.Length];
            for (int g = 0; g < graphics.Length; g++)
                originalColors[i][g] = graphics[g] != null ? graphics[g].color : Color.white;
        }
    }

    void DisableAutomaticNavigation()
    {
        if (buttons == null)
            return;

        var none = new Navigation { mode = Navigation.Mode.None };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null)
                continue;

            buttons[i].navigation = none;
            buttons[i].transition = Selectable.Transition.None;
        }
    }

    void HandleNavigate()
    {
        Vector2 move = actions.Player.Move.ReadValue<Vector2>();
        Vector2 nav = actions.UI.Navigate.ReadValue<Vector2>();
        Vector2 v = move.sqrMagnitude >= nav.sqrMagnitude ? move : nav;

        if (v.magnitude <= NavigateThreshold)
        {
            navigateLocked = false;
            return;
        }

        if (navigateLocked)
            return;

        navigateLocked = true;
        if (actions.Player.Move.activeControl != null)
            InputPromptDeviceTracker.RememberFromAction(actions.Player.Move);
        else
            InputPromptDeviceTracker.RememberFromAction(actions.UI.Navigate);

        Vector2 dir = Mathf.Abs(v.x) >= Mathf.Abs(v.y)
            ? new Vector2(Mathf.Sign(v.x), 0f)
            : new Vector2(0f, Mathf.Sign(v.y));

        HighlightIndex(FindNextIndex(currentIndex, dir));
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

    int FindNextIndex(int current, Vector2 dir)
    {
        if (buttons == null || buttons.Length == 0)
            return 0;

        current = Mathf.Clamp(current, 0, buttons.Length - 1);
        Vector2 origin = buttons[current] != null
            ? (Vector2)buttons[current].transform.position
            : Vector2.zero;

        int best = -1;
        float bestScore = float.MaxValue;

        for (int i = 0; i < buttons.Length; i++)
        {
            if (i == current || buttons[i] == null)
                continue;

            Vector2 delta = (Vector2)buttons[i].transform.position - origin;
            float along = Vector2.Dot(delta, dir);
            if (along <= 0.01f)
                continue;

            float sideways = Mathf.Abs(Vector2.Dot(delta, new Vector2(-dir.y, dir.x)));
            float score = along + sideways * 2f;
            if (score >= bestScore)
                continue;

            bestScore = score;
            best = i;
        }

        if (best >= 0)
            return best;

        best = current;
        float wrapScore = float.MinValue;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (i == current || buttons[i] == null)
                continue;

            Vector2 delta = (Vector2)buttons[i].transform.position - origin;
            float along = Vector2.Dot(delta, -dir);
            float sideways = Mathf.Abs(Vector2.Dot(delta, new Vector2(-dir.y, dir.x)));
            float score = along - sideways * 2f;
            if (score <= wrapScore)
                continue;

            wrapScore = score;
            best = i;
        }

        return best;
    }

    void HighlightIndex(int index)
    {
        if (buttons == null || buttons.Length == 0)
            return;

        int count = buttons.Length;
        index = ((index % count) + count) % count;
        if (index == currentIndex)
            return;

        currentIndex = index;

        for (int i = 0; i < buttons.Length; i++)
            ApplyVisuals(i, i == currentIndex);

        if (EventSystem.current != null && buttons[currentIndex] != null)
            EventSystem.current.SetSelectedGameObject(buttons[currentIndex].gameObject);
    }

    void ApplyVisuals(int index, bool highlighted)
    {
        if (buttonGraphics == null || index < 0 || index >= buttonGraphics.Length)
            return;

        var graphics = buttonGraphics[index];
        var originals = originalColors[index];
        for (int i = 0; i < graphics.Length; i++)
        {
            var graphic = graphics[i];
            if (graphic == null)
                continue;

            Color tint = highlighted ? originals[i] : originals[i] * DimColor;
            tint.a = originals[i].a;
            graphic.color = tint;
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
            var button = results[i].gameObject.GetComponentInParent<Button>();
            if (button == null)
                continue;

            HighlightButton(button);
            return;
        }
    }
}
