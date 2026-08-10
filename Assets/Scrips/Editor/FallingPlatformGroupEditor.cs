#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FallingPlatformGroup))]
public class FallingPlatformGroupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var group = (FallingPlatformGroup)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("切分预览", EditorStyles.boldLabel);

        bool hasPrefab = group.PiecePrefab != null;
        bool hasFalling = hasPrefab && group.PiecePrefab.GetComponentInChildren<FallingPlatform>(true) != null;

        if (!hasPrefab)
        {
            EditorGUILayout.HelpBox("请指定 piecePrefab（带 FallingPlatform 的小平台）。", MessageType.Warning);
        }
        else if (!hasFalling)
        {
            EditorGUILayout.HelpBox("piecePrefab 上找不到 FallingPlatform 组件。", MessageType.Error);
        }

        bool hasTotal = group.TryGetTotalWidth(out float totalWidth);
        bool hasPiece = group.TryGetPieceWidth(out float pieceWidth);
        int count = group.GetPieceCount();

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.FloatField("Total Width", hasTotal ? totalWidth : 0f);
            EditorGUILayout.FloatField("Piece Width", hasPiece ? pieceWidth : 0f);
            EditorGUILayout.IntField("Count", count);
        }

        if (hasTotal && hasPiece && count > 0)
        {
            float segment = totalWidth / count;
            EditorGUILayout.HelpBox(
                $"将生成 {count} 块，每块目标世界宽度约 {segment:0.###}。",
                MessageType.Info);
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(!hasPrefab || !hasFalling || !hasTotal || !hasPiece))
        {
            if (GUILayout.Button("Bake Pieces", GUILayout.Height(28)))
            {
                group.Bake();
            }
        }

        using (new EditorGUI.DisabledScope(!group.HasPieces()))
        {
            if (GUILayout.Button("Clear Pieces"))
            {
                group.ClearPieces();
            }
        }
    }
}
#endif
