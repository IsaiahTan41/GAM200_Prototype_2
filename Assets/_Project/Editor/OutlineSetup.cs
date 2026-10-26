#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Add Opponent Outline
    /// 1. Creates Assets/_Project/Art/Materials/SpriteSilhouette.mat ("GUI/Text Shader": draws a sprite as one solid
    ///    colour; referencing it from the scene also makes sure the shader is included in the phone build).
    /// 2. Adds SpriteOutline (red) to the Opponent's sprite (its FighterPoseView "Visual" child, or the Fighter itself)
    ///    and assigns the material. Safe to run again. Undoable. Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/OutlineSetup.cs
    /// </summary>
    public static class OutlineSetup
    {
        const string MaterialFolder = "Assets/_Project/Art/Materials";
        const string MaterialPath = MaterialFolder + "/SpriteSilhouette.mat";

        [MenuItem("KungFu Crashout/Add Opponent Outline", priority = 67)]
        public static void AddOpponentOutline()
        {
            Fighter opponent = null;
            foreach (var f in Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None))
            {
                string n = f.name.ToLowerInvariant();
                if (n.Contains("opponent") || n.Contains("enemy")) { opponent = f; break; }
            }
            if (!opponent)
            {
                EditorUtility.DisplayDialog("Add Opponent Outline",
                    "No Fighter named \"Opponent\" or \"Enemy\" in the open scene. Add SpriteOutline to its sprite by hand instead.", "OK");
                return;
            }

            var material = GetOrCreateMaterial();

            var view = opponent.GetComponentInChildren<FighterPoseView>(true);
            var target = view ? view.gameObject : opponent.gameObject;

            Undo.SetCurrentGroupName("Add Opponent Outline");
            int group = Undo.GetCurrentGroup();

            var outline = target.GetComponent<SpriteOutline>();
            if (!outline) outline = Undo.AddComponent<SpriteOutline>(target);
            var so = new SerializedObject(outline);
            so.FindProperty("silhouetteMaterial").objectReferenceValue = material;
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(target.scene);
            Selection.activeObject = target;

            Debug.Log($"[Outline] SpriteOutline on '{target.name}' ({opponent.name})" +
                      (material ? $", solid silhouette material {MaterialPath}." : ", NO silhouette shader found: tinted copy instead.") +
                      " Tune Colour / Thickness on the component. Save the scene (Ctrl+S).");
        }

        static Material GetOrCreateMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing) return existing;

            var shader = Shader.Find("GUI/Text Shader");
            if (!shader) return null;

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Art")) AssetDatabase.CreateFolder("Assets/_Project", "Art");
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/_Project/Art", "Materials");

            var mat = new Material(shader) { name = "SpriteSilhouette" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
#endif
