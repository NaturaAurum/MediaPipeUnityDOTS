using System;
using MediaPipeUnityDots.Runtime.Tracking;
using UnityEditor;
using Unity.InferenceEngine;
using UnityEngine;

namespace MediaPipeUnityDots.EditorTool
{
    /// <summary>
    /// 다운로드한 Depth ModelAsset을 선택한 소비자 프로바이더에 명시적으로 연결합니다.
    /// Task 프로바이더는 빈 override에서 ModelPaths 기본 경로를 사용하므로 자동 직렬화하지 않습니다.
    /// </summary>
    public static class ModelSetup
    {
        private const string DepthAssetProperty = "_modelAsset";

        [MenuItem("MediaPipe/Models/Assign Depth Model To Selected Providers")]
        public static void AssignDepthModelToSelectedProviders()
        {
            if (!DownloadDepthModel.TryGetDepthModelAsset(out var modelAsset))
            {
                return;
            }

            var assignedCount = AssignDepthModelToSelection(modelAsset);
            if (assignedCount == 0)
            {
                Debug.LogWarning("[MPUD] 선택된 오브젝트와 하위 오브젝트에서 DepthFrameProvider를 찾지 못했습니다.");
                return;
            }

            Debug.Log($"[MPUD] Depth ModelAsset을 {assignedCount}개 프로바이더에 연결했습니다.");
        }

        /// <summary>
        /// 외부 Editor tooling이 선택 오브젝트 집합에 Depth ModelAsset을 연결할 때 사용합니다.
        /// </summary>
        public static int AssignDepthModelToSelection(ModelAsset modelAsset)
        {
            if (modelAsset == null)
            {
                throw new ArgumentNullException(nameof(modelAsset));
            }

            var assignedCount = 0;
            foreach (var root in Selection.gameObjects)
            {
                foreach (var provider in root.GetComponentsInChildren<DepthFrameProvider>(true))
                {
                    var serializedObject = new SerializedObject(provider);
                    var modelProperty = serializedObject.FindProperty(DepthAssetProperty);
                    if (modelProperty == null)
                    {
                        Debug.LogError($"[MPUD] DepthFrameProvider에 {DepthAssetProperty} 필드가 없습니다: {provider.name}");
                        continue;
                    }

                    Undo.RecordObject(provider, "Assign MediaPipe Depth Model");
                    serializedObject.Update();
                    modelProperty.objectReferenceValue = modelAsset;
                    if (serializedObject.ApplyModifiedProperties())
                    {
                        EditorUtility.SetDirty(provider);
                    }

                    assignedCount++;
                }
            }

            return assignedCount;
        }
    }
}
