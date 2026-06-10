using UnityEngine;

public class MenuActions : MonoBehaviour
{
    public VoidEventSO newGameEvent;
    public VoidEventSO saveDataEvent;
    public VoidEventSO loadDataEvent;

    public void OnNewGame()
    {
        newGameEvent?.RaiseEvent();
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
