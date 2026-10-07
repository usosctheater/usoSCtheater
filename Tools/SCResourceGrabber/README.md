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
| `Services/ResourceClassifier.cs` | URL 확장자 / 매직 바이트 / 텍스트 패턴 / Content-Type으로 분류 (Spine atlas·skeleton json 포함) |
| `Services/ResourceSaver.cs` | 저장 폴더 바로 아래에 파일명으로 저장, 동일 파일 건너뛰기, 이름 충돌 처리 |
| `Services/AppSettings.cs` | `%AppData%\SCResourceGrabber\settings.json` |
| `Models/CapturedResource.cs` | 캡처 항목 모델 (썸네일 지연 로딩) |
| `ResourceDetailWindow.xaml(.cs)` | 리소스 상세 창 |
| `TrackingWindow.xaml(.cs)`, `Models/TrackedItem.cs` | 추적 모드 창 / 항목 |
| `Services/PageAudioMonitor.cs`, `Scripts/audio_hook.js` | 오디오 재생 추적 (스크립트 주입 + 메시지 수신) |
| `Services/CategoryFilterBar.cs` | 분류 필터 체크박스 줄 ([전체] 포함, 메인·추적 창 공용) |
| `Services/HotkeyMap.cs` | 단축키 (동작 목록·기본값·키 해석) |
| `Services/ListViewSorter.cs` | 목록 머리글 클릭 정렬 (종류·크기·받은 시각) |

## 주요 기능
- 분류: 이미지 / 오디오 / 비디오 / Spine / JSON / 폰트 / 암호화 / 기타 (선언 순서 = 필터·정렬 순서)
  - **암호화**: 게임 전용 암호화 형식(앞 4바이트 `5D AC 43 59`) 또는 알려진 형식이 아닌 무작위 바이너리. 게임의 Spine atlas·json도 이 형식으로 내려옴
- 목록 머리글 "종류·크기·받은 시각" 클릭 → 오름차순 ▲ / 내림차순 ▼ / 해제 (메인·추적 창)
- **Spine 수집 창**: [Spine 수집] → 같은 프로그램 창을 하나 더 열어 `SpineStartUrl`(기본 https://spine.shinycolors.moe/)에서 수집. 게임 창과 달리 Spine 분류 표시, 저장 시 URL 경로 폴더 유지(`SpineSaveFolder`, 기본 `D:\usosctheater\resource\Grabber\Spine`), 오디오 추적 없음
- 목록 더블클릭/Enter → 리소스 상세 창 (창 크기에 맞춰 표시, 1:1 원본 보기, 오디오·비디오 재생)
- **추적 모드**: [● 추적 모드] → 추적 창이 열려 있는 동안 새로 받은 리소스와 재생된 오디오를 URL 단위로 모아 표시 (재생 중 강조)
  - 오디오 재생은 `Scripts/audio_hook.js`(페이지 생성 시 주입)가 fetch/XHR 응답 → decodeAudioData → 재생을 추적해서 알림
  - 진단 로그: `%LocalAppData%\SCResourceGrabber\audio_hook.log` (실행마다 새로 씀)

## 단축키
`settings.json`의 `"Hotkeys"`에서 바꿀 수 있음 (수정 후 프로그램 재시작, 빈 문자열이면 해제). 글자 입력 칸에 포커스가 있을 때는 동작하지 않음.

| 동작 이름 | 기본 키 | 메인 창 | 추적 창 | 상세 창 |
|---|---|---|---|---|
| `SaveChecked` | Ctrl+S | 체크한 항목 저장 | 체크한 항목 저장 | 이 리소스 저장 |
| `ToggleTracking` | Ctrl+R | 추적 창 열기/닫기 | 추적 창 닫기 | - |
| `ToggleAllChecks` | Ctrl+A | 보이는 항목 전체 체크/해제 | 같음 | - |
| `ClearList` | Ctrl+L | 목록 비우기 | 목록 비우기 | - |

새 동작 추가: `HotkeyMap`에 이름·기본값 한 줄 추가 → 각 창 `OnPreviewKeyDown`의 switch에 처리 추가 (기존 settings.json에는 기본값이 자동으로 채워짐)

## exe 갱신
- 실행 중인 exe는 덮어쓸 수 없음 → 프로그램을 종료한 뒤 publish 결과를 이 폴더로 복사

## 데이터 위치
- 로그인 프로필: `%LocalAppData%\SCResourceGrabber\Profile` (지우면 로그아웃)
- 세션 캐시: `%LocalAppData%\SCResourceGrabber\Cache` (종료 시 삭제)
- 설정: `%AppData%\SCResourceGrabber\settings.json` (`HostFilter`로 캡처 호스트 제한 가능, `;` 구분)

## 알려진 제한
- 206(부분 응답)으로 스트리밍되는 미디어는 현재 건너뜀
- Service Worker가 직접 받는 요청은 캡처되지 않을 수 있음
- 오디오 미리보기는 Windows 미디어 재생 기반 (ogg는 코덱이 없으면 재생 불가, 저장은 정상)
