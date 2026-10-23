# 브라우저 UI와 API 통합 배포

저장소 루트의 `Dockerfile`은 Avalonia WebAssembly와 FastAPI를 한 이미지로
묶습니다. Synology Web Station은 기존과 같이 전체 사이트를 컨테이너의
`8000` 포트로 전달하면 됩니다. `fastapi_backend/Dockerfile`은 API 전용
빌드로 유지되며 브라우저 사이트 업데이트에 사용하면 안 됩니다.

## 빌드 계약

- 빌드 컨텍스트: 저장소 루트. `.dockerignore`는 필요한 소스만 허용하고
  `.env*`, DB, 인증서/키, `bin`, `obj` 등은 제외합니다. 운영 설정이나 데이터는
  소스 폴더에 복사하지 않습니다.
- 브라우저 빌드: .NET SDK `10.0.402`, workload set `10.0.402.1`의 `wasm-tools`,
  네이티브 WASM 링크를 유지한 Release publish.
- `dotnet publish -o /out/browser`의 `wwwroot` 전체를 `/app/web`에 복사합니다.
  `index.html`, `_framework/dotnet.js`, WASM 파일이 없으면 빌드를 실패시킵니다.
- Python 3.12, `uv==0.13.4`, 기존 `uv.lock`을 이용해 API 의존성을 설치합니다.
  실행 이미지는 .NET SDK 없이 기존과 같이 비루트 `app` 사용자로 실행합니다.
- API, `/health`, `/docs`, `/openapi.json`을 먼저 등록하고 `/` 정적 파일 마운트를
  마지막에 등록합니다. 등록된 API 경로의 슬래시 리디렉션과 잘못된 메서드 응답을
  보존하며 없는 경로를 `index.html`로 치환하지 않습니다.
- 브라우저 번들이 없으면 `/`는 `503`이며 API와 `/health`는 계속 작동합니다.
  `/health`의 `204`만으로 UI나 DB 연결까지 정상이라고 판단하지 않습니다.

```sh
# 저장소 루트에서 실행. Docker 빌드 장비는 NAS와 같은 CPU 아키텍처여야 합니다.
REVISION=$(git rev-parse HEAD)
IMAGE="worklog-web:$REVISION"
docker build --build-arg VCS_REF="$REVISION" -t "$IMAGE" .
docker image inspect "$IMAGE" --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}'
docker save "$IMAGE" > "worklog-web-$REVISION.tar"
```

다른 CPU 아키텍처에서 빌드한다면 Docker의 지원되는 cross-build 환경에서
NAS의 대상 플랫폼을 명시하고 검증해야 합니다. 이미지 base tag와 OS 패키지는
변할 수 있으므로 이 절차는 비트 단위 재현성을 보장하지 않습니다.

Docker 빌드에는 패키지 레지스트리 접근과 충분한 메모리/디스크 공간이 필요합니다.
WASM 네이티브 빌드는 MSBuild 작업 프로세스 간 통신을 사용합니다. 제한된 환경에서
Unix socket 접근이 거부되면 그 환경에서는 최종 번들을 검증할 수 없습니다.
`WasmBuildNative=false`, C#만의 Compile 성공, 기존 번들 재사용으로 대체하지 말고
정상 지원되는 Docker 빌드 장비에서 전체 빌드를 완료하세요.

## 운영 교체 전

1. 현재 이미지 ID/태그, Web Station 연결 대상, 포트, 재시작 정책, 사용자,
   네트워크, 환경변수 이름, `/app/data` 마운트를 기록합니다. 비밀값은 로그나
   저장소에 출력하지 않습니다. 기존 이미지를 롤백용으로 보존합니다.
2. 기존 MariaDB를 일관된 방식으로 백업하고 복구 가능성을 확인합니다. 외부
   MariaDB 사용 시 `/app/data` 백업만으로 업무 데이터가 백업되지 않습니다.
   SQLite 사용 시 DB 쓰기를 중지한 상태에서 해당 데이터를 백업합니다.
3. 운영 파일과 저장소의 차이를 검토합니다. 특히 `auth.py`, `auth_routes.py`,
   `settings.py`의 운영 전용 동작이 있으면 소스 호환성을 먼저 확인합니다.
   비밀값을 포함할 수 있는 운영 파일 전체를 무검토 복사하거나 공개하지 않습니다.
4. 테스트용 별도 DB와 비밀이 아닌 임시 설정을 사용해 새 이미지를 검증합니다.
   테스트 컨테이너를 운영 DB에 연결하지 마세요. 시작 시 DB 초기화/변환이 실행됩니다.
5. `/`, `/main.js`, 실제 `_framework/*.wasm`, `/health`, API 인증을 확인합니다.
   WASM의 Content-Type은 `application/wasm`이어야 합니다. 실제 브라우저에서
   로그인과 합의된 업무 흐름을 검증합니다. 합성 테스트 데이터만 사용합니다.

## Synology 운영 교체

Container Manager에서 새 태그의 이미지를 가져오거나 NAS에서 빌드합니다.
검증이 끝난 뒤에만 기존 컨테이너를 중지하고 새 이미지로 재생성합니다.
기존 Web Station 연결, 컨테이너 포트 `8000`, 재시작 정책, 네트워크 및
`/app/data`의 NAS 바인드 마운트를 유지합니다. 동일 호스트 포트를 사용하는
새 컨테이너와 기존 컨테이너를 동시에 시작하지 않습니다.

실행 시 기존 `WORKLOG_DATABASE_URL`과 `WORKLOG_JWT_SECRET_KEY`를 유지하고,
관리자 설정과 기타 환경변수도 검토 후 그대로 전달합니다. JWT 키를 새로 생성하면
기존 로그인 토큰이 무효화됩니다. DB URL을 누락하면 외부 MariaDB 대신 빈 SQLite를
사용할 수 있으므로 필수 확인 항목입니다. 환경변수/암호는 이미지 빌드 인수에
넣지 말고 기존 런타임 비밀 관리 경로로 전달하세요.

교체 후 공개 HTTPS 주소에서 새 UI와 API를 함께 확인합니다. 브라우저는 현재
페이지의 origin을 기본 API 서버로 사용하며 사용자가 명시적으로 선택한 서버
설정은 유지합니다. 데스크톱/모바일 기본 서버 주소에는 영향을 주지 않습니다.

오류 발생 시 새 컨테이너를 중지하고 이전 이미지와 기록한 실행 설정으로
되돌립니다. DB 변환이 수행되었다면 이미지 롤백만으로 데이터가 복구된다고
가정하지 말고 이전 코드와 현재 스키마의 호환성을 확인하세요. DB 복원은 이후의
업무 데이터를 잃을 수 있으므로 필요성과 복원 시점을 별도로 판단해야 합니다.

## 회귀 검사

```sh
cd fastapi_backend
uv sync --locked --group dev
uv run --no-sync pytest
```

정적 호스팅 검사는 임시 UI 파일과 격리된 SQLite DB로 실제 `main.py`를 실행해
라우트 순서, 인증 응답, WASM MIME, 없는 경로, 경로 이탈, 번들 누락을 검증합니다.
이 검사는 실제 WASM 렌더링이나 Synology 운영 환경의 검증을 대신하지 않습니다.

브라우저 기본 API 주소 테스트와 전체 프런트엔드 빌드 절차는
[`BUILDING.md`](avalonia_ui/WorkLogManagementSystem_UI/BUILDING.md)를 참고하세요.

공식 자료:
[Avalonia WebAssembly 배포](https://docs.avaloniaui.net/docs/deployment/webassembly),
[.NET WebAssembly 빌드 도구](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0).
