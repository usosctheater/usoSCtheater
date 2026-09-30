# 가짜샤니마스극장 (usoSCtheater) 회의록

> 작업할 때마다 Claude가 자동 갱신. 최신 항목이 위.
> 항목 형식: **내용** / **변경 위치** (파일 · 함수) / **원인** / **고려 사항**
> 목록 형식: 항목마다 `<details>`로 접고, `<summary>`에 `날짜 · [분류] 한 줄 요약 — 상태` 기재 (펼치면 상세)
> 원본 위치: `D:\usosctheater\usoSCtheater_회의록.md` + claude.ai 프로젝트 `claude/usoSCtheater_회의록.md` (동일 내용)
> 보기용 페이지(접기/펼치기·검색): https://claude.ai/artifact/G24K2D8xS7C9E6EQuP319r — md 갱신 후 `notes.md`로 재게시 (절차는 핸드오프 문서 '회의록 갱신 절차')

---

<details>
<summary><b>2026-09-30</b> · [용어 정리] Scene/Scenario/Act 용어 확정 + 1단계 식별자 이름 변경(SceneManager → ScenarioPlayer 등) — <i>테스트 전</i></summary>

### 2026-09-30 — [용어 정리] 용어 규칙 확정 및 1단계 이름 변경 (동작 변화 없음)

- 커밋: 미커밋 (0단계 기준점 커밋 이후 작업, Sourcetree로 커밋 예정)
- 변경 파일: `ScenarioPlayer.cs`(구 `SceneManager.cs`, .meta 함께 이름 변경), `DialogManager.cs`, `UIManager.cs`, `EffectManager.cs`, `AudioManager.cs`(주석), `Capture/ScreenCaptureUtil.cs`(구 `SceneCaptureUtil.cs`, .meta 함께), `Scenario/ScenarioCatalog.cs`, `Scenario/ScenarioSelection.cs`, 신규 `Scenario/ActXml.cs`, `Editor/ScenarioCatalogSync.cs`, `Editor/VNCaptureTool.cs`, `Editor/VNRecorderTool.cs`
- 사용자 직접 변경(Unity): `main.unity` → `CommunicationScene.unity`, `ScenarioSelectScene.unity` 생성, Build Settings
- 상태: **Unity 컴파일 및 인스펙터 연결/플레이 테스트 전**

### 용어 규칙 (확정)
| 용어 | 뜻 | 예 | 코드 |
|---|---|---|---|
| Scene (씬) | Unity 씬 전용 | `CommunicationScene` | `SceneTransitionManager`(예정), `EndingSceneController` |
| Scenario (시나리오) | 이야기 한 편 전체(폴더) | `IL`, `NKS`, `SC` | `ScenarioPlayer`, `ScenarioCatalog`, `ScenarioSelection` |
| Act (막) | 스크립트 XML 1개, SubTitle 단위 | `IL01.xml` | `LoadAct`, `actNames`, `<Act>` |
| 장 | 막 안 TRANSITION 구간 | — | 주석에서 한글로만 사용 |
| Screen / Panel | 화면 / 화면을 가리는 오브젝트 | — | `ClearScreen`, `ScreenCaptureUtil`, `wipeTransitionPanel` |

### 결정 사항
| # | 결정 | 이유 |
|---|---|---|
| 1 | Unity 씬 이름: `ScenarioSelectScene` / `CommunicationScene`(구 main) / `EndingScene` / `SpineDebugScene`. Build Settings 0 목록 · 1 커뮤 · 2 엔딩 | 역할별 씬이 늘어나므로 이름 규칙 필요. 커뮤니케이션 = 게임 내 스토리 기능 명칭 |
| 2 | 씬 전환은 static `SceneTransitionManager` 한 곳에서만 (`SceneId` enum + 이름 매핑, `GoToXxx`) — 3단계에서 구현 | 특정 씬에 종속되지 않고 어느 씬에서 Play해도 동작, 씬 추가 시 매니저만 수정 |
| 3 | `SceneManager` → `ScenarioPlayer` | Unity SceneManager/씬 전환 매니저와 혼동 방지 |
| 4 | 데이터 패치(외부 폴더) 방식은 도입하지 않고 현재 Resources + 재빌드 배포 유지 | 신규 시나리오마다 새 Spine이 들어가며 Spine은 CommunicationScene에 배치된 오브젝트라 재빌드 필수. 보이스 비동기 로드/캐싱 구조 변경 부담 |
| 5 | 리팩터링은 단계별 커밋: 1 이름 변경 → 2 데이터 폴더(`Resources/Scene` → `Resources/Scenario`) → 3 기능 | 동작 변화 없는 변경과 기능 변경 분리, 롤백 용이 |

