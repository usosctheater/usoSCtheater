# 가짜샤니마스극장 (usoSCtheater) 회의록

> 작업 완료 시점(커밋 전)에 Claude가 1회 갱신 → 해당 작업의 코드와 같은 커밋에 포함. 커밋 후에는 갱신하지 않음. 최신 항목이 위.
> 항목 형식: **내용** / **변경 위치** (파일 · 함수) / **원인** / **고려 사항**
> 목록 형식: 항목마다 `<details>`로 접고, `<summary>`에 `날짜 · [분류] 한 줄 요약 — 상태` 기재 (펼치면 상세)
> 원본: `D:\usosctheater\usoSCtheater_회의록.md` 하나뿐 (git 추적). 커밋 해시는 적지 않음 — 이 파일의 커밋 이력으로 확인
> 보기용 페이지(접기/펼치기·검색): https://claude.ai/artifact/G24K2D8xS7C9E6EQuP319r — 원본 기록과 같은 파일로 동시에 재게시 (절차: 핸드오프 '회의록 갱신 절차')

---

<details>
<summary><b>2026-10-07</b> · [도구] SCResourceGrabber 상세 창 분리 + 추적 모드 + 단축키·필터·정렬 + 암호화 분류 + Spine 수집 창 — <i>테스트 필요</i></summary>

### 2026-10-07 — [도구] SCResourceGrabber: 상세 창 분리, 저장 경로 변경, 추적 모드, 단축키

**1. 리소스 상세 창 분리**
- 원인: 목록 아래 미리보기는 메인 창 크기에 묶여, 이미지를 크게 보려면 프로그램 전체를 키워야 했음
- 목록 더블클릭/Enter → 리소스별 독립 창 (`ResourceDetailWindow`, 리소스당 1개, 이미 열려 있으면 앞으로)
- 이미지: 창 크기에 맞춰 확대/축소(Uniform), 처음 열 때 창을 이미지에 맞춤(작업 영역 85% 이내), 체크무늬 배경, `1:1` 토글(파일 DPI와 무관하게 픽셀 1:1 + 스크롤)
- 오디오/비디오: 열면 자동 재생, 재생/일시정지·정지·위치 슬라이더·시간 표시 / JSON: 들여쓰기 정리 표시(2MB까지)
- 메인 창의 미리보기 영역·관련 코드 삭제, `App.xaml` `ShutdownMode=OnMainWindowClose`(메인 종료 시 상세 창도 종료)
- 버그 수정 (사용자 제보): 투명도가 있는 WebP(VP8L)가 상세 창에서만 배경이 검게·글자 모양이 깨져 보임 → 원인: Windows WebP 디코더가 원본 크기 디코딩 시 형식을 `Bgr32`(투명도 없음)로 보고(픽셀 4번째 바이트에는 실제 알파 존재, 축소 디코딩인 썸네일은 `Bgra32`라 정상). `CapturedResource.LoadBitmap`에서 WebP + `Bgr32`이면 픽셀을 `Bgra32`로 재지정 (`IsWebp`, `ReinterpretAsBgra32`, 반환형 `BitmapSource`)

**2. 저장 경로**
- 기본 저장 폴더 `D:\usosctheater\resource\Grabber` — 기존 settings.json이 옛 기본값(내 문서\SCResourceGrabber)이면 자동으로 새 경로로 교체, 직접 지정한 경로는 유지
- 저장 시 `호스트/assets/` 폴더를 만들지 않고 저장 폴더 바로 아래에 파일명으로 저장 (모든 리소스 URL이 `assets/해시` 형식이라 폴더 구조가 의미 없음) — "URL 경로 구조 유지" 옵션·`KeepUrlPath` 설정 삭제

**3. 추적 모드**
- 결정: "지금 게임 화면에 그려지는 리소스 목록"은 게임이 단일 캔버스(WebGL)에 그려 페이지에 정보가 없고, 엔진 내부 접근은 게임 업데이트에 취약 → 채택하지 않음. 대신 추적 모드
- [● 추적 모드] → 추적 창(`TrackingWindow`)이 열려 있는 동안: 새로 캡처된 리소스 + 재생된 오디오를 URL 단위 한 줄로 합쳐 최신순 표시 (구분: 로드/재생/로드+재생, 재생 횟수, 마지막 재생 시각), 재생 중 행 강조("▶ 재생 중"), 다시 재생되면 맨 위로
- 분류 필터: 메인 목록과 같은 형식별 체크박스(이미지·오디오·비디오·Spine·JSON·폰트·기타, 개수 표시, 기본 표시 분류도 메인과 동일) — 게임이 주기적으로 보내는 Ping 등이 목록을 오염시키는 문제(사용자 테스트에서 발견)를 형식 필터로 해결. 캡처 목록에 없는 URL은 재생된 적 있으면 오디오, 아니면 기타로 분류
  - 처음 넣었던 "새로 로드 / 재생 / 재생 중만" 필터는 삭제 (오디오 필터 + 재생 시 맨 위 정렬로 충분)
- 체크한 항목 저장, 더블클릭 상세 창, 목록 비우기(재생 중 항목은 유지), 창 닫으면 추적 종료
- 저장 버튼 정리: "보이는 항목 (전부) 저장" 버튼 삭제 (메인·추적 창 모두) — 체크한 항목 저장만 유지
- 오디오 재생 추적: `Scripts/audio_hook.js`를 페이지 생성 시점에 주입(`AddScriptToExecuteOnDocumentCreated`, iframe 포함) — fetch/XHR 응답(ArrayBuffer·Blob) → URL 기억 → `decodeAudioData` 결과(AudioBuffer)에 URL 연결 → `AudioBufferSourceNode.start` / `HTMLMediaElement.play` 시 'play', `ended`/`pause` 시 'end' 메시지 → `PageAudioMonitor`가 수신. 프로그램 시작부터 주입되므로 추적 시작 전에 받은 보이스도 재생 시 잡힘
- 새로고침/페이지 이동 시 재생 중 표시 일괄 해제
- 진단 로그: `%LocalAppData%\SCResourceGrabber\audio_hook.log` (실행마다 새로 씀 — 훅 설치 여부, 재생 URL, 매핑 실패 통계)

**4. 분류 필터 [전체] 체크박스 (메인·추적 창 공용)**
- 분류 체크박스 맨 왼쪽 [전체]: 하나라도 꺼져 있으면 전부 켜고, 모두 켜져 있으면 전부 끔. 일부만 켜져 있으면 중간 상태 표시
- 메인·추적 창이 같은 코드를 쓰도록 `Services/CategoryFilterBar.cs`로 분리

