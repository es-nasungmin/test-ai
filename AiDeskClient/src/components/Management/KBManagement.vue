<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import axios from 'axios'

const API_URL = 'http://localhost:8080/api'

const kbList = ref([])
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const expandedId = ref(null)
const showWriter = ref(false)
const kbPage = ref(1)
const kbPageSize = 20
const kbTotal = ref(0)
const lowSimilarityQuestions = ref([])
const loadingLowSimilarity = ref(false)
const lowSimilarityPage = ref(1)
const lowSimilarityPageSize = 20
const lowSimilarityTotal = ref(0)
const lowSimilarityPlatformFilter = ref('all')
const platformOptions = ref(['공통'])
const newPlatformName = ref('')
const addingPlatform = ref(false)
const renamingPlatform = ref('')
const renamingPlatformName = ref('')
const savingRename = ref(false)
const deletingPlatform = ref('')
const showPlatformEditor = ref(false)
const showSimilarityGuide = ref(false)

const keyword = ref('')
const visibilityFilter = ref('all')
const kbPlatformFilter = ref('all')

const form = ref({
  id: null,
  representativeQuestion: '',
  solution: '',
  visibility: 'user',
  platforms: ['공통'],
  similarInput: ''
})
const tagInput = ref('')
const tagDraft = ref([])
const platformInput = ref('공통')
const isComposingTag = ref(false)

function normalizeSelectedPlatforms(platforms) {
  const normalized = Array.from(new Set((platforms || [])
    .map((x) => (typeof x === 'string' ? x.trim() : ''))
    .filter(Boolean)))

  if (normalized.length === 0) return ['공통']
  if (normalized.length > 1 && normalized.includes('공통')) {
    return normalized.filter((x) => x !== '공통')
  }
  return normalized
}

function normalizeTags(tags) {
  return Array.from(new Set((tags || [])
    .map((x) => (typeof x === 'string' ? x.trim() : ''))
    .filter(Boolean)))
}

function extractPlatforms(kb) {
  if (Array.isArray(kb.platforms) && kb.platforms.length > 0) {
    return normalizeSelectedPlatforms(kb.platforms)
  }
  if (typeof kb.platform === 'string' && kb.platform.trim()) {
    return normalizeSelectedPlatforms(kb.platform.split(','))
  }
  return ['공통']
}

const similarDraft = ref([])
const isComposingSimilar = ref(false)
const lastAdded = ref({ text: '', at: 0 })
const generatingSimilar = ref(false)
const MAX_SIMILAR_QUESTIONS = 10

const kbTotalPages = computed(() => Math.max(1, Math.ceil(kbTotal.value / kbPageSize)))
const lowSimilarityTotalPages = computed(() => Math.max(1, Math.ceil(lowSimilarityTotal.value / lowSimilarityPageSize)))

async function fetchKbs() {
  loading.value = true
  error.value = ''
  try {
    const res = await axios.get(`${API_URL}/knowledgebase/list`, {
      params: {
        page: kbPage.value,
        pageSize: kbPageSize,
        keyword: keyword.value.trim() || undefined,
        visibility: visibilityFilter.value,
        platform: kbPlatformFilter.value
      }
    })
    kbList.value = res.data.data || []
    kbTotal.value = Number(res.data.total || 0)
  } catch {
    error.value = 'KB 목록을 불러오지 못했습니다.'
  } finally {
    loading.value = false
  }
}

async function fetchPlatforms() {
  try {
    const res = await axios.get(`${API_URL}/knowledgebase/platforms`)
    const list = Array.isArray(res.data) ? res.data : []
    const normalized = list
      .map((x) => (typeof x === 'string' ? x.trim() : ''))
      .filter(Boolean)
    platformOptions.value = Array.from(new Set(['공통', ...normalized]))

    form.value.platforms = normalizeSelectedPlatforms(
      form.value.platforms.filter((p) => platformOptions.value.includes(p))
    )
    if (!platformOptions.value.includes(platformInput.value)) {
      platformInput.value = form.value.platforms[0] || platformOptions.value[0] || '공통'
    }
    if (kbPlatformFilter.value !== 'all' && !platformOptions.value.includes(kbPlatformFilter.value)) {
      kbPlatformFilter.value = 'all'
    }
    if (lowSimilarityPlatformFilter.value !== 'all' && !platformOptions.value.includes(lowSimilarityPlatformFilter.value)) {
      lowSimilarityPlatformFilter.value = 'all'
    }
  } catch {
    platformOptions.value = ['공통']
  }
}