### 구현 / 수정 내역
- **클래스**: `SceneManager` → `ScenarioPlayer`, `SceneCaptureUtil` → `ScreenCaptureUtil` (.cs/.meta 동시 이름 변경 → GUID 유지)
- **ScenarioPlayer**: `sceneFiles/currentSceneIndex` → `actFiles/currentActIndex`, `LoadSceneFiles/OnSceneEnd/PlayNextScene/DebugNext·PreviousScene/ApplyStartScene/ResolveScenePath` → `LoadActFiles/OnActEnd/PlayCurrentAct/DebugNext·PreviousAct/ApplyStartAct/ResolveScenario`, `scenePath` → `defaultScenarioFolder`(값은 마지막 경로 조각만 사용 → 기존 `Scene/NKS`도 `NKS`로 동작), 로그 태그 `[ScenarioPlayer]`, 로그 "씬" → "막"
- **DialogManager**: `sceneManager` → `scenarioPlayer`, `LoadScene` → `LoadAct`, `currentSceneName` → `currentActName`, `ClearScene` → `ClearScreen`, XML 루트 파싱 `ActXml.GetRoot`
- **UIManager**: `ShowSceneTitle` → `ShowActTitle`, `sceneTitleUI/MainText/SubText` → `actTitleUI/MainText/SubText`
- **EffectManager**: `wipeTransitionScene` → `wipeTransitionPanel`
- **ScenarioCatalog/Selection/Sync**: `sceneNames` → `actNames`, `SceneRoot` → `ScenarioRoot`, `StartSceneName` → `StartActName`, `GetScenePath` → `GetScenarioPath`, `SceneFolder` → `ScenarioRootFolder` (경로 값은 2단계까지 `Scene` 유지)
- **ActXml (신규)**: 루트 `<Act>` 우선, 기존 `<Scene>` 폴백. 막 XML 6개와 엑셀 템플릿(`Resources/XML`)이 전환되기 전까지 폴백 제거 금지. 루트가 없으면 에러 로그 + 빈 목록(기존 동작과 동일)
- **VNRecorderTool**: `ScenarioPlayer.defaultScenarioFolder`를 읽도록 수정(마지막 조각 사용)

### 고려 사항
- 직렬화 필드 7개에 `[FormerlySerializedAs]` 적용 → 인스펙터 연결/값 보존: `DialogManager.sceneManager`, `ScenarioPlayer.scenePath`, `UIManager.sceneTitleUI/MainText/SubText`, `EffectManager.wipeTransitionScene`, `ScenarioEntry.sceneNames`
- 검증: Assets 전체 스크립트에서 옛 식별자 코드 참조 0건 확인(주석·FormerlySerializedAs 문자열 제외). 남은 "Scene"은 Unity 씬 의미, 레거시 XML 태그, 2단계 폴더 경로뿐
- `ScenarioPlayer.OnActEnd`의 `LoadScene("EndingScene")`은 3단계에서 `SceneTransitionManager.GoToEnding()`으로 교체 예정

### 다음 단계
- 2단계: Unity에서 `Resources/Scene` → `Resources/Scenario` 이름 변경 → `ScenarioCatalog.ScenarioRoot`, `ScenarioCatalogSync.ScenarioRootFolder` 2곳 수정
- 3단계: `SceneTransitionManager` 추가 + `ScenarioSelectController`(목록 씬) 추가
- (선택, 나중) 막 XML/엑셀 템플릿 루트 태그 `<Scene>` → `<Act>`

### 테스트 필요
- [ ] Unity 컴파일 에러 없음
- [ ] 인스펙터 연결 유지: DialogManager `Scenario Player`, UIManager `Act Title UI/Main/Sub`, EffectManager `Wipe Transition Panel`, ScenarioPlayer `Default Scenario Folder`(값 `Scene/NKS` 그대로 보여도 정상)
- [ ] 카탈로그 `ScenarioCatalog.asset`에 `Act Names` 목록 유지
- [ ] CommunicationScene 직접 Play → NKS 재생, 막 타이틀 표시, TRANSITION(Wipe) 정상, 엔딩 전환
- [ ] PageUp/PageDown 막 이동, 캡처 툴 켜고 `CaptureOutput/{막 이름}` 생성
- [ ] 녹화 모드 폴더명 `Recordings/NKS`

</details>

<details>
<summary><b>2026-09-30</b> · [시나리오] 2단계: 시나리오 선택값(static) + 시나리오 카탈로그 자동 동기화 — <i>테스트 전</i></summary>

### 2026-09-30 — [시나리오] 2단계: ScenarioSelection / ScenarioCatalog / ScenarioCatalogSync

- 커밋: 미커밋 (Sourcetree로 커밋 예정)
- 변경 파일: 신규 `Assets/Scripts/Scenario/ScenarioCatalog.cs`, `Assets/Scripts/Scenario/ScenarioSelection.cs`, `Assets/Editor/ScenarioCatalogSync.cs` / 수정 `Assets/Scripts/SceneManager.cs`
- 상태: **Unity 컴파일 및 플레이 테스트 전**

