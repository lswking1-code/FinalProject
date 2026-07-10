#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SceneLoader))]
public class SceneLoaderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var loader = (SceneLoader)target;
        if (loader.playerRegistry == null || loader.playerRegistry.characters.Count == 0)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("角色选择（编辑器）", EditorStyles.boldLabel);

        var names = new string[loader.playerRegistry.characters.Count];
        int currentIndex = loader.playerRegistry.IndexOf(loader.selectedCharacter);
        for (int i = 0; i < names.Length; i++)
        {
            var character = loader.playerRegistry.characters[i];
            names[i] = character != null ? character.displayName : $"角色 {i + 1}";
        }

        if (currentIndex < 0)
            currentIndex = 0;

        EditorGUI.BeginChangeCheck();
        int newIndex = EditorGUILayout.Popup("当前角色", currentIndex, names);
        if (EditorGUI.EndChangeCheck())
            loader.SelectCharacterByIndex(newIndex);

        if (loader.developMode)
        {
            EditorGUILayout.HelpBox(
                "开发模式：运行时可按数字键 1/2/3 切换角色（不触发场景重载）。",
                MessageType.Info);
        }
    }
}
#endif
