using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using MediaPipeUnityDots.Runtime.Interop;
using MediaPipeUnityDots.Runtime.Tracking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// 네이티브 DLL 없이 결과 상태 전환과 caller-owned 복사 계약을 검증한다.
    /// </summary>
    public sealed class TrackingResultContractTests
    {
        private const int WaitTimeoutMs = 5000;

        [Test]
        public void HandService_PollDistinguishesResultLossFaultResetStaleAndDisposed()
        {
            var body = new ScriptedBody<MpudHandResult>();
            using var service = new HandTrackingService(body);
            var pixels = new Color32[4];
            var destination = new MpudNormalizedLandmark[HandTrackingSnapshot.LandmarkCapacity];

            body.Add(HandResult(100, 1, 1, 0.25f));
            Assert.IsTrue(service.TrySubmit(pixels, 2, 2, false, new CaptureStamp(1, 10, 7)));
            Assert.AreEqual(TrackingResultStatus.Success, Take(service));
            Assert.AreEqual(1, service.LatestMetadata.CaptureId);
            Assert.AreEqual(1, service.CopyLatestHandLandmarksTo(0, destination));
            Assert.AreEqual(0.25f, destination[0].x, 1e-6f);

            body.Add(HandResult(200, 0, 0, 0f));
            Assert.IsTrue(service.TrySubmit(pixels, 2, 2, false, new CaptureStamp(2, 20, 7)));
            Assert.AreEqual(TrackingResultStatus.NoDetection, Take(service));
            Assert.IsFalse(service.LatestIsValid);
            Assert.AreEqual(0, service.CopyLatestHandLandmarksTo(0, destination));
            AssertZero(destination);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("native failure"));
            body.Add(HandResult(0, 0, 0, 0f), false, "native failure");
            Assert.IsTrue(service.TrySubmit(pixels, 2, 2, false, new CaptureStamp(3, 30, 7)));
            Assert.AreEqual(TrackingResultStatus.Error, Take(service));
            Assert.IsFalse(service.LatestIsValid);
            StringAssert.Contains("native failure", service.LatestError);
            Assert.AreEqual(0, service.CopyLatestHandLandmarksTo(0, destination));
            AssertZero(destination);

            service.ResetTracker();
            Assert.AreEqual(TrackingResultStatus.Reset, service.LatestStatus);
            body.Add(HandResult(300, 1, 1, 0.75f));
            Assert.IsTrue(SpinWait.SpinUntil(
                () => service.TrySubmit(pixels, 2, 2, false, new CaptureStamp(4, 40, 8)),
                WaitTimeoutMs), "tracker did not reopen after reset");
            Assert.AreEqual(TrackingResultStatus.Success, Take(service));

            body.Add(HandResult(300, 1, 1, 0.9f));
            Assert.IsTrue(service.TrySubmit(pixels, 2, 2, false, new CaptureStamp(5, 50, 8)));
            Assert.AreEqual(TrackingResultStatus.Stale, Take(service));
            Assert.IsFalse(service.LatestIsValid);
            Assert.AreEqual(0, service.CopyLatestHandLandmarksTo(0, destination));
            AssertZero(destination);

            service.Dispose();
            Assert.AreEqual(TrackingResultStatus.Disposed, service.LatestStatus);
            Assert.AreEqual(TrackingResultStatus.Disposed, service.Poll());
            Assert.Throws<ObjectDisposedException>(() => service.CopyLatestHandLandmarksTo(0, destination));
        }

        [Test]
        public void TrackingMetadata_SeparatesWaitingDetectionFaultAndLifecycleStates()
        {
            var resultStatuses = new[]
            {
                TrackingResultStatus.Waiting,
                TrackingResultStatus.Success,
                TrackingResultStatus.NoDetection,
                TrackingResultStatus.Error,
                TrackingResultStatus.Stale,
                TrackingResultStatus.Reset,
                TrackingResultStatus.Disposed,
            };

            foreach (var resultStatus in resultStatuses)
            {
                var metadata = new TrackingResultMetadata(
                    resultStatus, 100, 2, 200, 3, 4, 5, 6);
                Assert.AreEqual(resultStatus, metadata.Status);
                Assert.AreEqual(resultStatus == TrackingResultStatus.Success
                    || resultStatus == TrackingResultStatus.NoDetection, metadata.HasResult);
                Assert.AreEqual(resultStatus == TrackingResultStatus.Error
                    || resultStatus == TrackingResultStatus.Disposed, metadata.IsTerminal);
            }
        }

        [Test]
        public void HandSnapshot_ClampsPayloadAndClearsCallerBufferOnEmptyAndReset()
        {
            var snapshot = new HandTrackingSnapshot();
            var result = HandResult(10, 1, 1, 0.5f);
            snapshot.UpdateFrom(ref result);
            var destination = new MpudNormalizedLandmark[HandTrackingSnapshot.LandmarkCapacity];
            Assert.AreEqual(1, snapshot.CopyHandLandmarksTo(0, destination));
            Assert.AreEqual(0.5f, destination[0].x, 1e-6f);

            result = HandResult(20, 1, 0, 0f);
            snapshot.UpdateFrom(ref result);
            Assert.AreEqual(0, snapshot.CopyHandLandmarksTo(0, destination));
            AssertZero(destination);

            snapshot.ResetToEmpty();
            Assert.AreEqual(0, snapshot.CopyHandWorldLandmarksTo(0, destination));
            AssertZero(destination);
        }

        [Test]
        public void FacePoseAndHolisticSnapshotsCopyRawPayloadAndResetToEmpty()
        {
            var face = new FaceTrackingSnapshot();
            FaceResult(11, out var faceResult);
            face.UpdateFrom(ref faceResult);
            var faceLandmarks = new MpudNormalizedLandmark[FaceTrackingSnapshot.LandmarkCapacity];
            var blendshapes = new float[FaceTrackingSnapshot.BlendshapeCapacity];
            Assert.AreEqual(1, face.CopyFaceLandmarksTo(0, faceLandmarks));
            Assert.AreEqual(1, face.CopyFaceBlendshapesTo(0, blendshapes));
            Assert.AreEqual(0.1f, faceLandmarks[0].x, 1e-6f);
            Assert.AreEqual(0.75f, blendshapes[0], 1e-6f);
            face.ResetToEmpty();
            Assert.AreEqual(0, face.CopyFaceLandmarksTo(0, faceLandmarks));
            Assert.AreEqual(0, face.CopyFaceBlendshapesTo(0, blendshapes));
            AssertZero(faceLandmarks);
            AssertZero(blendshapes);

            var pose = new PoseTrackingSnapshot();
            PoseResult(12, out var poseResult);
            pose.UpdateFrom(ref poseResult);
            var poseLandmarks = new MpudNormalizedLandmark[PoseTrackingSnapshot.LandmarkCapacity];
            Assert.AreEqual(1, pose.CopyPoseLandmarksTo(0, poseLandmarks));
            Assert.AreEqual(1, pose.CopyPoseWorldLandmarksTo(0, poseLandmarks));
            Assert.AreEqual(0.4f, poseLandmarks[0].x, 1e-6f);
            pose.ResetToEmpty();
            Assert.AreEqual(0, pose.CopyPoseLandmarksTo(0, poseLandmarks));
            AssertZero(poseLandmarks);

            var holistic = new HolisticTrackingSnapshot();
            HolisticResult(13, out var holisticResult);
            holistic.UpdateFrom(ref holisticResult);
            var holisticFace = new MpudNormalizedLandmark[HolisticTrackingSnapshot.FaceLandmarkCapacity];
            var holisticPose = new MpudNormalizedLandmark[HolisticTrackingSnapshot.PoseLandmarkCapacity];
            var holisticHand = new MpudNormalizedLandmark[HolisticTrackingSnapshot.HandLandmarkCapacity];
            Assert.AreEqual(1, holistic.CopyFaceTo(holisticFace));
            Assert.AreEqual(1, holistic.CopyPoseWorldTo(holisticPose));
            Assert.AreEqual(1, holistic.CopyRightHandWorldTo(holisticHand));
            Assert.AreEqual(0.3f, holisticFace[0].x, 1e-6f);
            holistic.ResetToEmpty();
            Assert.AreEqual(0, holistic.CopyFaceTo(holisticFace));
            Assert.AreEqual(0, holistic.CopyPoseWorldTo(holisticPose));
            Assert.AreEqual(0, holistic.CopyRightHandWorldTo(holisticHand));
            AssertZero(holisticFace);
            AssertZero(holisticPose);
            AssertZero(holisticHand);
        }

        private static TrackingResultStatus Take(HandTrackingService service)
        {
            var status = TrackingResultStatus.Waiting;
            Assert.IsTrue(SpinWait.SpinUntil(() =>
            {
                status = service.Poll();
                return status != TrackingResultStatus.Waiting;
            }, WaitTimeoutMs), "tracker completion was not delivered");
            return status;
        }

        private static void AssertZero(MpudNormalizedLandmark[] values)
        {
            foreach (var value in values)
            {
                Assert.AreEqual(0f, value.x, 1e-6f);
                Assert.AreEqual(0f, value.y, 1e-6f);
                Assert.AreEqual(0f, value.z, 1e-6f);
                Assert.AreEqual(0f, value.visibility, 1e-6f);
                Assert.AreEqual(0f, value.presence, 1e-6f);
            }
        }

        private static void AssertZero(float[] values)
        {
            foreach (var value in values)
            {
                Assert.AreEqual(0f, value, 1e-6f);
            }
        }

        private static MpudHandResult HandResult(long timestampUs, int handCount, int landmarkCount, float x)
        {
            var result = default(MpudHandResult);
            Fill(ref result, pointer =>
            {
                WriteInt32(pointer, typeof(MpudHandResult), "handCount", handCount);
                WriteInt64(pointer, typeof(MpudHandResult), "timestampUs", timestampUs);
                var data = Offset(typeof(MpudHandResult), "handData");
                Marshal.WriteInt32(pointer, data, landmarkCount);
                Marshal.WriteInt32(pointer, data + 4, 1);
                WriteFloat(pointer, data + 8, 0.9f);
                if (landmarkCount > 0)
                {
                    WriteLandmark(pointer, data + 12, x);
                    WriteLandmark(pointer, data + 12 + 21 * 5 * sizeof(float), x + 1f);
                }
            });
            return result;
        }

        private static void FaceResult(long timestampUs, out MpudFaceResult result)
        {
            result = default;
            Fill(ref result, pointer =>
            {
                WriteInt32(pointer, typeof(MpudFaceResult), "faceCount", 1);
                WriteInt64(pointer, typeof(MpudFaceResult), "timestampUs", timestampUs);
                var data = Offset(typeof(MpudFaceResult), "faceData");
                Marshal.WriteInt32(pointer, data, 1);
                WriteLandmark(pointer, data + 4, 0.1f);
                var blendshapeCount = data + 4 + 478 * 5 * sizeof(float);
                Marshal.WriteInt32(pointer, blendshapeCount, 1);
                WriteFloat(pointer, blendshapeCount + 4, 0.75f);
            });
        }

        private static void PoseResult(long timestampUs, out MpudPoseResult result)
        {
            result = default;
            Fill(ref result, pointer =>
            {
                WriteInt32(pointer, typeof(MpudPoseResult), "poseCount", 1);
                WriteInt64(pointer, typeof(MpudPoseResult), "timestampUs", timestampUs);
                var data = Offset(typeof(MpudPoseResult), "poseData");
                Marshal.WriteInt32(pointer, data, 1);
                WriteLandmark(pointer, data + 4, 0.2f);
                WriteLandmark(pointer, data + 4 + 33 * 5 * sizeof(float), 0.4f);
            });
        }

        private static void HolisticResult(long timestampUs, out MpudHolisticResult result)
        {
            result = default;
            Fill(ref result, pointer =>
            {
                WriteInt32(pointer, typeof(MpudHolisticResult), "faceLandmarkCount", 1);
                WriteInt32(pointer, typeof(MpudHolisticResult), "poseLandmarkCount", 1);
                WriteInt32(pointer, typeof(MpudHolisticResult), "leftHandLandmarkCount", 1);
                WriteInt32(pointer, typeof(MpudHolisticResult), "rightHandLandmarkCount", 1);
                WriteInt64(pointer, typeof(MpudHolisticResult), "timestampUs", timestampUs);
                var data = Offset(typeof(MpudHolisticResult), "landmarkData");
                WriteLandmark(pointer, data, 0.3f);
                var pose = data + 478 * 5 * sizeof(float);
                WriteLandmark(pointer, pose, 0.4f);
                var left = pose + 33 * 5 * sizeof(float);
                WriteLandmark(pointer, left, 0.5f);
                var right = left + 21 * 5 * sizeof(float);
                WriteLandmark(pointer, right, 0.6f);
                var poseWorld = right + 21 * 5 * sizeof(float);
                WriteLandmark(pointer, poseWorld, 0.7f);
                var leftWorld = poseWorld + 33 * 5 * sizeof(float);
                WriteLandmark(pointer, leftWorld, 0.8f);
                var rightWorld = leftWorld + 21 * 5 * sizeof(float);
                WriteLandmark(pointer, rightWorld, 0.9f);
            });
        }

        private static unsafe void Fill<T>(ref T value, Action<IntPtr> write) where T : unmanaged
        {
            // 큰 native 결과를 값 인자로 복사하지 않고 원래 저장소를 채운다.
            fixed (T* pointer = &value)
            {
                write((IntPtr)pointer);
            }
        }

        private static int Offset(Type type, string field)
        {
            return Marshal.OffsetOf(type, field).ToInt32();
        }

        private static void WriteInt32(IntPtr pointer, Type type, string field, int value)
        {
            Marshal.WriteInt32(pointer, Offset(type, field), value);
        }

        private static void WriteInt64(IntPtr pointer, Type type, string field, long value)
        {
            Marshal.WriteInt64(pointer, Offset(type, field), value);
        }

        private static void WriteFloat(IntPtr pointer, int offset, float value)
        {
            Marshal.Copy(new[] { value }, 0, IntPtr.Add(pointer, offset), 1);
        }

        private static void WriteLandmark(IntPtr pointer, int offset, float x)
        {
            WriteFloat(pointer, offset, x);
            WriteFloat(pointer, offset + 4, x + 0.01f);
            WriteFloat(pointer, offset + 8, x + 0.02f);
            WriteFloat(pointer, offset + 12, 1f);
            WriteFloat(pointer, offset + 16, 1f);
        }

        private sealed class ScriptedBody<T> : ITrackerWorkerBody<T> where T : unmanaged
        {
            private readonly Queue<Plan> _plans = new();

            public void Add(T result, bool ok = true, string error = null)
            {
                _plans.Enqueue(new Plan { Result = result, Ok = ok, Error = error });
            }

            public void Create()
            {
            }

            public bool Invoke(in TrackerWorkItem item, out T completed, out string error)
            {
                var plan = _plans.Dequeue();
                completed = plan.Result;
                error = plan.Error;
                return plan.Ok;
            }

            public void ResetBody()
            {
            }

            public void Destroy()
            {
            }

            private struct Plan
            {
                public T Result;
                public bool Ok;
                public string Error;
            }
        }
    }
}