### 결정 사항
| # | 결정 | 이유 |
|---|---|---|
| 1 | 카탈로그 = ScriptableObject `Resources/Data/ScenarioCatalog.asset`, 에디터에서 `Resources/Scene` 하위 폴더 자동 동기화 | 목록 파일의 확실성 + 수동 갱신 부담 제거 |
| 2 | `hidden` 항목 추가 | 테스트 시나리오(OT 등) 목록 숨김 |
| 3 | 카탈로그 git 변경 허용 | 시나리오 추가 시 다른 파일도 함께 변경됨 |
| 4 | 빌드(exe)는 빌드 시점 카탈로그 스냅샷을 읽기만 함 (런타임 갱신 없음) | Resources 폴더는 빌드 시 패키징되어 exe에서 시나리오 폴더를 추가할 수 없음 → 시나리오 추가 = 재빌드. 빌드 직전 동기화로 카탈로그·씬 파일 불일치 방지 |

### 구현 / 수정 내역

#### 1. ScenarioCatalog (신규, 런타임)
- `ScenarioEntry { folderName(자동), displayName(수동), hidden(수동), sceneNames(자동, Ordinal 정렬) }`
- `ScenarioCatalog.Load()` / `Find(folderName)` / `GetVisible()`(hidden 제외, 인스펙터 순서)
- 네임스페이스 `UsoSCTheater.Scenario`

#### 2. ScenarioSelection (신규, 런타임 static)
- `ScenarioFolder`, `StartSceneName`, `HasSelection`, `Select()`, `Clear()`, `GetScenePath()`("IL" → "Scene/IL")
- **고려 사항**: Domain Reload 비활성 프로젝트 → `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`로 Play 시작마다 초기화. 시작 씬은 인덱스가 아닌 파일명으로 전달(카탈로그가 오래돼도 안전).

#### 3. ScenarioCatalogSync (신규, 에디터)
- 갱신 시점: `Resources/Scene` 하위 에셋 추가/삭제/이동 시 자동(`AssetPostprocessor`, delayCall로 1회), 메뉴 `Tools > Scenario > Sync Catalog`, 카탈로그 없으면 에디터 로드 시 생성, 빌드 직전(`IPreprocessBuildWithReport`)
- 병합: folderName 기준, displayName/hidden/순서 유지, sceneNames만 갱신, 새 폴더는 끝에 추가, 삭제된 폴더 제거
- 시나리오 폴더 안에 하위 폴더가 있으면 경고(`Resources.LoadAll` 재귀 로드)
- **고려 사항**: 폴더명 변경 시 새 시나리오로 취급 → displayName/hidden 재설정 필요

#### 4. SceneManager 런타임 경로
- **변경 위치**: `activeScenePath` 필드, `ResolveScenePath()` / `ApplyStartScene()` 추가, `Start()` 순서 변경 + 씬 0개면 중단, `LoadSceneFiles()` / `GetScenarioFolderName()`이 `activeScenePath` 사용
- **동작**: 선택값 있으면 `Scene/{선택 폴더}`, 없으면 인스펙터 `scenePath`(main.unity 직접 Play 유지). 시작 씬 못 찾으면 경고 후 첫 씬.
- **고려 사항**: 인스펙터 `scenePath`는 덮어쓰지 않음 → VNRecorderTool(인스펙터 값 참조) 동작 유지, 4단계에서 대응. 중간 씬 시작 시 이전 씬 BGM/CG 상태 없음(PageUp 디버그와 동일).

### 테스트 필요
- [ ] Unity 컴파일 에러 없음
- [ ] `Resources/Data/ScenarioCatalog.asset` 자동 생성, IL[4] / NKS[1] / SC[1] 등록
- [ ] main.unity 직접 Play → "선택값 없음 → 인스펙터 scenePath 사용" 로그 + 기존과 동일 재생
- [ ] 씬 폴더에 XML 추가/삭제 시 카탈로그 자동 갱신, displayName/hidden 유지

</details>

<details>
<summary><b>2026-09-29</b> · [버그 수정] 보이스 폴더 이동 후 립싱크 전체 미동작 — 보이스 길이 조회를 AudioManager로 일원화 — <i>테스트 전</i></summary>

### 2026-09-29 — [버그 수정] 립싱크 미동작 (보이스 길이 0)

- 커밋: 미커밋 (Sourcetree로 커밋 예정)
- 변경 파일: `Assets/Scripts/AudioManager.cs`, `Assets/Scripts/DialogManager.cs`
- 상태: **Unity 플레이 테스트 전**

### 구현 / 수정 내역

#### 1. 보이스 길이 조회를 AudioManager로 일원화
- **변경 위치**: `AudioManager.GetVoiceDuration()` 추가(public), `DialogManager.GetVoiceDuration()` 본문을 `audioManager.GetVoiceDuration()` 위임으로 교체
- **원인**: 보이스가 시나리오별 하위 폴더(`Audio/Voice/{시나리오}/`)로 이동한 뒤, `AudioManager.PlayVoice`는 `LoadVoiceClip`(시나리오 폴더 → 루트 폴백)으로 찾지만 `DialogManager.GetVoiceDuration`은 루트(`Audio/Voice/{키}`)만 조회 → 길이 0 → `CGManager.SetCG` / `RestartLipSync`가 립 코루틴을 시작하지 않음 → **보이스는 재생되지만 립싱크 전체 미동작**. 같은 이유로 `AutoPlayCoroutine` 대기 시간이 타이핑 길이만 반영되어 자동재생/녹화에서 보이스가 끊길 수 있었음.
- **고려 사항**:
  - 보이스 경로 규칙을 `AudioManager.LoadVoiceClip` 한 곳에서만 관리 → 재생과 길이 계산이 다시 어긋나지 않도록 함.
  - 다중 키(공백/쉼표 구분)는 기존과 동일하게 가장 긴 길이 반환.
  - DialogManager의 기존 `audioManager` 참조 사용 → 인스펙터 추가 연결 불필요.
  - 원인 조사 중 CGGroup Lip 재사용 변경분을 git diff로 재점검 → 단일 CG 동작은 기존과 동일(원인 아님).

