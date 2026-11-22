using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → GDD Build → Apply Pause Button Art
    ///
    /// Turns the default "Options" button in the open FightScene into the diamond pause icon (UI_FightHUD_Pause, 96 × 96):
    ///   • Size: drawn at its own 96 px, the HUD's scale (half the 188 px timer diamond it matches), inside a 128 × 128
    ///     invisible touch area (~8 mm on the Pixel 9a). The touch area is the button's own rectangle, so a finger on its
    ///     edge opens the menu instead of being read as an attack (OptionsMenu claims touches on the button's rectangle).
    ///   • Place: top centre, 262 px down: right under the timer diamond and round coins, anchored to the top of the safe
    ///     area so it stays there on every phone.
    ///   • The old "Options" label is kept, hidden. Ctrl+Z undoes it. Safe to run again. Save the scene afterwards.
    ///
    /// Place in: Assets/_Project/Editor/PauseButtonSetup.cs
    /// </summary>
    public static class PauseButtonSetup
    {
        const string SpritePath = "Assets/_Project/Art/UI/FightHUD/UI_FightHUD_Pause.png";
        const float IconSize = 96f;   // the art's own size, at the HUD's 1 px = 1 unit scale
        const float TouchSize = 128f; // touch area around it
        const float FromTop = 262f;   // centre, below the timer diamond (ends ~204) and the coins (y 162)

        [MenuItem("Window/KungFu Crashout/GDD Build/Apply Pause Button Art", priority = 10045)]
        static void Apply()
        {
            var options = Object.FindAnyObjectByType<OptionsMenu>(FindObjectsInactive.Include);
            var button = options ? new SerializedObject(options).FindProperty("optionsButton").objectReferenceValue as Button : null;
            if (!button)
            {
                EditorUtility.DisplayDialog("Pause Button", "Open FightScene first (no OptionsMenu with an Options button found).", "OK");
                return;
            }
            var sprite = ImportSprite();
            if (!sprite)
            {
                EditorUtility.DisplayDialog("Pause Button", $"Missing {SpritePath}.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Apply Pause Button Art");
            int group = Undo.GetCurrentGroup();
            Undo.RegisterFullObjectHierarchyUndo(button.gameObject, "Apply Pause Button Art");

            // Root = the touch area: invisible, but it still takes the tap.
            var rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(TouchSize, TouchSize);
            rt.anchoredPosition = new Vector2(0f, -FromTop);
            rt.localScale = Vector3.one;
            var hit = button.GetComponent<Image>();
            if (!hit) hit = Undo.AddComponent<Image>(button.gameObject);
            hit.sprite = null;
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;

            // The icon
            var iconTf = button.transform.Find("Icon");
            Image icon;
            if (iconTf) icon = iconTf.GetComponent<Image>();
            else
            {
                var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(go, "Apply Pause Button Art");
                go.layer = button.gameObject.layer;
                go.transform.SetParent(button.transform, false);
                icon = go.GetComponent<Image>();
            }
            var irt = icon.rectTransform;
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(IconSize, IconSize);
            irt.anchoredPosition = Vector2.zero;
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.color = Color.white;
            icon.transform.SetAsFirstSibling();

            // Press feedback on the icon, not the invisible area
            button.targetGraphic = icon;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.97f, 0.9f);
            colors.pressedColor = new Color(0.75f, 0.72f, 0.66f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.5f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;

            // Keep the old label, hidden
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
                label.gameObject.SetActive(false);
            foreach (var label in button.GetComponentsInChildren<Text>(true))
                label.gameObject.SetActive(false);

            if (!button.gameObject.activeSelf) button.gameObject.SetActive(true);
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(button.gameObject.scene);
            Selection.activeGameObject = button.gameObject;
            Debug.Log($"[GDD Build] '{button.name}' is now the pause icon ({IconSize:0} px, {TouchSize:0} px touch area, top centre under the timer). Save the scene.");
        }

        static Sprite ImportSprite()
        {
            var ti = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
            if (!ti) return null;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 100f;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.maxTextureSize = 256;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(s);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        }
    }
}
