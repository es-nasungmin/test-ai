# Reusable Chat Module

여러 플랫폼에서 공통으로 재사용할 수 있는 Chat 모듈입니다.

## 가장 간단한 사용법 (복붙용)

아래 코드 그대로 페이지의 `</body>` 바로 위에 넣으면 바로 동작합니다.

```html
<script src="/chat-widget.js"></script>
<script>
  const widget = window.createCrmChatWidget({
    apiBaseUrl: 'http://localhost:8080/api',
    role: 'user',
    title: '고객 챗봇',
    defaultPlatform: '전체 플랫폼',
    showPlatformSelector: false,
    buttonLabel: 'Chat',
    initiallyOpen: false
  })

  widget.showWidget()
</script>
```

체크할 건 2개만 있으면 됩니다.

- `apiBaseUrl`만 실제 서버 주소로 맞추기
- 스크립트 위치는 `</body>` 바로 위에 두기

## 구성

- `chatbotApi.js`: API 호출 레이어
- `useChatbotSession.js`: 세션/질문/응답 상태 로직
- `ReusableChatbot.vue`: 바로 붙여서 쓸 수 있는 UI 컴포넌트

## 사용 예시

```vue
<script setup>
import ReusableChatbot from './modules/chatbot/ReusableChatbot.vue'
</script>

<template>
  <ReusableChatbot
    apiBaseUrl="http://localhost:8080/api"
    role="user"
    title="트러스트온 Chat"
    defaultPlatform="트러스트온"
    :showPlatformSelector="false"
    fabLabel="TrustOn"
    accent="#0f766e"
    :initiallyOpen="false"
  />
</template>
```

기본 동작은 `플로팅 버튼 표시 -> 클릭 시 채팅창 오픈`입니다.

## 순수 JavaScript mount 예시

```javascript
import { mountChatbotWidget } from './modules/chatbot'

mountChatbotWidget('#chatbot-root', {
  apiBaseUrl: 'http://localhost:8080/api',
  role: 'user',
  title: '트러스트온 고객 Chat',
  defaultPlatform: '트러스트온',
  showPlatformSelector: false,
  fabLabel: 'TrustOn',
  accent: '#0f766e',
  initiallyOpen: false
})
```

`showPlatformSelector: false`면 `defaultPlatform`으로 고정 전송되고, `showPlatformSelector: true`면 사용자가 플랫폼을 선택해 질문할 수 있습니다.

`fabLabel`로 플로팅 버튼 문구를 서비스명으로 바꿀 수 있습니다.

## 일반 HTML 임베드 예시

`AiDeskClient/public/chat-widget.js`를 사용하면 Vue 없이 일반 HTML에서도 위젯을 띄울 수 있습니다.

```html
<script src="/chat-widget.js"></script>
<script>
  window.createCrmChatWidget({
    apiBaseUrl: 'http://localhost:8080/api',
    role: 'user',
    title: '트러스트온 고객 Chat',
    defaultPlatform: '트러스트온',
    showPlatformSelector: false,
    fabLabel: 'TrustOn',
    accent: '#0f766e'
  })
</script>
```

## 관리자 예시

```javascript
mountChatbotWidget('#admin-chat-root', {
  apiBaseUrl: 'http://localhost:8080/api',
  role: 'admin',
  title: '운영 관리자 Chat',
  defaultPlatform: '전체 플랫폼',
  showPlatformSelector: true,
  fabLabel: 'ADMIN',
  accent: '#c05621'
})
```

## 인라인 테스트 예시

```vue
<ReusableChatbot
  apiBaseUrl="http://localhost:8080/api"
  role="admin"
  title="운영자 테스트 Chat"
  defaultPlatform="전체 플랫폼"
  accent="#c05621"
  :inline="true"
/>
```

`inline`을 켜면 플로팅 버튼 없이 카드 형태로 바로 렌더링됩니다.

## 플랫폼별 재사용 방법

- 서비스 A: `defaultPlatform="트러스트온"`
- 서비스 B: `defaultPlatform="모바일"`
- 공통 운영: `defaultPlatform="전체 플랫폼"`

컴포넌트 하나를 복사하지 말고 props만 바꿔 재사용하는 방식이 권장됩니다.