### 테스트 필요
- [ ] CG 지정 대사 / CG 생략 이어지는 대사 모두 입 움직임
- [ ] 자동재생 시 보이스가 끝난 뒤 다음 라인으로 진행 (녹화 포함)

</details>

<details>
<summary><b>2026-09-29</b> · [시나리오] 시나리오별 보이스 폴더 검색(루트 폴백) 구현, main → NKS 폴더/씬 파일명 변경 — <i>테스트 전</i></summary>

### 2026-09-29 — [시나리오] 1단계: 시나리오별 보이스 폴더 분리 + 폴더/파일명 정리

- 커밋: 미커밋 (Sourcetree로 커밋 예정)
- 변경 파일: `Assets/Scripts/AudioManager.cs`, `Assets/Scripts/SceneManager.cs`, `Assets/Editor/VNRecorderTool.cs`(주석)
- 사용자 직접 변경(Unity): 씬 폴더/파일명, 보이스 폴더 이동, `main.unity` 인스펙터 `scenePath = Scene/NKS`
- 상태: **Unity 컴파일 및 플레이 테스트 전**

### 결정 사항
| # | 결정 | 이유 |
|---|---|---|
| 1 | 시나리오 폴더 `main` 폐기 → `NKS` | 시나리오를 목록에서 불러오므로 `main`이 불필요, `main.unity`와 혼동 |
| 2 | 씬 파일명 `Scene01` → `{시나리오}{2자리}` (`NKS01`, `IL01~04`, `SC01`) | 시나리오 간 파일명 중복 제거 (캡처 폴더 `CaptureOutput/{씬 파일명}` 충돌 해소) |
| 3 | 보이스 폴더 `Audio/Voice/{NKS, IL, SC, OT}` | 시나리오 폴더명 = 보이스 폴더명. OT는 테스트 시나리오용으로 보존 |
| 4 | 카탈로그 파일이 git 변경으로 잡히는 것 허용 | 시나리오 추가 시 어차피 다른 파일도 함께 변경됨 |
| 5 | 카탈로그에 `hidden` 항목 추가 | 테스트용 시나리오(OT 등) 목록 숨김 |

### 구현 / 수정 내역

#### 1. 시나리오 폴더 우선 보이스 검색
- **변경 위치**: `AudioManager.scenarioVoiceFolder` 필드, `AudioManager.SetScenarioVoiceFolder()` / `AudioManager.LoadVoiceClip()` 추가, `AudioManager.PlayVoice()`의 `Resources.Load` → `LoadVoiceClip`, 경고 로그에 폴더 정보 추가
- **동작**: `Audio/Voice/{시나리오}/{키}` 우선 → 없으면 `Audio/Voice/{키}` 폴백
- **고려 사항**: 폴더 간 파일명 중복 허용. 루트 폴백으로 `Voice="OT/OT001"`처럼 다른 폴더 참조 가능. 실패 시 추가 조회 1회는 성능 영향 미미. XML 수정 없음.

#### 2. SceneManager에서 시나리오 폴더 전달
- **변경 위치**: `SceneManager.audioManager` SerializeField 추가, `SceneManager.Start()`에서 `SetScenarioVoiceFolder()` 호출, `SceneManager.GetScenarioFolderName()` 추가(`Scene/IL` → `IL`), `scenePath` 코드 기본값 `Scene/main` → `Scene/NKS`
- **고려 사항**: `audioManager` 미연결 시 경고 후 루트만 검색. 실제 scenePath는 인스펙터 값. 2단계(static 선택값)에서 폴더명 출처가 바뀔 예정.

#### 3. VNRecorderTool 주석
- **변경 위치**: `VNRecorderTool.GetScenarioName()` 위 주석 예시 `main` → `NKS` (동작 변화 없음)

### 테스트 필요
- [ ] Unity 컴파일 에러 없음
- [ ] `main.unity` SceneManager 인스펙터에 `audioManager` 연결
- [ ] NKS / IL / SC 각각 Play → 보이스 정상 재생, "보이스 파일 없음" 경고 없음 (IL은 `scenePath`를 바꿔 확인)
- [ ] 씬 순서 정상 (NKS01 → 엔딩, IL01 → IL04 → 엔딩)

</details>

