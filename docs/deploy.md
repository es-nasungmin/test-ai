# Windows 배포 가이드 (IIS + MSSQL)

이 문서는 AiDesk를 **Windows 서버 환경 (IIS + MSSQL)** 에서 배포하고 운영하는 방법을 정리합니다.

대상 환경:
- OS: Windows Server 2016 이상 (또는 Windows 10/11 Pro)
- 웹 서버: IIS (Internet Information Services)
- DB: SQL Server (기존 SSMS로 관리 중인 MSSQL 인스턴스 사용)
- Vector DB: Qdrant (Docker 또는 별도 서버)
- Backend: ASP.NET Core 10 Web API
- Frontend: Vue 3 + Vite 빌드 정적 파일

## 📌 처음 배포하는 분을 위한 시작 가이드

이 가이드는 다음 순서로 진행됩니다:
1. **필수 소프트웨어 설치** (IIS, .NET Runtime, 모듈들)
2. **데이터베이스 생성** (MSSQL)
3. **백엔드 빌드 & 배포** (.NET API)
4. **프론트엔드 빌드 & 배포** (Vue)
5. **테스트 & 확인**

> 💡 **팁**: 각 섹션을 완료한 후 반드시 명시된 확인 사항을 체크하세요. 문제가 생기면 로그를 먼저 확인하세요 (섹션 12 트러블슈팅 참고).

---

## 1. 전체 구성 개요

```
[브라우저/위젯]
    ↓ HTTPS
[IIS - 사이트 1: 프론트엔드]
    ─ C:\deploy\aidesk\frontend\  (Vue 빌드 결과물)
    ─ SPA fallback → index.html 처리

[IIS - 사이트 2: 백엔드 API]
    ─ C:\deploy\aidesk\api\  (dotnet publish 결과물)
    ─ ASP.NET Core Hosting Bundle 사용
    ─ HTTP/8080 또는 ARR 역방향 프록시(/api)

[SQL Server]  ← 기존 인스턴스 사용
    ─ AiDesk 전용 DB 생성
    ─ SSMS로 관리

[Qdrant]  ← Docker 또는 별도 서버
    ─ 기본 포트 6333
```

### 같은 IIS 서버에서 /api 경로로 백엔드를 프록시하는 구성 (권장)

IIS ARR(Application Request Routing)을 사용하면 프론트(`/`)와 백엔드 API(`/api`)를 하나의 도메인에서 운영할 수 있습니다.

---

## 2. 사전 요구사항

### 2.1 IIS 활성화

**IIS란?** 웹 서버입니다. 브라우저에서 요청하는 페이지를 제공하는 역할을 합니다.

Windows 기능에서 IIS를 활성화합니다.

**방법 A: PowerShell (관리자 권한 필요)**

```powershell
# PowerShell을 "관리자 권한으로 실행" 후 아래 명령어 입력
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole -All
Enable-WindowsOptionalFeature -Online -FeatureName IIS-ASPNET45 -All
Enable-WindowsOptionalFeature -Online -FeatureName IIS-ApplicationDevelopment -All
```

설치 완료 후 IIS 재시작 (서버 재부팅 불필요):
```powershell
iisreset
```

**방법 B: GUI (그래픽)**

1. 제어판 → 프로그램 → **Windows 기능 켜기/끄기**
2. 다음 항목을 체크:
   - ☑ **Internet Information Services**
   - ☑ **Internet Information Services 호스팅 환경**
   - ☑ **Application Development Features** (ASP.NET 관련)
3. 확인 → 완료 후 `iisreset` 실행

✅ **확인 방법**: `Win + R` → `inetmgr` 입력 → IIS 관리자 창 열리면 성공

---

### 2.2 ASP.NET Core Hosting Bundle 설치 (중요!)

**이게 뭔가요?** .NET Core로 만든 API를 IIS에서 실행하기 위한 필수 패키지입니다. 이걸 설치하지 않으면 API가 IIS에서 실행되지 않습니다.

1. https://dotnet.microsoft.com/en-us/download/dotnet/10.0 방문
2. "**ASP.NET Core Runtime 10.x (또는 최신)**" 찾기
3. **"Hosting Bundle"** 다운로드 (약 100MB)
4. 설치 프로그램 실행 (관리자 권한)
5. 설치 완료 후 IIS 재시작 (`iisreset` — 서버 재부팅 불필요):
```powershell
iisreset
```

설치 후 PowerShell에서 확인:
```powershell
dotnet --list-runtimes
# output 예:
# Microsoft.AspNetCore.App 10.0.x [C:\Program Files\dotnet\shared\...]
```

✅ **중요**: 반드시 IIS를 **재시작**해야 Hosting Bundle을 인식합니다:
```powershell
iisreset
```

---

### 2.3 IIS 추가 모듈 설치 (ARR & URL Rewrite)

