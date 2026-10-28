# SC Resource Grabber

WebView2를 내장한 WPF 프로그램. 게임을 프로그램 안에서 실행하면, 네트워크로 받은 리소스가 자동으로 오른쪽 목록에 쌓인다. DevTools를 열 필요가 없다.

## 빌드 / 실행
```
dotnet run                     # 개발 중 실행
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```
- .NET 10 SDK, WebView2 런타임 필요

## 구조
| 파일 | 역할 |
|---|---|
| `MainWindow.xaml(.cs)` | UI: 왼쪽 게임(WebView2), 오른쪽 분류 필터·목록·저장, 단축키 처리 |
| `Services/ResourceCapture.cs` | `WebResourceResponseReceived`로 응답 본문 캡처 → 세션 캐시 파일 기록 |
| `Services/ResourceClassifier.cs` | URL 확장자 / 매직 바이트 / 텍스트 패턴 / Content-Type으로 분류 |
| `Services/ResourceSaver.cs` | 저장 폴더 바로 아래에 파일명으로 저장, 동일 파일 건너뛰기, 이름 충돌 처리 |
| `Services/AppSettings.cs` | `%AppData%\SCResourceGrabber\settings.json` |
| `Models/CapturedResource.cs` | 캡처 항목 모델 (썸네일 지연 로딩) |
| `ResourceDetailWindow.xaml(.cs)` | 리소스 상세 창 |
| `TrackingWindow.xaml(.cs)`, `Models/TrackedItem.cs` | 추적 모드 창 / 항목 |
| `Services/PageAudioMonitor.cs`, `Scripts/audio_hook.js` | 오디오 재생 추적 (스크립트 주입 + 메시지 수신) |
| `Services/CategoryFilterBar.cs` | 분류 필터 체크박스 줄 ([전체] 포함, 메인·추적 창 공용) |
| `Services/HotkeyMap.cs` | 단축키 (동작 목록·기본값·키 해석) |
| `Services/ListViewSorter.cs` | 목록 머리글 클릭 정렬 (종류·크기·받은 시각) |
| `DownloadWindow.xaml(.cs)`, `Models/DownloadItem.cs` | 다운로드 창 / 항목 (미리보기·이름 붙이기·가공 저장) |
| `Services/ImageProcessor.cs` | PNG 변환, 투명 여백 자르기, 정사각형 가운데 정렬, 여백(%) |
| `Services/NameCatalog.cs`, `names.txt` | 이름 자동 완성 목록 (exe 옆 `names.txt`, `#` 뒤는 주석) |

## 주요 기능
- 분류: 이미지 / 오디오 / 비디오 / JSON / 폰트 / 암호화 / 기타 (선언 순서 = 필터·정렬 순서)
  - **암호화**: 게임 전용 암호화 형식(앞 4바이트 `5D AC 43 59`) 또는 알려진 형식이 아닌 무작위 바이너리. 게임의 Spine atlas·json도 이 형식으로 내려옴
  - Spine 분류·Spine 수집 창은 2026-10-28 삭제 (Spine 관리는 TE). 평문 atlas·skel은 기타, skeleton json은 JSON
- 목록 머리글 "종류·크기·받은 시각" 클릭 → 오름차순 ▲ / 내림차순 ▼ / 해제 (메인·추적 창)
- 저장: 저장 폴더 바로 아래에 URL의 파일명으로 (같은 내용이면 건너뜀, 다르면 번호를 붙임 — [같은 이름 덮어쓰기] 꺼짐일 때)
- **음소거**: 주소 줄 [이동]과 [DevTools] 사이 버튼 / Ctrl+M — 게임 화면(WebView2) 소리만 끔, 상태는 `Muted`로 기억. 출력만 끄므로 추적 창의 재생 기록·상세 창 미리 듣기는 그대로
- 목록 더블클릭/Enter → 리소스 상세 창 (창 크기에 맞춰 표시, 1:1 원본 보기, 오디오·비디오 재생)
- **추적 모드**: [● 추적 모드] → 추적 창이 열려 있는 동안 새로 받은 리소스와 재생된 오디오를 URL 단위로 모아 표시 (재생 중 강조)
  - 오디오 재생은 `Scripts/audio_hook.js`(페이지 생성 시 주입)가 fetch/XHR 응답 → decodeAudioData → 재생을 추적해서 알림
  - 진단 로그: `%LocalAppData%\SCResourceGrabber\audio_hook.log` (실행마다 새로 씀)