<details>
<summary><b>2026-09-29</b> · [에디터 도구] <code>Tools &gt; Release MCP Lock</code> 메뉴 추가 — desktop-commander node.exe 일괄 종료로 Unity 저장 실패 해결 — <i>테스트 전</i></summary>

### 2026-09-29 — [에디터 도구] MCP 파일 점유 해제 메뉴 (Tools > Release MCP Lock)

- 커밋: 미커밋 (Sourcetree로 커밋 예정)
- 변경 파일: `Assets/Editor/McpLockReleaser.cs` (신규)
- 상태: **Unity 컴파일 및 동작 테스트 전**

### 내용
- **변경 위치**: `McpLockReleaser.ReleaseMcpLock()` — 메뉴 `Tools/Release MCP Lock`
- **동작**: 확인 대화상자 → PowerShell(`-EncodedCommand`)로 CommandLine에 `desktop-commander`가 포함된 `node.exe`를 전부 `Stop-Process -Force` → 종료 개수를 Console 로그로 출력. `#if UNITY_EDITOR_WIN` 전용.
- **원인**: Claude가 desktop-commander MCP로 프로젝트 파일을 확인한 뒤 `node.exe`가 파일을 점유한 채 남아, Unity가 Temp 파일을 저장하지 못함. 기존에는 작업 관리자에서 `node.exe`를 수동 종료했음.
- **사용법**: Unity 저장 전에 메뉴를 한 번 누름.

### 조사 결과 (2026-09-29)
- desktop-commander 서버가 2세트(npx 래퍼 + 서버 본체 = `node.exe` 2개씩, 총 4개) 실행 중이었음. **두 세트 모두 같은 claude.exe(현재 계정) 자식** → 이전 계정의 잔존 프로세스가 아니라 Claude 앱 재실행/세션별 기동으로 누적된 것.
- 현재 대화가 쓰는 쪽이 **오래된(먼저 뜬) 세트**였음 → "최신 1세트만 남기기" 방식은 현재 연결을 끊을 수 있어 폐기.
- 서버 인스턴스 간 동기화 상태 없음(설정 파일만 공유) → 종료해도 파일/설정이 꼬이지 않음.

### 결정 사항
| # | 결정 | 이유 |
|---|---|---|
| 1 | desktop-commander `node.exe` **전부 종료** 방식 채택 | 단순·확실, 기존 수동 루틴과 동일. 연결이 끊기면 Claude 재연결로 복구 |
| 2 | 저장 시 자동 실행(OnWillSaveAssets) 대신 **수동 메뉴** | 저장 전에 한 번 누르면 충분, 저장 속도 영향 없음 |

### 고려 사항
- 누르면 실행 중인 모든 Claude 대화의 파일 도구 연결이 끊김 → Claude가 파일을 쓰는 중에는 누르지 말 것(덜 써진 파일 위험).
- 추후 개선안(보류): Sysinternals `handle.exe`로 `usoSCtheater\Temp`를 실제 점유한 PID만 종료 → 무관한 연결 유지.

### 테스트 필요
- [ ] Unity 컴파일 에러 없음
- [ ] 메뉴 실행 후 로그 `desktop-commander node.exe N개 종료`, 작업 관리자에서 해당 node.exe 사라짐
- [ ] 이후 Unity 저장 정상, Claude 재연결 후 파일 도구 정상

</details>

<details>
<summary><b>2026-09-29</b> · [설계 검토] 시나리오별 보이스 폴더 분리(루트 폴백) + 시나리오 선택 기능 방향 확정 — <i>1·2단계 반영 완료, 용어 정리 후 3단계 진행 중</i></summary>

### 2026-09-29 — [설계 검토] 시나리오별 보이스 분리 / 시나리오 선택 기능

- 커밋: 없음 (코드 변경 없음, 사전 검토만)
- 상태: **결정 완료, 1·2단계 반영 완료(위 항목), 3단계 설계 중**

### 현황 (코드 확인 결과)
- `SceneManager.scenePath`(인스펙터 SerializeField, 현재 `Scene/main`) → `Start()`에서 `Resources.LoadAll` + 파일명 Ordinal 정렬 → 마지막 씬 후 `EndingScene` 로드
- `AudioManager.voicePath = "Audio/Voice"` 단일 폴더, XML `Voice`는 파일명만 기재
- 보이스 접두어 ↔ 시나리오: main=`NKS`, IL=`s001~S004`(대소문자 혼재, `S0020231` 존재), SC=`SCS`, `OT001~054`는 어느 XML에서도 미사용
- Build Settings: `main.unity`(0), `EndingScene.unity`(1)
- `VNRecorderTool.GetScenarioName()`이 `scenePath` 인스펙터 값을 SerializedObject로 읽음
- IL/Scene01 헤더 `MainTtitle`/`SubTitle` — 코드는 `mainTitle`/`subTitle`(대소문자 구분) → 타이틀 미표시 상태

