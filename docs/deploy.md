# 서버 배포 가이드

이 문서는 AiDesk를 개발 서버/운영 서버에 배포하는 절차를 단계별로 설명합니다.

## 1. 배포 방식 선택

### 1.1 빠른 개발 서버 배포
- 목적: 기능 확인, 내부 테스트
- 방식: 소스코드 clone 후 스크립트 실행
- 실행 프로세스: run-all.sh (백엔드 + 프론트 개발 서버)

### 1.2 운영형 배포
- 목적: 외부 접속, 안정 운영
- 방식: 백엔드 publish + systemd, 프론트 build + Nginx 정적 서빙
- 권장: HTTPS + 도메인 + CORS 제한 + 비밀키 분리

## 2. 사전 요구사항

- OS: Ubuntu 22.04/24.04 권장
- Dotnet SDK: 10.x
- Node.js: 18+
- Nginx: 최신 안정 버전
- Qdrant: Docker 또는 별도 서버
- DB: SQLite(개발) 또는 MSSQL(운영 권장)

## 3. 빠른 개발 서버 배포

### 3.1 코드 배치

```bash
sudo mkdir -p /opt/aidesk
sudo chown -R $USER:$USER /opt/aidesk
cd /opt/aidesk
git clone <REPO_URL> AIDeskPJ
cd AIDeskPJ
```

### 3.2 백엔드 설정

파일: AiDeskApi/appsettings.Development.json

필수 확인 항목:
- OpenAI.ApiKey
- OpenAI.Model
- Qdrant.Enabled
- Qdrant.Url
- Cors.AllowedOrigins

주의:
- 실제 API 키를 Git에 커밋하지 않습니다.
- 운영 배포에서는 appsettings.Production.json 또는 환경 변수 사용을 권장합니다.

### 3.3 프론트 설정

파일: AiDeskClient/.env

```env
VITE_API_BASE_URL=http://<SERVER_IP>:8080/api
```

### 3.4 의존성 설치

```bash
cd /opt/aidesk/AIDeskPJ/AiDeskClient
npm install

cd /opt/aidesk/AIDeskPJ
dotnet restore AiDesk.sln
```

### 3.5 Qdrant 실행 (Docker)

```bash
docker run -d --name qdrant -p 6333:6333 qdrant/qdrant
```

### 3.6 실행

```bash
cd /opt/aidesk/AIDeskPJ
./scripts/run-all.sh
```

로그 확인:

```bash
tail -f /tmp/aideskapi.log
tail -f /tmp/aideskclient.log
```

중지:

```bash
./scripts/stop-all.sh
```

## 4. 운영형 배포 (권장)

## 4.1 디렉터리 구조 예시

```text
/opt/aidesk/
  AIDeskPJ/                # 소스
  publish/api/             # dotnet publish 결과
  frontend/dist/           # vite build 결과
```

## 4.2 백엔드 publish

```bash
cd /opt/aidesk/AIDeskPJ
dotnet publish AiDeskApi/AiDeskApi.csproj -c Release -o /opt/aidesk/publish/api
```

## 4.3 프론트 build

```bash
cd /opt/aidesk/AIDeskPJ/AiDeskClient
npm ci
VITE_API_BASE_URL=/api npm run build

mkdir -p /opt/aidesk/frontend
rm -rf /opt/aidesk/frontend/dist
cp -R dist /opt/aidesk/frontend/
```

## 4.4 운영 설정 준비

파일: AiDeskApi/appsettings.Production.json

필수 확인 항목:
- Database.Provider: mssql 권장
- ConnectionStrings.AiDeskDb
- Cors.AllowedOrigins: 실제 도메인만
- OpenAI 관련 키/모델
- Qdrant 연결 정보

중요 보안 권장:
- OpenAI, DB 비밀번호는 환경 변수 또는 비밀 저장소 사용
- CORS는 와일드카드 사용 금지
- JwtSettings.SecretKey는 32자 이상 랜덤 문자열

## 4.5 systemd 서비스 생성

파일: /etc/systemd/system/aidesk-api.service

```ini
[Unit]
Description=AiDesk API
After=network.target

[Service]
WorkingDirectory=/opt/aidesk/publish/api
ExecStart=/usr/bin/dotnet /opt/aidesk/publish/api/AiDeskApi.dll
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:8080
Restart=always
RestartSec=5
User=www-data
Group=www-data

[Install]
WantedBy=multi-user.target
```

적용:

```bash
sudo systemctl daemon-reload
sudo systemctl enable aidesk-api
sudo systemctl start aidesk-api
sudo systemctl status aidesk-api
```

로그:

```bash
sudo journalctl -u aidesk-api -f
```

## 4.6 Nginx 설정

파일: /etc/nginx/sites-available/aidesk

```nginx
server {
    listen 80;
    server_name your-domain.com;

    root /opt/aidesk/frontend/dist;
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;
    }

    location /api/ {
        proxy_pass http://127.0.0.1:8080/api/;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

활성화:

```bash
sudo ln -s /etc/nginx/sites-available/aidesk /etc/nginx/sites-enabled/aidesk
sudo nginx -t
sudo systemctl reload nginx
```

HTTPS (권장):

```bash
sudo apt-get install -y certbot python3-certbot-nginx
sudo certbot --nginx -d your-domain.com
```

## 5. 방화벽/네트워크

- 외부 오픈: 80, 443
- 내부만 사용: 8080, 6333
- UFW 예시:

```bash
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw deny 8080/tcp
sudo ufw deny 6333/tcp
sudo ufw enable
```

## 6. 배포 후 점검 체크리스트

- API 상태 확인

```bash
curl -I http://127.0.0.1:8080/swagger
```

- 프론트 접근 확인

```bash
curl -I http://your-domain.com
```

- 프록시 확인

```bash
curl -I http://your-domain.com/api/knowledgebase/list
```

- Qdrant 확인

```bash
curl http://127.0.0.1:6333/collections
```

## 7. 업데이트 배포 절차

```bash
cd /opt/aidesk/AIDeskPJ
git pull

dotnet publish AiDeskApi/AiDeskApi.csproj -c Release -o /opt/aidesk/publish/api

cd /opt/aidesk/AIDeskPJ/AiDeskClient
npm ci
VITE_API_BASE_URL=/api npm run build
rm -rf /opt/aidesk/frontend/dist
cp -R dist /opt/aidesk/frontend/

sudo systemctl restart aidesk-api
sudo systemctl reload nginx
```

## 8. 롤백 전략

- publish/api 를 릴리스 버전별 폴더로 보관
- frontend/dist 도 빌드 결과를 버전 폴더로 보관
- 문제 발생 시 심볼릭 링크만 이전 버전으로 되돌리고 서비스 재시작

## 9. 장애 대응 팁

### 9.1 run-backend.sh 종료코드 137
- 기존 프로세스를 스크립트가 kill -9 하면서 관측되는 경우가 많습니다.
- 개발용 스크립트 특성상 반복 실행 시 발생할 수 있습니다.
- 운영에서는 run-backend.sh 대신 systemd 상시 실행을 권장합니다.

### 9.2 CORS 오류
- Production 환경에서는 AllowedOrigins 미설정 시 API가 시작 실패할 수 있습니다.
- appsettings.Production.json 의 Cors.AllowedOrigins 를 반드시 설정하세요.

### 9.3 API 키 노출 위험
- appsettings 파일에 실키를 직접 커밋하지 마세요.
- 환경 변수 또는 비밀 저장소로 분리하세요.

## 10. 관련 문서

- docs/setup.md
- docs/tech-stack.md
- docs/rag-kb-process.md
- docs/api-spec.md