async function addPlatform() {
  const name = newPlatformName.value.trim()
  if (!name) {
    alert('플랫폼명을 입력해주세요.')
    return
  }

  addingPlatform.value = true
  try {
    const res = await axios.post(`${API_URL}/knowledgebase/platforms`, { name })
    const added = res.data?.name || name
    if (!platformOptions.value.includes(added)) {
      platformOptions.value.push(added)
    }
    form.value.platforms = normalizeSelectedPlatforms([...form.value.platforms, added])
    newPlatformName.value = ''
    await fetchPlatforms()
    await fetchLowSimilarityQuestions()
  } catch (err) {
    alert(err.response?.data?.error || '플랫폼 추가에 실패했습니다.')
  } finally {
    addingPlatform.value = false
  }
}

function openPlatformEditor() {
  newPlatformName.value = ''
  cancelRenamePlatform()
  showPlatformEditor.value = true
}

function startRenamePlatform(name) {
  if (name === '공통') return
  renamingPlatform.value = name
  renamingPlatformName.value = name
}

function cancelRenamePlatform() {
  renamingPlatform.value = ''
  renamingPlatformName.value = ''
}

async function submitRenamePlatform() {
  const oldName = renamingPlatform.value
  const newName = renamingPlatformName.value.trim()
  if (!oldName || !newName) {
    alert('변경할 플랫폼명을 입력해주세요.')
    return
  }

  savingRename.value = true
  try {
    await axios.put(`${API_URL}/knowledgebase/platforms/${encodeURIComponent(oldName)}`, {
      newName
    })
    cancelRenamePlatform()
    await fetchPlatforms()
    await fetchKbs()
    await fetchLowSimilarityQuestions()
  } catch (err) {
    alert(err.response?.data?.error || '플랫폼 수정에 실패했습니다.')
  } finally {
    savingRename.value = false
  }
}

async function removePlatform(name) {
  if (name === '공통') {
    alert('기본 플랫폼(공통)은 삭제할 수 없습니다.')
    return
  }
  if (!confirm(`플랫폼 '${name}'을(를) 삭제할까요? 해당 플랫폼 KB/채팅/모니터링 데이터가 모두 삭제됩니다.`)) return

  deletingPlatform.value = name
  try {
    await axios.delete(`${API_URL}/knowledgebase/platforms/${encodeURIComponent(name)}`)
    form.value.platforms = normalizeSelectedPlatforms(
      form.value.platforms.filter((p) => p !== name)
    )
    if (platformInput.value === name) {
      platformInput.value = form.value.platforms[0] || '공통'
    }
    if (lowSimilarityPlatformFilter.value === name) lowSimilarityPlatformFilter.value = 'all'
    await fetchPlatforms()
    await fetchKbs()
    await fetchLowSimilarityQuestions()
  } catch (err) {
    alert(err.response?.data?.error || '플랫폼 삭제에 실패했습니다.')
  } finally {
    deletingPlatform.value = ''
  }
}

async function fetchLowSimilarityQuestions() {
  loadingLowSimilarity.value = true
  try {
    const params = {
      page: lowSimilarityPage.value,
      pageSize: lowSimilarityPageSize,
      platform: lowSimilarityPlatformFilter.value === 'all' ? undefined : lowSimilarityPlatformFilter.value
    }
    const res = await axios.get(`${API_URL}/knowledgebase/low-similarity-questions`, { params })
    lowSimilarityQuestions.value = res.data?.data || []
    lowSimilarityTotal.value = Number(res.data?.total || 0)
  } catch {
    // keep silent in UI; main KB experience should not break
  } finally {
    loadingLowSimilarity.value = false
  }
}

function addSimilarFromInput() {
  const value = form.value.similarInput.trim()
  if (!value) return

  if (similarDraft.value.length >= MAX_SIMILAR_QUESTIONS) {
    alert(`유사질문은 최대 ${MAX_SIMILAR_QUESTIONS}개까지 등록 가능합니다.`)
    form.value.similarInput = ''
    return
  }

  // Prevent accidental double-add when IME enter events are emitted back-to-back.
  const now = Date.now()
  if (lastAdded.value.text === value && now - lastAdded.value.at < 400) {
    form.value.similarInput = ''
    return
  }

  const exists = similarDraft.value.some((x) => x.toLowerCase() === value.toLowerCase())
  if (!exists) {
    similarDraft.value.push(value)
    lastAdded.value = { text: value, at: now }
  }
  form.value.similarInput = ''
}

function onSimilarEnter() {
  if (isComposingSimilar.value) return
  addSimilarFromInput()
}

function removeSimilar(index) {
  similarDraft.value.splice(index, 1)
}

