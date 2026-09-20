# Changelog

## 0.1.0 — Unreleased

첫 UPM 배포 후보입니다. 아직 공개 릴리스 태그를 발행하지 않았습니다.

### 추가

- Unity 6000.6 기준 코어 의존성 선언. Inference Engine 포함.
- macOS arm64 네이티브 브리지와 비시스템 전이 의존 dylib 동봉, 명시적 importer, 로드 경로·서명·해시·최소 OS 검사.
- 정적/동적 네이티브 의존성 라이선스, 설치된 소스 레시피와 빌드 입력 provenance.
- SHA-256/리비전이 고정된 선택형 모델 준비, 원자적 교체, Depth ModelAsset 연결과 활성 Provider 빌드 검사.
- Hand/Face/Pose/Holistic의 caller-owned raw/world 복사, 상태 및 캡처 메타데이터.
- Hand/Pose landmark 이름·기본 연결선, Face blendshape 이름 조회.
- 렌더 독립 기본 One Euro/ECS 필터 버퍼, `ILandmarkFilter`와 명시적 소유권을 가진 managed coordinator.
- 빈 Git 소비자의 두 선택 샘플 컴파일, 실제 4종 검출 및 격리된 macOS Mono Player 검증 도구.

### 변경

- 데모 씬·UI·테마·프로필·설정을 선택형 `Samples~/TrackingDemo`로 이동. 원본 SampleScene 의존 없는 `Landmark API` 샘플 추가.
- 코어와 샘플 테스트 어셈블리 분리. 실제 패널 연결 및 submit 이벤트로 UI 동작 검증.
- 렌더러가 직접 반복 평활화하던 경로를 제거하고 별도 필터 결과를 소비하도록 변경.
- 프레임 ordinal/handedness를 영속 ID로 간주하지 않음. 대상 연속성 토큰을 증명하지 못하면 기본 필터 이력을 재사용하지 않음.
- 기존 `TryTakeCompleted` 사용처를 `Poll()`과 `TrackingResultStatus`로 전환.

### 수정

- 결과 대기와 새 미검출을 구분하고 오류·stale·reset·dispose 후 이전 데이터를 새 결과로 발행하지 않도록 정리.
- 사용자 필터 reset 예외 및 교체 실패 후 이전 캐시가 정상 출력으로 재사용되던 경우를 차단.
- 큰 interop 구조체를 값으로 왕복하던 회귀 검증의 Mono 크래시 경로를 ref 기반으로 변경.
- Depth 모델 재준비 시 기존 소비자 `.meta`와 importer 설정을 보존.
- 제출 스탬프 보관 구조의 장시간 누적과 사용자 필터 해제 실패 후 캐시 재노출을 차단.
- 네이티브 산출물에 빌드 입력·upstream 커밋·바이너리 해시를 결합하고 오래된 산출물 패키징을 거부.
- 샘플 fixture 반복 복사, UI 재바인딩 구독, Pose/Holistic Provider reset 표면과 Holistic 미제공 score를 정리.

### 배포 조건

- 자체 코드 MIT. 전체 native 번들·Unity 의존성·모델·사진은 별도 조건 적용.
- 현재 native 최소 OS는 동봉 의존성 기준 **macOS 26.0**, CPU/arm64. macOS 26.5.1/M4 Pro에서 Unity 6000.6.2f1 Editor와 Unity 6000.6.0f1 Mono Player 실행을 각각 검증했습니다.
- 모델과 검증 사진은 Git에 동봉하지 않습니다.
- 별도 Mac의 Homebrew/Bazel 미설치 검증 및 릴리스 승인 전에는 정식 태그를 생성하지 않습니다. 같은 호스트의 sandbox 실행과 별도 기기 검증을 구분합니다.

## 버전 정책

릴리스마다 package.json 버전, 이 변경 내역, 실제 태그 `v<package-version>`을 일치시킵니다. 소비자는 검증된 태그/커밋에 고정합니다. 공개 API 또는 설치·플랫폼 계약의 비호환 변경은 0.x에서도 최소 minor 버전을 올리고 이력을 남깁니다. 모든 변경은 저장소 BRANCH_RULE.md의 PR 절차를 따릅니다.
