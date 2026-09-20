# MediaPipeUnityDOTS

MediaPipe Hand·Face·Pose·Holistic 추론과 Unity DOTS 결과 처리를 제공하는 UPM 패키지입니다. 원시 결과 읽기, 렌더 독립 One Euro 필터, 소비자 구현 `ILandmarkFilter`를 지원합니다. 카메라·UI·데모 씬은 필수가 아닙니다.

## 설치

Unity **6000.6** 이상에서 Package Manager → **Install package from git URL**에 다음 형식의 URL을 입력합니다. `<revision>`은 실제 검증한 커밋 SHA 또는 발행된 태그로 바꿉니다.

```text
https://github.com/NaturaAurum/MediaPipeUnityDOTS.git?path=/MediaPipeUnityDOTS/Assets/MediaPipeUnityDots#<revision>
```

현재 패키지 버전은 `0.1.0`이며 공개 릴리스 태그는 아직 발행하지 않았습니다. 개발 브랜치를 릴리스로 간주하지 마세요. 별도 Mac 검증과 릴리스 게이트 상태는 [배포 계획의 검증 기록](Docs/LibraryDistributionPlan.md)을 확인하세요.

1. 패키지가 선언한 Entities·Burst·Collections·Mathematics·Inference Engine 의존성이 자동 설치됩니다.
2. **MediaPipe → Models → Download Hand/Face/Pose/Holistic Task Model**에서 사용할 모델만 준비합니다. 최초 준비에는 네트워크가 필요하고 이후 추론에는 필요하지 않습니다.
3. [패키지 README와 API 예제](MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/README.md)를 따라 서비스를 생성하고 결과를 읽습니다.
4. UI가 필요하면 Package Manager의 Samples에서 **Tracking Demo**, 렌더 없는 검증은 **Landmark API**를 선택 Import합니다. 샘플별 추가 설정은 패키지 README에 있습니다.

소비자에게 **Bazel·Homebrew·네이티브 빌드는 필요 없습니다.** macOS 플러그인과 비시스템 dylib 의존성을 패키지에 동봉합니다. 모델·검증 사진은 Git에 동봉하지 않습니다.

## 지원 및 검증 범위

| 항목 | 범위 |
| --- | --- |
| Unity 기준 | `6000.6.0f1`에서 검증, package.json 최소 `6000.6` |
| 네이티브 바이너리 | macOS **26.0 이상**, Apple Silicon `arm64`, CPU 추론 |
| Editor 실행 증거 | macOS `26.5.1`, Apple M4 Pro, Hand·Face·Pose·Holistic 실제 검출 |
| Player 실행 증거 | 같은 호스트의 macOS Standalone **Mono**, 네트워크·Homebrew 접근 차단 상태의 4종 실제 검출 |
| 별도 Mac | 아직 검증하지 않음. 같은 호스트의 sandbox 실행으로 대체해 완료 처리하지 않음 |
| IL2CPP·Intel Mac·다른 OS | 지원 검증 범위 밖 |
| Depth | 선택적 Inference Engine 모델 경로. 네이티브 4종과 별도이며 위 Player 검증에 포함하지 않음 |

macOS 최소 버전은 브리지 하나가 아니라 **동봉한 모든 dylib의 Mach-O 최소 OS 중 최댓값**입니다. 현재 번들은 12개 dylib, 45,401,664바이트입니다.

## 외부 소비자 검증

실제 Git 리비전으로 빈 프로젝트를 생성하고 설치 → 모델 준비 → 4종 추론 → 원시/필터 결과 → 초기화·미검출·해제 → Player 실행을 검사합니다. Unity 라이선스가 활성화된 macOS 호스트에서 실행합니다.

```bash
python3 Tools/ConsumerSmoke/consumer_smoke.py \
  --unity /Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity \
  --git-url 'https://github.com/NaturaAurum/MediaPipeUnityDOTS.git?path=/MediaPipeUnityDOTS/Assets/MediaPipeUnityDots#<revision>' \
  --workdir /tmp/mpud-consumer
```

`--workdir`는 존재하지 않는 경로여야 합니다. 로그·프로젝트·Player를 보존하며 기존 디렉터리를 삭제하지 않습니다. Player 단계는 macOS sandbox에서 네트워크 및 `/opt/homebrew`, `/usr/local/Cellar`, `/usr/local/opt` 읽기를 차단합니다. 이 검사는 **다른 기기에서의 실행 증거와 구분**합니다.

## 개발

```bash
git submodule update --init
python3 Tools/ImportSamples.py
Native/Build/DownloadModels.sh
Native/Build/BuildMacosEditor.sh
Native/Build/CopyArtifactsToUnity.sh
```

`Tools/ImportSamples.py --replace`는 이 패키지의 생성된 샘플 복사본만 갱신합니다. 샘플 원본은 `Samples~`에서 수정합니다. 네이티브 재빌드 도구와 출처·서명 검사는 [Native/README.md](Native/README.md)를 참고하세요.

```text
Native/Bridge + Build + Patches → 패키지 내 macOS dylib 번들
Runtime/Tracking → 원시 서비스 결과 / ECS 원시 버퍼
                 → 기본 또는 사용자 필터 → 별도 필터 결과
                                            → 선택적 좌표 변환·렌더링
Samples~ → 선택 Import하는 UI·씬·검증 예제
```

## 라이선스와 문서

프로젝트 자체 코드는 [MIT](LICENSE)입니다. MediaPipe, 네이티브 의존성, Unity 패키지, 모델·사진에는 각각의 조건이 적용됩니다. **전체 바이너리 번들이 MIT라는 뜻이 아닙니다.** [Third Party Notices](MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Third%20Party%20Notices.md)와 동봉된 라이선스·소스 레시피를 확인하세요.

- [폴더 구조](Docs/FolderStructure.md)
- [공개 API·모델·샘플 사용법](MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/README.md)
- [변경 내역](MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/CHANGELOG.md)
- [배포 계획과 검증 기록](Docs/LibraryDistributionPlan.md)
- [월드 표시 좌표](Docs/transform-to-unity-world.md)
- [필터 설명](Docs/landmark-noise-filtering.md)
- [브랜치·PR 규칙](BRANCH_RULE.md)
