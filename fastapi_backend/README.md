# Work Log FastAPI 백엔드

업무일지 관리 시스템의 FastAPI 백엔드입니다. 아래 명령은 Docker 이미지를 Synology Container Manager 화면과 동일한 `worklog-fastapi:latest`라는 이름으로 빌드하고 tar 파일로 내보냅니다.

이 문서의 Dockerfile과 빌드 명령은 **API 전용**입니다. Web Station에서
브라우저 UI와 API를 한 컨테이너로 제공하려면 저장소 루트의 Dockerfile과
[통합 배포 안내](../DEPLOYMENT.md)를 사용하세요. API 전용 이미지의 `/`는
UI 번들이 없음을 나타내는 `503`을 반환하며 `/health`와 API는 계속 동작합니다.

## 주요 구성

- Python 3.12 / FastAPI / Uvicorn
- SQLite 기본 사용, MariaDB 연결 지원
- JWT 기반 인증
- 업무 및 일별 기록 관리
- Excel 내보내기
- 상태 확인: `GET /health`
- OpenAPI 문서: `/docs`

## 로컬 회귀 테스트

Python 3.12 이상과 uv를 사용합니다.

```bash
cd fastapi_backend
uv sync --locked --group test --no-install-package mariadb
uv run --no-sync pytest
```

SQLite 테스트만 실행하므로 MariaDB 네이티브 드라이버 설치는 생략합니다. MariaDB 서버를 실제로 사용할 환경은 일반 `uv sync`로 해당 드라이버까지 설치해야 합니다.

테스트는 `.env`를 읽지 않으며, 매 테스트마다 임시 디렉터리에 새 SQLite DB를 생성합니다. 저장소의 `worklog.sqlite3`나 실행 중인 API에는 접근하지 않습니다. 테스트 업무일자는 실행일 기준 365일 이후로 계산합니다.

계정과 업무 소유권의 재시작 후 보존, 레거시 스키마 변환, 업무·일별 기록·비고 CRUD, 사용자 간 격리, 과업 계층 변경·삭제, 날짜 검증과 한국 시간대 필터, 잘못된 인증 토큰, Excel 내보내기를 검증합니다.

## 준비 사항

- Docker Engine 또는 Docker Desktop
- Docker 데몬에 접근 가능한 사용자 권한
- 컨테이너 실행에 사용할 환경변수 파일

Docker 접근 여부를 확인합니다.

```bash
docker info
```

권한 오류가 발생하면 명령 앞에 `sudo`를 사용하거나 사용자를 Docker 그룹에 추가한 뒤 다시 로그인합니다.

```bash
sudo usermod -aG docker "$USER"
```

## 이미지 빌드 및 tar 내보내기

다음 블록을 터미널에 그대로 복사하여 실행합니다.

```bash
cd ~/PycharmProjects/WorkLogMgmtSys/fastapi_backend
sudo docker build -t worklog-fastapi:latest .
sudo docker save worklog-fastapi:latest > "$HOME/worklog-fastapi.tar"
ls -lh "$HOME/worklog-fastapi.tar"
```

Docker를 일반 사용자 권한으로 사용할 수 있는 환경에서는 두 명령의 `sudo`를 제거해도 됩니다.

명령의 각 이름은 다음 의미입니다.

| 항목 | 값 |
|---|---|
| 이미지 이름 | `worklog-fastapi` |
| 이미지 태그 | `latest` |
| 전체 이미지 참조 | `worklog-fastapi:latest` |
| 내보낸 파일 | `~/worklog-fastapi.tar` |

이미지 이름과 태그는 `docker build`의 `-t` 옵션에서 정합니다. tar 파일 이름은 이미지 이름과 별개이지만 알아보기 쉽도록 동일하게 지정했습니다. `docker save -o` 대신 `>`로 저장하는 이유는 현재 환경에서 확인된 임시 파일 권한 오류를 피하기 위해서입니다.

버전 태그를 사용하려면 아래처럼 이름을 일관되게 변경합니다.

```bash
cd ~/PycharmProjects/WorkLogMgmtSys/fastapi_backend
sudo docker build -t worklog-fastapi:1.0.0 .
sudo docker save worklog-fastapi:1.0.0 > "$HOME/worklog-fastapi-1.0.0.tar"
ls -lh "$HOME/worklog-fastapi-1.0.0.tar"
```

Dockerfile에 표시되는 `SecretsUsedInArgOrEnv` 경고는 `CREDENTIALS`, `TOKEN`이라는 변수명에 대한 정적 검사 결과입니다. 현재 경고 대상인 CORS 허용 여부와 토큰 만료 시간은 비밀값이 아닙니다. 실제 비밀값인 관리자 암호와 JWT 서명 키는 이미지에 넣지 말고 실행 시 전달해야 합니다.

## 환경 설정

예제 파일을 복사합니다.

```bash
cp .env.example .env
```

운영 환경에서는 최소한 다음 값을 안전한 값으로 설정하는 것을 권장합니다.

```dotenv
WORKLOG_ADMIN_USERNAME=admin
WORKLOG_ADMIN_PASSWORD=충분히-긴-관리자-암호
WORKLOG_JWT_SECRET_KEY=충분히-긴-무작위-서명키
```

JWT 서명 키는 다음과 같이 생성할 수 있습니다.

```bash
openssl rand -hex 32
```

주요 환경변수:

| 환경변수 | 기본값/설명 |
|---|---|
| `WORKLOG_APP_TITLE` | `Work Log Management System` |
| `WORKLOG_APP_VERSION` | `0.1.0` |
| `WORKLOG_CORS_ORIGINS` | `*`, 여러 값은 쉼표로 구분 |
| `WORKLOG_CORS_ALLOW_CREDENTIALS` | `true` |
| `WORKLOG_ADMIN_USERNAME` | 선택 사항, 기본 관리자 계정 |
| `WORKLOG_ADMIN_PASSWORD` | 관리자 이름을 지정하면 함께 지정 필요 |
| `WORKLOG_JWT_SECRET_KEY` | 운영 환경에서 고정된 안전한 값 권장 |
| `WORKLOG_JWT_ACCESS_TOKEN_EXPIRE_MINUTES` | `1440` |
| `WORKLOG_DATABASE_URL` | 컨테이너 기본값은 `sqlite:////app/data/worklog.sqlite3` |

`.env` 파일은 이미지에 포함되지 않으며 저장소에도 커밋하지 않아야 합니다.

## 로컬 Docker 실행

SQLite 데이터를 Docker 볼륨에 보존하며 실행합니다.

```bash
docker run -d \
  --name worklog-fastapi \
  --restart unless-stopped \
  -p 8000:8000 \
  --env-file .env \
  -v worklog-data:/app/data \
  worklog-fastapi:latest
```

상태와 로그를 확인합니다.

```bash
docker ps --filter name=worklog-fastapi
docker logs -f worklog-fastapi
curl -i http://localhost:8000/health
```

정상 상태에서는 `/health`가 `204 No Content`를 반환합니다. API 문서는 `http://localhost:8000/docs`에서 확인할 수 있습니다.

컨테이너를 중지하고 제거해도 `worklog-data` 볼륨은 유지됩니다.

```bash
docker stop worklog-fastapi
docker rm worklog-fastapi
```

## tar 파일을 다른 장비에 배포

생성된 tar 파일을 NAS 또는 서버로 복사한 뒤 이미지를 불러옵니다.

```bash
docker load -i worklog-fastapi.tar
docker image inspect worklog-fastapi:latest
```

`docker load`는 tar 내부의 이미지 이름과 태그를 유지하므로, 불러온 결과는 `worklog-fastapi:latest`가 됩니다.

Synology Container Manager에서는 다음 순서로 가져올 수 있습니다.

1. **이미지** 화면을 엽니다.
2. **추가 → 파일에서 추가**를 선택합니다.
3. `worklog-fastapi.tar`를 선택해 가져옵니다.
4. `worklog-fastapi:latest` 이미지에서 컨테이너를 생성합니다.
5. 로컬 포트와 컨테이너 포트 `8000`을 연결합니다.
6. `/app/data`에 영구 저장용 폴더 또는 볼륨을 연결합니다.
7. `.env.example`을 참고하여 환경변수를 등록합니다.

NAS의 공유 폴더를 데이터 경로로 사용할 경우 컨테이너 내부 연결 경로는 반드시 `/app/data`로 설정합니다.

## MariaDB 사용

SQLite 대신 MariaDB를 사용하려면 `WORKLOG_DATABASE_URL`을 지정합니다.

```dotenv
WORKLOG_DATABASE_URL=mariadb+mariadbconnector://사용자:암호@DB호스트:3306/worklog?charset=utf8mb4
```

컨테이너에서 `127.0.0.1`은 컨테이너 자신을 의미합니다. MariaDB가 다른 컨테이너나 NAS에서 실행 중이라면 해당 서비스 이름 또는 실제 호스트 주소를 사용해야 합니다.

## 업데이트 절차

새 버전은 `latest`보다 명시적인 버전 태그로 관리하는 것이 안전합니다. 빌드 장비에서 다음 명령을 실행합니다.

```bash
cd ~/PycharmProjects/WorkLogMgmtSys/fastapi_backend
sudo docker build -t worklog-fastapi:1.0.1 .
sudo docker save worklog-fastapi:1.0.1 > "$HOME/worklog-fastapi-1.0.1.tar"
```

대상 장비에서 이미지를 불러온 후 기존 컨테이너를 새 이미지로 다시 생성합니다. `/app/data` 볼륨을 동일하게 연결하면 SQLite 데이터가 유지됩니다.

```bash
docker load -i worklog-fastapi-1.0.1.tar
docker stop worklog-fastapi
docker rm worklog-fastapi
docker run -d \
  --name worklog-fastapi \
  --restart unless-stopped \
  -p 8000:8000 \
  --env-file .env \
  -v worklog-data:/app/data \
  worklog-fastapi:1.0.1
```

## 문제 해결

### Docker API 권한 오류

```text
permission denied while trying to connect to the docker API
```

`sudo docker ...`를 사용하거나 사용자를 `docker` 그룹에 추가하고 다시 로그인합니다.

### tar 임시 파일 권한 오류

```text
failed to save image: open .../.tmp-worklog-fastapi.tar...: permission denied
```

`docker save -o`를 사용하지 않고 다음처럼 표준 출력을 저장합니다.

```bash
sudo docker save worklog-fastapi:latest > "$HOME/worklog-fastapi.tar"
```

### 컨테이너가 즉시 종료되는 경우

```bash
docker logs worklog-fastapi
```

관리자 이름만 지정하고 암호를 누락했는지, 또는 예제 JWT 키를 그대로 사용했는지 확인합니다. `docker-entrypoint.sh`는 잘못된 인증 설정이 있으면 실행을 중단합니다.

### 데이터가 재생성되는 경우

컨테이너의 `/app/data`에 볼륨이나 NAS 폴더가 연결되어 있는지 확인합니다. 볼륨 없이 컨테이너를 제거하면 컨테이너 내부 SQLite 데이터도 함께 사라집니다.
