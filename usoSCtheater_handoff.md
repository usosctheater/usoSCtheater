# 가짜샤니마스극장 (usoSCtheater) — 프로젝트 핸드오프 문서

> 이 문서는 이전 계정에서 진행하던 프로젝트를 이어가기 위한 컨텍스트입니다.
> 작업 규칙은 Project instructions에 별도로 있으며, 이 문서는 현황·학습·미결 사항을 담습니다.
> 작성 시점 이후 변경된 내용이 있을 수 있으니, 코드와 다르면 **코드를 기준**으로 삼고 알려주세요.

---

## 0. 인수인계 기준점

- 원격 저장소 : `[https://github.com/usosctheater/usoSCtheater]`
- 브랜치: `[origin/master]`
- 최신 커밋 해시: `[ae83a44e0558265fde1b92f8ea58fde36f24011b [ae83a44]]`
- 미커밋 변경분: `[없음]`
- 진행 중이던 작업 중 중단된 지점: `[없음]`

---

## 1. 프로젝트 개요

- 개발자: 극장장 (단독 개발)
- Unity 기반 비주얼 노벨 엔진 **usoSCtheater**
- Unity 버전: 6000.4.5f1
- 프로젝트 폴더: `D:\usosctheater\usoSCtheater`
- XML 기반 커스텀 대사/씬 스크립트 시스템
- Spine 2D 스켈레탈 캐릭터 애니메이션 (립싱크, 팔 relay 포함)
- 씬 구조: 메인 시나리오 → 엔딩 씬
- 에디터 도구: 녹화 및 스크린샷 캡처
- 개발 상태: 릴리스를 향해 진행 중. 일정이 촉박하면 **기능 완성도보다 안정성을 우선**함

---

## 2. 구현된 시스템

### 런타임 스크립트 (`Assets\Scripts\`)

| 파일 | 역할 |
|---|---|
| `DialogManager.cs` | 스크립트 재생 엔진의 중심. 노드 타입: TEXT, CG, SETCG, IMAGE, AUDIO, SE, TRANSITION, BG, BREAK, CGGROUP |
| `CGManager.cs` | Spine 애니메이션 제어. EndLoop, 립싱크 코루틴, 팔 relay, 아이돌별 `DefaultLipData.xml` 조회(`ExtractIdolKey()`로 복합 키의 접두어 추출), 런타임 인스펙터 디버그 뷰 |
| `AudioManager.cs` | 슬롯 기반 오디오. BGM 슬롯 + 루프 SE 슬롯 1~3. `PlayAudio` / `StopAudio` / `StopAllAudio` |
| `UIManager.cs` | speakerType에 따른 대사창 타입 전환, `EnableAutoPlay()`, `mainTitle`/`subTitle` 씬 타이틀 |
| `BGManager.cs`, `ImageManager.cs`, `SceneManager.cs` | 배경 / 이미지 / 씬 관리 |
| `SpineDebugManager.cs` | 디버그 씬. 우선순위 접두어 애니메이션 드롭다운(Track 0: 접두어 없음, Track 1: `face_`, Track 2: `lip_`, Track 3: `arm_`/`eye_`), 이벤트 기반 `UpdateBoneList()`, 전체 리셋 기능 |
| `ResolutionAspectController` | 레터박싱으로 1136:640 비율 유지 |

### 엔딩 씬 (`Assets\Scripts\Ending\` 네임스페이스)

- 크레딧/엔딩 씬 전체 구현: 스크롤러, SD Spine 컨트롤러, BGM 플레이어
- `EndingSceneController.cs`가 전체를 조율

### 에디터 도구 (`Assets\Editor\`)

| 파일 | 역할 |
|---|---|
| `VNRecorderTool.cs` | Unity Recorder 연동(에디터 전용). Tools 메뉴 토글. 녹화 시작 시 자동 재생 활성화 |
| `VNCaptureTool.cs` + `SceneCaptureUtil.cs` | 별도의 스크린샷 캡처 파이프라인 (AsyncGPUReadback, 1920×1080, `AutoPlayCoroutine`의 TEXT 라인별 캡처 훅) |
| `BGTexturePostProcessor.cs` | `Assets/Resources/BG/`와 `Assets/Resources/Image/` 모두 처리 |
| `SpineAutoImporter.cs` | Spine 자동 임포트 |

### 구조 변경 이력

- `ScriptNode`는 정적 팩토리 메서드(`CreateLine`, `CreateAudio`, `CreateSE` 등)로 리팩터링되었고, `DialogManager.cs`의 모든 호출부가 마이그레이션 완료됨
- 씬 로딩 버그는 인스펙터에 직렬화된 `scenePath` 값이 오래된 값으로 남아 있던 것이 원인이었고, 해결됨

### 주요 데이터/스크립트 경로

