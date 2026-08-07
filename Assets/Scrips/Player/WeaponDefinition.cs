using UnityEngine;

/// <summary>
/// 武器姿态 / 切枪动画替换定义。
/// <see cref="AnimProfile.SplitBody"/>：分轨角色（Player / Machinist）上半身 + 蹲姿全身。
/// <see cref="AnimProfile.FullBodyMelee"/>：Bob 单 Animator 全身（default_* 基座名）。
/// </summary>
[CreateAssetMenu(menuName = "Player/Weapon Definition", fileName = "WeaponDefinition")]
public class WeaponDefinition : ScriptableObject
{
    public enum AnimProfile
    {
        [Tooltip("分轨：上半身 + FullBody 蹲姿（Player / Machinist）")]
        SplitBody = 0,
        [Tooltip("全身近战：Bob / Melee_Player（melee_full 的 default_* 基座）")]
        FullBodyMelee = 1,
    }

    [Header("基本")]
    public int weaponId;
    public string displayName;
    [Tooltip("勾选后可进入 Q/E 轮换；未填写的动画字段会沿用 Animator 基座 Clip")]
    public bool enabledInCycle = true;
    [Tooltip("决定 Inspector 展示哪套替换槽；切枪映射仍按基座 Clip 名解析，两套可共用同一字段。")]
    public AnimProfile animProfile = AnimProfile.SplitBody;

    // —— 以下字段运行时两套角色共用；Inspector 按 animProfile 分区展示 ——

    public AnimationClip idle;
    public AnimationClip run;
    public AnimationClip jump;
    public AnimationClip fall;
    public AnimationClip leap;
    public AnimationClip leapAir;

    public AnimationClip lookUpStart;
    public AnimationClip lookUp;
    public AnimationClip lookUpEnd;
    public AnimationClip lookDownStart;
    public AnimationClip lookDown;
    public AnimationClip lookDownEnd;

    public AnimationClip shoot;
    public AnimationClip lookUpShoot;
    public AnimationClip lookDownShoot;
    public AnimationClip melee;
    public AnimationClip airMelee;
    public AnimationClip throwClip;
    public AnimationClip airThrow;
    public AnimationClip weaponSwitch;

    public AnimationClip crouch;
    public AnimationClip crouchStart;
    public AnimationClip crouchMove;
    public AnimationClip crouchTurn;
    public AnimationClip crouchShoot;
    public AnimationClip crouchMelee;
    public AnimationClip crouchThrow;
    public AnimationClip crouchWeaponSwitch;
    public AnimationClip land;
    public AnimationClip turn;
    public AnimationClip die;

    public bool IsFullBodyMelee => animProfile == AnimProfile.FullBodyMelee;

    /// <summary>
    /// 是否全部姿态字段已填。仅供编辑器/排查；未填字段切枪时沿用 Animator 基座动画，不作为切枪门槛。
    /// 手枪(0)恒为 true。分轨与 Bob 校验字段不同。
    /// </summary>
    public bool IsPoseComplete
    {
        get
        {
            if (weaponId == 0)
                return true;

            if (animProfile == AnimProfile.FullBodyMelee)
            {
                return idle && run && jump && (fall || jump) && weaponSwitch
                    && melee && (airMelee || melee);
            }

            return idle && run && jump && fall && leap && leapAir
                && lookUpStart && lookUp && lookUpEnd
                && lookDownStart && lookDown && lookDownEnd
                && shoot && lookUpShoot && lookDownShoot
                && melee && airMelee && throwClip && airThrow && weaponSwitch
                && crouch && crouchStart && crouchMove && crouchTurn
                && crouchShoot && crouchMelee && crouchThrow && crouchWeaponSwitch
                && land && turn && die;
        }
    }

    public bool CanEnterCycle => enabledInCycle;

    /// <summary>
    /// 按基座 Clip 名解析替换 Clip。返回 null 表示保持原 Clip。
    /// weaponId == 0 一律保持基座。
    /// </summary>
    public AnimationClip GetOverrideForBaseClip(AnimationClip original)
    {
        if (original == null || weaponId == 0)
            return null;

        switch (original.name)
        {
            case "idle_up": return idle;
            case "run_up": return run;
            case "jump_up": return jump;
            case "fall_up": return fall;
            case "leap_up": return leap;
            case "leapair_up": return leapAir;
            case "lookup_start": return lookUpStart;
            case "lookup": return lookUp;
            case "lookup_end": return lookUpEnd;
            case "lookdown_start": return lookDownStart;
            case "lookdown": return lookDown;
            case "lookdown_end": return lookDownEnd;
            case "shoot": return shoot;
            case "lookup_shoot": return lookUpShoot;
            case "lookdown_shoot": return lookDownShoot;
            case "melee": return melee;
            case "air_melee": return airMelee;
            case "throw": return throwClip;
            case "air_throw": return airThrow;
            case "stand_draw": return weaponSwitch;
            // Bob / melee_full 基座
            case "default_switch": return weaponSwitch;
            case "default_idle": return idle;
            case "default_run": return run;
            case "default_jump": return jump;
            case "default_fall": return fall != null ? fall : jump;
            case "default_melee": return melee;
            case "default_air_melee": return airMelee != null ? airMelee : melee;
            case "crouch": return crouch;
            case "crouch_start": return crouchStart;
            case "crouch_move": return crouchMove;
            case "crouch_turn": return crouchTurn;
            case "crouch_shoot": return crouchShoot;
            case "crouch_melee": return crouchMelee;
            case "crouch_throw": return crouchThrow;
            case "crouch_draw": return crouchWeaponSwitch;
            case "stop": return land;
            case "stand_turn": return turn;
            case "die": return die;
            case "idle_turning": return turn;
            default: return null;
        }
    }
}
