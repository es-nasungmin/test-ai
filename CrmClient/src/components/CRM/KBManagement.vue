<script setup>
import { ref, onMounted, computed } from 'vue'
import axios from 'axios'

const API_URL = 'http://localhost:8080/api'

// ---- 필터 ----
const typeFilter = ref('all')          // all | official | case
const visibilityFilter = ref('common') // all | common | internal
const keyword = ref('')

// ---- 목록 ----
const kbList = ref([])
const loading = ref(false)
const error = ref('')

// ---- 공식 KB 작성 폼 ----
const showOfficialForm = ref(false)
const officialForm = ref({ problem: '', solution: '', tags: '', visibility: 'common' })
const savingOfficial = ref(false)

// ---- 편집 모달 ----
const editingKb = ref(null)
const savingEdit = ref(false)

async function fetchKbs() {
  loading.value = true
  error.value = ''
  try {
    const res = await axios.get(`${API_URL}/knowledgebase/list`, {
      params: { pageSize: 200 }
    })
    kbList.value = res.data.data ?? res.data
  } catch (e) {
    error.value = '목록 불러오기 실패'
  } finally {
    loading.value = false
  }
}

onMounted(fetchKbs)

// ---- 필터링 ----
const totalCount = computed(() => kbList.value.length)
const caseCount = computed(() => kbList.value.filter(kb => (kb.sourceType ?? 'case') === 'case').length)
const officialCount = computed(() => kbList.value.filter(kb => kb.sourceType === 'official').length)

const displayList = computed(() => {
  const q = keyword.value.trim().toLowerCase()
  return kbList.value.filter(kb => {
    const src = kb.sourceType ?? 'case'
    const visibility = kb.visibility ?? 'internal'

    if (typeFilter.value !== 'all' && src !== typeFilter.value) return false
    if (visibilityFilter.value !== 'all' && visibility !== visibilityFilter.value) return false

    if (q) {
      const haystack = `${kb.problem ?? ''} ${kb.solution ?? ''} ${kb.tags ?? ''}`.toLowerCase()
      if (!haystack.includes(q)) return false
    }
    return true
  })
})

// ---- 상태 뱃지 ----
function statusLabel(kb) {
  if (kb.isApproved && kb.visibility === 'common') return { text: '공개', cls: 'badge-open' }
  if (kb.isApproved) return { text: '승인됨', cls: 'badge-approved' }
  return { text: '검토 대기', cls: 'badge-pending' }
}

// ---- 승인 (공개) ----
async function approve(kb) {
  try {
    await axios.put(`${API_URL}/knowledgebase/${kb.id}/approve`, { approvedBy: 'admin' })
    kb.isApproved = true
    kb.visibility = 'common'
  } catch {
    alert('승인 실패')
  }
}

// ---- 공개/내부 토글 ----
async function toggleVisibility(kb) {
  try {
    const res = await axios.put(`${API_URL}/knowledgebase/${kb.id}/visibility`)
    kb.visibility = res.data.visibility
    if (kb.visibility === 'common') kb.isApproved = true
  } catch {
    alert('변경 실패')
  }
}

// ---- 삭제 ----
async function deleteKb(kb) {
  if (!confirm(`"${kb.problem?.substring(0, 30)}..." KB를 삭제하시겠습니까?`)) return
  try {
    await axios.delete(`${API_URL}/knowledgebase/${kb.id}`)
    kbList.value = kbList.value.filter(k => k.id !== kb.id)
  } catch {
    alert('삭제 실패')
  }
}

// ---- 공식 KB 저장 ----
async function saveOfficial() {
  if (!officialForm.value.problem.trim() || !officialForm.value.solution.trim()) {
    alert('질문과 답변을 모두 입력해주세요.')
    return
  }
  savingOfficial.value = true
  try {
    await axios.post(`${API_URL}/knowledgebase/official`, {
      problem: officialForm.value.problem,
      solution: officialForm.value.solution,
      tags: officialForm.value.tags,
      visibility: officialForm.value.visibility,
      approvedBy: officialForm.value.visibility === 'common' ? 'admin' : null
    })
    officialForm.value = { problem: '', solution: '', tags: '', visibility: 'common' }
    showOfficialForm.value = false
    await fetchKbs()
  } catch {
    alert('저장 실패')
  } finally {
    savingOfficial.value = false
  }
}

// ---- 인라인 편집 ----
function startEdit(kb) {
  editingKb.value = { ...kb }
}