**이게 뭔가요?**
- **URL Rewrite**: 요청 경로를 변경 (예: `/api/*` → 백엔드로 라우팅)
- **ARR**: 역방향 프록시 (다양한 백엔드 서버로 요청 분배)

이 가이드에서는 프론트(`/`)와 백엔드 API(`/api`)를 **하나의 포트**에서 운영하기 위해 필요합니다.

**설치 방법:**

1. 아래 링크에서 다운로드:
   - **URL Rewrite Module 2.1** (약 200KB): https://www.iis.net/downloads/microsoft/url-rewrite
   - **Application Request Routing 3.0** (약 1MB): https://www.iis.net/downloads/microsoft/application-request-routing

2. 각각 설치 프로그램 실행
3. 설치 완료 후 IIS 재시작:
   ```powershell
   iisreset
   ```

✅ **확인**: IIS 관리자 → [서버명] → "**URL Rewrite**" 와 "**Application Request Routing Cache**" 표시되면 성공

---

### 2.4 Node.js 설치 (빌드 서버용)

**이게 뭔가요?** 프론트엔드(Vue)를 빌드할 때 필요합니다. **배포 대상 서버에는 불필요** (미리 빌드한 파일만 복사하면 됨).

1. https://nodejs.org → **LTS 버전 다운로드** (권장)
2. 설치 프로그램 실행 (기본 옵션 그대로)
3. 완료 후 PowerShell 새 창에서 확인:
   ```powershell
   node --version    # v18.x.x 이상이면 OK
   npm --version     # 10.x.x 이상이면 OK
   ```

---

### 2.5 .NET SDK 설치 (빌드 서버용)

**이게 뭔가요?** 백엔드(.NET) 코드를 **빌드**할 때만 필요합니다. 배포 서버에는 **Runtime만** 있으면 됩니다 (이미 2.2에서 설치함).

**빌드 서버에서만 필요:**

1. https://dotnet.microsoft.com → **.NET 10 SDK 다운로드**
2. 설치 프로그램 실행
3. 설치 후 PowerShell에서 확인:
   ```powershell
   dotnet sdk list   # 10.x.x 표시되면 OK
   ```

---

## 3. MSSQL DB 준비 (SSMS)

**MSSQL이 뭔가요?** 데이터베이스입니다. 사용자 정보, 채팅 기록 등 모든 데이터를 저장합니다.

기존 SQL Server 인스턴스에 AiDesk 전용 DB를 생성합니다.

### 3.1 DB 생성 (SSMS)

SSMS(SQL Server Management Studio)를 열고 새 쿼리를 실행합니다.

**아직 SSMS가 없다면:**
1. https://learn.microsoft.com/en-us/sql/ssms/download-sql-server-management-studio-ssms
2. 다운로드 후 설치

**DB 생성 & 계정 만들기:**

SSMS를 열고 아래 SQL을 **그대로 복사해서 실행**하세요:

```sql
-- 1. DB 생성 (한글 정렬)
CREATE DATABASE AiDeskProd
    COLLATE Korean_Wansung_CS_AS;
GO

-- 2. 전용 로그인 생성 (PASSWORD는 복잡하게 설정하세요!)
CREATE LOGIN aidesk_user WITH PASSWORD = 'StrongP@ssword123!';
GO

-- 3. DB에 사용자 추가 및 권한 부여
USE AiDeskProd;
CREATE USER aidesk_user FOR LOGIN aidesk_user;
EXEC sp_addrolemember 'db_owner', aidesk_user;
GO
```

✅ **확인 방법:**
- SSMS 좌측 [데이터베이스] → `AiDeskProd` 보이면 OK
- [보안] → [로그인] → `aidesk_user` 보이면 OK

**주의:**
- `PASSWORD = 'StrongP@ssword123!'` 부분은 **복잡한 비밀번호로 변경하세요**
- 이 비밀번호는 나중에 백엔드 설정파일(5.2)에서 사용합니다

### 3.2 데이터베이스 테이블 자동 생성

앱이 처음 시작될 때 **EF Core가 자동으로 테이블을 생성**합니다 (DatabaseInitializer 코드 참고).
따라서 지금은 DB와 로그인만 생성하면 됩니다.

### 3.3 연결 테스트

SSMS에서 아래를 실행해 연결을 테스트하세요:

```sql
SELECT GETDATE() AS CurrentTime;
```

결과: 현재 시간이 표시되면 OK

**다른 PC에서 접속할 경우:**

1. SQL Server 포트(기본 1433) 확인
   ```powershell
   # SQL Server가 1433 포트를 열고 있는지 확인
   Get-NetTCPConnection | Where-Object {$_.LocalPort -eq 1433}
   ```

2. Windows 방화벽에 1433 포트 개방
   ```powershell
   # 관리자 PowerShell
   New-NetFirewallRule -DisplayName "SQL Server" -Direction Inbound -LocalPort 1433 -Protocol TCP -Action Allow
   ```

