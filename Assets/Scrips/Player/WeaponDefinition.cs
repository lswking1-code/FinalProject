using UnityEngine;

[CreateAssetMenu(menuName = "Player/Weapon Definition", fileName = "WeaponDefinition")]
public class WeaponDefinition : ScriptableObject
{
    [Header("基本")]
    public int weaponId;
    public string displayName;
    [Tooltip("勾选后可进入 Q/E 轮换；未填写的动画字段会沿用 Animator 基座 Clip")]
    public bool enabledInCycle = true;

    [Header("上半身 Locomotion")]
    public AnimationClip idle;
    public AnimationClip run;
    public AnimationClip jump;
    public AnimationClip fall;
    public AnimationClip leap;
    public AnimationClip leapAir;

    [Header("上半身 Look")]
    public AnimationClip lookUpStart;
    public AnimationClip lookUp;
    public AnimationClip lookUpEnd;
    public AnimationClip lookDownStart;
    public AnimationClip lookDown;
    public AnimationClip lookDownEnd;

    [Header("上半身射击 / 动作 / 切枪")]
    public AnimationClip shoot;
    public AnimationClip lookUpShoot;
    public AnimationClip lookDownShoot;
    public AnimationClip melee;
    public AnimationClip airMelee;
    public AnimationClip throwClip;
    public AnimationClip airThrow;
    public AnimationClip weaponSwitch;

    [Header("全身蹲姿 / 切枪 / 其它")]
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

    /// <summary>
    /// 是否全部姿态字段已填。仅供编辑器/排查；未填字段切枪时沿用 Animator 基座动画，不作为切枪门槛。
    /// 手枪(0)恒为 true。
    /// </summary>
    public bool IsPoseComplete
    {
        get
        {
            if (weaponId == 0)
                return true;

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
    /// 手枪(0)一律保持基座。
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
            default: return null;
        }
    }
}