async function saveEdit() {
  if (!editingKb.value) return
  savingEdit.value = true
  try {
    // 현재 백엔드에 PUT /knowledgebase/:id 가 없으므로, 삭제 후 재등록
    await axios.delete(`${API_URL}/knowledgebase/${editingKb.value.id}`)
    await axios.post(`${API_URL}/knowledgebase/official`, {
      problem: editingKb.value.problem,
      solution: editingKb.value.solution,
      tags: editingKb.value.tags,
      visibility: editingKb.value.visibility,
      approvedBy: editingKb.value.approvedBy
    })
    editingKb.value = null
    await fetchKbs()
  } catch {
    alert('수정 실패')
  } finally {
    savingEdit.value = false
  }
}
</script>

<template>
  <div class="kb-mgmt">
    <!-- 헤더 -->
    <div class="kb-header">
      <div class="summary-group">
        <span class="summary-chip">전체 {{ totalCount }}</span>
        <span class="summary-chip official">공식 {{ officialCount }}</span>
        <span class="summary-chip case">상담추출 {{ caseCount }}</span>
      </div>

      <div class="header-actions">
        <button class="btn-refresh" @click="fetchKbs" :disabled="loading">
          {{ loading ? '⏳' : '🔄' }} 새로고침
        </button>
        <button class="btn-add" @click="showOfficialForm = !showOfficialForm">
          + 공식 KB 작성
        </button>
      </div>
    </div>

    <div class="filter-row">
      <select v-model="typeFilter" class="filter-select">
        <option value="all">타입: 전체</option>
        <option value="official">타입: 공식 KB</option>
        <option value="case">타입: 상담추출</option>
      </select>

      <select v-model="visibilityFilter" class="filter-select">
        <option value="all">공개범위: 전체</option>
        <option value="common">공개범위: 공개</option>
        <option value="internal">공개범위: 내부</option>
      </select>

      <input
        v-model="keyword"
        class="filter-input"
        placeholder="문제/해결/태그 검색"
      />
    </div>

    <!-- 범례 -->
    <div class="legend">
      <span class="badge badge-open">공개</span> 고객 챗봇 반영
      <span class="badge badge-pending">내부</span> 관리자만 참고
    </div>

    <!-- 공식 KB 작성 폼 -->
    <transition name="slide">
      <div v-if="showOfficialForm" class="official-form">
        <h4>📘 공식 KB 작성</h4>
        <div class="form-row">
          <label>
            질문 / 문제
            <textarea v-model="officialForm.problem" rows="2" placeholder="예) 세금계산서 조회가 안 됩니다" />
          </label>
        </div>
        <div class="form-row">
          <label>
            공식 답변
            <textarea v-model="officialForm.solution" rows="4" placeholder="예) 홈택스 → 공인인증서 로그인 후 조회..." />
          </label>
        </div>
        <div class="form-row two-col">
          <label>
            태그 (쉼표 구분)
            <input v-model="officialForm.tags" placeholder="홈택스, 세금계산서" />
          </label>
          <label>
            공개 범위
            <select v-model="officialForm.visibility">
              <option value="common">공개 (일반 사용자)</option>
              <option value="internal">내부 (관리자 전용)</option>
            </select>
          </label>
        </div>
        <div class="form-actions">
          <button class="btn-save" @click="saveOfficial" :disabled="savingOfficial">
            {{ savingOfficial ? '저장 중...' : '저장' }}
          </button>
          <button class="btn-cancel" @click="showOfficialForm = false">취소</button>
        </div>
      </div>
    </transition>

    <!-- 에러 -->
    <div v-if="error" class="error-msg">{{ error }}</div>

    <!-- KB 목록 -->
    <div v-if="loading" class="loading-msg">불러오는 중...</div>

    <div v-else-if="displayList.length === 0" class="empty-msg">
      필터 조건에 맞는 KB가 없습니다.
    </div>

    <div v-else class="kb-list">
      <div v-for="kb in displayList" :key="kb.id" class="kb-card">
        <!-- 표시 모드 -->
        <template v-if="editingKb?.id !== kb.id">
          <div class="kb-card-header">
            <div class="kb-meta">
              <span :class="['badge', statusLabel(kb).cls]">{{ statusLabel(kb).text }}</span>
              <span class="kb-source-type" v-if="kb.sourceType === 'official'">📘 공식</span>
              <span class="kb-source-type case" v-else>📋 사례</span>
              <span v-if="kb.tags" class="kb-tags">🏷 {{ kb.tags }}</span>
              <span class="kb-date">{{ new Date(kb.createdAt).toLocaleDateString('ko-KR') }}</span>
            </div>
            <div class="kb-card-actions">
              <!-- 승인 버튼: 미승인 상태일 때만 -->
              <button
                v-if="!kb.isApproved"
                class="btn-approve"
                @click="approve(kb)"
                title="공개 승인"
              >
                ✅ 공개 승인
              </button>
              <!-- 토글 버튼: 승인된 상태 -->
              <button
                v-else
                class="btn-toggle"
                :class="kb.visibility === 'common' ? 'is-public' : 'is-internal'"
                @click="toggleVisibility(kb)"
                title="공개/내부 전환"
              >
                {{ kb.visibility === 'common' ? '🔒 내부로' : '🌐 공개로' }}
              </button>
              <button
                v-if="kb.sourceType === 'official'"
                class="btn-edit"
                @click="startEdit(kb)"
                title="편집"
              >✏️</button>
              <button class="btn-delete" @click="deleteKb(kb)" title="삭제">🗑️</button>
            </div>
          </div>
          <div class="kb-problem">
            <strong>Q.</strong> {{ kb.problem }}
          </div>
          <div class="kb-solution">
            <strong>A.</strong> {{ kb.solution }}
          </div>
        </template>

        <!-- 편집 모드 -->
        <template v-else>
          <div class="edit-form-inline">
            <label>Q. (질문/문제)</label>
            <textarea v-model="editingKb.problem" rows="2" />
            <label>A. (답변/해결방법)</label>
            <textarea v-model="editingKb.solution" rows="3" />
            <div class="two-col">
              <label>
                태그
                <input v-model="editingKb.tags" placeholder="쉼표 구분" />
              </label>
              <label>
                공개 범위
                <select v-model="editingKb.visibility">
                  <option value="common">공개</option>
                  <option value="internal">내부</option>
                </select>
              </label>
            </div>
            <div class="edit-actions">
              <button class="btn-save" @click="saveEdit" :disabled="savingEdit">
                {{ savingEdit ? '저장 중...' : '저장' }}
              </button>
              <button class="btn-cancel" @click="editingKb = null">취소</button>
            </div>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>