async function generateSimilarQuestions() {
  if (!form.value.solution.trim()) {
    alert('답변을 먼저 작성해주세요.')
    return
  }

  generatingSimilar.value = true
  try {
    const res = await axios.post(`${API_URL}/knowledgebase/generate-similar-questions`, {
      representativeQuestion: form.value.representativeQuestion,
      solution: form.value.solution,
      count: 3
    })

    const items = Array.isArray(res.data?.items) ? res.data.items : []
    const room = Math.max(0, MAX_SIMILAR_QUESTIONS - similarDraft.value.length)
    const merged = [...similarDraft.value, ...items]
      .map((x) => (typeof x === 'string' ? x.trim() : ''))
      .filter(Boolean)
      .filter((x) => !form.value.representativeQuestion.trim() || x.toLowerCase() !== form.value.representativeQuestion.trim().toLowerCase())

    similarDraft.value = Array.from(new Set(merged.map((x) => x.toLowerCase())))
      .map((key) => merged.find((item) => item.toLowerCase() === key))
      .filter(Boolean)
      .slice(0, MAX_SIMILAR_QUESTIONS)

    if (items.length === 0) {
      alert('추가할 예상 질문을 생성하지 못했습니다.')
    } else if (room === 0) {
      alert(`유사질문은 최대 ${MAX_SIMILAR_QUESTIONS}개까지 등록 가능합니다.`)
    } else if (items.length > room) {
      alert(`남은 자리만큼만 추가되었습니다. 유사질문은 최대 ${MAX_SIMILAR_QUESTIONS}개까지 등록 가능합니다.`)
    }
  } catch (err) {
    if (err.code === 'ERR_NETWORK') {
      alert('예상 질문 생성 API에 연결할 수 없습니다. 백엔드 서버가 실행 중인지 확인해주세요.')
    } else if (err.response?.data?.error) {
      alert(`예상 질문 생성 실패: ${err.response.data.error}`)
    } else if (err.response?.status) {
      alert(`예상 질문 생성 실패: 서버 오류 (${err.response.status})`)
    } else {
      alert(`예상 질문 생성 실패: ${err.message || '알 수 없는 오류'}`)
    }
  } finally {
    generatingSimilar.value = false
  }
}

function resetForm() {
  form.value = {
    id: null,
    representativeQuestion: '',
    solution: '',
    visibility: 'user',
    platforms: ['공통'],
    similarInput: ''
  }
  tagInput.value = ''
  tagDraft.value = []
  platformInput.value = '공통'
  similarDraft.value = []
}

