using UnityEngine;
using UnityEngine.EventSystems;

public class MenuActions : MonoBehaviour
{
    public VoidEventSO newGameEvent;
    public VoidEventSO saveDataEvent;
    public VoidEventSO loadDataEvent;
    public GameObject startMenu;
    public GameObject characterSelect;

    void Awake()
    {
        if (startMenu == null)
        {
            var t = transform.Find("StartMenu");
            if (t != null)
                startMenu = t.gameObject;
        }

        if (characterSelect == null)
        {
            var t = transform.Find("CharacterSelect");
            if (t != null)
                characterSelect = t.gameObject;
        }

        if (characterSelect != null)
            characterSelect.SetActive(false);
        if (startMenu != null)
            startMenu.SetActive(true);
    }

    public void OnNewGame()
    {
        if (startMenu != null)
            startMenu.SetActive(false);
        if (characterSelect != null)
            characterSelect.SetActive(true);
    }

    public void ConfirmNewGame()
    {
        newGameEvent?.RaiseEvent();
    }

    public void BackToStartMenu()
    {
        if (characterSelect != null)
            characterSelect.SetActive(false);
        if (startMenu != null)
            startMenu.SetActive(true);

        var menu = GetComponent<Menu>();
        if (menu != null && menu.newGameButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(menu.newGameButton);
    }

    public void OnSave()
    {
        saveDataEvent?.RaiseEvent();
    }

    public void OnLoad()
    {
        loadDataEvent?.RaiseEvent();
    }

    public void OnExit()
    {
        Debug.Log("Quit!");
        Application.Quit();
    }
}
