#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按 <see cref="WeaponDefinition.AnimProfile"/> 切换 Inspector：
/// FullBodyMelee 只展示 Bob 全身替换槽；SplitBody 展示分轨角色布局。
/// </summary>
[CustomEditor(typeof(WeaponDefinition))]
public class WeaponDefinitionEditor : Editor
{
    SerializedProperty weaponId;
    SerializedProperty displayName;
    SerializedProperty enabledInCycle;
    SerializedProperty animProfile;

    SerializedProperty idle, run, jump, fall, leap, leapAir;
    SerializedProperty lookUpStart, lookUp, lookUpEnd;
    SerializedProperty lookDownStart, lookDown, lookDownEnd;
    SerializedProperty shoot, lookUpShoot, lookDownShoot;
    SerializedProperty melee, airMelee, throwClip, airThrow, weaponSwitch;
    SerializedProperty crouch, crouchStart, crouchMove, crouchTurn;
    SerializedProperty crouchShoot, crouchMelee, crouchThrow, crouchWeaponSwitch;
    SerializedProperty land, turn, die;

    void OnEnable()
    {
        weaponId = serializedObject.FindProperty("weaponId");
        displayName = serializedObject.FindProperty("displayName");
        enabledInCycle = serializedObject.FindProperty("enabledInCycle");
        animProfile = serializedObject.FindProperty("animProfile");

        idle = serializedObject.FindProperty("idle");
        run = serializedObject.FindProperty("run");
        jump = serializedObject.FindProperty("jump");
        fall = serializedObject.FindProperty("fall");
        leap = serializedObject.FindProperty("leap");
        leapAir = serializedObject.FindProperty("leapAir");

        lookUpStart = serializedObject.FindProperty("lookUpStart");
        lookUp = serializedObject.FindProperty("lookUp");
        lookUpEnd = serializedObject.FindProperty("lookUpEnd");
        lookDownStart = serializedObject.FindProperty("lookDownStart");
        lookDown = serializedObject.FindProperty("lookDown");
        lookDownEnd = serializedObject.FindProperty("lookDownEnd");

        shoot = serializedObject.FindProperty("shoot");
        lookUpShoot = serializedObject.FindProperty("lookUpShoot");
        lookDownShoot = serializedObject.FindProperty("lookDownShoot");
        melee = serializedObject.FindProperty("melee");
        airMelee = serializedObject.FindProperty("airMelee");
        throwClip = serializedObject.FindProperty("throwClip");
        airThrow = serializedObject.FindProperty("airThrow");
        weaponSwitch = serializedObject.FindProperty("weaponSwitch");

        crouch = serializedObject.FindProperty("crouch");
        crouchStart = serializedObject.FindProperty("crouchStart");
        crouchMove = serializedObject.FindProperty("crouchMove");
        crouchTurn = serializedObject.FindProperty("crouchTurn");
        crouchShoot = serializedObject.FindProperty("crouchShoot");
        crouchMelee = serializedObject.FindProperty("crouchMelee");
        crouchThrow = serializedObject.FindProperty("crouchThrow");
        crouchWeaponSwitch = serializedObject.FindProperty("crouchWeaponSwitch");
        land = serializedObject.FindProperty("land");
        turn = serializedObject.FindProperty("turn");
        die = serializedObject.FindProperty("die");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(weaponId);
        EditorGUILayout.PropertyField(displayName);
        EditorGUILayout.PropertyField(enabledInCycle);
        EditorGUILayout.PropertyField(animProfile, new GUIContent("动画替换配置", "SplitBody=分轨角色；FullBodyMelee=Bob 全身"));

        var profile = (WeaponDefinition.AnimProfile)animProfile.enumValueIndex;
        EditorGUILayout.Space(6);

        if (profile == WeaponDefinition.AnimProfile.FullBodyMelee)
            DrawBobFullBody();
        else
            DrawSplitBody();

        var def = (WeaponDefinition)target;
        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.Toggle("姿态是否配齐 (IsPoseComplete)", def.IsPoseComplete);
        }