function startEdit(kb) {
  showWriter.value = true
  form.value.id = kb.id
  form.value.representativeQuestion = kb.representativeQuestion || ''
  form.value.solution = kb.solution || ''
  form.value.visibility = kb.visibility || 'user'
  form.value.platforms = extractPlatforms(kb)
  form.value.similarInput = ''
  tagInput.value = ''
  tagDraft.value = normalizeTags((kb.tags || '').split(','))
  platformInput.value = form.value.platforms[0] || '공통'
  similarDraft.value = (kb.similarQuestions || []).map((item) => item.question)
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

function addTagFromInput() {
  const value = tagInput.value.trim()
  if (!value) return
  tagDraft.value = normalizeTags([...tagDraft.value, value])
  tagInput.value = ''
}

function onTagEnter() {
  if (isComposingTag.value) return
  addTagFromInput()
}

function removeTag(index) {
  tagDraft.value.splice(index, 1)
}

function addPlatformToForm() {
  const selected = (platformInput.value || '').trim()
  if (!selected) return
  form.value.platforms = normalizeSelectedPlatforms([...form.value.platforms, selected])
}

function removePlatformFromForm(index) {
  const current = form.value.platforms[index]
  form.value.platforms = normalizeSelectedPlatforms(
    form.value.platforms.filter((_, i) => i !== index)
  )
  if (platformInput.value === current && form.value.platforms.length > 0) {
    platformInput.value = form.value.platforms[0]
  }
}

async function saveKb() {
  if (!form.value.representativeQuestion.trim() || !form.value.solution.trim()) {
    alert('대표질문과 답변은 필수입니다.')
    return
  }

  saving.value = true
  try {
    const payload = {
      representativeQuestion: form.value.representativeQuestion,
      solution: form.value.solution,
      tags: normalizeTags(tagDraft.value).join(', '),
      visibility: form.value.visibility,
      platforms: normalizeSelectedPlatforms(form.value.platforms),
      similarQuestions: similarDraft.value
    }

    if (form.value.id) {
      await axios.put(`${API_URL}/knowledgebase/${form.value.id}`, payload)
    } else {
      await axios.post(`${API_URL}/knowledgebase`, payload)
    }

    resetForm()
    kbPage.value = 1
    await fetchKbs()
  } catch {
    alert('저장에 실패했습니다.')
  } finally {
    saving.value = false
  }
}

async function deleteKb(kb) {
  if (!confirm('해당 KB를 삭제하시겠습니까?')) return
  try {
    await axios.delete(`${API_URL}/knowledgebase/${kb.id}`)
    if (expandedId.value === kb.id) expandedId.value = null
    if (kbList.value.length === 1 && kbPage.value > 1) {
      kbPage.value -= 1
    }
    await fetchKbs()
  } catch {
    alert('삭제에 실패했습니다.')
  }
}

async function resolveLowSimilarityQuestion(item) {
  try {
    await axios.put(`${API_URL}/knowledgebase/low-similarity-questions/${item.id}/resolve`)
    if (lowSimilarityQuestions.value.length === 1 && lowSimilarityPage.value > 1) {
      lowSimilarityPage.value -= 1
    }
    await fetchLowSimilarityQuestions()
  } catch {
    alert('처리 상태 변경에 실패했습니다.')
  }
}

function goKbPage(page) {
  const next = Math.min(Math.max(1, page), kbTotalPages.value)
  if (next === kbPage.value) return
  kbPage.value = next
  fetchKbs()
}

function goLowSimilarityPage(page) {
  const next = Math.min(Math.max(1, page), lowSimilarityTotalPages.value)
  if (next === lowSimilarityPage.value) return
  lowSimilarityPage.value = next
  fetchLowSimilarityQuestions()
}

watch([keyword, visibilityFilter, kbPlatformFilter], () => {
  kbPage.value = 1
  fetchKbs()
})

watch(lowSimilarityPlatformFilter, () => {
  lowSimilarityPage.value = 1
  fetchLowSimilarityQuestions()
})

function toggleExpanded(id) {
  expandedId.value = expandedId.value === id ? null : id
}

function formatDateTime(value) {
  if (!value) return '-'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '-'
  return date.toLocaleString('ko-KR')
}

onMounted(async () => {
  await fetchPlatforms()
  await fetchKbs()
  await fetchLowSimilarityQuestions()
})
</script>

<template>
  <section class="kb-wrap">
    <div class="panel form-panel" :class="{ collapsed: !showWriter }">
      <div class="panel-head">
        <div class="panel-head-title">
          <h3>{{ form.id ? 'KB 수정' : 'KB 작성' }}</h3>
          <button class="ghost guide-trigger" type="button" @click="showSimilarityGuide = true">유사도를 높이려면?</button>
        </div>
        <div class="panel-head-actions">
          <button class="ghost" type="button" @click="showWriter = !showWriter">
            {{ showWriter ? '작성 영역 닫기' : '작성 영역 열기' }}
          </button>
          <button class="ghost" @click="resetForm">초기화</button>
        </div>
      </div>

      <div v-if="showWriter" class="form-grid">
        <label>
          공개수준
          <select v-model="form.visibility">
            <option value="user">사용자 공개</option>
            <option value="admin">관리자 전용</option>
          </select>
        </label>

        <label>
          대표질문
          <textarea v-model="form.representativeQuestion" rows="2" placeholder="예) 인증서 조회가 안 돼요" />
        </label>

        <label>
          답변
          <textarea v-model="form.solution" rows="4" placeholder="예) 인증서 위치를 확인한 뒤 다시 로그인하세요..." />
        </label>

        <label>
          태그(선택)
          <div class="similar-input-row">
            <input
              v-model="tagInput"
              placeholder="예) 인증서"
              @keydown.enter.prevent="onTagEnter"
              @compositionstart="isComposingTag = true"
              @compositionend="isComposingTag = false"
            />
            <button class="secondary" type="button" @click="addTagFromInput">추가</button>
          </div>
          <div class="chips" v-if="tagDraft.length > 0">
            <span v-for="(item, idx) in tagDraft" :key="`tag-${item}-${idx}`" class="chip">
              #{{ item }}
              <button type="button" @click="removeTag(idx)">×</button>
            </span>
          </div>
        </label>

        <div class="field-block">
          <div class="label-head-inline">
            <span class="field-title">플랫폼</span>
            <button class="ghost" type="button" @click="openPlatformEditor">관리</button>
          </div>
          <div class="field-header">
            <select v-model="platformInput">
              <option v-for="p in platformOptions" :key="p" :value="p">{{ p }}</option>
            </select>
            <div class="field-actions">
              <button class="secondary" type="button" @click="addPlatformToForm">추가</button>
            </div>
          </div>
          <div class="chips" v-if="form.platforms.length > 0">
            <span v-for="(item, idx) in form.platforms" :key="`platform-${item}-${idx}`" class="chip">
              {{ item }}
              <button type="button" @click="removePlatformFromForm(idx)">×</button>
            </span>
          </div>
          <small class="hint">복수 선택 가능 (공통은 단독 선택 시에만 유지)</small>
        </div>

        <label>
          <div class="label-head-inline">
            <span class="field-title">유사질문 추가</span>
            <button class="ghost" type="button" :disabled="generatingSimilar" @click="generateSimilarQuestions">
              {{ generatingSimilar ? '생성 중...' : '예상질문 생성하기' }}
            </button>
          </div>
          <div class="similar-input-row">
            <input
              v-model="form.similarInput"
              placeholder="예) 인증서 경로가 어디에 있나요?"
              @keydown.enter.prevent="onSimilarEnter"
              @compositionstart="isComposingSimilar = true"
              @compositionend="isComposingSimilar = false"
            />
            <button class="secondary" @click="addSimilarFromInput">추가</button>
          </div>
          <small class="hint">예상질문 생성은 3개씩 추가되며, 유사질문은 최대 10개까지 등록 가능합니다.</small>
        </label>

        <div class="chips" v-if="similarDraft.length > 0">
          <span v-for="(item, idx) in similarDraft" :key="`${item}-${idx}`" class="chip">
            {{ item }}
            <button @click="removeSimilar(idx)">×</button>
          </span>
        </div>
      </div>

      <div v-if="showWriter" class="actions">
        <button class="primary" :disabled="saving" @click="saveKb">
          {{ saving ? '저장 중...' : form.id ? '수정 저장' : 'KB 저장' }}
        </button>
      </div>
    </div>

    <div class="panel list-panel">
      <div class="panel-head">
        <h3>KB 목록</h3>
        <button class="ghost" :disabled="loading" @click="fetchKbs">새로고침</button>
      </div>

      <div class="filters">
        <select v-model="kbPlatformFilter">
          <option value="all">플랫폼 전체</option>
          <option v-for="p in platformOptions" :key="`kb-${p}`" :value="p">{{ p }}</option>
        </select>
        <select v-model="visibilityFilter">
          <option value="all">공개수준 전체</option>
          <option value="user">사용자 공개</option>
          <option value="admin">관리자 전용</option>
        </select>
        <input v-model="keyword" placeholder="대표질문/유사질문/답변/태그 검색" />
      </div>

      <div v-if="error" class="error">{{ error }}</div>
      <div v-else-if="loading" class="empty">불러오는 중...</div>
      <div v-else-if="kbList.length === 0" class="empty">등록된 KB가 없습니다.</div>

      <div v-else class="kb-list">
        <article v-for="kb in kbList" :key="kb.id" class="kb-item">
          <div class="kb-top">
            <button class="q-btn" @click="toggleExpanded(kb.id)">
              Q. {{ kb.representativeQuestion }}
            </button>
            <span class="scope" :class="kb.visibility">
              {{ kb.visibility === 'user' ? '사용자 공개' : '관리자 전용' }}
            </span>
          </div>

          <p class="answer">A. {{ kb.solution }}</p>

          <div class="meta">
            <span v-if="kb.tags">태그: {{ kb.tags }}</span>
            <span>플랫폼: {{ extractPlatforms(kb).join(', ') }}</span>
            <span>조회수: {{ kb.viewCount }}</span>
            <span>등록일: {{ formatDateTime(kb.createdAt) }}</span>
            <span>수정일: {{ formatDateTime(kb.updatedAt) }}</span>
          </div>

          <div v-if="expandedId === kb.id" class="similar-box">
            <h4>유사질문</h4>
            <ul v-if="kb.similarQuestions && kb.similarQuestions.length > 0">
              <li v-for="item in kb.similarQuestions" :key="item.id">{{ item.question }}</li>
            </ul>
            <p v-else>등록된 유사질문이 없습니다.</p>
          </div>

          <div class="item-actions">
            <button class="secondary" @click="startEdit(kb)">수정</button>
            <button class="danger" @click="deleteKb(kb)">삭제</button>
          </div>
        </article>
      </div>

      <div v-if="!loading && kbTotal > 0" class="pager">
        <button class="ghost" :disabled="kbPage <= 1" @click="goKbPage(kbPage - 1)">이전</button>
        <span>{{ kbPage }} / {{ kbTotalPages }} (총 {{ kbTotal }}건)</span>
        <button class="ghost" :disabled="kbPage >= kbTotalPages" @click="goKbPage(kbPage + 1)">다음</button>
      </div>
    </div>

    <div class="panel">
      <div class="panel-head">
        <h3>저유사도 문의 관리</h3>
        <div class="panel-tools">
          <select v-model="lowSimilarityPlatformFilter">
            <option value="all">플랫폼 전체</option>
            <option v-for="p in platformOptions" :key="p" :value="p">{{ p }}</option>
          </select>
          <button class="ghost refresh-fit" :disabled="loadingLowSimilarity" @click="fetchLowSimilarityQuestions">새로고침</button>
        </div>
      </div>

      <div v-if="loadingLowSimilarity" class="empty">불러오는 중...</div>
      <div v-else-if="lowSimilarityQuestions.length === 0" class="empty">미처리 문의가 없습니다.</div>

      <div v-else class="kb-list">
        <article v-for="item in lowSimilarityQuestions" :key="item.id" class="kb-item">
          <div class="kb-top">
            <strong>Q. {{ item.question }}</strong>
            <div class="badges">
              <span class="scope" :class="item.role === 'admin' ? 'admin' : 'user'">
                {{ item.role === 'admin' ? '관리자 챗봇' : '사용자 챗봇' }}
              </span>
              <span class="scope platform">{{ item.platform || '공통' }}</span>
            </div>
          </div>

          <p class="meta">
            <span>최대 유사도: {{ Math.round((item.topSimilarity || 0) * 100) }}%</span>
            <span v-if="item.topMatchedQuestion">매칭 후보: {{ item.topMatchedQuestion }}</span>
          </p>

          <div class="item-actions">
            <button class="secondary" @click="resolveLowSimilarityQuestion(item)">처리완료</button>
          </div>
        </article>
      </div>

      <div v-if="!loadingLowSimilarity && lowSimilarityTotal > 0" class="pager">
        <button class="ghost" :disabled="lowSimilarityPage <= 1" @click="goLowSimilarityPage(lowSimilarityPage - 1)">이전</button>
        <span>{{ lowSimilarityPage }} / {{ lowSimilarityTotalPages }} (총 {{ lowSimilarityTotal }}건)</span>
        <button class="ghost" :disabled="lowSimilarityPage >= lowSimilarityTotalPages" @click="goLowSimilarityPage(lowSimilarityPage + 1)">다음</button>
      </div>
    </div>

    <div v-if="showSimilarityGuide" class="modal-overlay" @click="showSimilarityGuide = false">
      <div class="modal info-modal" @click.stop>
        <div class="modal-head">
          <h4>유사도를 높이는 방법</h4>
          <button class="ghost" type="button" @click="showSimilarityGuide = false">닫기</button>
        </div>

        <div class="similarity-guide">
          <p>
            챗봇은 질문과 KB의 의미 유사도를 먼저 계산하고, 태그 키워드 일치분을 소폭 보정해
            최종 점수를 만듭니다. 아래 기준대로 작성하면 저유사도 비율을 줄이는 데 도움이 됩니다.
          </p>

          <div class="guide-card">
            <strong>1) 점수는 이렇게 계산됩니다</strong>
            <p>
              질문 임베딩과 KB 대표질문/유사질문 임베딩을 비교해 가장 높은 값을 기본 점수로 사용합니다.
            </p>
            <p>태그가 질문 키워드와 일치하면 매칭 1건당 +0.03, 최대 +0.12까지 가산됩니다.</p>
            <p class="guide-formula">최종 점수 = min(기본 점수 + 태그 보정, 1.0)</p>
          </div>

          <div class="guide-card">
            <strong>2) 대표질문을 이렇게 쓰면 유리합니다</strong>
            <p>
              실제 문의 문장처럼 작성하세요.
              예: "로그인이 안 돼요" 보다 "윈도우 앱에서 인증서 선택 후 로그인 실패"가 더 좋습니다.
            </p>
            <p>환경(웹/앱), 대상(고객/관리자), 증상(오류코드/현상)을 함께 넣으면 매칭이 안정적입니다.</p>
          </div>

          <div class="guide-card">
            <strong>3) 유사질문은 표현 다양화가 핵심입니다</strong>
            <p>
              같은 의미를 다른 표현으로 3~10개 정도 등록하세요.
              예: "로그인 실패", "인증서 고른 뒤 접속 불가", "서명 후 메인 화면 안 넘어감"
            </p>
            <p>오탈자/축약어(예: 공인인증서, 인증서, cert)도 자주 들어오면 함께 넣어두세요.</p>
          </div>

          <div class="guide-card">
            <strong>4) 태그는 짧고 구체적으로 넣으세요</strong>
            <ul>
              <li>권장: 원인/기능/대상 기준 키워드 (예: 인증서, 결제실패, 환불지연, 관리자승인)</li>
              <li>비권장: 너무 포괄적인 단어만 입력 (예: 오류, 문제, 문의)</li>
              <li>태그와 대표질문의 용어를 맞추면 가산점이 붙어 상위 노출 가능성이 높아집니다.</li>
            </ul>
          </div>

          <div class="guide-card">
            <strong>5) 저유사도 문의가 쌓일 때 점검 순서</strong>
            <ul>
              <li>저유사도 문의의 원문을 보고, 유사한 표현을 유사질문에 추가</li>
              <li>기존 태그가 실제 사용자 표현과 맞는지 수정</li>
              <li>해결안이 다른 케이스와 섞이지 않게 KB를 분리 작성</li>
            </ul>
          </div>
        </div>
      </div>
    </div>

    <div v-if="showPlatformEditor" class="modal-overlay" @click="showPlatformEditor = false">
      <div class="modal" @click.stop>
        <div class="modal-head">
          <h4>플랫폼 관리</h4>
          <button class="ghost" type="button" @click="showPlatformEditor = false">닫기</button>
        </div>

        <div class="platform-editor-add">
          <input
            v-model="newPlatformName"
            placeholder="예) windows, android"
            @keydown.enter.prevent="addPlatform"
          />
          <button class="secondary" :disabled="addingPlatform" @click="addPlatform">추가</button>
        </div>

        <div class="platform-items" v-if="platformOptions.length > 0">
          <div v-for="p in platformOptions" :key="p" class="platform-item">
            <template v-if="renamingPlatform === p">
              <input v-model="renamingPlatformName" @keydown.enter.prevent="submitRenamePlatform" />
              <button class="secondary" :disabled="savingRename" @click="submitRenamePlatform">저장</button>
              <button class="ghost" :disabled="savingRename" @click="cancelRenamePlatform">취소</button>
            </template>
            <template v-else>
              <span class="platform-name">{{ p }}</span>
              <span v-if="p === '공통'" class="scope platform">기본(삭제불가)</span>
              <button v-else class="secondary" @click="startRenamePlatform(p)">수정</button>
              <button v-if="p !== '공통'" class="danger" :disabled="deletingPlatform === p" @click="removePlatform(p)">삭제</button>
            </template>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.kb-wrap {
  display: grid;
  gap: 14px;
}

