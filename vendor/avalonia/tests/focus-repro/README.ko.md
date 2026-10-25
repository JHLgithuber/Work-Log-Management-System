# 브라우저 조합 포커스 회귀 최소 앱

일반 입력·암호 입력·여러 줄 입력만 있는 독립 합성 재현본이다. 실제 계정이나 서버 연결은 없다.

저장소 루트의 NuGet.Config를 사용한다.

```sh
dotnet publish vendor/avalonia/tests/focus-repro/Minimal.csproj -c Release -o focus-publish
python3 -m http.server 8080 --directory focus-publish/wwwroot
```

브라우저로 열고 개발자 도구에서 `runFocusRegression()`을 실행한다. 기대값은 18/18이다. 조합 중 정역방향 필드 이동, 확정 후 이동, 선택 교체, 읽기전용·비활성화, 이전 DOM의 지연 종료가 새 필드를 오염시키지 않는지 검사한다. 공식 비교본은 별도 출력 폴더에 `-p:BrowserVersion=11.3.22`로 게시한다.

이 합성 검사는 실제 IME를 대체하지 않는다. 실제 모바일 키보드로 일반 칸 입력 후 암호 칸 탭, 반대 방향, 한글·영문, 여러 줄 칸 전환을 별도로 확인해야 한다. 비밀번호는 합성 문자열만 입력한다. 실제 업무일지 로그인은 격리 서버의 일반 QA 계정으로 화면 키 탭만 사용하고 로그인 후 화면 진입까지 확인한다.