- **다운로드 창**: 체크한 항목을 [다운로드 창으로 ▶](Ctrl+D)로 보냄 (메인·추적 창이 같은 창 하나를 씀, 같은 항목(Id 또는 URL)은 한 번만)
  - 항목마다 미리보기(체커 배경) + 저장 이름 입력. 비워 두면 원본 이름. `names.txt` 기반 자동 완성, 이름 겹침 경고
  - 목록 클릭 → 포커스는 목록에 (Delete → 목록에서 빼기, Enter → 이름 칸으로, 글자를 바로 치면 이름 칸으로 넘어가 그 글자부터 입력·기존 이름 덮어씀)
  - 이름 칸에서 Enter → 이름 확정 후 다음 항목 (이름 칸에 남음)
  - 옵션: PNG로 변환(webp 등) / 투명 여백 자르기 / 정사각형(가운데 정렬) / 여백 %(긴 변 기준). 자르기·정사각형·여백 중 하나라도 켜면 PNG로 저장
  - 저장 폴더는 직접 지정 가능(`DownloadSaveFolder`), [덮어쓰기] 꺼짐이면 같은 이름 파일은 "이미 있음"으로 건너뜀
  - 옵션·폴더·덮어쓰기는 마지막 설정을 `settings.json`에 기억
  - 저장할 이름끼리 겹치면 저장하지 않고 "이름 겹침" 표시
  - 메인 창에서 목록을 비워도 다운로드 창 목록은 남음 (그 항목의 캐시 파일은 지우지 않고, 종료 시 세션 캐시와 함께 삭제)

## 단축키
`settings.json`의 `"Hotkeys"`에서 바꿀 수 있음 (수정 후 프로그램 재시작, 빈 문자열이면 해제). 글자 입력 칸에 포커스가 있을 때는 동작하지 않음.

| 동작 이름 | 기본 키 | 메인 창 | 추적 창 | 상세 창 | 다운로드 창 |
|---|---|---|---|---|---|
| `SaveChecked` | Ctrl+S | 체크한 항목 저장 | 체크한 항목 저장 | 이 리소스 저장 | 모두 저장 (입력 중에도 동작) |
| `ToggleTracking` | Ctrl+R | 추적 창 열기/닫기 | 추적 창 닫기 | - | - |
| `ClearList` | Ctrl+L | 목록 비우기 | 목록 비우기 | - | - |
| `SendToDownload` | Ctrl+D | 체크한 항목을 다운로드 창으로 | 같음 | - | - |
| `ToggleMute` | Ctrl+M | 게임 소리 끄기/켜기 | 같음 | - | - |

목록 키 (바꿀 수 없음, 메인·추적 창 목록에 포커스가 있을 때):
- Space: 선택한(파란색) 항목 체크/해제 — 하나라도 체크가 안 돼 있으면 모두 체크, 다 돼 있으면 모두 해제. 버튼 [선택 항목 체크]와 같음
- Enter / 더블클릭: 상세 창
- Ctrl+A: 전체 선택 (목록 기본 동작), Shift·Ctrl+클릭: 여러 개 선택

없앤 동작: `ToggleAllChecks`(보이는 항목 전체 체크, 2026-10-28) — settings.json에 남아 있으면 읽을 때 지움 (`HotkeyMap.Removed`)

새 동작 추가: `HotkeyMap`에 이름·기본값 한 줄 추가 → 각 창 `OnPreviewKeyDown`의 switch에 처리 추가 (기존 settings.json에는 기본값이 자동으로 채워짐)

## exe 갱신
- 실행 중인 exe는 덮어쓸 수 없음 → 프로그램을 정상 종료(창 닫기)한 뒤 publish 결과를 이 폴더로 복사. 강제 종료는 하지 않음

## 데이터 위치
- 로그인 프로필: `%LocalAppData%\SCResourceGrabber\Profile` (지우면 로그아웃)
- 세션 캐시: `%LocalAppData%\SCResourceGrabber\Cache` (종료 시 삭제)
- 설정: `%AppData%\SCResourceGrabber\settings.json` (`HostFilter`로 캡처 호스트 제한 가능, `;` 구분)

## 알려진 제한
- 206(부분 응답)으로 스트리밍되는 미디어는 현재 건너뜀
- Service Worker가 직접 받는 요청은 캡처되지 않을 수 있음
- 오디오 미리보기는 Windows 미디어 재생 기반 (ogg는 코덱이 없으면 재생 불가, 저장은 정상)
