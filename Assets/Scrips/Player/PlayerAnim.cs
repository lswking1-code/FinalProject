using UnityEngine;

/// <summary>
/// 玩家动画控制：站立时上下半身分层显示，蹲姿时切换为全身整图显示。
/// </summary>
public class PlayerAnim : MonoBehaviour
{
    [Header("动画机")]
    public Animator upperAnimator;
    public Animator lowerAnimator;
    public Animator crouchAnimator;

    [Header("显示层")]
    [Tooltip("站立时的上半身物体")]
    public GameObject upBody;
    [Tooltip("站立时的下半身物体")]
    public GameObject downBody;
    [Tooltip("蹲姿时的全身物体（整图精灵）")]
    public GameObject crouchBody;

    /// <summary>当前是否处于蹲姿模式</summary>
    private bool isCrouching;

    void Start()
    {
        // 初始化为站立显示
        SetStandingMode();
    }

    public bool IsCrouching => isCrouching;

    /// <summary>进入蹲姿：隐藏上下半身，显示全身蹲姿层，播放下蹲过渡动画</summary>
    public void PlayCrouchAnim()
    {
        if (isCrouching)
            return;

        isCrouching = true;
        upBody.SetActive(false);
        downBody.SetActive(false);
        crouchBody.SetActive(true);

        ResetCrouchParams();
        crouchAnimator.Play("CrouchStart", 0, 0f);
    }

    /// <summary>退出蹲姿：恢复上下半身显示</summary>
    public void PlayStandAnim()
    {
        if (!isCrouching)
            return;

        isCrouching = false;
        ResetCrouchParams();
        crouchBody.SetActive(false);
        upBody.SetActive(true);
        downBody.SetActive(true);
    }

    /// <summary>停止移动，回到待机（站立或蹲姿各自处理）</summary>
    public void PlayIdleAnim()
    {
        if (isCrouching)
        {
            crouchAnimator.SetBool("IsRun", false);
            crouchAnimator.SetBool("IsShoot", false);
            return;
        }

        upperAnimator.SetBool("IsRun", false);
        lowerAnimator.SetBool("IsRun", false);
        upperAnimator.SetBool("IsShoot", false);
        upperAnimator.SetBool("IsLookUp", false);
        upperAnimator.SetBool("IsLookDown", false);
    }

    /// <summary>开始移动（站立跑步 / 蹲姿爬行）</summary>
    public void PlayRunAnim()
    {
        if (isCrouching)
        {
            crouchAnimator.SetBool("IsRun", true);
            return;
        }

        upperAnimator.SetBool("IsRun", true);
        lowerAnimator.SetBool("IsRun", true);
    }

    /// <summary>跳跃会先退出蹲姿，再触发上下半身的空中动画</summary>
    public void PlayJumpAnim()
    {
        if (isCrouching)
            PlayStandAnim();

        upperAnimator.SetBool("IsGrounded", false);
        upperAnimator.SetTrigger("Air");
        lowerAnimator.SetTrigger("Air");
    }

    /// <summary>同步地面状态到动画机，用于空中/落地状态切换</summary>
    public void UpdateGroundedState(bool grounded)
    {
        if (isCrouching)
            return;

        upperAnimator.SetBool("IsGrounded", grounded);
    }

    /// <summary>近战攻击（站立 / 蹲姿自动分流）</summary>
    public void PlayMeleeAnim()
    {
        if (isCrouching)
        {
            crouchAnimator.SetTrigger("Melee");
            return;
        }

        upperAnimator.SetTrigger("Melee");
    }

    /// <summary>投掷（站立 / 蹲姿自动分流）</summary>
    public void PlayThrowAnim()
    {
        if (isCrouching)
        {
            crouchAnimator.SetTrigger("Throw");
            return;
        }

        upperAnimator.SetTrigger("Throw");
    }

    /// <summary>射击（站立 / 蹲姿自动分流）</summary>
    public void PlayShootAnim()
    {
        if (isCrouching)
        {
            crouchAnimator.SetBool("IsShoot", true);
            return;
        }

        upperAnimator.SetBool("IsShoot", true);
    }

    /// <summary>抬头瞄准（仅站立时有效）</summary>
    public void PlayLookUpAnim()
    {
        if (isCrouching)
            return;

        upperAnimator.SetBool("IsLookUp", true);
    }

    /// <summary>低头瞄准（仅站立时有效）</summary>
    public void PlayLookDownAnim()
    {
        if (isCrouching)
            return;

        upperAnimator.SetBool("IsLookDown", true);
    }

    /// <summary>切换到站立显示层</summary>
    private void SetStandingMode()
    {
        isCrouching = false;
        if (crouchBody != null)
            crouchBody.SetActive(false);
        if (upBody != null)
            upBody.SetActive(true);
        if (downBody != null)
            downBody.SetActive(true);
    }

    /// <summary>重置蹲姿动画机的 Bool 参数</summary>
    private void ResetCrouchParams()
    {
        if (crouchAnimator == null)
            return;

        crouchAnimator.SetBool("IsRun", false);
        crouchAnimator.SetBool("IsShoot", false);
    }
}
