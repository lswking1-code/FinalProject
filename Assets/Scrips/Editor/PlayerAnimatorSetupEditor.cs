#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PlayerAnimatorSetupEditor
{
    private const string ControllerPath = "Assets/Arts/Metal Slug/Player.controller";
    private const string AnimFolder = "Assets/Arts/Metal Slug";

    [MenuItem("Lost Division/Build Player Animator")]
    public static void BuildPlayerAnimator()
    {
        EnsureControllerDirectory();

        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (existing != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var rootStateMachine = controller.layers[0].stateMachine;

        foreach (var entry in ClipEntries)
        {
            var clip = LoadClip(entry.ClipName);
            if (clip == null)
            {
                Debug.LogWarning($"Missing animation clip: {entry.ClipName}");
                continue;
            }

            var state = rootStateMachine.AddState(entry.StateName, entry.Position);
            state.motion = clip;

            if (entry.StateName == "Idle")
                rootStateMachine.defaultState = state;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Built player animator at {ControllerPath}");
    }

    private static void EnsureControllerDirectory()
    {
        if (!Directory.Exists(AnimFolder))
            Directory.CreateDirectory(AnimFolder);
    }

    private static AnimationClip LoadClip(string clipName)
    {
        var guids = AssetDatabase.FindAssets($"{clipName} t:AnimationClip", new[] { AnimFolder });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip != null && clip.name == clipName)
                return clip;
        }

        return AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimFolder}/{clipName}.anim");
    }

    private static readonly (string StateName, string ClipName, PlayerAnimState AnimState, Vector3 Position)[] ClipEntries =
    {
        ("Idle", "Idle", PlayerAnimState.Idle, new Vector3(0f, 0f, 0f)),
        ("LookUp1", "LookUp1", PlayerAnimState.LookUp1, new Vector3(250f, 100f, 0f)),
        ("LookUp2", "LookUp2", PlayerAnimState.LookUp2, new Vector3(250f, 0f, 0f)),
        ("Run1", "Run1", PlayerAnimState.Run1, new Vector3(500f, 100f, 0f)),
        ("Run2", "Run2", PlayerAnimState.Run2, new Vector3(500f, 0f, 0f)),
        ("LookUpRun1", "LookUpRun1", PlayerAnimState.LookUpRun1, new Vector3(750f, 100f, 0f)),
        ("LookUpRun2", "LookUpRun2", PlayerAnimState.LookUpRun2, new Vector3(750f, 0f, 0f)),
        ("LookUpRun3", "LookUpRun3", PlayerAnimState.LookUpRun3, new Vector3(750f, -100f, 0f)),
        ("Crouch1", "Crouch1", PlayerAnimState.Crouch1, new Vector3(1000f, 100f, 0f)),
        ("Crouch2", "Crouch2", PlayerAnimState.Crouch2, new Vector3(1000f, 0f, 0f)),
        ("CrouchMove1", "CrouchMove1", PlayerAnimState.CrouchMove1, new Vector3(1250f, 100f, 0f)),
        ("CrouchMove2", "CrouchMove2", PlayerAnimState.CrouchMove2, new Vector3(1250f, 0f, 0f)),
        ("Jump", "Jump", PlayerAnimState.Jump, new Vector3(1500f, 100f, 0f)),
        ("Jump2", "Jump2", PlayerAnimState.Jump2, new Vector3(1500f, 0f, 0f)),
        ("Land1", "Land1", PlayerAnimState.Land1, new Vector3(1750f, 0f, 0f)),
        ("Stop1", "Stop1", PlayerAnimState.Stop1, new Vector3(2000f, 100f, 0f)),
        ("Stop2", "Stop2", PlayerAnimState.Stop2, new Vector3(2000f, 0f, 0f)),
        ("Shoot1", "Shoot1", PlayerAnimState.Shoot1, new Vector3(2250f, 100f, 0f)),
        ("Shoot2", "Shoot2", PlayerAnimState.Shoot2, new Vector3(2250f, 0f, 0f)),
        ("ShootUp1", "ShootUp1", PlayerAnimState.ShootUp1, new Vector3(2500f, 100f, 0f)),
        ("ShootUp2", "ShootUp2", PlayerAnimState.ShootUp2, new Vector3(2500f, 0f, 0f)),
        ("CrouchShoot1", "CrouchShoot1", PlayerAnimState.CrouchShoot1, new Vector3(2750f, 100f, 0f)),
        ("CrouchShoot2", "CrouchShoot2", PlayerAnimState.CrouchShoot2, new Vector3(2750f, 0f, 0f)),
        ("ShootDown1", "ShootDown1", PlayerAnimState.ShootDown1, new Vector3(3000f, 100f, 0f)),
        ("ShootDown2", "ShootDown2", PlayerAnimState.ShootDown2, new Vector3(3000f, 0f, 0f)),
    };
}
#endif
