using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace TutorialInfo.Editor
{
    [CustomEditor(typeof(Readme))]
    [InitializeOnLoad]
    sealed class ReadmeEditor : UnityEditor.Editor
    {
        private const string KShowedReadmeSessionStateName = "ReadmeEditor.showedReadme";
        private const string KReadmeSourceDirectory = "Assets/TutorialInfo";

        static ReadmeEditor()
            => EditorApplication.delayCall += SelectReadmeAutomatically;

        private static void SelectReadmeAutomatically()
        {
            if (!SessionState.GetBool(KShowedReadmeSessionStateName, false))
            {
                var readme = SelectReadme();
                SessionState.SetBool(KShowedReadmeSessionStateName, true);

                if (readme && !readme.LoadedLayout)
                {
                    EditorUtility.LoadWindowLayout(Path.Combine(Application.dataPath, "TutorialInfo/Layout.wlt"));
                    readme.LoadedLayout = true;
                }
            }
        }

        private static Readme SelectReadme()
        {
            var ids = AssetDatabase.FindAssets("Readme t:Readme");
            if (ids.Length != 1)
            {
                Debug.Log("Couldn't find a readme");
                return null;
            }

            var readmeObject = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(ids[0]));
            Selection.objects = new UnityEngine.Object[] { readmeObject };
            return (Readme)readmeObject;
        }

        private void RemoveTutorial()
        {
            if (EditorUtility.DisplayDialog("Remove Readme Assets",
            
                $"All contents under {KReadmeSourceDirectory} will be removed, are you sure you want to proceed?",
                "Proceed",
                "Cancel"))
            {
                if (Directory.Exists(KReadmeSourceDirectory))
                {
                    FileUtil.DeleteFileOrDirectory(KReadmeSourceDirectory);
                    FileUtil.DeleteFileOrDirectory(KReadmeSourceDirectory + ".meta");
                }
                else
                {
                    Debug.Log($"Could not find the Readme folder at {KReadmeSourceDirectory}");
                }

                var readmeAsset = SelectReadme();
                if (readmeAsset != null)
                {
                    var path = AssetDatabase.GetAssetPath(readmeAsset);
                    FileUtil.DeleteFileOrDirectory(path + ".meta");
                    FileUtil.DeleteFileOrDirectory(path);
                }

                AssetDatabase.Refresh();
            }
        }

        //Remove ImGUI
        protected sealed override void OnHeaderGUI() { }
        public sealed override void OnInspectorGUI() { }

        public override VisualElement CreateInspectorGUI()
        {
            var readme = (Readme)target;

            VisualElement root = new();
            root.styleSheets.Add(readme.CommonStyle);
            root.styleSheets.Add(EditorGUIUtility.isProSkin ? readme.DarkStyle : readme.LightStyle);

            VisualElement ChainWithClass(VisualElement created, string className)
            {
                created.AddToClassList(className);
                return created;
            }

            //Header
            VisualElement title = new();
            title.AddToClassList("title");
            title.Add(ChainWithClass(new Image() { image = readme.Icon }, "title__icon"));
            title.Add(ChainWithClass(new Label(readme.Title), "title__text"));
            root.Add(title);

            //Content
            foreach (var section in readme.Sections)
            {
                VisualElement part = new();
                part.AddToClassList("section");

                if (!string.IsNullOrEmpty(section.Heading))
                {
                    part.Add(ChainWithClass(new Label(section.Heading), "section__header"));
                }

                if (!string.IsNullOrEmpty(section.Text))
                {
                    part.Add(ChainWithClass(new Label(section.Text), "section__body"));
                }

                if (!string.IsNullOrEmpty(section.LinkText))
                {
                    var link = ChainWithClass(new Label(section.LinkText), "section__link");
                    link.RegisterCallback<ClickEvent>(evt => Application.OpenURL(section.URL));
                    part.Add(link);
                }

                root.Add(part);
            }

            var button = new Button(RemoveTutorial) { text = "Remove Readme Assets" };
            button.AddToClassList("remove-readme-button");
            root.Add(button);

            return root;
        }
    }
}
