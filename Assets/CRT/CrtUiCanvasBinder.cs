using UnityEngine;
using UnityEngine.SceneManagement;

public class CrtUiCanvasBinder : MonoBehaviour
{
    const float PlaneDistance = 1f;
    const string UiSortingLayer = "UI";

    static readonly string[] CanvasNames =
    {
        "UI Canvas",
        "Fade Canvas",
        "Menu Canvas"
    };

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Bind();
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        Bind();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Bind();
    }

    static void Bind()
    {
        Camera camera = Camera.main;
        if (camera == null)
            return;

        var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || !ShouldBind(canvas))
                continue;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = PlaneDistance;
            canvas.sortingLayerName = UiSortingLayer;
        }
    }

    static bool ShouldBind(Canvas canvas)
    {
        string name = canvas.gameObject.name;
        for (int i = 0; i < CanvasNames.Length; i++)
        {
            if (name == CanvasNames[i])
                return true;
        }

        return false;
    }
}
