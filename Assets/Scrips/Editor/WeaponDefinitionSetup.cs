#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class WeaponDefinitionSetup
{
    const string WeaponsFolder = "Assets/Data SO/Weapons";
    const string ClipRoot = "Assets/Arts/Metal Slug";
    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

    [InitializeOnLoadMethod]
    static void ScheduleEnsure()
    {
        EditorApplication.delayCall += () =>
        {
            // 仅补缺 asset / 接线；不覆盖 Inspector 里已改过的动画引用
            EnsureWeaponDefinitions(forceFill: false);
            EnsurePlayerWeaponController(forceFill: false);
        };
    }

    [MenuItem("Lost Division/Ensure Weapon Definitions")]
    public static void EnsureWeaponDefinitionsMenu()
    {
        EnsureWeaponDefinitions(forceFill: true);
        EnsurePlayerWeaponController(forceFill: true);
        Debug.Log("Weapon definitions refilled from setup script and PlayerWeaponController ensured.");
    }

    public static WeaponDefinition[] EnsureWeaponDefinitions(bool forceFill = false)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Data SO"))
            AssetDatabase.CreateFolder("Assets", "Data SO");
        if (!AssetDatabase.IsValidFolder(WeaponsFolder))
            AssetDatabase.CreateFolder("Assets/Data SO", "Weapons");

        var defs = new WeaponDefinition[5];

        defs[0] = EnsureDef("Weapon_0_Pistol", 0, "手枪", true, FillPistol, forceFill);
        defs[1] = EnsureDef("Weapon_1_Heavy", 1, "机枪", true, FillHeavy, forceFill);
        // ID2=电磁/镭射，ID3=霰弹（与 Weapon_*.asset 内 weaponId、BulletUI/BulletBox 对齐）
        defs[2] = EnsureDef("Weapon_3_Laser", 2, "镭射枪", true, FillLaser, forceFill);
        defs[3] = EnsureDef("Weapon_2_Shotgun", 3, "霰弹枪", false, FillShotgun, forceFill);
        defs[4] = EnsureDef("Weapon_4_Flame", 4, "火焰枪", false, FillFlame, forceFill);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return defs;
    }

    public static void EnsurePlayerWeaponController(bool forceFill = false)
    {
        var defs = EnsureWeaponDefinitions(forceFill);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
        {
            Debug.LogWarning($"[WeaponDefinitionSetup] Player prefab not found: {PlayerPrefabPath}");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        if (root == null)
            return;

        try
        {
            var controller = root.GetComponent<PlayerWeaponController>();
            if (controller == null)
                controller = root.AddComponent<PlayerWeaponController>();

            var so = new SerializedObject(controller);
            var weaponsProp = so.FindProperty("weapons");
            weaponsProp.arraySize = defs.Length;
            for (int i = 0; i < defs.Length; i++)
                weaponsProp.GetArrayElementAtIndex(i).objectReferenceValue = defs[i];

            so.FindProperty("initialWeaponId").intValue = 0;
            so.FindProperty("holdToInitialDuration").floatValue = 0.4f;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static WeaponDefinition EnsureDef(
        string fileName,
        int id,
        string displayName,
        bool enabledInCycle,
        System.Action<WeaponDefinition> fill,
        bool forceFill)
    {
        string path = $"{WeaponsFolder}/{fileName}.asset";
        var def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
        bool created = false;
        if (def == null)
        {
            def = ScriptableObject.CreateInstance<WeaponDefinition>();
            AssetDatabase.CreateAsset(def, path);
            created = true;
        }

        def.weaponId = id;
        def.displayName = displayName;

        // 新建或菜单强制同步时才按 Fill* 写动画；平时保留 Inspector 手改
        if (created || forceFill)
            fill(def);

        // 基础武器 (id 0) 不进 Q/E 轮换，仅长按切回；其它按调用方传入
        if (id == 0)
            def.enabledInCycle = false;
        else if (forceFill || created)
            def.enabledInCycle = enabledInCycle;

        EditorUtility.SetDirty(def);
        return def;
    }

    static AnimationClip Clip(string name) =>
        AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipRoot}/{name}.anim");

    static void FillPistol(WeaponDefinition d)
    {
        // 基座：字段可空，运行时不 Override
        d.idle = Clip("idle_up");
        d.run = Clip("run_up");
        d.jump = Clip("jump_up");
        d.fall = Clip("fall_up");
        d.leap = Clip("leap_up");
        d.leapAir = Clip("leapair_up");
        d.lookUpStart = Clip("lookup_start");
        d.lookUp = Clip("lookup");
        d.lookUpEnd = Clip("lookup_end");
        d.lookDownStart = Clip("lookdown_start");
        d.lookDown = Clip("lookdown");
        d.lookDownEnd = Clip("lookdown_end");
        d.shoot = Clip("shoot");
        d.lookUpShoot = Clip("lookup_shoot");
        d.lookDownShoot = Clip("lookdown_shoot");
        d.melee = Clip("melee");
        d.airMelee = Clip("air_melee");
        d.throwClip = Clip("throw");
        d.airThrow = Clip("air_throw");
        d.weaponSwitch = Clip("stand_draw");
        d.crouch = Clip("crouch");
        d.crouchStart = Clip("crouch_start");
        d.crouchMove = Clip("crouch_move");
        d.crouchTurn = Clip("crouch_turn");
        d.crouchShoot = Clip("crouch_shoot");
        d.crouchMelee = Clip("crouch_melee");
        d.crouchThrow = Clip("crouch_throw");
        d.crouchWeaponSwitch = Clip("crouch_draw");
        d.land = Clip("stop");
        d.turn = Clip("stand_turn");
        d.die = Clip("die");
    }

    static void FillHeavy(WeaponDefinition d)
    {
        // 有专用 Clip 的填专用；缺口用同族 h_* 最近姿态占位（仍是持机枪姿，非手枪兜底）
        d.idle = Clip("h_idle_up");
        d.run = Clip("h_run_up");
        d.jump = Clip("h_leap_up");
        d.fall = Clip("h_idle_up");
        d.leap = Clip("h_leap_up");
        d.leapAir = Clip("h_leap_up");
        d.lookUpStart = Clip("h_lookup_start");
        d.lookUp = Clip("h_lookup");
        d.lookUpEnd = Clip("h_lookup_end");
        d.lookDownStart = Clip("h_lookdown_start");
        d.lookDown = Clip("h_lookdown_start");
        d.lookDownEnd = Clip("h_lookdown_start");
        d.shoot = Clip("h_stand_shoot");
        d.lookUpShoot = Clip("h_lookup_shoot");
        d.lookDownShoot = Clip("h_lookdown_shoot");
        d.melee = Clip("h_melee_up");
        d.airMelee = Clip("h_melee_up");
        d.throwClip = Clip("h_throw_up");
        d.airThrow = Clip("h_throw_up");
        d.weaponSwitch = Clip("h_idle_up"); // 缺专用 draw，暂用持枪 idle
        d.crouch = Clip("h_crouch");
        d.crouchStart = Clip("h_crouch_start");
        d.crouchMove = Clip("h_crouch_move");
        d.crouchTurn = Clip("h_crouch_turn");
        d.crouchShoot = Clip("h_crouch_shoot");
        d.crouchMelee = Clip("h_crouch_melee");
        d.crouchThrow = Clip("h_crouch_throw");
        d.crouchWeaponSwitch = Clip("h_crouch_start");
        d.land = Clip("h_stand_stop");
        d.turn = Clip("h_stand_turn");
        d.die = Clip("h_idle_up");
    }

    static void FillShotgun(WeaponDefinition d)
    {
        ClearAll(d);
        // 完整姿态在 asset 中已维护；forceFill 时至少恢复射击 + 蓄力
        d.shoot = Clip("s_stand_up_shoot");
        d.lookUpShoot = Clip("s_lookup_shoot");
        d.lookDownShoot = Clip("s_lookdown_shoot");
        d.crouchShoot = Clip("s_crouch_shoot");
        d.chargeStart = Clip("s_stand_up_shoot");
        d.chargeLoop = Clip("s_stand_up_shoot");
        d.chargeShoot = Clip("f_stand_shoot");
        d.lookUpChargeStart = Clip("s_lookup_shoot");
        d.lookUpChargeLoop = Clip("s_lookup_shoot");
        d.lookUpChargeShoot = Clip("f_lookup_shoot");
        d.lookDownChargeStart = Clip("s_lookdown_shoot");
        d.lookDownChargeLoop = Clip("s_lookdown_shoot");
        d.lookDownChargeShoot = Clip("f_lookdown_shoot");
        d.crouchChargeStart = Clip("s_crouch_shoot");
        d.crouchChargeLoop = Clip("s_crouch_shoot");
        d.crouchChargeShoot = Clip("f_crouch_shoot");
    }

    static void FillLaser(WeaponDefinition d)
    {
        ClearAll(d);
        d.shoot = Clip("l_stand_up_shoot");
        d.lookUpShoot = Clip("l_lookup_shoot");
        d.lookDownShoot = Clip("l_lookdown_shoot");
        d.crouchShoot = Clip("l_crouch_shoot");
    }

    static void FillFlame(WeaponDefinition d)
    {
        ClearAll(d);
        d.shoot = Clip("f_stand_shoot");
        d.lookUpShoot = Clip("f_lookup_shoot");
        d.lookDownShoot = Clip("f_lookdown_shoot");
        d.crouchShoot = Clip("f_crouch_shoot");
    }

    static void ClearAll(WeaponDefinition d)
    {
        d.idle = d.run = d.jump = d.fall = d.leap = d.leapAir = null;
        d.lookUpStart = d.lookUp = d.lookUpEnd = null;
        d.lookDownStart = d.lookDown = d.lookDownEnd = null;
        d.shoot = d.lookUpShoot = d.lookDownShoot = null;
        d.melee = d.airMelee = d.throwClip = d.airThrow = d.weaponSwitch = null;
        d.chargeStart = d.chargeLoop = d.chargeShoot = null;
        d.lookUpChargeStart = d.lookUpChargeLoop = d.lookUpChargeShoot = null;
        d.lookDownChargeStart = d.lookDownChargeLoop = d.lookDownChargeShoot = null;
        d.crouchChargeStart = d.crouchChargeLoop = d.crouchChargeShoot = null;
        d.crouch = d.crouchStart = d.crouchMove = d.crouchTurn = null;
        d.crouchShoot = d.crouchMelee = d.crouchThrow = d.crouchWeaponSwitch = null;
        d.land = d.turn = d.die = null;
    }
}
#endif