### 결정 사항 (2026-09-29 확정)
| # | 결정 | 메모 |
|---|---|---|
| 1-1 | 보이스는 `Audio/Voice/{시나리오 폴더명}/{키}` 우선 검색, XML 수정 없음 | 시나리오 폴더명 = `Resources/Scene` 하위 폴더명 |
| 1-2 | 파일명은 폴더 간 중복 허용. XML에 적힌 이름을 시나리오 폴더에서 먼저 찾고, 없으면 `Audio/Voice/` 루트에서 검색 | 루트 폴백 덕분에 `Voice="OT/OT001"`처럼 다른 폴더 참조도 가능. 기존 파일 리네임은 보류 |
| 1-3 | `OT` 보이스 = 테스트 시나리오(현재 삭제됨)용, 보존 → `Audio/Voice/OT/` | |
| 2-1 | 시나리오 목록은 에디터 스크립트로 `Resources/Scene` 하위 폴더를 자동 동기화 | ScriptableObject 카탈로그 + AssetPostprocessor(설계안) |
| 2-2 | 선택값 전달은 static 클래스, 미선택 시 인스펙터 scenePath 폴백 | 도메인 리로드 비활성 프로젝트 → Play 시작 시 초기화 필수 |
| 2-3 | 녹화 도구 대응은 씬 구조 확정 후 마지막에 | |
| 2-4 | 엔딩 종료 → 녹화 종료 신호 → 목록 씬 자동 복귀 | 엔딩 SDSpine 업데이트는 추후 |
| 2-5 | 플레이 중 목록 복귀: 우선도 낮음, 볼륨 보고 결정 | 엔딩 복귀와 같은 함수 공유 가능 |
| 2-6 | 시나리오 선택 시점에 해당 폴더만 로드, 시나리오 내 시작 씬 선택 지원 | 상세는 목록 씬 GUI 설계 때 |
| 2-7 | 목록 씬 GUI는 사용자가 Unity에서 직접 구성, 기능/함수 연결은 상세 구현 때 | |

### 추가 확인 사항
- `EditorSettings`: Enter Play Mode Options 활성 + Domain/Scene Reload 모두 비활성 → static 값이 Play 세션 간 유지됨. static 선택값은 `RuntimeInitializeOnLoadMethod(SubsystemRegistration)`로 초기화 필요
- 빌드에서 백로그가 `Time.timeScale = 0` 사용 → 백로그 열린 상태로 씬 전환 시 목록 씬에서 시간 정지 가능 → 씬 전환 시 `timeScale = 1` 복구
- 중간 씬부터 시작하면 이전 씬에서 설정된 BGM/CG/CGGroup 상태가 없음 (PageUp 디버그와 동일)
- `Resources.LoadAll`은 하위 폴더까지 재귀 → 시나리오 폴더 안에 하위 폴더 금지

### 예상 문제
- Explorer로 파일 이동 시 .meta 분리 → 반드시 Unity 에디터 내에서 이동
- `VNRecorderTool` 시나리오명이 항상 인스펙터 값으로 기록되는 문제 → 런타임 선택값 참조로 변경 필요
- 커스텀 `SceneManager` 클래스명이 Unity `SceneManager`와 충돌 → 신규 코드에서 전체 이름 사용
- `Resources` 전체가 빌드에 포함 → 시나리오 증가 시 빌드 크기 증가 (장기 과제)

### 제안 작업 순서
1. 보이스 폴더 분리 + AudioManager 폴더 주입(폴백 포함)
2. 선택값 static 클래스 + SceneManager 런타임 경로(인스펙터 폴백)
3. 매니페스트 + 최소 목록 씬 + Build Settings 0번 등록
4. VNRecorderTool 시나리오명 대응
5. 엔딩 → 목록 복귀
6. 씬/보이스 파일 리네임

</details>

<details>
<summary><b>2026-09-29</b> · [기능 정리] 녹화 파일 시나리오별 저장, CGGroup 립싱크 재사용, ResourceKey 도입 외 버그 수정 — <i>테스트 전</i></summary>

### 2026-09-29 — 캡처/녹화 기능 정리, CGGroup 립싱크 대응

- 커밋: 미커밋 (Sourcetree로 커밋 예정)
- 변경 파일: `Assets/Scripts/Capture/SceneCaptureUtil.cs`, `Assets/Editor/VNRecorderTool.cs`, `Assets/Scripts/DialogManager.cs`
- 상태: **Unity 컴파일 및 플레이 테스트 전**

### 결정 사항

| # | 결정 | 이유 |
|---|---|---|
| 1 | 영상 녹화(VNRecorderTool)가 핵심 기능, 당분간 녹화만 사용 | 녹화는 이미 가속 동작 중 → 캡처 툴 가속(터보) 불필요 |
| 2 | 녹화 파일명: `Recordings/{시나리오}/{yyyy-MM-dd_HH-mm-ss}.mp4` | 시나리오 구분 + 덮어쓰기 방지 |
| 3 | CGGroup TEXT에서 그룹 전원 립싱크는 **정상 동작** | CGGroup은 여러 CG가 함께 대사하는 연출용 |
| 4 | 화자가 바뀐 뒤 같은 캐릭터가 다시 말할 때는 CG/Animation 재지정 | 기획 의도 (새 Ani로 대화 이어가기). Name 불일치 시 Lip 추적 초기화 유지 |
| 5 | CGGroup 화자명은 전용 속성 대신 `Name="그룹명, 화자명"` | CGGroup은 자주 쓰는 기능이 아님 → 속성 추가 지양 |
| 6 | BG/IMAGE 리소스 키 속성명 `Key` → `ResourceKey` (신규 기본), `Key`도 계속 읽음 | 기존 XML 71곳 일괄 수정은 위험 → 양방향 호환 |
| 7 | 회의록 운영 시작 | 커밋별 변경점·의도 기록 |

