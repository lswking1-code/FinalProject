using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Savepoint : MonoBehaviour,IInteractable
{
    [Header("�㲥")]
    public VoidEventSO saveDataEvent;

    [Header("��������")]
    public SpriteRenderer spriteRenderer;
    public GameObject Light2D;
    public Sprite darkSprite;
    public Sprite lightSprite;
    public bool isDone;
   
    private void OnEnable()
    {
        if (spriteRenderer != null)
            spriteRenderer.sprite = isDone ? lightSprite : darkSprite;
        if (Light2D != null)
            Light2D.SetActive(isDone);
    }
    public void TriggerAction()
    {
        if (!isDone)
        {
            isDone= true;
            if (spriteRenderer != null)
                spriteRenderer.sprite = lightSprite;
            if (Light2D != null)
                Light2D.SetActive(true);
            //��������
            saveDataEvent.RaiseEvent();
            this.gameObject.tag = "Untagged";
        }
    }
}
