using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

/// <summary>
/// 把 {Ability2} / {Player/Jump} 换成当前设备对应的 TMP 按键精灵标签。
/// </summary>
public static class InputPromptFormatter
{
    public const string KeySpriteAsset = "KeyIcons";
    public const string GamepadSpriteAsset = "GamepadIcons";

    static readonly Regex Placeholder = new(@"\{([^}]+)\}", RegexOptions.Compiled);
    static readonly string[] CompositeOrder = { "up", "left", "down", "right" };

    static readonly Dictionary<string, string> KeyboardAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = "spacebar",
        ["leftShift"] = "shift",
        ["rightShift"] = "shift",
        ["shift"] = "shift",
        ["leftCtrl"] = "control",
        ["rightCtrl"] = "control",
        ["ctrl"] = "control",
        ["leftAlt"] = "alt",
        ["rightAlt"] = "alt",
        ["alt"] = "alt",
        ["upArrow"] = "arrow_up",
        ["downArrow"] = "arrow_down",
        ["leftArrow"] = "arrow_left",
        ["rightArrow"] = "arrow_right",
        ["enter"] = "enter",
        ["numpadEnter"] = "enter",
        ["escape"] = "escape",
        ["backspace"] = "backspace",
        ["tab"] = "tab",
        ["delete"] = "delete",
        ["comma"] = "comma",
        ["period"] = "dot",
        ["slash"] = "forwardslash",
        ["minus"] = "dash",
        ["equals"] = "equal",
        ["leftBracket"] = "square_bracket_left",
        ["rightBracket"] = "square_bracket_right",
        ["semicolon"] = "semicolon",
        ["quote"] = "quote",
        ["backquote"] = "grave_accent",
        ["numpad0"] = "0",
        ["numpad1"] = "1",
        ["numpad2"] = "2",
        ["numpad3"] = "3",
        ["numpad4"] = "4",
        ["numpad5"] = "5",
        ["numpad6"] = "6",
        ["numpad7"] = "7",
        ["numpad8"] = "8",
        ["numpad9"] = "9",
    };

    static InputSystem_Actions s_actions;

    static InputActionAsset Asset
    {
        get
        {
            s_actions ??= new InputSystem_Actions();
            return s_actions.asset;
        }
    }

    public static string Format(string source)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        return Placeholder.Replace(source, match => ResolveToken(match.Groups[1].Value));
    }

    static string ResolveToken(string token)
    {
        var action = FindAction(token);
        if (action == null)
            return token;

        bool useGamepad = InputPromptDeviceTracker.UsesGamepad;
        GamepadFamily family = InputPromptDeviceTracker.CurrentGamepadFamily;

        if (TryFormatComposite(action, useGamepad, family, out string composite))
            return composite;

        if (TryGetMatchingBinding(action, useGamepad, out InputBinding binding, out int bindingIndex))
        {
            string sprite = SpriteTagFromPath(binding.effectivePath, useGamepad, family);
            if (!string.IsNullOrEmpty(sprite))
                return sprite;

            string display = SanitizeDisplayString(action.GetBindingDisplayString(bindingIndex));
            return string.IsNullOrEmpty(display) ? token : display;
        }

        string fallback = SanitizeDisplayString(action.GetBindingDisplayString());
        return string.IsNullOrEmpty(fallback) ? token : fallback;
    }

    static InputAction FindAction(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        string trimmed = token.Trim();
        var found = Asset.FindAction(trimmed, false);
        if (found != null)
            return found;

        if (!trimmed.Contains('/'))
            return Asset.FindAction("Player/" + trimmed, false);

        return null;
    }

    static bool TryFormatComposite(
        InputAction action,
        bool useGamepad,
        GamepadFamily family,
        out string result)
    {
        result = null;
        var bindings = action.bindings;
        int chosen = -1;

        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            if (!binding.isComposite)
                continue;

            if (!CompositeMatchesDevice(action, i, useGamepad))
                continue;

            // 手柄优先用单独的摇杆绑定，不要拆成 D-pad 四键。
            if (useGamepad && HasNonCompositeMatch(action, true))
                return false;

            chosen = i;
            if (!useGamepad)
                break;
        }

        if (chosen < 0)
            return false;

        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = chosen + 1; i < bindings.Count; i++)
        {
            var part = bindings[i];
            if (!part.isPartOfComposite)
                break;
            if (!PathMatchesDevice(part.effectivePath, useGamepad))
                continue;

            string tag = SpriteTagFromPath(part.effectivePath, useGamepad, family);
            if (string.IsNullOrEmpty(tag))
                tag = part.name;
            parts[part.name ?? string.Empty] = tag;
        }

        if (parts.Count == 0)
            return false;

        var sb = new StringBuilder();
        bool anyOrdered = false;
        for (int i = 0; i < CompositeOrder.Length; i++)
        {
            if (!parts.TryGetValue(CompositeOrder[i], out string tag))
                continue;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(tag);
            anyOrdered = true;
        }

        if (!anyOrdered)
        {
            foreach (var pair in parts)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(pair.Value);
            }
        }

        result = sb.ToString();
        return true;
    }

    static bool HasNonCompositeMatch(InputAction action, bool useGamepad)
    {
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            if (binding.isComposite || binding.isPartOfComposite)
                continue;
            if (PathMatchesDevice(binding.effectivePath, useGamepad))
                return true;
        }

        return false;
    }

    static bool CompositeMatchesDevice(InputAction action, int compositeIndex, bool useGamepad)
    {
        var bindings = action.bindings;
        for (int i = compositeIndex + 1; i < bindings.Count; i++)
        {
            var part = bindings[i];
            if (!part.isPartOfComposite)
                break;
            if (PathMatchesDevice(part.effectivePath, useGamepad))
                return true;
        }

        return false;
    }

    static bool TryGetMatchingBinding(
        InputAction action,
        bool useGamepad,
        out InputBinding binding,
        out int bindingIndex)
    {
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            var candidate = bindings[i];
            if (candidate.isComposite || candidate.isPartOfComposite)
                continue;
            if (!PathMatchesDevice(candidate.effectivePath, useGamepad))
                continue;

            binding = candidate;
            bindingIndex = i;
            return true;
        }

        binding = default;
        bindingIndex = -1;
        return false;
    }

    static bool PathMatchesDevice(string path, bool useGamepad)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (useGamepad)
            return path.StartsWith("<Gamepad>", StringComparison.OrdinalIgnoreCase);

        return path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("<Pointer>", StringComparison.OrdinalIgnoreCase);
    }

    static string SpriteTagFromPath(string path, bool useGamepad, GamepadFamily family)
    {
        string control = ControlName(path);
        if (string.IsNullOrEmpty(control))
            return null;

        if (!useGamepad)
        {
            string sprite = ResolveKeyboardSprite(control);
            return string.IsNullOrEmpty(sprite) ? null : SpriteTag(KeySpriteAsset, sprite);
        }

        string padSprite = ResolveGamepadSprite(control, family);
        return string.IsNullOrEmpty(padSprite) ? null : SpriteTag(GamepadSpriteAsset, padSprite);
    }

    static string ResolveKeyboardSprite(string control)
    {
        if (KeyboardAliases.TryGetValue(control, out string alias))
            return alias;
        return control.ToLowerInvariant();
    }

    static string ResolveGamepadSprite(string control, GamepadFamily family)
    {
        string key = control;
        int slash = control.LastIndexOf('/');
        if (slash >= 0)
            key = control.Substring(slash + 1);

        return key switch
        {
            "buttonSouth" => Face("xbox_a", "ps_cross", "nintendo_b", family),
            "buttonEast" => Face("xbox_b", "ps_circle", "nintendo_a", family),
            "buttonWest" => Face("xbox_x", "ps_square", "nintendo_y", family),
            "buttonNorth" => Face("xbox_y", "ps_triangle", "nintendo_x", family),
            "leftShoulder" => family == GamepadFamily.PlayStation ? "left_shoulder_ps" : "left_shoulder",
            "rightShoulder" => family == GamepadFamily.PlayStation ? "right_shoulder_ps" : "right_shoulder",
            "leftTrigger" => family == GamepadFamily.PlayStation ? "left_trigger_ps" : "left_trigger",
            "rightTrigger" => family == GamepadFamily.PlayStation ? "right_trigger_ps" : "right_trigger",
            "leftStick" => "left_stick",
            "rightStick" => "right_stick",
            "leftStickPress" => "left_stick",
            "rightStickPress" => "right_stick",
            "up" => "dpad_up",
            "down" => "dpad_down",
            "left" => "dpad_left",
            "right" => "dpad_right",
            "start" => "start",
            "select" => "select",
            _ => null,
        };
    }

    static string Face(string xbox, string playstation, string nintendo, GamepadFamily family)
    {
        return family switch
        {
            GamepadFamily.PlayStation => playstation,
            GamepadFamily.Nintendo => nintendo,
            _ => xbox,
        };
    }

    static string SpriteTag(string asset, string name) =>
        $"<sprite=\"{asset}\" name=\"{name}\">";

    static string SanitizeDisplayString(string display)
    {
        if (string.IsNullOrEmpty(display))
            return display;
        // Input System 可能返回 <Keyboard>/i，TMP 会当成坏标签显示成乱码。
        return display.Replace("<", "(").Replace(">", ")");
    }

    static string ControlName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        int slash = path.LastIndexOf('/');
        return slash >= 0 && slash < path.Length - 1
            ? path.Substring(slash + 1)
            : path.Trim('<', '>');
    }
}