.panel {
  border: 1px solid #dee2e6;
  border-radius: 12px;
  background: #ffffff;
  box-shadow: 0 0.45rem 1rem rgba(0, 0, 0, 0.06);
  padding: 18px;
}

.panel-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 12px;
}

.form-panel.collapsed .panel-head {
  margin-bottom: 0;
}

.panel-head-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.panel-head-title {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.pager {
  margin-top: 12px;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 10px;
  color: #495057;
  font-size: 13px;
}

.head-visibility {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  font-weight: 700;
  color: #495057;
}

.head-visibility select {
  width: 150px;
  padding: 7px 8px;
}

.panel-tools {
  display: flex;
  align-items: center;
  justify-content: flex-start;
  gap: 8px;
  width: 100%;
}

.panel-tools select {
  width: 160px;
  min-width: 160px;
  flex: 0 0 160px;
}

.panel-head h3 {
  margin: 0;
  color: #212529;
  font-size: 1.05rem;
}

.form-grid {
  display: grid;
  gap: 10px;
}

label {
  display: grid;
  gap: 6px;
  font-weight: 700;
  color: #495057;
  font-size: 0.92rem;
}

textarea,
input,
select {
  width: 100%;
  border: 1px solid #ced4da;
  border-radius: 10px;
  padding: 10px;
  font-size: 14px;
  color: #212529;
  background: #fff;
}

textarea:focus,
input:focus,
select:focus {
  outline: none;
  border-color: #86b7fe;
  box-shadow: 0 0 0 0.25rem rgba(13, 110, 253, 0.25);
}

.row-two {
  display: grid;
  grid-template-columns: 1fr 220px;
  gap: 10px;
}

.similar-input-row {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 8px;
}

.field-header {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 8px;
  align-items: center;
}

.label-head-inline {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.field-block {
  display: grid;
  gap: 6px;
}

.field-title {
  font-weight: 700;
  color: #495057;
  font-size: 0.92rem;
}

.label-head-inline .ghost {
  padding: 4px 9px;
  font-size: 12px;
}

.guide-trigger {
  border-color: #7c3aed;
  color: #5b21b6;
  background: #f3e8ff;
}

.guide-trigger:hover {
  background: #ede9fe;
}

.field-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.hint {
  font-size: 12px;
  color: #6c757d;
}

.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 2px;
}

.platform-manager {
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  background: #f8fafc;
  padding: 10px;
  display: grid;
  gap: 8px;
}

.platform-title {
  font-size: 13px;
  font-weight: 700;
  color: #334155;
}

.platform-items {
  display: grid;
  gap: 6px;
}

.platform-item {
  display: flex;
  align-items: center;
  gap: 6px;
}

.platform-item input {
  flex: 1;
}

.platform-name {
  min-width: 90px;
  font-weight: 600;
  color: #1f2937;
}

.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1200;
  padding: 12px;
}

