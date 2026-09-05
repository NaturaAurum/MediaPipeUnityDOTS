using System;
using System.Runtime.InteropServices;

namespace MediaPipeUnityDots.Runtime.Interop
{
    /// <summary>
    /// MediaPipe Hand Tracker 네이티브 브리지 P/Invoke 래퍼.
    /// 모든 메서드는 메인 스레드에서 호출할 것 (thread_local 에러 저장소 주의).
    /// </summary>
    public static class MpudBridge
    {
        private const string DllName = "mpud_bridge";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpud_create_hand_tracker(
            ref MpudHandTrackerConfig config, out IntPtr tracker);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpud_start_hand_tracker(IntPtr tracker);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpud_destroy_hand_tracker(IntPtr tracker);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpud_submit_frame(
            IntPtr tracker, ref MpudImageFrame frame);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpud_try_get_latest_result(
            IntPtr tracker, out MpudHandResult result);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpud_get_last_error();

        public static string GetLastError()
        {
            var ptr = mpud_get_last_error();
            return ptr == IntPtr.Zero ? "unknown error" : Marshal.PtrToStringAnsi(ptr);
        }
    }

    /// <summary>
    /// MediaPipe Face Tracker 네이티브 브리지 P/Invoke 래퍼.
    /// 모든 메서드는 메인 스레드에서 호출할 것 (thread_local 에러 저장소 주의, hand와 별개).
    /// </summary>
    public static class MpudFaceBridge
    {
        private const string DllName = "mpud_bridge";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpud_create_face_tracker(
            ref MpudFaceTrackerConfig config, out IntPtr tracker);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpud_destroy_face_tracker(IntPtr tracker);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpud_submit_face_frame(
            IntPtr tracker, ref MpudImageFrame frame);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpud_try_get_latest_face_result(
            IntPtr tracker, out MpudFaceResult result);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpud_get_last_face_error();

        public static string GetLastFaceError()
        {
            var ptr = mpud_get_last_face_error();
            return ptr == IntPtr.Zero ? "unknown error" : Marshal.PtrToStringAnsi(ptr);
        }
    }
    /// <summary>
    /// MediaPipe Pose Tracker 네이티브 브리지 P/Invoke 래퍼.
    /// 모든 메서드는 메인 스레드에서 호출할 것 (thread_local 에러 저장소 주의, hand/face와 별개).
    /// </summary>
    public static class MpudPoseBridge
    {
        private const string DllName = "mpud_bridge";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpud_create_pose_tracker(
            ref MpudPoseTrackerConfig config, out IntPtr tracker);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpud_destroy_pose_tracker(IntPtr tracker);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpud_submit_pose_frame(
            IntPtr tracker, ref MpudImageFrame frame);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpud_try_get_latest_pose_result(
            IntPtr tracker, out MpudPoseResult result);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpud_get_last_pose_error();

        public static string GetLastPoseError()
        {
            var ptr = mpud_get_last_pose_error();
            return ptr == IntPtr.Zero ? "unknown error" : Marshal.PtrToStringAnsi(ptr);
        }
    }
}
