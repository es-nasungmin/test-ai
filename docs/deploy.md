# 윈도우 서버 배포 가이드

이 문서는 AiDesk를 윈도우 서버 환경에서 개발/운영하는 방법을 정리합니다.

대상 환경:
- 개발 서버: Windows Server
- 운영 서버: Windows Server
- Backend: ASP.NET Core API
- Frontend: Vue 3 + Vite
- Vector DB: Qdrant
- DB: SQLite 또는 MSSQL

---

## 1. 권장 배포 구조

### 1.1 개발 서버 권장 구조
- Frontend: Vite build 결과물을 IIS로 서빙하거나 `npm run dev -- --host`로 테스트
- Backend: `dotnet run` 또는 `dotnet publish` 후 실행
- Vector DB: 같은 서버 Docker의 Qdrant 또는 별도 서버 Qdrant
- DB: SQLite 또는 MSSQL

### 1.2 운영 서버 권장 구조
- Frontend: IIS 정적 배포
- Backend: `dotnet publish` 후 Windows Service 또는 IIS behind Kestrel
- Vector DB: 별도 Qdrant 서버 또는 Docker 기반 별도 호스트 권장
- DB: MSSQL 권장

실무적으로 가장 무난한 구조:
1. IIS가 프론트 정적 파일 서빙
2. IIS 또는 Windows Service가 백엔드 API 실행
3. Qdrant는 같은 서버 Docker 또는 별도 서버
4. 운영 DB는 MSSQL 사용

---

## 2. 사전 요구사항

윈도우 서버에 아래 항목을 준비합니다.

### 2.1 필수 소프트웨어
- .NET SDK 또는 Runtime 10.x
- Node.js 18 이상
- Git
- IIS

### 2.2 권장 추가 구성
- IIS URL Rewrite
- IIS Application Request Routing(ARR)
- Docker Desktop 또는 Docker Engine 대체 환경(Qdrant용)
- SQL Server 또는 외부 MSSQL 서버

### 2.3 외부 의존 서비스
- OpenAI API Key
- Gemini API Key(선택)
- Qdrant URL
- MSSQL 연결 문자열(운영 권장)

---

## 3. 현재 애플리케이션 설정 방식

현재 백엔드는 설정에 따라 DB를 선택합니다.

1. `Database:Provider = sqlite`
- 앱 폴더 내부의 `aidesk.db` 사용

2. `Database:Provider = mssql`
- `ConnectionStrings:AiDeskDb` 연결 문자열 사용

근거:
- `AiDeskApi/Program.cs`

즉, 윈도우 서버에서도 SQLite와 MSSQL 둘 다 가능합니다.

---

## 4. 디렉터리 권장 구조

윈도우 서버 예시:

```text
C:\deploy\aidesk\
  source\AIDeskPJ\          # git clone 원본
  api\                       # dotnet publish 결과
  frontend\                  # vite build 결과(dist)
  logs\                      # 운영 로그
```

예시 경로:
- 소스: `C:\deploy\aidesk\source\AIDeskPJ`
- API publish: `C:\deploy\aidesk\api`
- Frontend dist: `C:\deploy\aidesk\frontend`

---

## 5. 개발 서버 배포 절차 (Windows Server)

## 5.1 코드 배치

PowerShell:

```powershell
mkdir C:\deploy\aidesk -Force
cd C:\deploy\aidesk
git clone <REPO_URL> source\AIDeskPJ
cd .\source\AIDeskPJ
```

## 5.2 백엔드 설정

파일:
- `AiDeskApi/appsettings.Development.json`

필수 확인 항목:
- `OpenAI.ApiKey`
- `OpenAI.Model`
- `Qdrant.Enabled`
- `Qdrant.Url`
- `Cors.AllowedOrigins`
- `Database.Provider`
- `ConnectionStrings.AiDeskDb` (MSSQL 사용 시)

예시: SQLite 개발 서버

```json
{
  "Database": {
    "Provider": "sqlite"
  },
  "Qdrant": {
    "Enabled": true,
    "Url": "http://127.0.0.1:6333",
    "CollectionName": "aidesk_kb"
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5173",
      "http://127.0.0.1:5173"
    ]
  }
}
```

예시: MSSQL 개발 서버

```json
{
  "Database": {
    "Provider": "mssql"
  },
  "ConnectionStrings": {
    "AiDeskDb": "Server=DEV-SQL;Database=AiDeskDev;User Id=devuser;Password=devpass;TrustServerCertificate=True;Encrypt=True;"
  }
}
```