### 구현 / 수정 내역

#### 1. VNCaptureTool 터보 모드 — 무기한 보류 주석
- **변경 위치**: `SceneCaptureUtil.TurboEnabled`, `SceneCaptureUtil.TurboFramerate` (주석만, 동작 변화 없음)
- **원인**: VNRecorderTool이 `FrameRatePlayback.Constant` 30fps + `CapFrameRate=false`로 이미 실시간보다 빠르게 녹화함(게임 시간은 프레임당 1/30초 고정, 렌더는 최대 속도). 캡처 툴 전용 가속이 필요 없어짐.
- **고려 사항**: 코드 삭제 대신 주석으로 보류 표시(재개 여지). 프로퍼티는 어디서도 참조하지 않음.

#### 2. 녹화 파일명 시나리오별 저장
- **변경 위치**: `VNRecorderTool.StartRecording()` (출력 폴더), `VNRecorderTool.GetScenarioName()` 추가
- **원인**: 기존 `Recordings/{timestamp}.mp4`로는 어떤 시나리오(main/IL/SC)의 녹화인지 구분 불가.
- **고려 사항**:
  - 한 번의 녹화 = `SceneManager.scenePath` 폴더 전체 씬 → 엔딩. 시나리오명은 scenePath 마지막 조각(`Scene/main` → `main`).
  - scenePath는 private `[SerializeField]` → `SerializedObject`로 인스펙터 값 직접 읽음(코드 기본값이 아닌 실제 값).
  - SceneManager 못 찾으면 `Unknown` + 경고. 파일명 불가 문자는 `_` 치환.
  - 전역 `SceneManager` 클래스와 `UnityEngine.SceneManagement.SceneManager` 충돌 방지를 위해 `global::SceneManager` 명시.
  - 폴더별 저장 + 타임스탬프 파일명으로 덮어쓰기 없음.

#### 3. CGGroup Zoom 경고 로그
- **변경 위치**: `DialogManager.ShowLine()` 이펙트 처리, `DialogManager.ProcessSetCG()` zoom 처리
- **원인**: 그룹명으로 `SetZoom` 호출 시 "등록되지 않은 CG 키" 경고만 나고 무동작.
- **고려 사항**: 그룹 Zoom은 그룹 전원 Zoom이 필요해 별도 구현 대상. 현재는 `[TODO]` 주석 + "CGGroup에 Zoom하는 기능은 별도 구현이 필요합니다" 로그만 출력하고 `SetZoom` 미호출.

#### 4. CGGroup Lip 재사용 (이어지는 대사 립싱크)
- **변경 위치**:
  - 필드: `lastCgKey / lastAnimation / lastSpeakerName` → `List<CGGroupEntry> lastLipTargets` + `string lastLineSpeakerName`
  - `DialogManager.ShowLine()` CG 처리 분기
  - `DialogManager.RestartLipByName()` 추가, `DialogManager.ResetLipTracking()` 추가
  - `DialogManager.LoadScene()` CGGROUP 파싱: `Name="그룹명, 화자명"` 분리
  - `DialogManager.ProcessSetCG()` CG=none 처리
  - `CGGroupEntry.speakerName` 필드 + 생성자 인자(기본값 `""`로 기존 호출 호환)
- **원인**: 그룹 사용 후 CG를 생략한 이어지는 대사에서 `lastCgKey`=그룹명 → `RestartLipSync`가 spineDict에 없는 키라 즉시 return → 립싱크 안 됨. CG 재등록으로 우회 가능하나 번거로움.
- **동작**:
  - CG 생략 + 직전 CG 지정 라인과 Name 같음 → 대상 전원 립싱크(단일 CG / 그룹 전원 대사)
  - CG 생략 + CGGROUP 화자명과 Name 일치 → 해당 멤버만 립싱크
  - 일치 없음 → 추적 초기화(기존 동작)
- **고려 사항**:
  - Lip 재사용의 핵심은 CG가 아닌 **Name 비교** → 멤버별 화자명 필요.
  - 사용자 아이디어(cgKey/animation/speakerName 3개 List)를 인덱스 불일치 방지를 위해 `CGGroupEntry` 1개 List로 구현(의미 동일).
  - animation을 멤버별로 저장 — 기존 그룹 처리의 `lastAnimation=null`이 그대로 넘어가면 `LipSyncCoroutine`의 `Split`에서 NRE.
  - 그룹 원본 List 참조가 아닌 복사본 저장(추후 그룹 생명주기 변경 대비).
  - 화자명 미지정 멤버는 개별 비교에서 제외 → Name이 빈 나레이션과 오매칭 방지.
  - Name 규칙: 첫 번째 쉼표만 구분자(그룹명에 쉼표 불가), 앞뒤 공백 Trim, 화자명은 TEXT Name과 글자 그대로 일치해야 함. 쉼표 없는 기존 형식도 동작.
  - 비활성 CG에 대한 립 코루틴은 자원 부담 미미하여 필터링하지 않음.

