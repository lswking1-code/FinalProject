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
    SerializedProperty melee, airMelee, upMelee, airUpMelee, downMelee;
    SerializedProperty throwClip, airThrow, weaponSwitch;
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
        upMelee = serializedObject.FindProperty("upMelee");
        airUpMelee = serializedObject.FindProperty("airUpMelee");
        downMelee = serializedObject.FindProperty("downMelee");
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
                "weaponId = 0（空手）时不做 Override。A↔B 六边切枪动画在 Melee_Player 的 PlayerFullBodyAnim 组件上配置。",
                MessageType.Info);
        }

        serializedObject.ApplyModifiedProperties();
    }

    void DrawBobFullBody()
    {
        EditorGUILayout.LabelField("Bob / 全身近战 · 动画替换", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "对应 melee_full 的 default_* 基座。rush/whip/buzzsaw 之间的 from→to 切枪在 PlayerFullBodyAnim 上配置，不在此处。",
            MessageType.None);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Locomotion（替换 default_*）", EditorStyles.boldLabel);
        DrawClip(idle, "Idle", "default_idle");
        DrawClip(run, "Run", "default_run");
        DrawClip(jump, "Jump", "default_jump");
        DrawClip(fall, "Fall", "default_fall（空则沿用 Jump）");
        DrawClip(leap, "Leap", "可与 Jump 同 clip");
        DrawClip(leapAir, "Leap Air", "可与 Fall 同 clip");

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("切枪（空手相关 / 方向表缺失时 fallback）", EditorStyles.boldLabel);
        DrawClip(weaponSwitch, "Weapon Switch", "default_switch · 与空手切换或六边表未配时使用");

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("攻击", EditorStyles.boldLabel);
        DrawClip(melee, "站立攻击 Attack", "default_melee · *_attack");
        DrawClip(airMelee, "跳跃攻击 Jump Attack", "default_air_melee · *_jump_attack");
        DrawClip(crouchMelee, "蹲姿攻击 Crouch", "蹲姿近战");

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("向上 / 向下攻击", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "对应 melee_full 状态 UpMelee / AirUpMelee / DownMelee。上看(W)/下看(空中 S) 时近战会选用。",
            MessageType.Info);
        DrawClip(upMelee, "向上攻击 Up", "地面向上 · default_up_melee · *_upattack");
        DrawClip(airUpMelee, "空中向上攻击 Air Up", "空中向上 · default_air_up_melee · *_jump_upattack");
        DrawClip(downMelee, "向下攻击 Down", "向下 · default_down_melee（可空，空中回退跳跃攻击）");

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("其它全身", EditorStyles.boldLabel);
        DrawClip(land, "Land", "着陆");
        DrawClip(turn, "Turn", "转身");
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
        EditorGUILayout.LabelField("蓄力射击（基座 charge_* / crouch_charge_*）", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("chargeStart"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("chargeLoop"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("chargeShoot"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lookUpChargeStart"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lookUpChargeLoop"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lookUpChargeShoot"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lookDownChargeStart"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lookDownChargeLoop"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lookDownChargeShoot"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("crouchChargeStart"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("crouchChargeLoop"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("crouchChargeShoot"));

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

    void DrawClip(SerializedProperty prop, string label, string hint)
    {
        if (prop == null)
        {
            EditorGUILayout.HelpBox($"字段丢失：{label}（请确认 WeaponDefinition 已重新编译）", MessageType.Warning);
            return;
        }

        EditorGUILayout.PropertyField(prop, new GUIContent(label, hint));
    }
}
#endif
