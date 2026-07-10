using UnityEngine;

public class GameOverActions : MonoBehaviour
{
    VoidEventSO loadDataEvent;

    public void Configure(VoidEventSO loadEvent)
    {
        loadDataEvent = loadEvent;
    }

    public void OnRestartFromSave()
    {
        loadDataEvent?.RaiseEvent();
    }
}