```xml
<Line Type="CGGROUP" Name="groupA, 메이"   CG="mei_idol_3"    Position="left"  Animation="smile1"/>
<Line Type="CGGROUP" Name="groupA, 후유코" CG="fuyuko_idol_2" Position="right" Animation="wait"/>
<Line Type="TEXT" Name="메이&amp;후유코" CG="groupA" Voice="..."/>  <!-- 둘 다 립 -->
<Line Type="TEXT" Name="메이&amp;후유코" Voice="..."/>              <!-- 둘 다 립 -->
<Line Type="TEXT" Name="메이" Voice="..."/>                       <!-- 메이만 립 -->
```

#### 5. ClearScene 시 Lip 추적 초기화
- **변경 위치**: `DialogManager.ClearScene()` → `ResetLipTracking()` 호출
- **원인**: ClearScene이 CG는 모두 숨기지만 Lip 추적 값은 남아, TRANSITION 직후 같은 화자의 CG 생략 대사가 숨겨진 CG에 립싱크 실행.
- **고려 사항**: `DebugResetState()`도 ClearScene을 거치므로 함께 해결.

#### 6. DebugPrevLine 반복 방향 버그
- **변경 위치**: `DialogManager.DebugPrevLine()` — `i++` → `i--`
- **원인**: 직전 노드가 TEXT가 아니면 앞으로 탐색 → 엉뚱한 라인 표시 또는 인덱스 범위 초과 가능.
- **고려 사항**: 디버그 전용(Backspace) 기능.

#### 7. 리소스 키 속성 ResourceKey 도입 (Key 하위 호환)
- **변경 위치**: `DialogManager.GetResourceKey()` 추가, `LoadScene()`의 BG / IMAGE 파싱
- **원인**: `Key`라는 속성명이 모호함(CG 키, 그룹 키 등과 혼동).
- **고려 사항**:
  - `ResourceKey` 우선, 없으면 `Key` 읽음 → 기존 XML과 신규 XML 모두 동작.
  - 기존 씬 XML의 `Key="` 71곳은 수정하지 않음(일괄 치환 누락 시 BG/이미지가 조용히 안 뜨는 위험).
  - 코드에 **"모든 XML이 ResourceKey로 전환되기 전까지 Key 폴백을 제거하지 말 것"** 주석 명시.

### 보류 사항

| 항목 | 내용 | 재개 조건 / 메모 |
|---|---|---|
| VNCaptureTool 터보 모드 | 무기한 보류 | `Time.captureFramerate` 적용, UI 토글, `AutoPlayCoroutine`의 `WaitForEndOfFrame` 게이팅 모두 미구현 상태로 둠 |
| CGGroup 생명주기 (C) | TRANSITION 시 `ClearScene`이 `cgGroupDict.Clear()` → 같은 씬에서 트랜지션 후 그룹 사용 불가 | 추후 "한 번 등록하면 해당 스크립트 동안 유지"로 변경 (Clear 위치를 `LoadScene` 시작으로 이동 등) |
| CGGroup 전체 Zoom (D) | 그룹 전원 Zoom 기능 | 현재 계획 없음. 사용 시 경고 로그만 |
| 백로그 SD 아이콘 CGGroup 대응 (E) | 백로그가 그룹명을 cgKey로 조회 → 디폴트 아이콘 표시 예상 | 백로그 퀄리티 업 작업 시 함께 |
| 눈 깜빡임 트랜지션 | 이전 핸드오프에서 구현 취소 | 추후 결정 |

### 발견된 이슈 (미논의)
- `BacklogManager` 백로그 생성 시 `line.cgKey`를 직접 대입(`null` / 직전 cgKey)함 → `GetReadNodes()`는 `scriptNodes.GetRange()`(얕은 복사)라 원본 DialogLine이 변형됨을 확인. 영향 범위는 `DebugPrevLine` 재생 등 제한적일 것으로 보임.

### 테스트 필요
- [ ] Unity 컴파일 에러 없음 확인
- [ ] 녹화 모드: `Recordings/main/{타임스탬프}.mp4` 생성, 로그 경로 확인
- [ ] 기존 씬(Key 속성) BG/IMAGE 정상 표시
- [ ] 단일 CG Lip 재사용 기존과 동일 동작
- [ ] CGGroup 테스트 씬: 그룹 전원 / 멤버별 Lip 재사용, 그룹 Zoom 시 경고 로그
- [ ] TRANSITION 직후 CG 생략 대사에서 립싱크 미발생
- [ ] Backspace(DebugPrevLine) 이전 라인 이동

</details>