.modal {
  width: min(680px, 100%);
  max-height: 85vh;
  overflow: auto;
  border: 1px solid #dee2e6;
  border-radius: 12px;
  background: #fff;
  padding: 16px;
}

.modal-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
}

.modal-head h4 {
  margin: 0;
  color: #212529;
}

.info-modal {
  width: min(760px, 100%);
}

.similarity-guide {
  display: grid;
  gap: 10px;
  color: #495057;
}

.similarity-guide p {
  margin: 0;
  line-height: 1.55;
}

.guide-card {
  border: 1px solid #e2e8f0;
  background: #f8fafc;
  border-radius: 10px;
  padding: 11px;
  display: grid;
  gap: 6px;
}

.guide-card strong {
  color: #1f2a44;
}

.guide-card ul {
  margin: 0;
  padding-left: 18px;
  display: grid;
  gap: 4px;
}

.guide-formula {
  font-weight: 700;
  color: #0d6efd;
}

.platform-editor-add {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 8px;
  margin-bottom: 10px;
}

.chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: #e7f1ff;
  color: #0d6efd;
  border-radius: 999px;
  padding: 7px 11px;
  font-size: 13px;
  border: 1px solid #cfe2ff;
}

.chip button {
  border: none;
  background: transparent;
  cursor: pointer;
  color: #0f3f39;
}