## 5.3 프론트 설정

파일:
- `AiDeskClient/.env`

개발 서버에서 API 직접 호출 시:

```env
VITE_API_BASE_URL=http://<WINDOWS_SERVER_IP>:8080/api
```

같은 서버에서 IIS reverse proxy를 쓸 예정이면:

```env
VITE_API_BASE_URL=/api
```

## 5.4 의존성 설치

```powershell
cd C:\deploy\aidesk\source\AIDeskPJ
dotnet restore .\AiDesk.sln

cd .\AiDeskClient
npm install
```

## 5.5 개발 서버 실행

백엔드:

```powershell
cd C:\deploy\aidesk\source\AIDeskPJ\AiDeskApi
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run
```

프론트:

```powershell
cd C:\deploy\aidesk\source\AIDeskPJ\AiDeskClient
npm run dev -- --host
```

주의:
- 현재 저장소의 `run-all.sh` / `run-backend.sh` / `run-frontend.sh` 는 `zsh` 기준이라 윈도우에서 그대로 사용하지 않습니다.
- 윈도우에서는 PowerShell 명령으로 실행하는 것이 맞습니다.

---

## 6. 운영 서버 배포 절차 (Windows Server)

## 6.1 백엔드 publish

```powershell
cd C:\deploy\aidesk\source\AIDeskPJ
dotnet publish .\AiDeskApi\AiDeskApi.csproj -c Release -o C:\deploy\aidesk\api
```

산출물:
- `C:\deploy\aidesk\api\AiDeskApi.dll`

## 6.2 프론트 build

```powershell
cd C:\deploy\aidesk\source\AIDeskPJ\AiDeskClient
npm ci
$env:VITE_API_BASE_URL="/api"
npm run build
```

빌드 결과 복사:

```powershell
Remove-Item C:\deploy\aidesk\frontend -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item .\dist C:\deploy\aidesk\frontend -Recurse
```

## 6.3 운영 설정

파일:
- `AiDeskApi/appsettings.Production.json`

운영 서버에서는 최소 아래를 맞춰야 합니다.

```json
{
  "Database": {
    "Provider": "mssql"
  },
  "ConnectionStrings": {
    "AiDeskDb": "Server=PROD-SQL;Database=AiDeskProd;User Id=produser;Password=prodpass;TrustServerCertificate=True;Encrypt=True;"
  },
  "Cors": {
    "AllowedOrigins": [
      "https://your-domain.com"
    ]
  }
}
```

권장 추가 환경 변수:

```powershell
setx ASPNETCORE_ENVIRONMENT "Production"
setx OpenAI__ApiKey "<REAL_OPENAI_KEY>"
setx Gemini__ApiKey "<REAL_GEMINI_KEY>"
setx Qdrant__Url "http://10.0.0.20:6333"
setx JwtSettings__SecretKey "32자이상충분히긴랜덤문자열"
```

노트:
- Production 환경에서는 `Cors:AllowedOrigins`가 비어 있으면 앱이 시작 실패합니다.
- 이 동작은 현재 코드에 실제로 반영되어 있습니다.

---

## 7. DB 연결 방법

## 7.1 SQLite 사용

개발 환경이나 소규모 테스트 서버에 적합합니다.

설정:

```json
{
  "Database": {
    "Provider": "sqlite"
  }
}
```

동작:
- API 실행 폴더 내부에 `aidesk.db` 생성
- 앱 시작 시 자동 초기화

장점:
- 설치가 가장 쉬움
- 서버 한 대에서 빠르게 테스트 가능

주의:
- 운영 다중 사용자 환경에는 MSSQL이 더 적합
- 백업/복구/동시성/운영 관리 측면에서 한계가 있음

## 7.2 MSSQL 사용

운영 환경 권장 방식입니다.

설정:

```json
{
  "Database": {
    "Provider": "mssql"
  },
  "ConnectionStrings": {
    "AiDeskDb": "Server=DBHOST;Database=AiDeskProd;User Id=produser;Password=prodpass;TrustServerCertificate=True;Encrypt=True;"
  }
}
```

윈도우 서버의 SQL Server 로컬 인스턴스를 사용할 수도 있고, 별도 DB 서버를 사용할 수도 있습니다.

예시:

```text
Server=localhost\\SQLEXPRESS;Database=AiDeskProd;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;
```

또는 SQL 로그인:

```text
Server=10.0.0.30;Database=AiDeskProd;User Id=produser;Password=prodpass;TrustServerCertificate=True;Encrypt=True;
```

권장:
- 개발 서버: SQLite 또는 별도 DEV MSSQL
- 운영 서버: MSSQL

## 7.3 DB 초기화

현재 구조에서는 앱 시작 시 DB 초기화 코드가 동작합니다.

즉:
- DB가 없으면 생성 시도
- 기본 테이블/기초 데이터 초기화

운영에서는 초기 기동 전 백업 정책을 먼저 정하는 것이 좋습니다.

---

## 8. Qdrant(Vector DB) 도입 방법

현재 시스템은 Qdrant를 HTTP로 붙는 구조입니다.

필수 조건은 하나입니다.
- 백엔드가 접근 가능한 Qdrant URL이 있어야 함

즉, 꼭 윈도우 서버에 직접 설치할 필요는 없습니다.

## 8.1 옵션 A: 같은 윈도우 서버에서 Docker로 실행

가장 쉬운 개발/사내 테스트 방식입니다.

```powershell
docker run -d --name qdrant -p 6333:6333 qdrant/qdrant
```

설정:

```json
{
  "Qdrant": {
    "Enabled": true,
    "Url": "http://127.0.0.1:6333",
    "CollectionName": "aidesk_kb"
  }
}
```

장점:
- 빠르게 시작 가능
- 같은 서버에서 구성 단순

단점:
- 운영 리소스가 API 서버와 섞임
- 장애 영향 범위가 큼

## 8.2 옵션 B: 별도 서버에 Qdrant 실행

운영 권장 방식입니다.

예:
- Windows 서버: Frontend + Backend
- Linux 서버/VM: Qdrant

설정:

```json
{
  "Qdrant": {
    "Enabled": true,
    "Url": "http://10.0.0.20:6333",
    "CollectionName": "aidesk_kb"
  }
}
```

장점:
- API 서버와 벡터 DB 분리
- 운영 안정성 좋음
- 백업/모니터링 분리 쉬움

## 8.3 옵션 C: Qdrant Cloud

관리형 서비스가 가능하면 가장 편한 방식입니다.

설정은 동일하게 URL만 변경합니다.

---

## 9. 윈도우 서버에서 백엔드 운영 방법

## 9.1 방법 A: Windows Service 등록

가장 권장합니다.

방법 1: `sc.exe`

```powershell
sc create AiDeskApi binPath= "C:\Program Files\dotnet\dotnet.exe C:\deploy\aidesk\api\AiDeskApi.dll" start= auto
```

시작:

```powershell
sc start AiDeskApi
```

중지:

```powershell
sc stop AiDeskApi
```

주의:
- 실무에서는 NSSM(Non-Sucking Service Manager)로 등록하는 방식이 더 편한 경우가 많습니다.

## 9.2 방법 B: IIS + ASP.NET Core Hosting Bundle

가능합니다. 다만 현재 구조에서는 reverse proxy 또는 ASP.NET Core Hosting Bundle 설정이 추가로 필요합니다.

윈도우 서버에 ASP.NET Core Hosting Bundle 설치 후 IIS 사이트로 붙일 수 있습니다.

운영 난이도는 조직 표준에 따라 선택하면 됩니다.

---

## 10. 윈도우 서버에서 프론트 운영 방법

## 10.1 IIS 정적 사이트 생성

1. IIS 관리자 실행
2. 사이트 추가
3. Physical path를 `C:\deploy\aidesk\frontend`로 지정
4. 바인딩(도메인/포트) 설정

## 10.2 SPA fallback 설정

Vue SPA 이므로 새로고침 시 라우팅 fallback 이 필요할 수 있습니다.

`web.config` 예시:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="Vue Routes" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
            <add input="{REQUEST_URI}" pattern="^/api/.*" negate="true" />
          </conditions>
          <action type="Rewrite" url="/index.html" />
        </rule>
      </rules>
    </rewrite>
  </system.webServer>
</configuration>
```

저장소 예시 파일:
- `docs/windows-web.config.example`

## 10.3 `/api` reverse proxy

IIS URL Rewrite + ARR 사용 시 `/api`를 백엔드로 전달할 수 있습니다.

예시 개념:
- 사용자 요청: `https://your-domain.com/api/...`
- 내부 전달: `http://127.0.0.1:8080/api/...`