public enum GamepadFamily
{
    Xbox,
    PlayStation,
    Nintendo,
}

/// <summary>
/// 记录选角/菜单最后一次操作是键盘鼠标还是手柄。方案会跨场景保留。
/// </summary>
public static class InputPromptDeviceTracker
{
    public static event Action Changed;

    static bool s_useGamepad;
    static GamepadFamily s_family = GamepadFamily.Xbox;
    static bool s_hooked;

    public static bool UsesGamepad
    {
        get
        {
            EnsureHooked();
            return s_useGamepad;
        }
    }

    public static GamepadFamily CurrentGamepadFamily
    {
        get
        {
            EnsureHooked();
            return s_useGamepad ? s_family : GamepadFamily.Xbox;
        }
    }

    public static void Remember(InputDevice device)
    {
        if (device == null)
            return;

        bool useGamepad = device is Gamepad;
        if (device is Pointer || device is Mouse)
            useGamepad = false;

        GamepadFamily family = s_family;
        if (useGamepad)
            family = ResolveFamily(device as Gamepad ?? Gamepad.current);

        if (s_useGamepad == useGamepad && s_family == family)
            return;

        s_useGamepad = useGamepad;
        s_family = family;
        Changed?.Invoke();
    }

    public static void RememberKeyboard() => Remember(Keyboard.current ?? Mouse.current as InputDevice);