.actions,
.item-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 12px;
}

.primary,
.secondary,
.ghost,
.danger {
  border: 1px solid transparent;
  border-radius: 10px;
  padding: 8px 12px;
  font-weight: 700;
  cursor: pointer;
}

.primary {
  background: #0d6efd;
  border-color: #0d6efd;
  color: #fff;
}

.primary:hover:not(:disabled) {
  background: #0b5ed7;
}

.primary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.secondary {
  background: #ffffff;
  color: #0d6efd;
  border-color: #0d6efd;
}

.secondary:hover {
  background: #e7f1ff;
}

.ghost {
  background: #ffffff;
  color: #6c757d;
  border-color: #ced4da;
  white-space: nowrap;
  word-break: keep-all;
  flex: 0 0 auto;
}

.refresh-fit {
  width: auto;
  margin-left: auto;
}

.ghost:hover {
  background: #f8f9fa;
}

.danger {
  background: #ffffff;
  color: #9d2d22;
  border-color: #dc3545;
}

.danger:hover {
  background: #fdecef;
}

.filters {
  display: grid;
  grid-template-columns: 170px 170px 1fr;
  gap: 8px;
  margin-bottom: 12px;
}

.kb-list {
  display: grid;
  gap: 10px;
}

.kb-item {
  border: 1px solid #dee2e6;
  border-radius: 10px;
  padding: 14px;
  background: #ffffff;
}

