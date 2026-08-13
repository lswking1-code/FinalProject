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

    [Header("Bob 攻击扩展（仅 FullBodyMelee）")]
    [Tooltip("地面向上攻击 · 替换动画机 default_up_melee")]
    public AnimationClip upMelee;
    [Tooltip("空中向上攻击 · 替换动画机 default_air_up_melee")]
    public AnimationClip airUpMelee;
    [Tooltip("向下攻击扩展 · 替换 default_down_melee；JumpDownAttack 用 default_jump_downattack（未填 downMelee 时保持基座共用片）")]
    public AnimationClip downMelee;
    [Tooltip("特技 · 替换动画机 default_special（rush/whip/buzzsaw；空手无）")]
    public AnimationClip special;

    [Header("蓄力射击（分轨；基座 clip 名 charge_*）")]
    public AnimationClip chargeStart;
    public AnimationClip chargeLoop;
    public AnimationClip chargeShoot;
    public AnimationClip lookUpChargeStart;
    public AnimationClip lookUpChargeLoop;
    public AnimationClip lookUpChargeShoot;
    public AnimationClip lookDownChargeStart;
    public AnimationClip lookDownChargeLoop;
    public AnimationClip lookDownChargeShoot;
    public AnimationClip crouchChargeStart;
    public AnimationClip crouchChargeLoop;
    public AnimationClip crouchChargeShoot;

    [Header("蹲姿 / 其它")]
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
            case "charge_start": return chargeStart;
            case "charge_loop": return chargeLoop;
            case "charge_shoot": return chargeShoot;
            case "lookup_charge_start": return lookUpChargeStart;
            case "lookup_charge_loop": return lookUpChargeLoop;
            case "lookup_charge_shoot": return lookUpChargeShoot;
            case "lookdown_charge_start": return lookDownChargeStart;
            case "lookdown_charge_loop": return lookDownChargeLoop;
            case "lookdown_charge_shoot": return lookDownChargeShoot;
            case "crouch_charge_start": return crouchChargeStart;
            case "crouch_charge_loop": return crouchChargeLoop;
            case "crouch_charge_shoot": return crouchChargeShoot;
            // 兼容直接挂火焰/枪姿态 clip 为蓄力基座 motion 的情况
            case "f_stand_shoot": return chargeShoot;
            case "f_lookup_shoot": return lookUpChargeShoot;
            case "f_lookdown_shoot": return lookDownChargeShoot;
            case "f_crouch_shoot": return crouchChargeShoot;
            case "s_stand_up_shoot": return chargeStart != null ? chargeStart : shoot;
            case "s_lookup_shoot": return lookUpChargeStart != null ? lookUpChargeStart : lookUpShoot;
            case "s_lookdown_shoot": return lookDownChargeStart != null ? lookDownChargeStart : lookDownShoot;
            case "s_crouch_shoot": return crouchChargeStart != null ? crouchChargeStart : crouchShoot;
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
            case "default_up_melee": return upMelee;
            case "default_air_up_melee": return airUpMelee != null ? airUpMelee : upMelee;
            case "default_down_melee": return downMelee;
            // 空中落地砸地四武器共用基座片；勿回退 airMelee，否则会叠上分武器空中攻击动画/音效
            case "default_jump_downattack": return downMelee;
            case "default_special": return special;
            case "default_attack": return melee;
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