- XML 씬 스크립트: `Assets/Resources/Scene/main/`
- `DefaultLipData.xml` 등 데이터: `Assets/Resources/Data/` (`Resources.Load<TextAsset>("Data/파일명")`, 확장자 없이 로드)

---

## 3. 학습 사항 (같은 시행착오를 반복하지 않기 위해)

- **Spine 언키(unkeyed) 본 유지**: Spine은 애니메이션 전환 시 키가 없는 본을 리셋하지 않음. `PlayAnimation()` 전에 `ClearTracks()` + `SetToSetupPose()`를 호출하는 처리가 이미 구현되어 있고, 라인 단위 구조에서 올바르게 동작함
- **`WaitForSecondsRealtime` vs `WaitForSeconds`**: Realtime은 Unity Recorder의 Cap-off 가속에 영향을 받지 않음. `#if UNITY_EDITOR`로 분기하여 에디터에서는 `WaitForSeconds`(가속 가능), 릴리스 빌드에서는 `WaitForSecondsRealtime`(백로그 일시정지와 호환)을 사용
- **`loop_start`, `relay` 이벤트**: Spine JSON 애니메이션 타임라인 안에 들어 있으며 외부 설정이 필요 없음
- **인스펙터 직렬화 값이 코드 기본값을 덮어씀**: 원인 불명의 동작은 에디터에서 직렬화된 필드 값부터 확인할 것
- **`DefaultLipData.xml` 키**: 런타임 `cgKey`는 `mei_idol_3` 같은 복합 키이므로, `ExtractIdolKey()`가 첫 밑줄 앞 접두어를 추출해서 딕셔너리를 조회해야 함
- **`edit_block` 신뢰성**: 공백/들여쓰기가 정확히 일치해야 함. 반복 구조가 많은 파일은 `old_string`에 주변 컨텍스트를 충분히 포함할 것. 매칭이 실패하면 `write_file` 전체 쓰기로 대체

---

## 4. 진행 중 / 미결 사항

> 아래 항목은 마지막으로 정리된 시점 기준입니다. **[확인 필요]** 는 이후 진행 여부를 직접 확인해야 합니다.

1. **VNCaptureTool 터보 모드** — `Time.captureFramerate` 사용, `WaitForEndOfFrame`을 `CaptureEnabled` 체크 뒤로 게이팅하는 수정이 진행 중이었음 `[확인 필요]`
2. **`ExtractIdolKey()` 수정** — `DefaultLipData.xml` 복합 키 조회용. 밑줄 접두어 규칙이 모든 키에 보편적으로 성립하는지 확인이 남아 있었음 `[확인 필요]`
3. **`DefaultLipData.xml` 중복 `<Asahi>` 요소** 수정 필요 `[확인 필요]`
4. **녹화 결과물 파일명** — 타임스탬프 대신 시나리오별 파일명 사용 (향후 작업)
5. **눈 깜빡임 트랜지션 연출** — 이중 패널 방식을 프로토타입한 뒤 **단일 타원형 마스크 방식으로 전환**했고, 구현은 미완료 `[구현 취소, 추후 결정]`
6. **CGGROUP `lastCgKey`/`lastAnimation` 추적 수정** — CGGROUP 표시 이후 다음 라인에서 립싱크와 EndLoop가 동작하도록 하는 수정 `[확인 필요]`

---

## 5. 도구 및 환경

- 모든 파일 작업은 **desktop-commander MCP**로 수행
- 파일명 검색: `start_search`, `searchType: "files"`
- 코드베이스 문자열 검색: `searchType: "content"`, `filePattern: "*.cs"`, `literalSearch: True`
- 대용량 파일 일부 읽기: `read_file`의 `offset`/`length`
- 수정: `edit_block`(정밀 수정), 매칭 실패 시 `write_file`
- 폴더 구조 확인: `list_directory`, `depth: 3`
- 대용량 Spine JSON 분석: `start_process`로 Node.js 원라이너(`node -e "..."`). **Python 사용 불가**(WindowsApps 스텁)
- Unity 패키지 API 검색: `Library/PackageCache/[package]@[hash]/Editor/*.api` 파일이 소스 검색보다 빠름
- 버전 관리: Git + GitHub 원격. Sourcetree로 push/pull, TortoiseGit으로 파일 단위 히스토리 확인

---

## 6. 이어서 작업할 때의 진행 방식 (요약)

- 코드를 제안하기 전에 관련 프로젝트 파일을 먼저 읽어 기존 네이밍/패턴에 맞출 것
- 큰 기능은 타당성과 설계 결정을 먼저 확정한 뒤에 코드를 작성할 것
- 원인을 충분히 파악한 뒤에 수정할 것
- 변경은 논리 단위로 작게 나눠서 diff가 읽기 쉽게 유지할 것
