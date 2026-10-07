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
| `MainWindow.xaml(.cs)` | UI: 왼쪽 게임(WebView2), 오른쪽 분류 필터·목록·미리보기·저장 |
| `Services/ResourceCapture.cs` | `WebResourceResponseReceived`로 응답 본문 캡처 → 세션 캐시 파일 기록 |
| `Services/ResourceClassifier.cs` | URL 확장자 / 매직 바이트 / 텍스트 패턴 / Content-Type으로 분류 (Spine atlas·skeleton json 포함) |
| `Services/ResourceSaver.cs` | 저장 경로 생성, 동일 파일 건너뛰기, 이름 충돌 처리 |
| `Services/AppSettings.cs` | `%AppData%\SCResourceGrabber\settings.json` |
| `Models/CapturedResource.cs` | 캡처 항목 모델 (썸네일 지연 로딩) |

## 데이터 위치
- 로그인 프로필: `%LocalAppData%\SCResourceGrabber\Profile` (지우면 로그아웃)
- 세션 캐시: `%LocalAppData%\SCResourceGrabber\Cache` (종료 시 삭제)
- 설정: `%AppData%\SCResourceGrabber\settings.json` (`HostFilter`로 캡처 호스트 제한 가능, `;` 구분)

## 알려진 제한
- 206(부분 응답)으로 스트리밍되는 미디어는 현재 건너뜀
- Service Worker가 직접 받는 요청은 캡처되지 않을 수 있음
- 오디오 미리보기는 Windows MediaPlayer 기반 (ogg는 코덱이 없으면 재생 불가, 저장은 정상)