<style scoped>
.kb-mgmt { display: flex; flex-direction: column; gap: 12px; }

/* 헤더 */
.kb-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.summary-group {
  display: flex;
  align-items: center;
  gap: 6px;
}

.summary-chip {
  padding: 5px 12px;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  font-size: 0.82em;
  font-weight: 600;
  color: #718096;
}
.summary-chip.official { background: #dbeafe; color: #1e40af; }
.summary-chip.case { background: #d1fae5; color: #065f46; }

.filter-row {
  display: grid;
  grid-template-columns: 180px 180px 1fr;
  gap: 8px;
}

.filter-select,
.filter-input {
  border: 1px solid #d1d5db;
  border-radius: 8px;
  padding: 8px 10px;
  font-size: 0.88em;
}

.filter-select:focus,
.filter-input:focus {
  outline: none;
  border-color: #667eea;
}

.header-actions { display: flex; gap: 8px; }

.btn-refresh {
  padding: 6px 12px;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  background: white;
  font-size: 0.85em;
  cursor: pointer;
  color: #4a5568;
}
.btn-refresh:hover { background: #f7fafc; }

.btn-add {
  padding: 6px 14px;
  background: #667eea;
  color: white;
  border: none;
  border-radius: 6px;
  font-size: 0.88em;
  font-weight: 600;
  cursor: pointer;
}
.btn-add:hover { background: #5a67d8; }

/* 범례 */
.legend {
  font-size: 0.82em;
  color: #718096;
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
}

/* 뱃지 */
.badge {
  display: inline-block;
  font-size: 0.78em;
  padding: 2px 8px;
  border-radius: 10px;
  font-weight: 600;
}
.badge-pending { background: #fef3c7; color: #92400e; }
.badge-approved { background: #dbeafe; color: #1e40af; }
.badge-open { background: #d1fae5; color: #065f46; }

/* 공식 KB 작성 폼 */
.official-form {
  background: #f0f4ff;
  border: 1px solid #c3d3ff;
  border-radius: 10px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.official-form h4 { margin: 0; color: #4a5568; font-size: 0.95em; }
.form-row label { display: flex; flex-direction: column; gap: 4px; font-size: 0.85em; font-weight: 600; color: #4a5568; }
.form-row textarea, .form-row input, .form-row select {
  border: 1px solid #c3d3ff;
  border-radius: 6px;
  padding: 7px 10px;
  font-size: 0.88em;
  font-family: inherit;
  resize: vertical;
}
.form-row textarea:focus, .form-row input:focus, .form-row select:focus {
  outline: none;
  border-color: #667eea;
}
.two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }

.form-actions, .edit-actions { display: flex; gap: 8px; }
.btn-save {
  padding: 6px 18px;
  background: #667eea;
  color: white;
  border: none;
  border-radius: 6px;
  font-size: 0.88em;
  font-weight: 600;
  cursor: pointer;
}
.btn-save:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-save:hover:not(:disabled) { background: #5a67d8; }
.btn-cancel {
  padding: 6px 14px;
  border: 1px solid #e2e8f0;
  background: white;
  border-radius: 6px;
  font-size: 0.88em;
  cursor: pointer;
  color: #718096;
}
.btn-cancel:hover { background: #f7fafc; }

/* KB 목록 */
.kb-list { display: flex; flex-direction: column; gap: 10px; }

.kb-card {
  background: white;
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  padding: 14px 16px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  transition: box-shadow 0.15s;
}
.kb-card:hover { box-shadow: 0 2px 8px rgba(0,0,0,0.07); }

.kb-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
}
.kb-meta { display: flex; align-items: center; gap: 7px; flex-wrap: wrap; }

.kb-source-type {
  font-size: 0.78em;
  color: #1e40af;
  background: #dbeafe;
  border-radius: 4px;
  padding: 1px 7px;
  font-weight: 600;
}
.kb-source-type.case { color: #065f46; background: #d1fae5; }

.kb-tags { font-size: 0.78em; color: #718096; }
.kb-date { font-size: 0.75em; color: #a0aec0; }

.kb-card-actions { display: flex; gap: 6px; align-items: center; }

.btn-approve {
  padding: 4px 12px;
  background: #38a169;
  color: white;
  border: none;
  border-radius: 6px;
  font-size: 0.82em;
  font-weight: 600;
  cursor: pointer;
}
.btn-approve:hover { background: #2f855a; }

.btn-toggle {
  padding: 4px 12px;
  border: none;
  border-radius: 6px;
  font-size: 0.82em;
  font-weight: 600;
  cursor: pointer;
}
.btn-toggle.is-public { background: #ebf8ff; color: #2b6cb0; }
.btn-toggle.is-public:hover { background: #bee3f8; }
.btn-toggle.is-internal { background: #fff5f5; color: #c53030; }
.btn-toggle.is-internal:hover { background: #fed7d7; }

.btn-edit {
  padding: 4px 8px;
  background: #f7fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  font-size: 0.82em;
  cursor: pointer;
}
.btn-edit:hover { background: #edf2f7; }

.btn-delete {
  padding: 4px 8px;
  background: #fff5f5;
  border: 1px solid #fed7d7;
  color: #c53030;
  border-radius: 6px;
  font-size: 0.82em;
  cursor: pointer;
}
.btn-delete:hover { background: #fed7d7; }

.kb-problem, .kb-solution {
  font-size: 0.88em;
  color: #2d3748;
  line-height: 1.5;
}
.kb-problem strong, .kb-solution strong { color: #667eea; margin-right: 4px; }

/* 편집 인라인 */
.edit-form-inline {
  display: flex;
  flex-direction: column;
  gap: 8px;
  font-size: 0.88em;
}
.edit-form-inline label { font-weight: 600; color: #4a5568; }
.edit-form-inline textarea, .edit-form-inline input, .edit-form-inline select {
  border: 1px solid #c3d3ff;
  border-radius: 6px;
  padding: 7px 10px;
  font-family: inherit;
  font-size: 0.95em;
  resize: vertical;
}
.edit-form-inline .two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
.edit-form-inline .two-col label { display: flex; flex-direction: column; gap: 4px; }

/* 기타 */
.loading-msg, .empty-msg { text-align: center; color: #a0aec0; font-size: 0.9em; padding: 32px 0; line-height: 1.8; }
.error-msg { color: #c53030; font-size: 0.88em; background: #fff5f5; border-radius: 6px; padding: 8px 12px; }

/* 트랜지션 */
.slide-enter-active, .slide-leave-active { transition: all 0.2s ease; }
.slide-enter-from, .slide-leave-to { opacity: 0; transform: translateY(-8px); }

@media (max-width: 900px) {
  .filter-row {
    grid-template-columns: 1fr;
  }

  .kb-header {
    align-items: flex-start;
  }
}
</style>