---

## 4. 소스코드 가져오기 (Windows PC)

### 4.0 Git으로 소스코드 클론

**Git이 설치되어 있어야 합니다.** (2.4절 참고)

PowerShell에서:

```powershell
# 원하는 폴더로 이동 (예: C:\Users\사용자명\)
cd C:\Users\$env:USERNAME

# GitHub에서 소스코드 가져오기
git clone https://github.com/YOUR_REPO/AIDeskPJ.git

# 또는 USB/공유폴더로 복사한 경우 해당 경로로 이동
cd C:\Users\$env:USERNAME\AIDeskPJ
```

> 💡 **GitHub에 올라가있지 않은 경우**: Mac에서 ZIP으로 압축해서 USB나 공유폴더로 옮기거나, GitHub에 private repo로 올린 뒤 clone 하세요.

**Mac에서 ZIP 만들기 (터미널):**
```bash
cd ~/Projects
zip -r AIDeskPJ.zip AIDeskPJ/ --exclude "*/node_modules/*" --exclude "*/.git/*" --exclude "*/bin/*" --exclude "*/obj/*"
```

✅ **확인**: `ls C:\Users\$env:USERNAME\AIDeskPJ` 에 파일들이 보이면 OK

---

## 5. 백엔드 배포

### 5.1 소스 빌드

**빌드가 뭔가요?** 소스 코드를 실행 가능한 프로그램으로 컴파일하는 과정입니다.

**배포 폴더 생성:**

먼저 배포할 폴더를 만듭니다:
```powershell
# 배포 폴더 구조
mkdir C:\deploy\aidesk\api
mkdir C:\deploy\aidesk\frontend
mkdir C:\deploy\qdrant\storage
```

**빌드 실행:**

PowerShell에서:
```powershell
# 프로젝트 폴더로 이동 (깃 클론한 폴더)
cd C:\Users\YourName\AIDeskPJ  # 또는 실제 경로

# 백엔드 빌드 (Release 모드 = 운영용)
dotnet publish .\AiDeskApi\AiDeskApi.csproj -c Release -o C:\deploy\aidesk\api

# 완료 후 확인
ls C:\deploy\aidesk\api
```

✅ **확인**: 폴더에 다음 파일이 생기면 OK:
- `AiDeskApi.dll` (실행 파일)
- `web.config` (IIS 설정)
- `appsettings.*.json` (설정 파일들)

### 5.2 운영 설정 파일 구성

**설정 파일이 뭔가요?** 앱이 시작할 때 DB 주소, API 키 등을 읽는 파일입니다.

`C:\deploy\aidesk\api\appsettings.Production.json` 파일을 **메모장**으로 열어서 아래 내용으로 덮어씁니다:

```json
{
  "Database": {
    "Provider": "mssql"
  },
  "ConnectionStrings": {
    "AiDeskDb": "Server=localhost;Database=AiDeskProd;User Id=aidesk_user;Password=StrongP@ssword123!;TrustServerCertificate=True;Encrypt=True;"
  },
  "Qdrant": {
    "Enabled": true,
    "Url": "http://127.0.0.1:6333",
    "CollectionName": "aidesk_kb"
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost",
      "http://your-server-ip-or-domain"
    ]
  }
}
```

**수정해야 할 부분:**
- `StrongP@ssword123!` → 3.1에서 만든 실제 비밀번호로 변경
- `http://your-server-ip-or-domain` → 실제 서버 IP 또는 도메인 (예: `http://192.168.1.100`)

> ⚠️ **보안 주의**: `OpenAI:ApiKey`, `JwtSettings:SecretKey` 는 **파일에 쓰지 말고** 환경 변수(4.3)로 관리합니다!

### 5.3 환경 변수 설정 (관리자 PowerShell)

**환경 변수가 뭔가요?**
- 앱이 시작될 때 필요한 민감 정보(API 키, DB 비밀번호 등)를 파일이 아닌 **시스템 환경 변수**로 관리합니다.
- 이렇게 하면 보안이 향상되고, 여러 환경(개발/운영)에서 쉽게 설정을 바꿀 수 있습니다.

**필수 환경 변수:**

