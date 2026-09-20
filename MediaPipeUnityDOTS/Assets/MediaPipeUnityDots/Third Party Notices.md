# Third Party Notices

이 프로젝트가 작성한 C#/C++/도구 코드는 [MIT](LICENSE.md)입니다. **MIT는 아래 라이브러리·Unity 패키지·모델·사진의 라이선스를 대체하지 않습니다.**

## 동봉 네이티브 코드

전체 고지 원문은 [THIRD_PARTY_LICENSES.txt](Runtime/Plugins/macOS/THIRD_PARTY_LICENSES.txt), 구성 요소별 원문과 설치된 소스/빌드 레시피는 [ThirdPartyLicenses](Runtime/Plugins/macOS/ThirdPartyLicenses)에 있습니다. [native-dependency-manifest.txt](Runtime/Plugins/macOS/native-dependency-manifest.txt)는 dylib별 SHA-256, 크기, 최소 OS, MediaPipe 커밋, Bazel 버전 및 브리지/빌드 스크립트/패치 해시를 기록합니다.

| 구성 | 이번 번들의 출처 | 조건을 확인할 원문 |
| --- | --- | --- |
| MediaPipe | `google-ai-edge/mediapipe`, `6d31f1ebc3284db74d211d62bdc4f0a0c29ea120` (`v1.0.0`) | Apache-2.0, 동봉 MediaPipe LICENSE/NOTICE |
| OpenCV core/imgproc/calib3d/features2d/flann | Homebrew `opencv@4` `4.14.0_6` | Apache-2.0 및 내부 third-party 고지, `opencv-4.txt` |
| oneTBB | Homebrew `tbb` `2023.1.0` | Apache-2.0 및 LLVM exception 등 해당 원문, `tbb.txt` |
| OpenBLAS | Homebrew `openblas` `0.3.34` | BSD 계열 원문, `openblas.txt` |
| LLVM OpenMP | Homebrew `libomp` `23.1.0` | Apache-2.0 with LLVM exception 등 해당 원문, `libomp.txt` |
| GCC runtime: libgcc_s, libgfortran, libquadmath | Homebrew `gcc` `16.2.0` | **GNU GPL/LGPL 및 해당 런타임 예외**, `gcc.txt`. 모든 라이브러리에 동일한 예외를 일괄 적용하지 않음 |
| Homebrew 소스 레시피 | 실제 설치 prefix의 `.brew/*.rb` | BSD-2-Clause, `homebrew-recipes.txt` |

브리지에 정적으로 들어간 C/C++ 의존성도 별도 대상입니다. 패키징 도구는 해당 Bazel `CcInfo` 의존 그래프에서 MediaPipe, TensorFlow/Lite·TSL·XLA, XNNPACK, KleidiAI, FP16, FXdiv, Abseil, Protobuf, FlatBuffers, Eigen, gflags, glog, cpuinfo, farmhash, fft2d, gemmlowp, pthreadpool, ruy, zlib의 라이선스/NOTICE를 수집합니다. `static-*.txt`에 원문이 있습니다. 저장소 단위 고지에는 이번 바이너리에 링크하지 않은 하위 구성의 고지가 함께 들어갈 수 있으며, 그 사실만으로 해당 하위 코드의 사용을 뜻하지 않습니다.

저작권/라이선스/NOTICE를 바이너리와 함께 유지하세요. Apache/BSD/MIT, MPL, GNU 계열 조건은 서로 다릅니다. 이 번들을 하나의 MIT 전용 바이너리로 재표기하지 마세요. 시스템 프레임워크는 macOS가 제공하며 패키지에 복사하지 않습니다.

### GNU 런타임의 대응 소스와 재배포

이 번들의 GNU 런타임 대응 소스는 다음에서 얻을 수 있습니다.