**5. 검색창 삭제**: 검색 기준이 URL인데 모든 리소스 URL이 `assets/해시`라 의미 없음 → 검색창·관련 코드 삭제 (필요해지면 그때 다시 추가)

**6. 프로그램 전용 단축키 (설정 파일 편집 방식)**
- 결정: 고정 키 / 설정 파일 편집 / 설정 창 중 "설정 파일 편집" 채택 (자주 바꾸지 않음). 설정 창은 필요해지면 이 구조 위에 추가
- `settings.json`의 `"Hotkeys"`(동작 이름 → "Ctrl+S" 형식, 빈 값이면 해제), 수정 후 재시작. 새 동작은 기존 설정 파일에도 기본값 자동 추가, 잘못된 키·중복은 상태 표시줄에 경고
- 기본: `SaveChecked` Ctrl+S(체크 저장, 상세 창에서는 그 리소스 저장) / `ToggleTracking` Ctrl+R(추적 창 열기·닫기) / `ToggleAllChecks` Ctrl+A(보이는 항목 전체 체크/해제) / `ClearList` Ctrl+L(목록 비우기) — 포커스가 있는 창의 목록에 적용
- 게임 화면에 포커스가 있어도 동작하며, 처리한 키는 브라우저 기본 동작(Ctrl+R 새로고침 등)을 막음. 새로고침은 F5. 글자 입력 칸에 포커스가 있으면 단축키 무시
- 버튼 툴팁에 현재 단축키 표시

**7. Spine 데이터 조사 결과 → 암호화 분류**
- 확인: 게임은 Spine atlas·json을 평문으로 보내지 않음. 수집 데이터(tempdata 339개) 중 `.txt` 227개가 모두 같은 바이트(`5D AC 43 59 57 4C 5B 44 49`)로 시작하는 무작위 바이너리(엔트로피 ≈ 8) = 게임 전용 암호화 형식. 평문 atlas/Spine JSON은 0개
- 기존 Spine 데이터(bbang.shinymaskr.work에서 받은 것, Spine 3.6.53, atlas 7.6KB / json 696KB)와 크기 비교: 동일 크기 없음(암호화 파일 최대 약 210KB) → 압축 후 암호화로 추정, 압축 크기 근처 파일(1.4KB대 3개, 93~95KB대)은 있으나 확정 불가
- 결정: 암호화 해제(직접 복호화·게임 내부에서 풀린 데이터 가로채기)는 하지 않음
- 분류 `Encrypted`("암호화") 추가 — 앞 4바이트 `5D AC 43 59`, 또는 알려진 형식이 아닌 512바이트 이상·엔트로피 7.5 초과 바이너리. 기타에서 분리되어 Ping 등만 기타에 남음. 상세 창은 "암호화된 데이터" 안내 표시
- 게임 창 필터에서 Spine 분류 제거 (게임이 평문 Spine을 보내지 않음). 분류 자체는 Spine 수집 창에서 사용

**8. 목록 정렬 + 받은 시각**
- 메인 목록에 "받은 시각" 열 추가, 추적 창 "처음" → "받은 시각"
- 머리글 "종류·크기·받은 시각" 클릭 정렬: 오름차순 ▲ → 내림차순 ▼ → 해제(받은 순서). 종류는 필터 순서(이미지·오디오·비디오·Spine·JSON·폰트·암호화·기타) — `Services/ListViewSorter.cs`, 메인·추적 창 공용

