<script setup>
import CRMPage from './views/CRMPage.vue'
import FloatingChatbot from './components/FloatingChatbot.vue'
</script>

<template>
  <div class="app">
    <header class="app-header">
      <div class="header-content">
        <h1>AI 테스트 랩</h1>
      </div>
    </header>

    <main class="app-main">
      <div class="page">
        <CRMPage />
      </div>
    </main>

    <!-- 플로팅 챗봇 (모든 페이지에서 표시) -->
    <FloatingChatbot />
  </div>
</template>

<style scoped>
.app {
  min-height: 100vh;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  padding: 0;
}

.app-header {
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  padding: 20px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  position: sticky;
  top: 0;
  z-index: 100;
}

.header-content {
  max-width: 1400px;
  margin: 0 auto;
}

.app-header h1 {
  font-size: 2em;
  margin: 0;
  text-align: center;
}

.app-main {
  padding: 20px;
}

.page {
  max-width: 1400px;
  margin: 0 auto;
}

</style>