    public static void RememberFromAction(InputAction action)
    {
        if (action?.activeControl?.device != null)
            Remember(action.activeControl.device);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_useGamepad = false;
        s_family = GamepadFamily.Xbox;
        s_hooked = false;
        Changed = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureHooked()
    {
        if (s_hooked)
            return;

        InputSystem.onActionChange += OnActionChange;
        s_hooked = true;
    }

    static void OnActionChange(object obj, InputActionChange change)
    {
        if (change != InputActionChange.ActionPerformed)
            return;
        if (obj is not InputAction action || action.activeControl == null)
            return;

        Remember(action.activeControl.device);
    }

    static GamepadFamily ResolveFamily(Gamepad gamepad)
    {
        if (gamepad == null)
            return GamepadFamily.Xbox;

        if (gamepad is DualShockGamepad)
            return GamepadFamily.PlayStation;

        string name = gamepad.displayName ?? gamepad.name ?? string.Empty;
        if (name.IndexOf("dualsense", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("dualshock", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("playstation", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("wireless controller", StringComparison.OrdinalIgnoreCase) >= 0)
            return GamepadFamily.PlayStation;

        if (name.IndexOf("switch", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("pro controller", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("joy-con", StringComparison.OrdinalIgnoreCase) >= 0)
            return GamepadFamily.Nintendo;

        return GamepadFamily.Xbox;
    }
}
