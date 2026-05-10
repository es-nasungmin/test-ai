# 환경 설정 가이드

## 사전 요구사항

| 항목 | 버전 |
|------|------|
| .NET SDK | 10.0 이상 |
| Node.js | 18 이상 |
| Qdrant | 최신 |

## 1. Qdrant 실행

Docker (권장):

```bash
docker run -d --name qdrant -p 6333:6333 qdrant/qdrant
```

macOS(Homebrew):

```bash
brew install qdrant
qdrant
```

기본 URL: `http://localhost:6333`

## 2. 백엔드 설정

기본 설정 파일:
- `AiDeskApi/appsettings.json`
- `AiDeskApi/appsettings.Development.json`

핵심 값:

```json
"OpenAI": {
  "ApiKey": "YOUR_OPENAI_API_KEY",
  "Model": "gpt-4o-mini"
},
"Qdrant": {
  "Enabled": true,
  "Url": "http://localhost:6333",
  "CollectionName": "aidesk_kb"
}
```

권장: 실제 키는 환경변수로 주입

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

## 3. 프론트엔드 설정

`AiDeskClient/.env`:

```env
VITE_API_BASE_URL=http://localhost:8080/api
```

## 4. 실행

```bash
./scripts/run-all.sh
```

중지:

```bash
./scripts/stop-all.sh
```

## 5. 상태 확인

```bash
curl http://localhost:8080/health
curl http://localhost:8080/ready
```

## 6. 데이터 초기화

```bash
# SQLite
rm AiDeskApi/aidesk.db

# Qdrant 컬렉션 제거
curl -X DELETE http://localhost:6333/collections/aidesk_kb
```

## 7. 운영 반영 전 체크

- CORS 도메인 정확히 설정
- JWT Secret 32자 이상 랜덤값
- OpenAI API Key 환경변수 주입
- Qdrant 외부화/백업 정책 설정
- 벤치 2종 실행 및 리포트 확인