.kb-top {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  align-items: center;
}

.badges {
  display: flex;
  align-items: center;
  gap: 6px;
}

.q-btn {
  border: none;
  background: transparent;
  font-weight: 800;
  text-align: left;
  cursor: pointer;
  color: #212529;
  padding: 0;
  line-height: 1.35;
}

.scope {
  padding: 4px 8px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 700;
}

.scope.user {
  background: #d1e7dd;
  color: #0f5132;
}

.scope.admin {
  background: #fff3cd;
  color: #664d03;
}

.scope.platform {
  background: #eef2ff;
  color: #3730a3;
}

.answer {
  margin: 10px 0 6px;
  color: #495057;
  white-space: pre-wrap;
  line-height: 1.6;
}

.meta {
  display: flex;
  gap: 14px;
  color: #6c757d;
  font-size: 13px;
}

.similar-box {
  margin-top: 10px;
  border-top: 1px dashed #dee2e6;
  padding-top: 10px;
}

.similar-box h4 {
  margin: 0 0 6px;
  color: #212529;
}

.similar-box ul {
  margin: 0;
  padding-left: 18px;
  color: #495057;
  line-height: 1.55;
}

.empty,
.error {
  padding: 16px;
  border-radius: 10px;
  text-align: center;
}

.empty {
  background: #f8f9fa;
  color: #6c757d;
}

.error {
  background: #ffe6e1;
  color: #8b2e24;
}

@media (max-width: 900px) {
  .row-two,
  .filters {
    grid-template-columns: 1fr;
  }

  .panel-tools {
    justify-content: flex-start;
  }

  .field-header {
    grid-template-columns: 1fr;
    width: 100%;
  }

  .panel-tools select {
    width: 140px;
    min-width: 140px;
    flex: 0 0 140px;
  }
}
</style>
