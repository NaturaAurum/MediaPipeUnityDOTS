using UnityEngine;
using UnityEditor;
using MediaPipeUnityDots.Runtime.Interop;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MediaPipeUnityDotsSamples.HandTracking.Editor
{
    public static class NativeSmokeTestRunner
    {
        [MenuItem("MediaPipe/Run Smoke Test")]
        public static void Run()
        {
            Debug.Log("[MPUD Smoke] === Native Bridge Smoke Test ===");

            string err = MpudBridge.GetLastError();
            Debug.Log($"[MPUD Smoke] Initial error state: {err}");

            string modelPath = System.IO.Path.Combine(
                Application.streamingAssetsPath,
                "MediaPipe/Models/hand_landmarker.task");
            Debug.Log($"[MPUD Smoke] Model path: {modelPath}");

            IntPtr modelPathNative = MarshalStringToUtf8(modelPath);
            try
            {
                var config = new MpudHandTrackerConfig
                {
                    modelAssetPath = modelPathNative,
                    numHands = 1,
                    minDetectionConfidence = 0.5f,
                    minTrackingConfidence = 0.5f,
                    runningMode = 1,
                };

                int status = MpudBridge.mpud_create_hand_tracker(ref config, out IntPtr tracker);
                Debug.Log($"[MPUD Smoke] create_hand_tracker status: {status}");

                if (status != MpudStatus.Ok)
                {
                    Debug.LogWarning($"[MPUD Smoke] create failed: {MpudBridge.GetLastError()}");
                }
                else
                {
                    Debug.Log("[MPUD Smoke] Tracker created successfully!");
                    MpudBridge.mpud_destroy_hand_tracker(tracker);
                    Debug.Log("[MPUD Smoke] destroy_hand_tracker completed");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(modelPathNative);
            }

            Debug.Log("[MPUD Smoke] === Smoke Test Complete (no crash) ===");
        }

        static IntPtr MarshalStringToUtf8(string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr, bytes.Length, 0);
            return ptr;
        }
    }
}