- GCC `16.2.0` 원본: [gcc-16.2.0.tar.xz](https://ftp.gnu.org/gnu/gcc/gcc-16.2.0/gcc-16.2.0.tar.xz)
- 원본 SHA-256: `e6738e29597f733270731aa90600f37ffdc045079dfc27ec7e8192cc81085c3e`
- Homebrew Darwin/Apple Silicon 패치: [gcc-16.2.0.diff, 고정된 Homebrew 리비전](https://raw.githubusercontent.com/Homebrew/homebrew-core/0b8b230246061244152f4140f6838beda0c0bc57/Patches/gcc/gcc-16.2.0.diff)
- 실제 설치된 GCC 소스/설정/빌드 레시피와 그 SHA-256: [`gcc.txt`](Runtime/Plugins/macOS/ThirdPartyLicenses/gcc.txt)의 `Installed source/build recipe` 절
- 번들 가공 절차: 저장소의 `Native/Build/PackageMacosNative.py`. arm64 슬라이스, dylib 로드 경로 및 ad-hoc 서명을 처리하며 GNU 라이브러리의 기능 코드를 수정하지 않습니다.

GNU 라이브러리 자체의 배포 조건은 GCC Runtime Library Exception의 응용프로그램 링크 허용과 별도로 확인해야 합니다. 특히 LGPL 대상에 대해 고지·해당 소스 접근·사용자 라이브러리 교체/수정 관련 조건을 유지해야 합니다. 앱 배포자가 추가하는 계약이나 서명 정책으로 해당 권리를 제한하지 마세요. 바이너리를 재배포하는 주체는 적용 라이선스가 요구하는 동안 대응 소스와 패치의 접근성을 유지해야 합니다. 업그레이드한 라이브러리에 이 버전의 소스 링크를 그대로 사용하면 안 됩니다.

## Unity 의존성

Entities, Entities Graphics, Burst, Collections, Mathematics, Inference Engine 및 선택적 URP는 Unity Package Manager가 별도로 설치합니다. 각 설치 패키지의 `LICENSE.md`, `Third Party Notices.md` 및 Unity 이용 조건을 따릅니다. 이 저장소가 해당 패키지의 권리를 재허가하지 않습니다.

## 모델: Git에 포함하지 않음

[ModelManifest.txt](EditorTool/ModelManifest.txt)에 URL·리비전·SHA-256을 고정하고 소비자의 명시적 요청으로 다운로드합니다.

| 모델 | 출처 및 조건 확인 위치 |
| --- | --- |
| Hand task, float16 revision 1 | [MediaPipe Hand Landmarker 모델 문서](https://ai.google.dev/edge/mediapipe/solutions/vision/hand_landmarker#models) |
| Face task, float16 revision 1 | [MediaPipe Face Landmarker 모델 문서](https://ai.google.dev/edge/mediapipe/solutions/vision/face_landmarker#models) |
| Pose full task, float16 revision 1 | [MediaPipe Pose Landmarker 모델 문서](https://ai.google.dev/edge/mediapipe/solutions/vision/pose_landmarker#models) |
| Holistic task, float16 revision 1 | [MediaPipe Holistic Landmarker 모델 문서](https://ai.google.dev/edge/mediapipe/solutions/vision/holistic_landmarker#models) |
| Depth Anything V2 Small ONNX | [onnx-community 모델 카드, 고정 리비전](https://huggingface.co/onnx-community/depth-anything-v2-small/tree/4472b7362082ad9968fee890ca0f1e5aca36b93d), Apache-2.0 표기. Small 이외 모델의 조건까지 동일하다고 가정하지 않음 |

MediaPipe **소스 코드**의 Apache-2.0 또는 문서 페이지 하단의 코드 예제 라이선스만으로 모든 모델 가중치의 조건을 추정하지 않습니다. 본 저장소는 Task/ONNX 가중치에 MIT를 적용하거나 재배포 권리를 부여하지 않습니다. 소비자가 모델을 Player에 포함하여 외부에 배포할 때는 해당 모델 카드·출처의 조건을 확인하고 필요한 고지를 함께 제공해야 합니다. 모델 다운로드 동작과 모델 사용/재배포 허가는 같은 개념이 아닙니다.

## 실제 입력 검증 사진

`male_full_height_hands.jpg`, `portrait.jpg`는 MediaPipe 공개 테스트 자산에서 검증 시 다운로드합니다. 재배포 권리를 추정하여 Git이나 패키지 원본에 사진을 넣지 않습니다. URL과 SHA-256은 저장소의 `Tools/ConsumerSmoke/consumer_smoke.py`, 사용 설명은 샘플의 `Fixtures/NOTICE.txt`에 있습니다. 생성된 검증 프로젝트/Player에는 로컬 테스트를 위한 사진이 포함되므로, 그 산출물을 공개 배포하기 전에 사진 자체의 권리를 별도로 확인하세요.
