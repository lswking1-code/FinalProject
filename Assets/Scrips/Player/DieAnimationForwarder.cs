using UnityEngine;

public class DieAnimationForwarder : MonoBehaviour
{
    public void OnDeathAnimationFinished()
    {
        GetComponentInParent<PlayerDeath>()?.OnDeathAnimationFinished();
    }
}