| 변수명 | 값 | 설명 |
|--------|-----|------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | 앱이 운영 모드로 실행 |
| `OpenAI__ApiKey` | `sk-xxxx...` | OpenAI API 키 (https://platform.openai.com) |
| `JwtSettings__SecretKey` | (아래 생성 방법 참고) | 로그인 토큰 암호화 키 |
| `Qdrant__Url` | `http://127.0.0.1:6333` | 벡터DB 주소 |

**JWT SecretKey 생성 방법 (매우 중요!)**

JWT는 사용자 로그인 토큰을 암호화할 때 사용하는 비밀 문자열입니다. **반드시 임의의 복잡한 문자열**을 생성해야 합니다.

**PowerShell에서 자동 생성:**
```powershell
# 32자 이상의 랜덤 문자열 생성
$randomBytes = [byte[]]::new(32)
[Security.Cryptography.RNGCryptoServiceProvider]::Create().GetBytes($randomBytes)
$secretKey = [Convert]::ToBase64String($randomBytes)
Write-Host $secretKey

# 출력 예시:
# aBcD1234efGH5678ijKL90mnoPQRStuVwxyZ+/==
```

생성된 문자열을 복사해서 아래에 사용합니다.

**환경 변수 설정:**

PowerShell을 **관리자 권한으로 실행** 후:

```powershell
# ASPNETCORE_ENVIRONMENT = Production (필수)
[System.Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production", "Machine")

# OpenAI API Key (https://platform.openai.com에서 발급받은 키를 넣으세요)
[System.Environment]::SetEnvironmentVariable("OpenAI__ApiKey", "sk-YOUR_ACTUAL_KEY_HERE", "Machine")

# JWT Secret Key (위에서 생성한 문자열을 넣으세요)
[System.Environment]::SetEnvironmentVariable("JwtSettings__SecretKey", "aBcD1234efGH5678ijKL90mnoPQRStuVwxyZ+/==", "Machine")

# Qdrant URL (로컬 또는 별도 서버)
[System.Environment]::SetEnvironmentVariable("Qdrant__Url", "http://127.0.0.1:6333", "Machine")
```

**주의사항:**
- `:` 구분자는 환경 변수에서 `__` (언더스코어 2개)로 변환됩니다 (ASP.NET Core 규칙)
- 각 명령어는 **따로따로** 실행하세요
- 설정 후 **반드시 IIS 재시작** 필요:
  ```powershell
  iisreset
  ```

**환경 변수 확인:**
```powershell
# 설정이 제대로 됐는지 확인
$env:OpenAI__ApiKey        # OpenAI 키가 보이면 OK
$env:JwtSettings__SecretKey   # JWT 키가 보이면 OK
```

✅ **확인**: 모든 변수가 출력되면 성공

### 5.4 IIS 사이트 생성 (백엔드 API)

**IIS 사이트가 뭔가요?** 웹 서버가 어느 폴더의 파일을 어느 포트로 제공할지 설정하는 것입니다.

**단계별 설명:**

1. **IIS 관리자 열기**
   ```powershell
   inetmgr
   ```

2. **좌측 트리** → `[서버명]` → `[사이트]` 우클릭 → **"사이트 추가"**

3. **사이트 추가 창**에서:
   - **사이트 이름**: `AiDeskApi` (자유롭게)
   - **실제 경로**: `C:\deploy\aidesk\api` (정확히!)
   - **바인딩 → 포트**: `8080` (또는 다른 미사용 포트)
   - **확인** 클릭

4. **응용 프로그램 풀 설정** (매우 중요!)
   - 좌측 [응용 프로그램 풀] 선택
   - `AiDeskApiPool` (자동 생성됨) 우클릭 → **기본 설정**
   - **"고급 설정"** → **.NET CLR 버전 = "관리 코드 없음"** ← **반드시 이렇게!**
   
   > ⚠️ 왜? ASP.NET Core는 IIS의 내장 .NET을 사용하지 않고 **Kestrel**이라는 자체 웹 서버를 사용하기 때문입니다.

5. **로그 폴더 생성** (오류 발생 시 로그 확인용)
   ```powershell
   mkdir C:\deploy\aidesk\api\logs
   ```

6. **web.config 확인** (publish 시 자동 생성됨)
   
   파일: `C:\deploy\aidesk\api\web.config`
   
   아래와 같이 되어 있어야 합니다:

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
     <location path="." inheritInChildApplications="false">
       <system.webServer>
         <handlers>
           <add name="aspNetCore" path="*" verb="*"
                modules="AspNetCoreModuleV2" resourceType="Unspecified" />
         </handlers>
         <aspNetCore processPath="dotnet"
                     arguments=".\AiDeskApi.dll"
                     stdoutLogEnabled="true"
                     stdoutLogFile=".\logs\stdout"
                     hostingModel="inprocess">
           <environmentVariables>
             <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
           </environmentVariables>
         </aspNetCore>
       </system.webServer>
     </location>
   </configuration>
   ```

✅ **확인**: IIS 관리자에서 `AiDeskApi` 사이트가 보이고, 상태가 "시작됨"이면 OK

**테스트:**
```powershell
# 브라우저에서 열기 (또는 아래 명령 실행)
Invoke-WebRequest http://127.0.0.1:8080/health

# 결과 예시:
# StatusCode : 200  ← 성공
# Content    : OK
```

만약 502 Bad Gateway 등의 오류가 나면 → 섹션 13 트러블슈팅 참고

---

## 6. 프론트엔드 배포

### 6.1 프론트 빌드 (빌드 서버 또는 개발 PC)

**프론트엔드 빌드가 뭔가요?** Vue 코드를 브라우저가 실행할 수 있는 HTML/CSS/JS로 변환하는 과정입니다.

**PowerShell에서:**

```powershell
# 프로젝트 폴더로 이동
cd C:\Users\YourName\AIDeskPJ\AiDeskClient

# 1단계: 의존성 설치 (처음만 또는 package.json이 변경될 때마다)
npm ci

# 2단계: API 엔드포인트 설정 및 빌드
# API가 같은 서버의 /api 경로에 있으면 (권장):
$env:VITE_API_BASE_URL = "/api"

# 또는 API가 별도 포트면:
# $env:VITE_API_BASE_URL = "http://your-server:8080/api"

# 3단계: 빌드 실행
npm run build

# 완료 후 확인
ls .\dist
```

✅ **확인**: `dist` 폴더가 생기고 내부에 `index.html` 파일이 있으면 OK

### 6.2 빌드된 파일 배포 폴더로 복사

```powershell
# 기존 폴더 삭제 (있으면)
Remove-Item C:\deploy\aidesk\frontend -Recurse -Force -ErrorAction SilentlyContinue

# 빌드 결과물 복사
Copy-Item .\dist C:\deploy\aidesk\frontend -Recurse

# 확인
ls C:\deploy\aidesk\frontend
# 결과: index.html, js/, css/, img/ 등이 보이면 OK
```

### 6.3 IIS 사이트 생성 (프론트엔드)

**IIS 사이트 생성:**

1. IIS 관리자 → [사이트] → [사이트 추가]
   - **사이트 이름**: `AiDeskFront`
   - **실제 경로**: `C:\deploy\aidesk\frontend`
   - **바인딩 포트**: `80` (또는 원하는 포트)
   - **확인**

2. **응용 프로그램 풀 설정**
   - 좌측 [응용 프로그램 풀] → `AiDeskFrontPool` 우클릭 → 기본 설정
   - **.NET CLR 버전 = "관리 코드 없음"** (정적 HTML이므로 관계없지만 통일)

✅ **확인**: IIS 관리자에서 `AiDeskFront` 사이트가 "시작됨" 상태

### 6.4 SPA 라우팅 설정 (매우 중요!)

**Vue SPA 라우팅이란?** 사용자가 `/dashboard` 같은 URL로 이동할 때, 실제로는 `/index.html`을 로드하고 Vue가 페이지를 그리는 방식입니다.

새로고침 시에도 이 동작이 필요합니다. IIS에서 이를 지원하기 위해 **URL Rewrite** 설정을 합니다.

**파일 생성:** `C:\deploy\aidesk\frontend\web.config`

메모장으로 **새 파일**을 만들고 아래 내용을 넣은 후 저장합니다:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="Vue SPA" stopProcessing="true">
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

> 💡 `<staticContent>` 는 IIS에 이미 기본 MIME 타입이 등록되어 있으므로 추가하면 중복 오류가 납니다. 넣지 마세요.

✅ **확인**: 파일이 `C:\deploy\aidesk\frontend\web.config` 에 있으면 OK

**테스트:**
```powershell
# 프론트엔드 접속
Invoke-WebRequest http://127.0.0.1

# 결과 예시:
# StatusCode : 200  ← 성공
```

---

## 7. IIS ARR 역방향 프록시 설정 (선택 — 단일 포트 운영 시)

**프록시가 뭔가요?** 브라우저 → 프론트(포트 80) → 백엔드(포트 8080)로 요청을 중계해주는 기능입니다. 사용자는 포트 80만 알면 되고, 내부적으로 자동 중계됩니다.

**이 설정이 필요한 경우:**
- 프론트와 백엔드를 같은 포트에서 운영하고 싶을 때
- 예: `http://myserver/` (프론트) + `http://myserver/api/` (백엔드)

**이 설정이 불필요한 경우:**
- 프론트를 포트 80, 백엔드를 포트 8080으로 분리해서 사용
- 예: `http://myserver:80/` (프론트) + `http://myserver:8080/api/` (백엔드)

### 7.1 ARR 프록시 활성화

IIS 관리자에서:

1. 좌측 [서버명] 선택
2. 우측 **"Application Request Routing Cache"** 더블클릭
3. 우측 패널 → **"Server Proxy Settings"**
4. **"Enable proxy"** ☑ 체크
5. **적용**

### 7.2 프론트엔드 사이트에 /api 프록시 규칙 추가

프론트엔드 사이트(`AiDeskFront`)의 `web.config` 를 수정합니다:

`C:\deploy\aidesk\frontend\web.config` 을 열어서 `<rewrite><rules>` 부분에 다음을 **추가**합니다:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <!-- /api/* → 백엔드 API로 역방향 프록시 -->
        <rule name="API Proxy" stopProcessing="true">
          <match url="^api/(.*)" />
          <action type="Rewrite" url="http://127.0.0.1:8080/api/{R:1}" />
        </rule>

        <!-- 기존 Vue SPA 규칙 (아래에 있어야 함) -->
        <rule name="Vue SPA" stopProcessing="true">
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

✅ **확인**: 설정 후 `/api/health` 요청이 내부적으로 `http://127.0.0.1:8080/api/health` 로 라우팅됨

이 방식을 사용하면:
- 프론트의 `VITE_API_BASE_URL = "/api"` 설정이 작동
- 백엔드는 8080 포트에서 내부 통신
- 사용자는 포트 80만 봄

---

## 8. Qdrant (Vector DB) 설치

**Qdrant가 뭔가요?** 벡터 데이터베이스입니다. 임베딩된 문서들을 저장하고, 유사한 문서를 빠르게 검색할 때 사용됩니다.  
**Qdrant는 오픈소스(Apache 2.0)로 완전 무료입니다.** Cloud 버전만 유료입니다.

### 옵션 A: 바이너리 직접 실행 (권장 ✅)

Docker 없이 Qdrant 실행 파일을 바로 실행하는 방식입니다. 설치가 간단하고 Docker 오버헤드 없이 동작합니다.

**설치:**

1. https://github.com/qdrant/qdrant/releases 에서 최신 릴리즈 페이지로 이동
2. `qdrant-x86_64-pc-windows-msvc.zip` 다운로드
3. `C:\deploy\qdrant\` 에 압축 해제

**폴더 준비:**

```powershell
mkdir C:\deploy\qdrant\storage
```

**실행:**

```powershell
# 관리자 PowerShell에서
cd C:\deploy\qdrant
.\qdrant.exe
```

**Windows 서비스로 등록 (부팅 시 자동 시작):**

```powershell
# NSSM(Non-Sucking Service Manager) 사용
# https://nssm.cc/download 에서 다운로드 후 PATH에 추가

nssm install qdrant "C:\deploy\qdrant\qdrant.exe"
nssm set qdrant AppDirectory "C:\deploy\qdrant"
nssm set qdrant Description "Qdrant Vector DB"
nssm start qdrant
```

**확인:**

```powershell
Invoke-WebRequest http://127.0.0.1:6333/health
# StatusCode : 200
```

✅ **확인**: 결과가 200이고 "ok" 메시지가 보이면 정상 실행

---

### 옵션 B: Docker로 실행

Docker Desktop이 이미 설치된 환경이라면 이 방법도 가능합니다.

**Docker 설치:**

1. https://www.docker.com/products/docker-desktop 다운로드
2. 설치 프로그램 실행 (기본 옵션)
3. PowerShell에서 확인:
   ```powershell
   docker --version
   ```

**Qdrant 실행:**

```powershell
docker run -d --name qdrant --restart=always `
  -p 6333:6333 `
  -v C:\deploy\qdrant\storage:/qdrant/storage `
  qdrant/qdrant
```

**확인:**

```powershell
docker ps
Invoke-WebRequest http://127.0.0.1:6333/health
```

---

### 옵션 C: 별도 Linux/Windows 서버에 설치 (대규모 운영)

대규모 트래픽이 예상되면 별도 서버에 Qdrant를 설치합니다.

1. 별도 서버(Linux 권장)에 바이너리 또는 Docker로 설치
2. 백엔드 설정에서 Qdrant URL 변경:

```json
{
  "Qdrant": {
    "Url": "http://10.0.0.20:6333"  // 별도 서버 IP
  }
}
```

그 후 환경 변수 업데이트:
```powershell
[System.Environment]::SetEnvironmentVariable("Qdrant__Url", "http://10.0.0.20:6333", "Machine")
iisreset
```

---

## 9. 외부 서비스/위젯 연동

**위젯이 뭔가요?** 다른 사람의 웹사이트에 AiDesk 챗봇을 임베드하는 기능입니다. 예: WebForms 기반 레거시 시스템에도 추가 가능.

### 9.1 chat-widget.js 위치

빌드 후 `AiDeskClient/public/chat-widget.js` 가 `dist/` 에 그대로 복사됩니다.

**배포된 파일 위치:**
```
http://your-server/chat-widget.js
또는
http://your-server:80/chat-widget.js
```

### 9.2 기존 웹사이트 (ASPX/HTML)에 삽입

**예시: 기존 WebForms 페이지**

`Default.aspx` 또는 `Master.aspx` 의 `</body>` 직전에 추가:

```html
<!-- AiDesk 챗봇 위젯 -->
<script src="http://your-server/chat-widget.js"></script>
<script>
  document.addEventListener('DOMContentLoaded', function () {
    var widget = window.createCrmChatWidget({
      // 필수 설정
      apiBaseUrl: 'http://your-server/api',  // 백엔드 주소
      role: 'user',                           // 사용자 역할
      platform: '링크업',                      // 플랫폼명 (KB 관리에서 등록한 이름과 정확히 일치)
      platformLabel: '링크업 고객지원',         // 인사말에 표시할 이름
      
      // 선택 설정
      title: '링크업 고객 챗봇',
      themeColor: '#185ca2',                  // 채팅 버튼 색상
      
      // 사용자 정보 (ASPX에서 세션값 주입 가능)
      userId: '<%= Session["UserId"] %>',
      username: '<%= Session["UserName"] %>',
      userLoginId: '<%= Session["LoginID"] %>'
    });
    
    // 위젯 표시
    widget.showWidget();
  });
</script>
```

**설정값 설명:**

| 설정 | 필수 | 설명 | 예시 |
|------|------|------|------|
| `apiBaseUrl` | ✓ | 백엔드 API 주소 | `http://192.168.1.100/api` 또는 `/api` (같은 서버) |
| `role` | ✓ | 사용자 역할 | `user`, `admin`, `agent` |
| `platform` | ✓ | 플랫폼 이름 (**KB에 등록된 것과 정확히 일치해야 함**) | `링크업`, `고객지원팀` |
| `platformLabel` |  | 인사말에 표시할 이름 | `링크업 고객 채팅` |
| `title` |  | 위젯 제목 | `고객지원 챗봇` |
| `themeColor` |  | 버튼 색상 (16진수) | `#185ca2` |
| `userId` |  | 사용자 ID | 세션에서 주입 |
| `username` |  | 사용자 이름 | 세션에서 주입 |
| `userLoginId` |  | 사용자 로그인 ID | 세션에서 주입 |

✅ **확인**: 페이지 우측 하단에 "CHAT" 버튼이 떠 있고, 클릭하면 채팅 창이 열리면 OK

---

## 10. 배포 후 헬스 체크

**헬스 체크가 뭔가요?** 각 서비스가 정상 작동하는지 확인하는 과정입니다.

### 10.1 각 서비스 생존 확인

PowerShell에서:

```powershell
# 1. 백엔드 API 생존 확인 (프로세스가 실행 중인가?)
Invoke-WebRequest http://127.0.0.1:8080/health
# 예상 결과: StatusCode 200, Content: "OK"

# 2. 백엔드 준비 상태 확인 (DB + Qdrant 연결됐나?)
Invoke-WebRequest http://127.0.0.1:8080/ready
# 예상 결과: StatusCode 200 (정상) 또는 503 (DB/Qdrant 미준비)

# 3. Qdrant 생존 확인
Invoke-WebRequest http://127.0.0.1:6333/health
# 예상 결과: StatusCode 200, Content: {"ok":true}

# 4. 프론트엔드 확인
Invoke-WebRequest http://127.0.0.1
# 예상 결과: StatusCode 200, 내용에 "<!DOCTYPE html>" 보임
```

### 10.2 오류 대응

만약 위의 요청이 실패하면:

**502 Bad Gateway (백엔드):**
```powershell
# 로그 확인
Get-Content C:\deploy\aidesk\api\logs\stdout*.log -Tail 50

# IIS 재시작
iisreset

# 환경 변수 확인
$env:OpenAI__ApiKey
$env:JwtSettings__SecretKey
```

**503 Service Unavailable (DB/Qdrant):**
```powershell
# DB 연결 문자열 확인 (SSMS로 직접 접속)
# 또는
Invoke-WebRequest http://127.0.0.1:6333/health  # Qdrant 확인

# Qdrant가 안 켜져 있으면 시작
docker start qdrant
```

**404 (프론트엔드 라우팅 오류):**
```powershell
# web.config가 있는지 확인
Test-Path C:\deploy\aidesk\frontend\web.config

# URL Rewrite 모듈 설치 확인
# (IIS 관리자에서 서버 → "URL Rewrite" 표시되는지 확인)
```

---

## 11. 운영 설정 체크리스트

배포 전 아래 항목을 반드시 확인합니다.

| 항목 | 내용 | 확인 |
|------|------|------|
| ASP.NET Core Hosting Bundle | 설치 및 iisreset 완료 | ☐ |
| URL Rewrite 모듈 | IIS에 설치 완료 | ☐ |
| ARR 모듈 | /api 프록시 사용 시 설치 완료 | ☐ |
| MSSQL DB 생성 | AiDeskProd DB + 전용 계정 생성 | ☐ |
| 연결 문자열 | appsettings.Production.json 설정 | ☐ |
| OpenAI API Key | 환경 변수 `OpenAI__ApiKey` 설정 | ☐ |
| JwtSettings SecretKey | 환경 변수 `JwtSettings__SecretKey` 설정 (32자 이상) | ☐ |
| Qdrant 실행 | `/ready` 응답 200 확인 | ☐ |
| CORS AllowedOrigins | 실제 도메인/IP로 설정 | ☐ |
| web.config (SPA fallback) | 프론트 폴더에 배치 | ☐ |
| IIS 앱 풀 | .NET CLR 버전 = 관리 코드 없음 | ☐ |
| 로그 폴더 | `C:\deploy\aidesk\api\logs` 생성 | ☐ |

## 12. 코드 업데이트 배포 절차

이후 새로운 버전을 배포할 때 순서입니다.

```powershell
# 1단계: IIS 사이트 중지 (진행 중인 요청이 끝나도록)
Import-Module WebAdministration
Stop-WebSite -Name "AiDeskApi"

# 2단계: 새 버전 빌드 (개발/빌드 PC에서)
cd C:\Users\YourName\AIDeskPJ
dotnet publish .\AiDeskApi\AiDeskApi.csproj -c Release -o C:\deploy\aidesk\api

# 3단계: 프론트 빌드 및 배포
cd .\AiDeskClient
$env:VITE_API_BASE_URL = "/api"
npm ci
npm run build
Remove-Item C:\deploy\aidesk\frontend -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item .\dist C:\deploy\aidesk\frontend -Recurse

# 4단계: IIS 사이트 재시작
Start-WebSite -Name "AiDeskApi"
iisreset /noforce

# 5단계: 헬스 체크 (위 섹션 9 참고)
Invoke-WebRequest http://127.0.0.1:8080/ready
Invoke-WebRequest http://127.0.0.1
```

💡 **팁**: 업데이트 전에 현재 백업을 권장합니다:
```powershell
Copy-Item C:\deploy\aidesk -Destination C:\deploy\aidesk.backup -Recurse
```

---

## 13. 트러블슈팅 (문제 발생 시)

배포 중 문제가 생기면 이 표를 참고하세요.

| 증상 | 원인 | 해결 방법 |
|------|------|---------|
| **502 Bad Gateway** (백엔드 안 됨) | 1. 백엔드 미실행 2. 포트 불일치 3. 환경변수 누락 | 1. `Get-Content C:\deploy\aidesk\api\logs\stdout*.log -Tail 50` 로그 확인 2. IIS 관리자에서 사이트 상태 확인 3. 환경변수 재설정 후 `iisreset` |
| **500 Internal Server Error** | 1. OpenAI API 키 오류 2. DB 연결 문자열 오류 3. JWT 키 미설정 | 환경변수 재확인: `$env:OpenAI__ApiKey`, `$env:JwtSettings__SecretKey` (값이 보이면 OK) |
| **403 Forbidden** | CORS 설정 오류 | `appsettings.Production.json` 의 `Cors:AllowedOrigins` 에 실제 도메인/IP 추가 |
| **새로고침하면 404** | SPA fallback 미설정 | 1. `C:\deploy\aidesk\frontend\web.config` 존재 확인 2. URL Rewrite 모듈 설치 확인 (IIS 관리자에서 "URL Rewrite" 표시되는가?) |
| **DB 연결 실패** | 1. 연결 문자열 오류 2. MSSQL 서버 미실행 3. 계정 권한 부족 | SSMS에서 직접 로그인 시도: `aidesk_user` / 비밀번호 입력해서 성공하는가? |
| **Qdrant 연결 실패** (`/ready` 반환 503) | 1. Qdrant 미실행 2. 포트 6333 차단 | 1. `docker ps` 로 Qdrant 실행 여부 확인 2. 미실행시 `docker start qdrant` 3. `Invoke-WebRequest http://127.0.0.1:6333/health` 확인 |
| **앱 풀 오류 (.NET CLR)** | 앱 풀을 "관리 코드 없음"이 아닌 다른 설정으로 만듦 | IIS 관리자 → [응용 프로그램 풀] → `AiDeskApiPool` 우클릭 → 고급 설정 → **.NET CLR 버전 = "관리 코드 없음"** |
| **프론트 챗봇 연결 안 됨** | API 주소 오류 (cors domain 미등록) | `appsettings.Production.json` 에 프론트 도메인 등록 후 `iisreset` |
| **로그인 안 됨** | JWT 키 변경됨 또는 Token 만료 | 브라우저 개발자 도구 → Application → 쿠키에서 `token` 삭제 후 재로그인 |

### 디버깅 팁

```powershell
# 백엔드 로그 실시간 확인
Get-Content C:\deploy\aidesk\api\logs\stdout*.log -Wait -Tail 100

# IIS 응용프로그램 풀 상태 확인
Get-WebAppPoolState

# 특정 포트가 누가 사용 중인지 확인 (예: 8080)
Get-NetTCPConnection -LocalPort 8080 | Get-Process

# IIS 완전 재설정 (마지막 수단)
iisreset /restart
```
