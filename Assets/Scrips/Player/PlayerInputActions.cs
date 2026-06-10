using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputActions : IDisposable
{
    private readonly InputActionAsset _asset;

    public InputAction Move { get; }
    public InputAction Jump { get; }
    public InputAction Crouch { get; }
    public InputAction Attack { get; }

    public PlayerInputActions(InputActionAsset source = null)
    {
        if (source == null)
        {
            source = Resources.Load<InputActionAsset>("InputSystem_Actions");
#if UNITY_EDITOR
            if (source == null)
            {
                source = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                    "Assets/InputSystem_Actions.inputactions");
            }
#endif
        }

        if (source == null)
            throw new InvalidOperationException("InputSystem_Actions asset not found.");

        _asset = UnityEngine.Object.Instantiate(source);
        var playerMap = _asset.FindActionMap("Player", true);
        playerMap.Enable();

        Move = playerMap.FindAction("Move", true);
        Jump = playerMap.FindAction("Jump", true);
        Crouch = playerMap.FindAction("Crouch", true);
        Attack = playerMap.FindAction("Attack", true);
    }

    public void Enable() => _asset.Enable();

    public void Disable() => _asset.Disable();

    public void Dispose()
    {
        Disable();
        if (_asset != null)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(_asset);
            else
                UnityEngine.Object.DestroyImmediate(_asset);
        }
    }
}
