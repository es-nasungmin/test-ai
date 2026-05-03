# 환경 설정 가이드

---

## 사전 요구사항

| 항목 | 버전 | 설치 |
|------|------|------|
| .NET SDK | 10.0 이상 | https://dotnet.microsoft.com |
| Node.js | 18 이상 | https://nodejs.org |
| Qdrant | 최신 | 아래 참고 |

---

## 1. Qdrant 실행

### Docker (권장)
```bash
docker run -d --name qdrant -p 6333:6333 qdrant/qdrant
```

### Homebrew (macOS)
```bash
brew install qdrant
qdrant
```

> 기본 주소: `http://localhost:6333`

---

## 2. 백엔드 시크릿 설정 (권장: 환경 변수)

`AiDeskApi/appsettings.Development.json`은 플레이스홀더만 유지하고, 실제 키는 환경 변수로 주입합니다.

```json
{
  "Database": {
    "Provider": "sqlite"
  },
  "OpenAI": {
    "ApiKey": "YOUR_OPENAI_API_KEY",
    "Model": "gpt-4o-mini"
  },
  "Qdrant": {
    "Enabled": true,
    "Url": "http://localhost:6333",
    "CollectionName": "aidesk_kb"
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5173"
    ]
  }
}
```

macOS/Linux:

```bash
export OpenAI__ApiKey="<REAL_OPENAI_KEY>"
export JwtSettings__SecretKey="32자이상충분히긴랜덤문자열"
```

Windows PowerShell:

```powershell
setx OpenAI__ApiKey "<REAL_OPENAI_KEY>"
setx JwtSettings__SecretKey "32자이상충분히긴랜덤문자열"
```

노트:
- ASP.NET Core 규칙에 따라 `:` 구분자는 환경 변수에서 `__`로 치환합니다.
- 운영(Production)에서는 `OpenAI:ApiKey`, `JwtSettings:SecretKey` 미설정 시 앱이 시작되지 않도록 보호됩니다.

### MSSQL 사용 시 (선택)
```json
{
  "Database": {
    "Provider": "mssql"
  },
  "ConnectionStrings": {
    "AiDeskDb": "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;"
  }
}
```

---

## 3. 프론트엔드 환경 변수 설정

`AiDeskClient/.env` 파일 생성 (없으면 기본값 `/api` 사용):
```env
VITE_API_BASE_URL=http://localhost:8080/api
```

> 프록시 없이 직접 API를 호출할 때는 전체 URL로 지정하세요.

---

## 4. OCR 설정 (PDF 문서 KB 사용 시)

macOS:
```bash
brew install tesseract tesseract-lang poppler
```

`appsettings.Development.json`에서 경로 확인:
```json
"Ocr": {
  "TesseractPath": "/opt/homebrew/bin/tesseract",
  "PdfToPpmPath": "/opt/homebrew/bin/pdftoppm",
  "PdfToTextPath": "/opt/homebrew/bin/pdftotext",
  "Language": "kor+eng"
}
```

---

## 5. 실행

```bash
# 한 번에 실행 (백엔드 8080 + 프론트 5173)
./scripts/run-all.sh

# 중지
./scripts/stop-all.sh
```

로그 확인:
```bash
tail -f /tmp/aideskapi.log
tail -f /tmp/aideskclient.log
```

---

## 6. 초기 관리자 계정

앱 최초 실행 시 자동 생성됩니다. `DatabaseInitializer.cs` 참고.  
기본 계정은 코드에서 확인 후 로그인 즉시 비밀번호를 변경하세요.

---

## 7. 데이터 초기화

```bash
# SQLite DB 초기화 (백엔드 중지 후)
rm AiDeskApi/aidesk.db

# Qdrant 벡터 초기화
curl -X DELETE http://localhost:6333/collections/aidesk_kb
```

DB는 다음 실행 시 자동 재생성됩니다.

---

## 8. 운영 환경 배포 시 체크리스트

- [ ] `appsettings.Production.json`의 `Cors:AllowedOrigins`를 실제 도메인으로 교체
- [ ] `JwtSettings:SecretKey`를 충분히 긴 랜덤 문자열로 변경 (32자 이상)
- [ ] `Database:Provider`를 `mssql`로 변경 및 ConnectionString 설정
- [ ] OpenAI API 키를 환경 변수 또는 Secret Manager로 관리
- [ ] Qdrant를 클라우드 또는 별도 서버로 분리
- [ ] HTTPS 적용