이 경우 프론트 `.env`는 아래처럼 두는 것이 가장 깔끔합니다.

```env
VITE_API_BASE_URL=/api
```

---

## 11. 운영 체크리스트

### 11.1 보안
- 실제 API 키를 저장소에 커밋하지 않기
- CORS 허용 도메인을 실제 도메인만 남기기
- JWT SecretKey를 충분히 길게 설정하기
- Qdrant, MSSQL 포트를 외부 직접 오픈하지 않기

### 11.2 백업
- MSSQL 정기 백업
- Qdrant 스냅샷 또는 데이터 볼륨 백업
- 프론트/백엔드 배포 산출물 버전 보관

### 11.3 모니터링
- Windows Event Viewer 또는 서비스 로그 확인
- IIS 로그 확인
- Qdrant health 체크

---

## 12. 배포 후 점검 절차

### 12.1 API 확인

```powershell
curl http://127.0.0.1:8080/swagger
```

### 12.2 프론트 확인
- 브라우저에서 메인 페이지 접속
- 로그인/KB 목록/채팅 로그 화면 확인

### 12.3 Qdrant 확인

```powershell
curl http://127.0.0.1:6333/collections
```

또는 별도 서버면 해당 주소로 확인:

```powershell
curl http://10.0.0.20:6333/collections
```

### 12.4 DB 확인
- SQLite: `aidesk.db` 생성 여부 확인
- MSSQL: 테이블 생성/접속 확인

---

## 13. 업데이트 배포 절차

### 13.1 백엔드

```powershell
cd C:\deploy\aidesk\source\AIDeskPJ
git pull
dotnet publish .\AiDeskApi\AiDeskApi.csproj -c Release -o C:\deploy\aidesk\api
```

### 13.2 프론트

```powershell
cd C:\deploy\aidesk\source\AIDeskPJ\AiDeskClient
npm ci
$env:VITE_API_BASE_URL="/api"
npm run build
Remove-Item C:\deploy\aidesk\frontend -Recurse -Force
Copy-Item .\dist C:\deploy\aidesk\frontend -Recurse
```

### 13.3 재시작
- Windows Service 재시작
- IIS 사이트 또는 앱풀 재시작

저장소 예시 스크립트:
- `scripts/deploy-windows.ps1`
- `scripts/register-windows-service.ps1`

---

## 14. 장애 대응 팁

### 14.1 백엔드가 기동 실패하는 경우
- `appsettings.Production.json`의 CORS 설정 확인
- `OpenAI__ApiKey` 환경 변수 확인
- `Qdrant__Url` 접근 가능 여부 확인
- MSSQL 연결 문자열 확인

### 14.2 run-backend.sh 종료코드 137
- 이 스크립트는 macOS/zsh 개발용이라 윈도우 배포와 직접 관련이 없습니다.
- 윈도우 운영에서는 PowerShell + Windows Service 기준으로 보시면 됩니다.

### 14.3 Qdrant 연결 실패
- URL 오타
- 방화벽 차단
- 컨테이너 미기동
- Qdrant 컬렉션 접근 실패

### 14.4 MSSQL 연결 실패
- SQL Browser/방화벽 문제
- 인증 방식 문제
- `TrustServerCertificate=True` 누락
- 서버명 또는 인스턴스명 오타

---

## 15. 추천 운영 시나리오

### 시나리오 A: 개발 서버(윈도우 1대)
- Frontend: IIS 또는 Vite dev server
- Backend: dotnet run
- Qdrant: 같은 서버 Docker
- DB: SQLite 또는 DEV MSSQL

### 시나리오 B: 운영 서버(윈도우 1대 + DB 별도)
- Frontend: IIS
- Backend: Windows Service
- Qdrant: 같은 서버 Docker 또는 별도 서버
- DB: 별도 MSSQL 서버

### 시나리오 C: 운영 서버(권장 분리형)
- Windows Server: Frontend + Backend
- Linux VM: Qdrant
- MSSQL Server: 별도 DB 서버

---

## 16. 관련 문서

- `docs/setup.md`
- `docs/tech-stack.md`
- `docs/rag-kb-process.md`
- `docs/api-spec.md`
- `docs/windows-web.config.example`
- `scripts/deploy-windows.ps1`
- `scripts/register-windows-service.ps1`
