using UnityEditor;

namespace MediaPipeUnityDots.Tests.EditMode
{
    internal sealed class AttachedPanelTestWindow : EditorWindow
    {
        public static AttachedPanelTestWindow Open()
        {
            var window = EditorWindow.CreateInstance<AttachedPanelTestWindow>();
            window.Show();
            return window;
        }
    }
}
