using UnityEngine;

public static class PlayerAnimatorIds
{
    public static readonly int State = Animator.StringToHash("State");

    public static readonly int Idle = Animator.StringToHash("Idle");
    public static readonly int LookUp1 = Animator.StringToHash("LookUp1");
    public static readonly int LookUp2 = Animator.StringToHash("LookUp2");
    public static readonly int Run1 = Animator.StringToHash("Run1");
    public static readonly int Run2 = Animator.StringToHash("Run2");
    public static readonly int LookUpRun1 = Animator.StringToHash("LookUpRun1");
    public static readonly int LookUpRun2 = Animator.StringToHash("LookUpRun2");
    public static readonly int LookUpRun3 = Animator.StringToHash("LookUpRun3");
    public static readonly int Crouch1 = Animator.StringToHash("Crouch1");
    public static readonly int Crouch2 = Animator.StringToHash("Crouch2");
    public static readonly int CrouchMove1 = Animator.StringToHash("CrouchMove1");
    public static readonly int CrouchMove2 = Animator.StringToHash("CrouchMove2");
    public static readonly int Jump = Animator.StringToHash("Jump");
    public static readonly int Jump2 = Animator.StringToHash("Jump2");
    public static readonly int Land1 = Animator.StringToHash("Land1");
    public static readonly int Stop1 = Animator.StringToHash("Stop1");
    public static readonly int Stop2 = Animator.StringToHash("Stop2");
    public static readonly int Shoot1 = Animator.StringToHash("Shoot1");
    public static readonly int Shoot2 = Animator.StringToHash("Shoot2");
    public static readonly int ShootUp1 = Animator.StringToHash("ShootUp1");
    public static readonly int ShootUp2 = Animator.StringToHash("ShootUp2");
    public static readonly int CrouchShoot1 = Animator.StringToHash("CrouchShoot1");
    public static readonly int CrouchShoot2 = Animator.StringToHash("CrouchShoot2");
    public static readonly int ShootDown1 = Animator.StringToHash("ShootDown1");
    public static readonly int ShootDown2 = Animator.StringToHash("ShootDown2");
}

public enum PlayerAnimState
{
    Idle = 0,
    LookUp1 = 1,
    LookUp2 = 2,
    Run1 = 3,
    Run2 = 4,
    LookUpRun1 = 5,
    LookUpRun2 = 6,
    LookUpRun3 = 7,
    Crouch1 = 8,
    Crouch2 = 9,
    CrouchMove1 = 10,
    CrouchMove2 = 11,
    Jump = 12,
    Jump2 = 13,
    Land1 = 14,
    Stop1 = 15,
    Stop2 = 16,
    Shoot1 = 17,
    Shoot2 = 18,
    ShootUp1 = 19,
    ShootUp2 = 20,
    CrouchShoot1 = 21,
    CrouchShoot2 = 22,
    ShootDown1 = 23,
    ShootDown2 = 24,
}