        if (profile == WeaponDefinition.AnimProfile.FullBodyMelee && def.weaponId == 0)
        {
            EditorGUILayout.HelpBox(
                "weaponId = 0 时不会做 Override（与基座 knife/default 保持一致）。切换到其他武器 id 时才会应用下列替换。",
                MessageType.Info);
        }

        serializedObject.ApplyModifiedProperties();
    }

    void DrawBobFullBody()
    {
        EditorGUILayout.LabelField("Bob / 全身近战 · 动画替换", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "对应 melee_full 基座 Clip 名。未填字段保留动画机默认 motion。",
            MessageType.None);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Locomotion（替换 default_*）", EditorStyles.boldLabel);
        DrawClip(idle, "Idle", "default_idle");
        DrawClip(run, "Run", "default_run");
        DrawClip(jump, "Jump", "default_jump");
        DrawClip(fall, "Fall", "default_fall（空则沿用 Jump）");
        DrawClip(leap, "Leap", "空中前跃，可与 Jump 同 clip");
        DrawClip(leapAir, "Leap Air", "前跃下坠，可与 Fall 同 clip");

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("切枪", EditorStyles.boldLabel);
        DrawClip(weaponSwitch, "Weapon Switch", "default_switch · 切到本武器时播放");

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("攻击", EditorStyles.boldLabel);
        DrawClip(melee, "Melee", "default_melee");
        DrawClip(airMelee, "Air Melee", "default_air_melee");
        DrawClip(crouchMelee, "Crouch Melee", "蹲姿近战");

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("其它全身", EditorStyles.boldLabel);
        DrawClip(land, "Land", "着陆");
        DrawClip(turn, "Turn", "转身 / idle_turning");
        DrawClip(crouch, "Crouch", "蹲待机");
        DrawClip(crouchStart, "Crouch Start", "下蹲起势");
        DrawClip(crouchMove, "Crouch Move", "蹲走");
        DrawClip(crouchTurn, "Crouch Turn", "蹲转身");
        DrawClip(die, "Die", "死亡");
    }

    void DrawSplitBody()
    {
        EditorGUILayout.LabelField("分轨角色 · 动画替换", EditorStyles.boldLabel);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("上半身 Locomotion", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(idle);
        EditorGUILayout.PropertyField(run);
        EditorGUILayout.PropertyField(jump);
        EditorGUILayout.PropertyField(fall);
        EditorGUILayout.PropertyField(leap);
        EditorGUILayout.PropertyField(leapAir);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("上半身 Look", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(lookUpStart);
        EditorGUILayout.PropertyField(lookUp);
        EditorGUILayout.PropertyField(lookUpEnd);
        EditorGUILayout.PropertyField(lookDownStart);
        EditorGUILayout.PropertyField(lookDown);
        EditorGUILayout.PropertyField(lookDownEnd);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("上半身射击 / 动作 / 切枪", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(shoot);
        EditorGUILayout.PropertyField(lookUpShoot);
        EditorGUILayout.PropertyField(lookDownShoot);
        EditorGUILayout.PropertyField(melee);
        EditorGUILayout.PropertyField(airMelee);
        EditorGUILayout.PropertyField(throwClip);
        EditorGUILayout.PropertyField(airThrow);
        EditorGUILayout.PropertyField(weaponSwitch);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("全身蹲姿 / 切枪 / 其它", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(crouch);
        EditorGUILayout.PropertyField(crouchStart);
        EditorGUILayout.PropertyField(crouchMove);
        EditorGUILayout.PropertyField(crouchTurn);
        EditorGUILayout.PropertyField(crouchShoot);
        EditorGUILayout.PropertyField(crouchMelee);
        EditorGUILayout.PropertyField(crouchThrow);
        EditorGUILayout.PropertyField(crouchWeaponSwitch);
        EditorGUILayout.PropertyField(land);
        EditorGUILayout.PropertyField(turn);
        EditorGUILayout.PropertyField(die);
    }

    static void DrawClip(SerializedProperty prop, string label, string hint)
    {
        EditorGUILayout.PropertyField(prop, new GUIContent(label, hint));
    }
}
#endif