**9. Spine 수집 창**
- 목적: Spine atlas·json은 이미 풀린 데이터를 제공하는 공개 뷰어 사이트(https://spine.shinycolors.moe/)에서 수집, 텍스처 이미지는 게임에서 수집. 기존에 쓰던 외부 사이트의 서버 상태에 의존하지 않는 수집 경로 확보
- [Spine 수집] 버튼 → 같은 프로그램 창(`MainWindow(CollectorKind.Spine)`)을 하나 더 열어 `SpineStartUrl`로 접속, 동일한 방식으로 캡처
- 게임 창과 차이: 제목, Spine 분류 표시(기본: 이미지·Spine), 저장 폴더 `SpineSaveFolder`(기본 `D:\usosctheater\resource\Grabber\Spine`), 저장 시 URL 경로 폴더 유지(data.json·data.atlas 이름 겹침 방지), 오디오 추적 없음, 이전 세션 캐시 정리는 게임 창만
- 설정 객체는 두 창이 공유(`AppSettings.Shared`), 게임 창을 닫으면 Spine 창·추적 창도 함께 정리

**변경 위치**
| 파일 | 변경 |
|---|---|
| `ResourceDetailWindow.xaml(.cs)` | 신규 — 상세 창 |
| `TrackingWindow.xaml(.cs)`, `Models/TrackedItem.cs` | 신규 — 추적 창 / 항목 (분류 필터는 `CategoryFilterBar`, `TrackedItem.Category`, 단축키 처리) |
| `ResourceDetailWindow.xaml.cs` | 저장 단축키 → 이 리소스 저장, `BitmapSource` 사용 |
| `Services/PageAudioMonitor.cs`, `Scripts/audio_hook.js` | 신규 — 스크립트 주입·메시지 수신 (js는 EmbeddedResource) |
| `MainWindow.xaml(.cs)` | 미리보기 영역 삭제, 더블클릭/Enter → `OpenDetail`, [● 추적 모드] 버튼·`Tracking_Click`/`ToggleTracking`, URL→리소스 사전, 오디오 모니터 연결·로그, `SaveVisible_Click`·버튼 삭제, `DefaultVisible`을 추적 창과 공유(internal), 검색창·경로 유지 옵션 삭제, `CategoryFilterBar` 사용, `OnPreviewKeyDown` 단축키·툴팁 |
| `Services/AppSettings.cs` | 기본 저장 경로 변경 + 옛 기본값 이전, `KeepUrlPath` 삭제, `Hotkeys` 추가(기본값 자동 보충) |
| `Services/ResourceSaver.cs` | `BuildTargetPath`: 저장 폴더 + 파일명만 |
| `Services/CategoryFilterBar.cs`, `Services/HotkeyMap.cs`, `Services/ListViewSorter.cs` | 신규 — 분류 필터 줄([전체] 포함, 표시 분류 지정), 단축키, 머리글 정렬 |
| `Services/ResourceClassifier.cs` | 암호화 판정(매직 바이트·엔트로피) |
| `Services/AppSettings.cs`(추가) | `SpineStartUrl`, `SpineSaveFolder`, `Shared` |
| `Services/ResourceSaver.cs`(추가) | `BuildTargetPath`/`Save`가 폴더·경로 유지 여부를 인자로 받음 |
| `Models/CapturedResource.cs` | `LoadBitmap` WebP 투명도 수정, `Encrypted` 분류·`CapturedAtText` |
| `App.xaml`, `SCResourceGrabber.csproj`, `README.md` | 종료 모드, 스크립트 리소스, 문서 |

**고려 사항 / 한계**
- 이미지·Spine은 "새로 받은 시점"만 알 수 있음 — 게임이 메모리에 가진 리소스를 재사용하면 추적 목록에 안 나옴 (커뮤 진입 직전에 추적 시작 권장)
- 게임의 실제 오디오 재생 방식은 미확인 — 테스트 페이지에서 fetch·XHR·Audio 요소 3경로 동작 확인. 게임이 JS에서 데이터를 가공(복호화 등)한 뒤 재생하면 매핑 실패 → 로그로 확인 후 보완
- exe 갱신 규칙: 실행 중이면 exe를 덮어쓸 수 없으므로, 갱신 전 사용자에게 종료 요청 (또는 Claude가 종료 후 교체 — 사용자 허락)

**테스트 필요**: 실제 게임에서 추적 모드 — 보이스/BGM 재생 표시, audio_hook.log 확인 / WebP 상세 창 표시 / 게임 화면 포커스 상태에서 단축키 / Spine 수집 창에서 atlas·json 캡처·분류·폴더 저장 (사용자)

</details>

<details>
<summary><b>2026-10-07</b> · [도구] 리소스 수집 도구 SCResourceGrabber 신규 + 저장소 Tools/ 폴더 도입 — <i>완료</i></summary>

### 2026-10-07 — [도구] SCResourceGrabber 신규 작성 + 개발 보조 도구 관리 방식 결정

**배경**
- 리소스(이미지·보이스·Spine 등)를 CYKViewer에서 찾아 DevTools로 하나씩 받던 작업이 오래 걸려 일괄 수집 도구를 만들기로 함
- CYKViewer는 WPF + WebView2 앱이라 Edge 확장(DevTools 패널)을 붙일 수 없음 → Chrome/Edge 확장 시안은 폐기
- 기능을 계속 추가할 예정이라 확장 프로그램보다 자유도가 높은 별도 프로그램으로 결정, 언어는 Unity 확장성을 고려해 C#

**SCResourceGrabber v0.1** (`Tools/SCResourceGrabber`, C# WPF + WebView2, .NET 10)
| 파일 | 역할 |
|---|---|
| `MainWindow.xaml(.cs)` | 왼쪽 게임 화면(WebView2, 주소창·DevTools 버튼), 오른쪽 분류 필터·목록(썸네일)·미리보기(이미지/오디오/텍스트)·저장 |
| `Services/ResourceCapture.cs` | `WebResourceResponseReceived`로 응답 본문 캡처 → 세션 캐시 파일 기록 (같은 URL 1회, GET·2xx만) |
| `Services/ResourceClassifier.cs` | URL 확장자 / 매직 바이트 / 텍스트 패턴 / Content-Type으로 분류 — 이미지·오디오·비디오·Spine(atlas, skeleton json, skel)·JSON·폰트·기타 |
| `Services/ResourceSaver.cs` | 저장 경로(URL 경로 유지 또는 분류별 폴더), 동일 내용 건너뜀, 이름 충돌 시 번호 |
| `Services/AppSettings.cs` | `%AppData%\SCResourceGrabber\settings.json` (시작 주소, 저장 폴더, HostFilter 등) |
| `Models/CapturedResource.cs` | 캡처 항목 모델 (썸네일 지연 로딩) |
- 로그인: 전용 프로필 `%LocalAppData%\SCResourceGrabber\Profile` 유지 → 첫 실행 때만 로그인
- 실행 파일: `dotnet publish` 단일 exe (네이티브 dll 포함), 아이콘 `app.ico`(icon.png 변환)
- 사용자 실행·리소스 로드 테스트 완료

**저장소 구조 결정 (개발 보조 도구 관리)**
- 메인 저장소 하나에서 관리, 별도 저장소/서브모듈은 사용하지 않음 (다른 프로젝트 공유·별도 배포가 생기면 그때 분리)
- Unity 에디터 안에서 도는 도구(녹화, Spine 임포트 등) → 기존처럼 `usoSCtheater/Assets/Editor` 쪽
- 독립 실행 프로그램 → 저장소 루트 `Tools/` (Unity 프로젝트 밖이라 Unity가 읽지 않음)
- 소스만 추적, 빌드 결과물·exe는 무시 (exe는 Tools 폴더 안에 두고 사용)

**변경 위치**
| 파일 | 변경 |
|---|---|
| `Tools/SCResourceGrabber/` | 신규 (`resource/SCResourceGrabber`에서 이동) |
| `.gitignore` (루트) | `Tools/` 규칙 추가 — `!/Tools/**/*.csproj`(기존 `*.csproj` 무시 규칙 예외), `bin/`·`obj/`·`publish/`·`*.exe`·`*.zip` 무시 |

**알려진 제한 / 다음 후보**
- 206(부분 응답) 스트리밍 미디어는 건너뜀, Service Worker 요청은 캡처 안 될 수 있음
- 오디오 미리보기는 Windows MediaPlayer 기반 (ogg는 코덱 없으면 재생 불가, 저장은 정상)
- 추가 예정 기능: 미리보기 강화, Spine 묶음(atlas·json·png) 처리 등

</details>

<details>
<summary><b>2026-10-06</b> · [기능] 시나리오 태그·등장인물 데이터(ScenarioData.xml) → 카탈로그 동기화 — <i>완료</i></summary>

### 2026-10-06 — [기능] 시나리오 태그·등장인물 데이터 1단계: 데이터 문서 + 카탈로그 동기화

**배경 (이번 대화 작업 계획)**
1. 시나리오별 태그·등장인물 데이터 적용 (목록 항목에 태그 = 공통 TAG 이미지 + 텍스트 자동 리사이징, 등장인물 = 캐릭터별 아이콘을 데이터 순서대로 출력)
2. (1번 완료 후) 엔딩 씬 SD Spine을 해당 시나리오 등장인물 중 랜덤 1명으로 교체

이번 항목은 1번의 1단계(데이터 → 카탈로그). 목록 UI 표시(태그/아이콘 프리팹)와 캐릭터 DB는 다음 단계.

**데이터 문서**: `Assets/Resources/Data/ScenarioData.xml` (신규, 사용자 작성)
```xml
<ScenarioData>
    <Scenario Id="IL" Tag="태그1, 태그2" Character="asahi, fuyuko" />
</ScenarioData>
```
- 작성 편의를 위해 요소 1개 + 속성 나열 방식 (초안의 `<Tag>` / `<Character>` 자식 요소 방식에서 변경)
- `Tag` / `Character`는 쉼표 구분 목록 — 앞뒤 공백·빈 항목 무시, 태그 문구에 쉼표 사용 불가
- 같은 요소에 같은 속성을 두 번 쓰면 XML 문법 오류 → 파일 전체 파싱 실패(전부 빈 값 + 경고)

**변경 위치**
| 파일 | 변경 |
|---|---|
| `Scripts/Scenario/ScenarioCatalog.cs` | `ScenarioEntry.tags` / `characters` 필드 추가(동기화 자동 저장). `ScenarioCatalog.Find()` 대소문자 무시 비교 |
| `Scripts/Scenario/ScenarioSelectController.cs` | `RestoreScrollToLastScenario()` 시나리오 ID 비교 대소문자 무시 |
| `Editor/ScenarioCatalogSync.cs` | `ReadScenarioData()` / `SplitList()` / `IsName()` 추가, `Sync()`에서 태그·등장인물 병합·비교, `IsRelated()`에 ScenarioData.xml 경로 추가(변경 시 자동 동기화), 폴더 스캔·병합 딕셔너리/셋 대소문자 무시, 클래스 주석 갱신 |

**결정 사항**
- 시나리오 ID = 시나리오 폴더명(개발명) 그대로 사용, **대/소문자 구분 없음** (XML Id ↔ 폴더명, 카탈로그 조회, 스크롤 복원). 요소/속성 이름도 대소문자 무시
- 캐릭터 ID = 소문자 로마자 (`asahi`), 동기화 시 소문자로 저장
- 데이터 문서는 XML. 클라이언트는 문서를 직접 읽지 않고 동기화된 카탈로그(SO)만 읽음
- 데이터 문서 위치: `~Data` 문서를 한 폴더에 모으기 위해 `Assets/Resources/Data`로 통일 — DefaultLipData.xml·ScenarioCatalog.asset·(예정) CharacterDatabase.asset은 런타임 `Resources.Load` 대상이라 Resources 필수, ScenarioData.xml은 에디터 전용이라 빌드에 포함되지만 수 KB라 무시
- 카탈로그 갱신은 감지 범위 확장(자동)으로 처리 — 동기화가 가벼움(시나리오 XML 몇 개 + 데이터 문서 1개)
- 불일치 처리는 동기화를 멈추지 않고 경고 로그만: 문서에 없는 시나리오(비움), 폴더 없는 Id(무시), Id 누락/중복(첫 항목), 등장인물 중복, 알 수 없는 속성(오타 확인용), 문서 없음/파싱 실패

**다음 단계 결정 (구현 전)**
- 캐릭터 리소스는 `CharacterDatabase`(SO, `Resources/Data`) 하나로 통합 관리 — id → 목록 아이콘 / 엔딩 SD SkeletonDataAsset (추후 항목 확장). SD 에셋 이름 규칙이 제각각(`asahi_normal_1_SkeletonData` / `sd_fuyuko_idol_3`)이라 경로 규칙 로드 불가 → 인스펙터 직접 지정
- 태그 리사이징은 코드 없이 UGUI 레이아웃: 9-slice(Sliced) Image + HorizontalLayoutGroup(Control Child Size) + TMP 선호 너비. 레이아웃 그룹 밖 단독 사용 시에만 ContentSizeFitter. 최소/최대 너비 제한이 필요해지면 공통 컴포넌트 추가 검토
- 목록 표시 한도: 최대 개수에서 자름(경고) — 추후 스크롤 뷰/크기 조절 예정
- 엔딩 SD: 넘어온 시나리오가 없으면(에디터에서 CommunicationScene/EndingScene 직접 Play) 씬 기본 SD 출력. 애니메이션은 현재처럼 전체에서 랜덤(노말/아이돌 타입별 구성 차이는 허용)

**테스트**: 예시 데이터(IL / nks / SC)로 동기화 정상 동작 확인 (사용자)

</details>

<details>
<summary><b>2026-10-06</b> · [정리] 시나리오 선택 기능 구현 점검 + 임시 디버그 GUI 코드 삭제 — <i>완료</i></summary>

### 2026-10-06 — [정리] 시나리오 선택 기능 구현 점검 + 임시 GUI 코드 삭제
**커밋**: `e456d97` 시나리오 목록 씬 임시 디버그 GUI 코드 삭제 (로컬 master, push 전)

**구현 점검 결과 (코드 기준, 모두 존재 확인)**
| 기능 | 위치 |
|---|---|
| 시나리오별 보이스 폴더 (폴더 우선 → 루트 폴백) | AudioManager.LoadVoiceClip |
| 목록 씬 첫 씬 · 카탈로그 자동 동기화 | ScenarioSelectScene(Build 0) · ScenarioCatalogSync |
| 씬 전환 단일 창구 | SceneTransitionManager (LoadScene 호출 1곳) |
| 재생 범위 (모두 재생 → 엔딩 → 목록 / 단일 막 → 목록) | ScenarioPlayer.OnActEnd |
| ESC 강제 종료 → 목록 (녹화 종료 신호 포함) | ScenarioPlayer (KeyCode.Escape) |
| 엔딩 좌클릭 스킵(1초 후) → 목록 | EndingSceneController |
| 녹화 재생 범위 연동 (ALL_ / 막 이름 파일) | RecordingSignal · VNRecorderTool |
| 정식 목록 UI (썸네일·제목·막 목록·PlayButton 즉시 재생·스크롤 복원) | ScenarioListItem · ActListItem · ScenarioSelectController |
| 제목 대소문자 무시 읽기 | ActXml.GetAttrIgnoreCase |

**삭제**: ScenarioSelectController 하단 주석 블록 (선택 API·OnGUI 디버그 화면), 미사용 `using System;`, 클래스 주석의 '임시 GUI 주석 처리' 문구

**남은 항목 (보류/후순위)**: 태그·등장인물 데이터, 첫 CG Y값 문제, 녹화 상태 표시 UI, ScenarioListItem 보강(선택), 엔딩 SD Spine 갱신, XML `MainTtitle` 오타 수정(데이터)

</details>

<details>
<summary><b>2026-10-06</b> · [버그 수정] 시나리오/막 제목 미표시 — XML 제목 속성을 대소문자 구분 없이 읽도록 변경 + 목록 UI 프리팹 설정 수정 — <i>테스트 전</i></summary>

### 2026-10-06 — [버그 수정] 목록 UI 점검 결과 정리 + 제목 속성 대소문자 무시

- 커밋: 미커밋
- 변경 파일: `Scenario/ActXml.cs`, `Editor/ScenarioCatalogSync.cs`, `ScenarioPlayer.cs`
- 사용자 직접 수정(Unity): Content/ScenarioListItem 레이아웃, 썸네일 Image, ActListItem 컴포넌트 위치
- 상태: **Unity 컴파일 및 카탈로그 재동기화 후 확인 전**

### 목록 UI 점검 결과 (사용자 수정 완료)
| 증상 | 원인 | 조치 |
|---|---|---|
| 항목 자식들이 가운데 정렬 안 됨 | 씬 Content VerticalLayoutGroup의 Control Child Size Width ✘ → 가로 늘이기 앵커(sizeDelta.x 0)인 ScenarioListItem 너비가 0이 됨 + 항목 HorizontalLayoutGroup Force Expand Width ✔ | Content Control Child Size Width ✔, Content 앵커 top-stretch, 항목 HLG Force Expand ✘ + Middle Center |
| 썸네일 미표시 | ScenarioThumbnail Image Color가 (0,0,0,0.39) — 배경 패널 설정 복사 | Color 흰색, Image Type Simple, Preserve Aspect |
| ActListItem_All 사라짐 | ActListItem 컴포넌트가 ActList(부모)에 붙어 있고 Play All Item이 ActList를 가리킴 → 자식 정리 코드가 ActListItem_All까지 삭제 | 컴포넌트를 ActListItem_All로 이동, Play All Item 재연결 |

### 시나리오/막 제목 미표시
- **원인**: 막 XML 6개 모두 헤더가 `<Scene MainTtitle="…" SubTitle="…">` — 코드는 `mainTitle` / `subTitle`(대소문자 구분)만 읽음. 동기화는 정상 실행됐으나 빈 값 저장. 같은 이유로 CommunicationScene 막 타이틀(ShowActTitle)도 미표시였음
- **결정**: XML은 수정하지 않고 코드에서 **대소문자 구분 없이** 읽음. 철자 오타(`MainTtitle`) 보강은 하지 않음 — 제목이 안 나오면 오타로 판단
- **변경 위치**: `ActXml.GetAttrIgnoreCase()` 추가, `ScenarioCatalogSync.ReadActHeader()` / `ScenarioPlayer.PlayCurrentAct()`가 이 함수 사용
- **고려 사항**:
  - 현재 데이터 기준: `SubTitle` → 막 제목 정상 표시 / `MainTtitle`(t 2개 오타) → 시나리오 제목은 여전히 표시명으로 대체 + 경고 (XML 오타 수정 시 해결)
  - 카탈로그 동기화는 시나리오 폴더 에셋 변경 시에만 자동 실행 → 코드만 바뀐 경우 **`Tools > Scenario > Sync Catalog` 수동 실행 필요**

### 테스트 필요
- [ ] 컴파일 후 Sync Catalog 수동 실행 → 카탈로그 `Act Titles` 채워짐 (횜의 법칙 / 작용점 / 빛의 관성 / 작용과 반작용 / 언니와 동생 / 수상한 시계)
- [ ] 목록 씬 막 제목 표시, 시나리오 제목은 오타 수정 전까지 표시명 + 경고
- [ ] CommunicationScene 막 시작 시 타이틀 UI 표시 (subTitle)

</details>

<details>
<summary><b>2026-10-06</b> · [목록 UI] 정식 시나리오 목록 UI: ScenarioListItem/ActListItem + PlayButton 즉시 재생 + 카탈로그 제목 저장 + 복귀 시 스크롤 위치 복원 — <i>테스트 전</i></summary>

### 2026-10-06 — [목록 UI] 시나리오 목록 정식 UI 연결

- 커밋: 미커밋 (재생 범위/녹화 대응 커밋 이후 작업)
- 변경 파일: 신규 `Scenario/ScenarioListItem.cs`, `Scenario/ActListItem.cs` / 수정 `Scenario/ScenarioSelectController.cs`, `Scenario/ScenarioCatalog.cs`, `Scenario/ActXml.cs`, `Editor/ScenarioCatalogSync.cs`
- 사용자 직접 작업(Unity): `Prefab/ScenarioListItem.prefab`, `Prefab/ActListItem.prefab` 제작, ScenarioSelectScene UI 구성
- 상태: **Unity 컴파일 및 인스펙터 연결/플레이 테스트 전**

### 결정 사항
| # | 결정 | 이유 |
|---|---|---|
| 1 | 선택 → 시작 버튼 흐름 폐지, 각 행의 PlayButton 클릭 시 즉시 재생 (ActListItem_All = 모두 재생, ActListItem = 단일 막) | 조작 단계 축소 |
| 2 | 막 목록은 스크롤뷰 중첩 대신 VerticalLayoutGroup. 항목 크기 고정, 막 수에 따라 행 높이가 Preferred(80) 이하로만 줄어듦 (LayoutElement Min~Preferred) | 중첩 ScrollRect의 입력 충돌(휠/드래그를 안쪽이 모두 소비)·마스크/레이아웃 비용 제거, 막 개수는 많지 않음 |
| 3 | 썸네일: `Resources/ScenarioThumbnail/{시나리오 폴더명}` (Sprite), 없으면 기본 썸네일 | |
| 4 | 시나리오 제목 = 막 XML `mainTitle`(처음으로 값이 있는 막), 막 제목 = 각 막 `subTitle` — 카탈로그 동기화 때 저장. 없으면 표시명/파일명 대체 + 경고 로그 | 목록 씬에서 XML을 열지 않음 |
| 5 | 목록 복귀 시 직전 재생 시나리오 위치로 스크롤 — `restoreScrollToLastScenario`로 온/오프 | 편의성 비교 후 결정 |
| 6 | NestedScrollRect 미도입 | 2번 결정으로 불필요 |
| 7 | 기존 선택 API·임시 디버그 GUI는 주석 처리, 정식 UI 완료 후 삭제 | |

### 구현 / 수정 내역
- **ScenarioListItem (신규)**: `Setup(entry, defaultThumbnail, onPlayAll, onPlayAct)` — 썸네일/제목(에디터에서 hidden은 `[hidden]` 표기), `ApplyTags`/`ApplyCharacters`(빈 함수, 추후 시나리오 데이터 파일 기반으로 TagArea/CharacterIconArea에 프리팹 생성), ActListItem_All 연결, 막 수만큼 ActListItem 생성 후 `LayoutRebuilder.ForceRebuildLayoutImmediate`, 행이 Min Height까지 줄어도 넘치면 경고
- **ActListItem (신규)**: `Setup(title, onPlay)` — 제목 갱신(null이면 프리팹 문구 유지), PlayButton 리스너 코드 연결 (인스펙터 OnClick 미사용 — 중복 실행 방지)
- **ScenarioSelectController**: 목록 UI 필드(`scenarioScrollRect`, `scenarioItemPrefab`, `defaultThumbnail`, `restoreScrollToLastScenario`), `Start()`에서 `BuildList()` + `RestoreScrollToLastScenario()`, `PlayAll(entry)` / `PlayAct(entry, index)` → `SceneTransitionManager.GoToCommunicationScene`. 기존 선택 API(`OnSelectionChanged`, `Selected*`, `SelectScenario/SelectAct/StartSelected/StartPlayAll`), `GetDisplayName`, `showDebugGUI`/`OnGUI` 주석 처리
- **ScenarioCatalog**: `ScenarioEntry.scenarioTitle`, `actTitles`, `DisplayNameOrFolder`, `GetActTitle(i)`, `ScenarioCatalog.ThumbnailRoot`
- **ScenarioCatalogSync**: 막 XML 루트의 mainTitle/subTitle 읽기(`ReadActHeader`, 파싱 실패 시 경고), 막/제목 변경 감지 갱신 (`ScanResult`)
- **ActXml**: `MainTitleAttr`, `SubTitleAttr` 상수

### 프리팹 설정 확인 결과 (코드 반영 시점, 수정 필요)
- `ScenarioListItem/ActListScrollView`(막 목록) VerticalLayoutGroup: Control Child Size Height ✘ / Force Expand Height ✔ → 행 자동 축소가 동작하지 않음 → **Height ✔ / ✘로 변경**
- `ActListItem_All`에 LayoutElement 없음 → ActListItem과 동일하게 Min 40 / Preferred 80 / Flexible 0 추가
- PlayButton: Anchor Y 0.5 고정 높이 60 → 행이 줄어도 버튼이 안 줄어듦 → Anchor Y 0~1 + Top/Bottom 여백
- ActTitle: Anchor Y 0.5 고정 높이 80 → Anchor Y 0~1(세로 늘이기)로 해야 Auto Size가 행 높이에 맞춰 축소

### 테스트 필요
- [ ] Unity 컴파일 에러 없음, `Tools > Scenario > Sync Catalog` 후 카탈로그에 `Scenario Title`/`Act Titles` 필드 표시
- [ ] 목록 씬: 시나리오 3개 생성, 제목 없음 경고(현재 XML 대부분 제목 없음 — 정상), 썸네일 없으면 기본 썸네일
- [ ] IL(5행) 행 높이 자동 축소(약 61), NKS/SC(2행)는 80 유지
- [ ] 모두 재생 / 막 PlayButton 즉시 재생, 재생 범위 규칙 동일
- [ ] 복귀 시 직전 시나리오 위치로 스크롤 (옵션 On/Off 비교)
- [ ] 휠/드래그 스크롤이 막 목록 위에서도 정상 동작

</details>

<details>
<summary><b>2026-09-30</b> · [시나리오] 재생 범위 규칙(모두 재생/단일 막) + 엔딩 좌클릭 스킵·목록 복귀 + ESC 강제 종료 + 녹화 도구 재생 시작 신호 대응 — <i>테스트 전</i></summary>

### 2026-09-30 — [시나리오] 재생 범위 규칙, 엔딩 스킵/복귀, ESC 강제 종료, 녹화 도구 대응

- 커밋: 미커밋 (3단계 커밋 이후 작업)
- 변경 파일: `Recording/RecordingSignal.cs`, `Scenario/ScenarioSelection.cs`, `ScenarioPlayer.cs`, `Ending/EndingSceneController.cs`, `Editor/VNRecorderTool.cs`, `Scenario/ScenarioSelectController.cs`
- 상태: **Unity 컴파일 및 플레이/녹화 테스트 전**

### 결정 사항
| # | 결정 | 이유 |
|---|---|---|
| 1 | **재생 범위 규칙**: 목록의 "모두 재생"으로 시작할 때만 전체 막 → 엔딩 씬. 특정 막(첫 막 포함)을 고르면 그 막만 재생 후 목록 씬 | 통 재생 + 엔딩 연결은 모두 재생 버튼으로만. CommunicationScene 직접 Play(선택값 없음)도 모두 재생 |
| 2 | "특정 막부터 끝까지 이어서 재생"은 지원하지 않음 | 규칙 단순화 (필요 시 별도 버튼으로 추가) |
| 3 | 단일 막 재생 중 PageUp/PageDown 디버그 이동 허용, 어느 막에서 끝나든 목록 복귀 | 디버그 기능 유지 |
| 4 | 목록 GUI "처음부터" → "모두 재생", `StartFromBeginning()` → `StartPlayAll()` | 기획 용어와 일치 |
| 5 | 엔딩 씬 좌클릭 스킵, 엔딩 시작 후 1초(`skipInputDelay`, 인스펙터 조절)는 클릭 무시 | 엔딩에는 상호작용 버튼이 없음. 대사 연타 클릭이 엔딩 즉시 스킵으로 이어지는 것 방지 |
| 6 | 녹화 파일명: 모두 재생 `Recordings/{시나리오}/ALL_{타임스탬프}.mp4`, 단일 막 `Recordings/{시나리오}/{막}_{타임스탬프}.mp4` | 파일만 보고 범위 구분 |
| 7 | 한 Play 안에서 재생할 때마다 녹화 파일 1개 (목록 복귀 후 다른 막 재생 시 새 파일) | 막 단위 분할 녹화 편의 |
| 8 | ESC: 막 재생 중 강제 종료 → 녹화 종료 신호 → 목록 씬 | 테스트/녹화 중단 편의 |
| 9 | 녹화 시작은 Play 진입이 아닌 ScenarioPlayer 재생 시작 신호 | 목록 씬 녹화 방지, 녹화 시점에 UIManager 존재 보장, 시나리오명을 런타임 선택값으로 |
| 10 | 녹화 신호는 `RecordingSignal` 사용 (`SceneTransitionManager.OnBeforeLoad` 미사용) | OnBeforeLoad는 Play마다 초기화되어 에디터 도구 구독이 사라짐 |

### 구현 / 수정 내역

#### 1. RecordingSignal
- `OnRecordingStartRequested(string scenarioFolder, string actName)` 이벤트 + `RequestStart()` 추가 (actName null = 모두 재생)

#### 2. ScenarioSelection
- `IsPlayAll` 속성 추가 (`StartActName`이 비어 있으면 true)

#### 3. ScenarioPlayer
- `Start()`: 재생 직전 `RecordingSignal.RequestStart(시나리오, 모두 재생이면 null / 단일이면 시작 막 이름)`
- `OnActEnd()`: 단일 막이면 `RecordingSignal.RequestStop()` → `GoToScenarioSelectScene()`, 모두 재생이면 기존대로 다음 막 → 엔딩
- `Update()`: ESC → `ForceReturnToScenarioSelect()` (`DebugResetState` + 보이스/오디오 정지 → 녹화 종료 신호 → 목록 씬, `isExiting`으로 중복 방지)
- 로그 "시작 막: 처음부터" → "재생 범위: 모두 재생"

#### 4. EndingSceneController
- `Update()`: 좌클릭 → `FinishEnding()` (유예 시간 이후)
- `FinishEnding()`: `_isFinished`로 1회 실행 보장, 타이머 코루틴 중단, 기존 정리 + 녹화 종료 신호 → `SceneTransitionManager.GoToScenarioSelectScene()`
- `StartEnding()`: 스킵 상태/시작 시각 초기화

#### 5. VNRecorderTool
- `EnteredPlayMode` 자동 시작 제거, `OnRecordingStartRequested` 구독 → 녹화 모드일 때만 (남은 녹화 정리 후) 시작 + 자동재생 On
- 인스펙터 값(SerializedObject)으로 시나리오명을 읽던 `GetScenarioName()` 삭제 → 신호의 시나리오/막 이름 사용 (`SanitizeFileName`)
- Play 종료 시 정리(`ExitingPlayMode` → StopRecording)는 유지

#### 6. ScenarioSelectController
- `StartPlayAll()`, 디버그 GUI "모두 재생" 표기 및 재생 범위 설명

### 녹화 상태 실시간 조회 (후순위 — 녹화 표시 UI용 메모)
- 현재는 런타임 코드에서 조회 불가: 녹화 모드 여부(EditorPrefs)와 실제 녹화 여부(`RecorderController.IsRecording`)가 모두 에디터 전용 `VNRecorderTool` 내부에 있고, 런타임 어셈블리는 에디터 어셈블리를 참조할 수 없음
- 구현 방안: `RecordingSignal`(런타임)에 `IsRecordingModeEnabled` / `IsRecording` 정적 상태 + `OnRecordingStateChanged` 이벤트를 두고, `VNRecorderTool`이 모드 토글·녹화 시작/종료 시점에 값을 갱신 → UI는 `RecordingSignal.IsRecording`만 읽으면 됨 (빌드에서는 항상 false)
- 주의: 녹화 모드 여부는 에디터 로드/토글 시점에 한 번 밀어 넣어야 함(Domain Reload 비활성이라 static 값이 유지되는 점 고려)

### 보류 (후순위)
- CommunicationScene 첫 번째로 로드되는 CG의 Y값이 제자리를 찾지 못함 (두 번째 이후 로드되는 CG는 정상) — 원인 미조사
- 녹화 중 표시 UI (위 조회 방안 기반)

### 테스트 필요
- [ ] Unity 컴파일 에러 없음
- [ ] 목록 → IL "모두 재생": IL01 → IL04 → 엔딩 → (30초 또는 1초 이후 좌클릭) → 목록 씬, 목록에서 IL이 선택된 상태로 복원
- [ ] 목록 → IL03 선택: IL03만 재생 후 바로 목록 씬 (엔딩 없음), IL01 선택도 IL01만 재생
- [ ] 엔딩 시작 직후(1초 이내) 클릭은 무시, 이후 클릭 시 즉시 목록 복귀
- [ ] 막 재생 중 ESC → 목록 씬 (백로그 열린 상태에서도 멈춤 없이 복귀)
- [ ] 녹화 모드: 목록 화면은 녹화되지 않고 재생 시작 시 녹화 시작 + 자동재생 On
- [ ] 녹화 파일: 모두 재생 `Recordings/IL/ALL_…mp4`(엔딩 포함), 단일 막 `Recordings/IL/IL03_…mp4`, ESC 강제 종료 시에도 파일 저장
- [ ] 한 Play에서 연속 녹화(IL01 → 목록 → IL02) 시 파일 2개 정상, 목록 복귀 후 정상 속도
- [ ] CommunicationScene 직접 Play → 모두 재생 → 엔딩 → 목록 씬

</details>

<details>
<summary><b>2026-09-30</b> · [시나리오] 3단계: SceneTransitionManager(씬 전환 단일 창구) + 목록 씬 컨트롤러(임시 디버그 GUI) — <i>테스트 전</i></summary>

### 2026-09-30 — [시나리오] 3단계: 씬 전환 매니저 + ScenarioSelectController

- 커밋: 미커밋 (2단계 커밋 이후 작업)
- 변경 파일: 신규 `Assets/Scripts/SceneFlow/SceneTransitionManager.cs`, 신규 `Assets/Scripts/Scenario/ScenarioSelectController.cs` / 수정 `Assets/Scripts/ScenarioPlayer.cs`
- 사용자 직접 작업(Unity): `ScenarioSelectScene`에 GameObject + `ScenarioSelectController` 배치
- 상태: **Unity 컴파일 및 플레이 테스트 전**

### 결정 사항
| # | 결정 | 이유 |
|---|---|---|
| 1 | 전환 함수 이름은 도착 씬 이름 + `Scene`: `GoToScenarioSelectScene()` / `GoToCommunicationScene(folder, startAct)` / `GoToEndingScene()` | 도착지가 Unity 씬임을 이름으로 명확히 |
| 2 | 매니저는 static (MonoBehaviour/DontDestroyOnLoad 아님) | 특정 씬 배치에 의존하지 않아 어느 씬에서 Play해도 동작. 페이드/비동기 로드는 추후 내부 확장으로 대응(호출부 불변) |
| 3 | 목록 씬 정식 GUI 전까지 OnGUI 임시 디버그 GUI 사용 (`showDebugGUI`로 끔) | GUI 제작 전에 시나리오 전환부터 검증 |

### 구현 / 수정 내역

#### 1. SceneTransitionManager (신규, `UsoSCTheater.SceneFlow`)
- `SceneId { ScenarioSelect, Communication, Ending }` + `GetSceneName()` — Unity 씬 이름은 이곳에서만 관리
- `Load(SceneId)` 공통 처리: 연타 방지(`IsLoading`, `sceneLoaded`에서 해제), 이름 미등록/Build Settings 누락 시 에러 로그 후 취소, `Time.timeScale = 1` 복구(빌드 백로그 일시정지 대응), `OnBeforeLoad` 이벤트(녹화 등 공통 훅)
- `GoToCommunicationScene`: 전환 가능 여부를 먼저 검사한 뒤 `ScenarioSelection.Select` → 실패 시 선택값 불변
- Domain Reload 비활성 대응: `SubsystemRegistration`에서 상태/이벤트 초기화 + `sceneLoaded` 중복 구독 방지
- **규칙**: `UnityEngine.SceneManagement.SceneManager.LoadScene`은 이 클래스 밖에서 호출 금지 (반영 후 검사: 매니저 1곳만 존재)

#### 2. ScenarioSelectController (신규, `UsoSCTheater.Scenario`)
- 카탈로그 로드 → 표시 목록(hidden은 에디터에서만 표시, 막 0개 시나리오 제외) → 직전 선택 복원
- GUI 연결용 public API: `Scenarios`, `SelectedScenario`, `SelectedScenarioIndex`, `SelectedActIndex`(-1 = 처음부터), `SelectScenario(int)`, `SelectScenarioByFolder(string)`, `SelectAct(int)`, `StartSelected()`, `StartFromBeginning()`, 이벤트 `OnSelectionChanged`
- 시작은 `SceneTransitionManager.GoToCommunicationScene(folder, 막 파일명)` — 시작 막은 인덱스가 아닌 파일명 전달
- 임시 디버그 GUI: 1080p 기준 스케일, 시나리오/시작 막 선택 + 시작 버튼

#### 3. ScenarioPlayer
- **변경 위치**: `OnActEnd()` 마지막 막 이후 `LoadScene("EndingScene")` → `SceneTransitionManager.GoToEndingScene()`

### 고려 사항
- 엔딩 종료 → 목록 복귀(`EndingSceneController.FinishEnding` → `GoToScenarioSelectScene`)는 다음 단계. 현재는 엔딩 후 정지 상태 유지
- 녹화 모드로 목록 씬에서 Play 시 목록 화면부터 녹화 → 녹화 도구 대응 전까지는 CommunicationScene 직접 Play로 녹화
- 중간 막부터 시작 시 이전 막의 BGM/CG 상태 없음 (PageUp 디버그와 동일)

### 테스트 필요
- [ ] Unity 컴파일 에러 없음
- [ ] ScenarioSelectScene Play → 임시 GUI에 IL(4막) / NKS(1막) / SC(1막) 표시
- [ ] IL → 처음부터 → 시작: `[SceneTransitionManager] ScenarioSelectScene → CommunicationScene`, "선택된 시나리오: IL (시작 막: 처음부터)", IL01부터 재생
- [ ] IL → IL03 → 시작: IL03부터 재생
- [ ] 마지막 막 종료 → `CommunicationScene → EndingScene` 로그와 함께 엔딩 전환
- [ ] 시작 버튼 연타 시 "전환 중이라 … 무시" 경고만, 씬 1회 로드
- [ ] Play 종료 후 CommunicationScene 직접 Play → "선택값 없음" 로그 (static 초기화)

</details>

<details>
<summary><b>2026-09-30</b> · [용어 정리] 2단계: 데이터 폴더 Resources/Scene → Resources/Scenario, 경로 상수 2곳 수정 — <i>테스트 전</i></summary>

### 2026-09-30 — [용어 정리] 2단계: 시나리오 데이터 폴더 이름 변경

- 커밋: 미커밋 (1단계 커밋 이후 작업)
- 사용자 직접 변경(Unity): `Assets/Resources/Scene` → `Assets/Resources/Scenario` (하위 IL / NKS / SC, GUID 유지)
- 변경 파일: `Scenario/ScenarioCatalog.cs`, `Editor/ScenarioCatalogSync.cs` / 주석만: `Scenario/ScenarioSelection.cs`, `ScenarioPlayer.cs`
- 상태: **Unity 컴파일 및 플레이 테스트 전**

### 구현 / 수정 내역
- **변경 위치**: `ScenarioCatalog.ScenarioRoot` `"Scene"` → `"Scenario"`, `ScenarioCatalogSync.ScenarioRootFolder` `"Assets/Resources/Scene"` → `"Assets/Resources/Scenario"`
- 경로 예시 주석 `Scene/IL` → `Scenario/IL` (ScenarioSelection.GetScenarioPath, ScenarioPlayer.activeScenarioPath, ScenarioCatalogSync 클래스 설명)
- **고려 사항**: 두 상수는 항상 일치해야 함(런타임 로드 경로 / 에디터 동기화 경로). 인스펙터 `defaultScenarioFolder` 값이 `Scene/NKS`로 남아 있어도 마지막 조각(`NKS`)만 사용하므로 동작에 영향 없음 — 정리 시 `NKS`로 변경 권장.

### 테스트 필요
- [ ] Unity 컴파일 에러 없음
- [ ] `Tools > Scenario > Sync Catalog` 로그: IL[4], NKS[1], SC[1] (displayName/hidden 유지)
- [ ] CommunicationScene 직접 Play → "경로에 막 파일이 없습니다" 에러 없이 NKS 재생, 보이스 정상
- [ ] 인스펙터 `defaultScenarioFolder`를 `IL` / `SC`로 바꿔 각각 재생 확인

</details>

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
<summary><b>2026-09-29</b> · [설계 검토] 시나리오별 보이스 폴더 분리(루트 폴백) + 시나리오 선택 기능 방향 확정 — <i>정식 목록 UI까지 반영 (태그/등장인물·임시 GUI 삭제 남음)</i></summary>

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
