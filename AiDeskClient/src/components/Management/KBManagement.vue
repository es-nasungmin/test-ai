<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import axios from 'axios'
import { API_BASE_URL } from '../../config'

const API_URL = API_BASE_URL

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
const isComposingPlatformName = ref(false)
const isComposingRenamePlatform = ref(false)
const showSimilarityGuide = ref(false)

const keyword = ref('')
const visibilityFilter = ref('all')
const kbPlatformFilter = ref('all')

const form = ref({
  id: null,
  title: '',
  content: '',
  visibility: 'user',
  platforms: ['공통'],
  expectedInput: ''
})
const keywordInput = ref('')
const keywordDraft = ref([])
const platformInput = ref('공통')
const isComposingKeyword = ref(false)

function getActorHeader() {
  try {
    const token = localStorage.getItem('token')
    const userRaw = localStorage.getItem('user')
    const user = userRaw ? JSON.parse(userRaw) : null
    const candidates = [
      user?.username,
      user?.Username,
      user?.name,
      user?.Name
    ]
    const actorName = candidates
      .find((x) => typeof x === 'string' && x.trim())
      ?.trim() || null

    const headers = {}
    if (token) headers.Authorization = `Bearer ${token}`
    if (actorName) headers['X-Actor-Name'] = actorName
    return headers
  } catch {
    return {}
  }
}

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

function normalizeKeywords(keywords) {
  return Array.from(new Set((keywords || [])
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
const generatingKeywords = ref(false)
const refiningSolution = ref(false)
const generatedCandidates = ref([])
const showRefinePreview = ref(false)
const refinedSolutionPreview = ref('')
const showWriterPromptEditor = ref(false)
const savingWriterPromptTemplate = ref(false)
const writerPromptTemplateForm = ref({
  keywordSystemPrompt: '',
  keywordRulesPrompt: '',
  answerRefineSystemPrompt: '',
  answerRefineRulesPrompt: ''
})
const MAX_EXPECTED_QUESTIONS = 10

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
  const value = form.value.expectedInput.trim()
  if (!value) return

  if (similarDraft.value.length >= MAX_EXPECTED_QUESTIONS) {
    alert(`예상질문은 최대 ${MAX_EXPECTED_QUESTIONS}개까지 등록 가능합니다.`)
    form.value.expectedInput = ''
    return
  }

  // Prevent accidental double-add when IME enter events are emitted back-to-back.
  const now = Date.now()
  if (lastAdded.value.text === value && now - lastAdded.value.at < 400) {
    form.value.expectedInput = ''
    return
  }

  const exists = similarDraft.value.some((x) => x.toLowerCase() === value.toLowerCase())
  if (!exists) {
    similarDraft.value.push(value)
    lastAdded.value = { text: value, at: now }
  }
  form.value.expectedInput = ''
}

function onSimilarEnter() {
  if (isComposingSimilar.value) return
  addSimilarFromInput()
}

function removeSimilar(index) {
  similarDraft.value.splice(index, 1)
}

async function generateSimilarQuestions() {
  if (!form.value.content.trim()) {
    alert('내용을 먼저 작성해주세요.')
    return
  }

  generatingSimilar.value = true
  try {
    const res = await axios.post(`${API_URL}/knowledgebase/generate-similar-questions`, {
      title: form.value.title,
      content: form.value.content,
      count: 5
    })

    const items = Array.isArray(res.data?.items) ? res.data.items : []
    generatedCandidates.value = items

    if (items.length === 0) {
      alert('추가할 예상 질문을 생성하지 못했습니다.')
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
    title: '',
    content: '',
    visibility: 'user',
    platforms: ['공통'],
    expectedInput: ''
  }
  keywordInput.value = ''
  keywordDraft.value = []
  platformInput.value = '공통'
  similarDraft.value = []
  generatedCandidates.value = []
  refinedSolutionPreview.value = ''
  showRefinePreview.value = false
}

function startEdit(kb) {
  showWriter.value = true
  form.value.id = kb.id
  form.value.title = kb.title || ''
  form.value.content = kb.content || kb.solution || ''
  form.value.visibility = kb.visibility || 'user'
  form.value.platforms = extractPlatforms(kb)
  form.value.expectedInput = ''
  keywordInput.value = ''
  keywordDraft.value = normalizeKeywords((kb.keywords || kb.tags || '').split(','))
  platformInput.value = form.value.platforms[0] || '공통'
  similarDraft.value = (kb.expectedQuestions || kb.similarQuestions || []).map((item) => item.question)
  generatedCandidates.value = []
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

function addKeywordFromInput() {
  const value = keywordInput.value.trim()
  if (!value) return
  keywordDraft.value = normalizeKeywords([...keywordDraft.value, value])
  keywordInput.value = ''
}

function onKeywordEnter() {
  if (isComposingKeyword.value) return
  addKeywordFromInput()
}

function removeKeyword(index) {
  keywordDraft.value.splice(index, 1)
}

function addPlatformToForm() {
  const selected = (platformInput.value || '').trim()
  if (!selected) return
  form.value.platforms = normalizeSelectedPlatforms([...form.value.platforms, selected])
}

function onPlatformSelectChanged() {
  addPlatformToForm()
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
  if (!form.value.title.trim() || !form.value.content.trim()) {
    alert('제목, 내용은 필수입니다.')
    return
  }

  saving.value = true
  try {
    const payload = {
      title: form.value.title.trim(),
      content: form.value.content,
      keywords: normalizeKeywords(keywordDraft.value).join(', '),
      visibility: form.value.visibility,
      platforms: normalizeSelectedPlatforms(form.value.platforms),
      expectedQuestions: similarDraft.value
    }

    if (form.value.id) {
      await axios.put(`${API_URL}/knowledgebase/${form.value.id}`, payload, {
        headers: getActorHeader()
      })
    } else {
      await axios.post(`${API_URL}/knowledgebase`, payload, {
        headers: getActorHeader()
      })
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

function addGeneratedAsSimilar(candidate) {
  const text = typeof candidate === 'string' ? candidate.trim() : ''
  if (!text) return
  if (similarDraft.value.length >= MAX_EXPECTED_QUESTIONS) {
    alert(`예상질문은 최대 ${MAX_EXPECTED_QUESTIONS}개까지 등록 가능합니다.`)
    return
  }

  const exists = similarDraft.value.some((x) => x.toLowerCase() === text.toLowerCase())
  if (exists) return
  similarDraft.value.push(text)
}

async function generateKeywords() {
  if (!form.value.title.trim() || !form.value.content.trim()) {
    alert('제목/내용을 먼저 작성해주세요')
    return
  }

  generatingKeywords.value = true
  try {
    const res = await axios.post(`${API_URL}/knowledgebase/generate-keywords`, {
      title: form.value.title,
      content: form.value.content,
      expectedQuestions: similarDraft.value,
      count: 5
    })

    const generated = Array.isArray(res.data?.combined)
      ? res.data.combined
      : (Array.isArray(res.data?.items) ? res.data.items : [])
    if (generated.length === 0) {
      alert('추가할 키워드를 생성하지 못했습니다.')
      return
    }

    keywordDraft.value = normalizeKeywords([...keywordDraft.value, ...generated])
  } catch (err) {
    if (err.response?.data?.error) {
      alert(`키워드 생성 실패: ${err.response.data.error}`)
    } else {
      alert(`키워드 생성 실패: ${err.message || '알 수 없는 오류'}`)
    }
  } finally {
    generatingKeywords.value = false
  }
}

async function refineSolutionWithAi() {
  if (!form.value.content.trim()) {
    alert('내용을 먼저 작성해주세요.')
    return
  }

  refiningSolution.value = true
  try {
    const res = await axios.post(`${API_URL}/knowledgebase/refine-solution`, {
      title: form.value.title,
      content: form.value.content
    })

    const refined = typeof res.data?.solution === 'string' ? res.data.solution.trim() : ''
    if (!refined) {
      alert('정리된 답변을 생성하지 못했습니다.')
      return
    }

    refinedSolutionPreview.value = refined
    showRefinePreview.value = true
  } catch (err) {
    if (err.response?.data?.error) {
      alert(`답변 정리 실패: ${err.response.data.error}`)
    } else {
      alert(`답변 정리 실패: ${err.message || '알 수 없는 오류'}`)
    }
  } finally {
    refiningSolution.value = false
  }
}

function applyRefinedSolution() {
  if (!refinedSolutionPreview.value.trim()) return
  form.value.content = refinedSolutionPreview.value
  showRefinePreview.value = false
}

function cancelRefinedSolution() {
  refinedSolutionPreview.value = ''
  showRefinePreview.value = false
}

function normalizeLineBreaks(value) {
  if (typeof value !== 'string') return ''
  return value.replace(/\\n/g, '\n').replace(/\r\n/g, '\n')
}

async function fetchWriterPromptTemplate() {
  const response = await axios.get(`${API_URL}/knowledgebase/writer-prompt-template`)
  writerPromptTemplateForm.value = {
    keywordSystemPrompt: normalizeLineBreaks(response.data.keywordSystemPrompt),
    keywordRulesPrompt: normalizeLineBreaks(response.data.keywordRulesPrompt),
    answerRefineSystemPrompt: normalizeLineBreaks(response.data.answerRefineSystemPrompt),
    answerRefineRulesPrompt: normalizeLineBreaks(response.data.answerRefineRulesPrompt)
  }
}

async function openWriterPromptEditor() {
  try {
    await fetchWriterPromptTemplate()
    showWriterPromptEditor.value = true
  } catch (err) {
    alert('KB 작성 프롬프트를 불러오지 못했습니다: ' + (err.response?.data?.error || err.message))
  }
}

async function saveWriterPromptTemplate() {
  const formData = writerPromptTemplateForm.value
  if (
    !formData.keywordSystemPrompt.trim() ||
    !formData.keywordRulesPrompt.trim() ||
    !formData.answerRefineSystemPrompt.trim() ||
    !formData.answerRefineRulesPrompt.trim()
  ) {
    alert('KB 작성 프롬프트 항목을 모두 입력해주세요.')
    return
  }

  savingWriterPromptTemplate.value = true
  try {
    const response = await axios.put(`${API_URL}/knowledgebase/writer-prompt-template`, {
      keywordSystemPrompt: formData.keywordSystemPrompt,
      keywordRulesPrompt: formData.keywordRulesPrompt,
      answerRefineSystemPrompt: formData.answerRefineSystemPrompt,
      answerRefineRulesPrompt: formData.answerRefineRulesPrompt
    })

    writerPromptTemplateForm.value = {
      keywordSystemPrompt: normalizeLineBreaks(response.data.keywordSystemPrompt),
      keywordRulesPrompt: normalizeLineBreaks(response.data.keywordRulesPrompt),
      answerRefineSystemPrompt: normalizeLineBreaks(response.data.answerRefineSystemPrompt),
      answerRefineRulesPrompt: normalizeLineBreaks(response.data.answerRefineRulesPrompt)
    }

    alert('KB 작성 프롬프트가 저장되었습니다. 다음 생성부터 반영됩니다.')
    showWriterPromptEditor.value = false
  } catch (err) {
    alert('KB 작성 프롬프트 저장에 실패했습니다: ' + (err.response?.data?.error || err.message))
  } finally {
    savingWriterPromptTemplate.value = false
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

function hasUpdateHistory(kb) {
  if (!kb) return false

  const createdBy = typeof kb.createdBy === 'string' ? kb.createdBy.trim() : ''
  const updatedBy = typeof kb.updatedBy === 'string' ? kb.updatedBy.trim() : ''
  if (createdBy && updatedBy && createdBy !== updatedBy) return true

  const createdAt = kb.createdAt ? new Date(kb.createdAt).getTime() : NaN
  const updatedAt = kb.updatedAt ? new Date(kb.updatedAt).getTime() : NaN
  if (!Number.isNaN(createdAt) && !Number.isNaN(updatedAt) && updatedAt > createdAt) return true

  return false
}

onMounted(async () => {
  await fetchPlatforms()
  await fetchKbs()
  await fetchLowSimilarityQuestions()
  await fetchDocuments()
})

// ─── 문서 관리 ─────────────────────────────────────────────────
const activeTab = ref('kb') // 'kb' | 'documents'
const documentList = ref([])
const loadingDocuments = ref(false)
const documentError = ref('')
const docPlatformFilter = ref('all')
const uploadingDocument = ref(false)
const updatingDocId = ref(null)
const editingDocumentId = ref(null)
const docFileInput = ref(null)
const selectedDocumentFile = ref(null)
const selectedEditDocumentFile = ref(null)
const docForm = ref({ displayName: '', visibility: 'admin', platform: '공통' })
const editDocForm = ref({ displayName: '', visibility: 'admin', platform: '공통' })

async function fetchDocuments() {
  loadingDocuments.value = true
  documentError.value = ''
  try {
    const platform = docPlatformFilter.value === 'all' ? undefined : docPlatformFilter.value
    const res = await axios.get(`${API_URL}/knowledgebase/documents`, {
      params: { role: 'admin', platform }
    })
    documentList.value = res.data.data || []
    if (editingDocumentId.value && !documentList.value.some((x) => x.id === editingDocumentId.value)) {
      editingDocumentId.value = null
    }
  } catch {
    documentError.value = '문서 목록을 불러오지 못했습니다.'
  } finally {
    loadingDocuments.value = false
  }
}

function startEditDocument(doc) {
  editingDocumentId.value = doc.id
  selectedEditDocumentFile.value = null
  editDocForm.value = {
    displayName: doc.displayName || '',
    visibility: doc.visibility || 'admin',
    platform: doc.platform || '공통'
  }
}

function cancelEditDocument() {
  const currentEditingId = editingDocumentId.value
  editingDocumentId.value = null
  selectedEditDocumentFile.value = null
  if (currentEditingId) {
    const inputEl = document.getElementById(`edit-file-input-${currentEditingId}`)
    if (inputEl) {
      inputEl.value = ''
    }
  }
}

function onSelectEditDocumentFile(e) {
  const file = e.target.files?.[0] || null
  selectedEditDocumentFile.value = file
}

async function updateDocument(doc) {
  const displayName = (editDocForm.value.displayName || '').trim()
  if (!displayName) {
    alert('표시 이름을 입력해주세요.')
    return
  }

  if (updatingDocId.value) return
  updatingDocId.value = doc.id

  try {
    await axios.put(`${API_URL}/knowledgebase/documents/${doc.id}`, {
      displayName,
      visibility: editDocForm.value.visibility || 'admin',
      platform: editDocForm.value.platform || '공통'
    }, {
      headers: getActorHeader()
    })

    if (selectedEditDocumentFile.value) {
      const fileName = selectedEditDocumentFile.value.name || ''
      if (!fileName.toLowerCase().endsWith('.pdf')) {
        alert('파일 교체는 PDF만 가능합니다.')
        return
      }

      const fd = new FormData()
      fd.append('file', selectedEditDocumentFile.value)
      try {
        await axios.post(`${API_URL}/knowledgebase/documents/${doc.id}/reindex`, fd, {
          headers: getActorHeader()
        })
      } catch (err) {
        await fetchDocuments()
        alert(err.response?.data?.error || '메타데이터는 저장됐지만 파일 재인덱싱에 실패했습니다.')
        return
      }
    }

    selectedEditDocumentFile.value = null
    const editInputEl = document.getElementById(`edit-file-input-${doc.id}`)
    if (editInputEl) {
      editInputEl.value = ''
    }
    editingDocumentId.value = null
    await fetchDocuments()
  } catch (err) {
    if (err.response?.status === 405) {
      alert('백엔드가 최신 코드로 반영되지 않았습니다. 서버를 재시작한 뒤 다시 시도해주세요.')
    } else {
      alert(err.response?.data?.error || '문서 메타데이터 수정에 실패했습니다.')
    }
  } finally {
    updatingDocId.value = null
  }
}

function onSelectDocumentFile(e) {
  const file = e.target.files?.[0] || null
  selectedDocumentFile.value = file
}

async function uploadDocument() {
  const file = selectedDocumentFile.value
  if (!file) {
    alert('먼저 업로드할 PDF 파일을 선택해주세요.')
    return
  }

  if (uploadingDocument.value) return
  uploadingDocument.value = true
  documentError.value = ''

  try {
    const fd = new FormData()
    fd.append('file', file)
    if (docForm.value.displayName.trim()) fd.append('displayName', docForm.value.displayName.trim())
    fd.append('visibility', docForm.value.visibility)
    fd.append('platform', docForm.value.platform || '공통')

    await axios.post(`${API_URL}/knowledgebase/documents/upload`, fd, {
      headers: { ...getActorHeader(), 'Content-Type': 'multipart/form-data' }
    })
    docForm.value.displayName = ''
    selectedDocumentFile.value = null
    if (docFileInput.value) {
      docFileInput.value.value = ''
    }
    await fetchDocuments()
  } catch (err) {
    documentError.value = err.response?.data?.error || '업로드에 실패했습니다.'
  } finally {
    uploadingDocument.value = false
  }
}

async function deleteDocument(doc) {
  if (!confirm(`"${doc.displayName}" 문서를 삭제하시겠습니까?`)) return
  try {
    await axios.delete(`${API_URL}/knowledgebase/documents/${doc.id}`, {
      headers: getActorHeader()
    })
    await fetchDocuments()
  } catch (err) {
    alert(err.response?.data?.error || '삭제에 실패했습니다.')
  }
}

async function downloadDocument(doc) {
  try {
    const response = await axios.get(`${API_URL}/knowledgebase/documents/${doc.id}/download`, {
      responseType: 'blob',
      headers: getActorHeader()
    })

    const blob = new Blob([response.data], { type: 'application/pdf' })
    const url = window.URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    const fallback = `document-${doc.id}.pdf`
    const name = (doc.displayName || doc.fileName || fallback).toLowerCase().endsWith('.pdf')
      ? (doc.displayName || doc.fileName || fallback)
      : `${doc.displayName || doc.fileName || fallback}.pdf`

    anchor.href = url
    anchor.download = name
    document.body.appendChild(anchor)
    anchor.click()
    document.body.removeChild(anchor)
    window.URL.revokeObjectURL(url)
  } catch (err) {
    alert(err.response?.data?.error || '문서 다운로드에 실패했습니다.')
  }
}

function formatDate(val) {
  if (!val) return '-'
  const d = new Date(val)
  return isNaN(d.getTime()) ? '-' : d.toLocaleDateString('ko-KR', { year: 'numeric', month: '2-digit', day: '2-digit' })
}
</script>

<template>
  <section class="kb-wrap">
    <div class="tab-bar">
      <button class="tab-btn" :class="{ active: activeTab === 'kb' }" @click="activeTab = 'kb'">가이드KB</button>
      <button class="tab-btn" :class="{ active: activeTab === 'documents' }" @click="activeTab = 'documents'">문서KB</button>
    </div>

    <template v-if="activeTab === 'documents'">
      <div class="panel">
        <div class="panel-head">
          <h3>PDF 문서 업로드</h3>
        </div>
        <div class="doc-upload-form">
          <label>
            표시 이름
            <input v-model="docForm.displayName" placeholder="예) 사용자 매뉴얼 v2.pdf" />
          </label>
          <label>
            공개수준
            <select v-model="docForm.visibility">
              <option value="user">사용자 공개</option>
              <option value="admin">관리자 전용</option>
            </select>
          </label>
          <label>
            <div class="label-head-inline">
              <span class="field-title">플랫폼</span>
              <button class="ghost" type="button" @click="openPlatformEditor">관리</button>
            </div>
            <select v-model="docForm.platform">
              <option v-for="p in platformOptions" :key="p" :value="p">{{ p }}</option>
            </select>
          </label>
          <div class="upload-btn-row">
            <input ref="docFileInput" type="file" accept=".pdf" style="display:none" @change="onSelectDocumentFile" />
            <button class="secondary" :disabled="uploadingDocument" @click="docFileInput.click()">
              파일 업로드
            </button>
            <button class="primary" :disabled="uploadingDocument || !selectedDocumentFile" @click="uploadDocument">
              {{ uploadingDocument ? '등록 중...' : '등록' }}
            </button>
          </div>
          <div v-if="selectedDocumentFile" class="selected-file-name hint">선택됨: {{ selectedDocumentFile.name }}</div>
          <div v-if="documentError" class="error">{{ documentError }}</div>
        </div>
      </div>

      <div class="panel list-panel">
        <div class="panel-head">
          <h3>문서 목록</h3>
          <div style="display:flex;gap:8px;align-items:center">
            <select v-model="docPlatformFilter" @change="fetchDocuments">
              <option value="all">플랫폼 전체</option>
              <option v-for="p in platformOptions" :key="p" :value="p">{{ p }}</option>
            </select>
            <button class="ghost" :disabled="loadingDocuments" @click="fetchDocuments">새로고침</button>
          </div>
        </div>

        <div v-if="loadingDocuments" class="empty">불러오는 중...</div>
        <div v-else-if="documentList.length === 0" class="empty">업로드된 문서가 없습니다.</div>

        <div v-else class="kb-list">
          <article v-for="doc in documentList" :key="doc.id" class="kb-item">
            <div class="kb-top">
              <div class="badges">
                <span class="scope" :class="doc.status === 'ready' ? 'user' : 'admin'">
                  {{ doc.status === 'ready' ? '✅ 준비됨' : '⏳ 인덱싱 중' }}
                </span>
                <span class="scope" :class="doc.visibility">
                  {{ doc.visibility === 'user' ? '사용자 공개' : '관리자 전용' }}
                </span>
                <span class="scope platform">{{ doc.platform || '공통' }}</span>
              </div>
              <div class="kb-item-actions">
                <button class="secondary" :disabled="updatingDocId === doc.id" @click="startEditDocument(doc)">수정</button>
                <button class="danger" @click="deleteDocument(doc)">삭제</button>
              </div>
            </div>
            <div class="kb-body">
              <div class="kb-row">
                <div class="kb-label">파일명</div>
                <div class="kb-value">
                  <button class="file-link-btn" type="button" @click="downloadDocument(doc)">{{ doc.fileName }}</button>
                </div>
              </div>
              <div class="kb-row">
                <div class="kb-label">표시명</div>
                <div v-if="editingDocumentId === doc.id" class="kb-value">
                  <input v-model="editDocForm.displayName" placeholder="표시 이름" />
                </div>
                <div v-else class="kb-value kb-title">{{ doc.displayName }}</div>
              </div>
              <div class="kb-row">
                <div class="kb-label">공개수준</div>
                <div v-if="editingDocumentId === doc.id" class="kb-value">
                  <select v-model="editDocForm.visibility">
                    <option value="user">사용자 공개</option>
                    <option value="admin">관리자 전용</option>
                  </select>
                </div>
                <div v-else class="kb-value">{{ doc.visibility === 'user' ? '사용자 공개' : '관리자 전용' }}</div>
              </div>
              <div class="kb-row">
                <div class="kb-label">플랫폼</div>
                <div v-if="editingDocumentId === doc.id" class="kb-value">
                  <select v-model="editDocForm.platform">
                    <option v-for="p in platformOptions" :key="`doc-edit-${doc.id}-${p}`" :value="p">{{ p }}</option>
                  </select>
                </div>
                <div v-else class="kb-value">{{ doc.platform || '공통' }}</div>
              </div>
              <div v-if="editingDocumentId === doc.id" class="kb-row">
                <div class="kb-label">파일 교체</div>
                <div class="kb-value">
                  <div class="doc-file-inline-row">
                    <input class="doc-file-picker" :id="`edit-file-input-${doc.id}`" type="file" accept=".pdf" @change="onSelectEditDocumentFile" />
                    <span class="hint">파일을 선택하지 않으면 기존 PDF 파일이 그대로 유지됩니다.</span>
                  </div>
                  <div class="upload-btn-row" v-if="selectedEditDocumentFile">
                    <span class="hint">현재 선택된 파일: {{ selectedEditDocumentFile.name }} (저장 시 파일이 교체됩니다)</span>
                  </div>
                </div>
              </div>
              <div class="kb-row">
                <div class="kb-label">수정일</div>
                <div class="kb-value">{{ formatDate(doc.updatedAt) }}</div>
              </div>
              <div v-if="editingDocumentId === doc.id" class="upload-btn-row">
                <button class="primary" :disabled="updatingDocId === doc.id" @click="updateDocument(doc)">
                  {{ updatingDocId === doc.id ? (selectedEditDocumentFile ? '저장/인덱싱 중...' : '저장 중...') : '수정 저장' }}
                </button>
                <button class="ghost" :disabled="updatingDocId === doc.id" @click="cancelEditDocument">취소</button>
              </div>
            </div>
          </article>
        </div>
      </div>
    </template>

    <template v-else>
    <div class="panel form-panel" :class="{ collapsed: !showWriter }">
      <div class="panel-head writer-toggle-head" @click="showWriter = !showWriter">
        <div class="panel-head-title">
          <h3>{{ form.id ? 'KB 수정' : 'KB 작성' }}</h3>
          <span class="writer-toggle-indicator" :aria-label="showWriter ? '작성 영역 열림' : '작성 영역 닫힘'">
            {{ showWriter ? '▲' : '▼' }}
          </span>
          <button class="ghost guide-trigger" type="button" @click.stop="showSimilarityGuide = true">가이드KB 작성 방법</button>
        </div>
        <div class="panel-head-actions">
          <button class="ghost" @click.stop="resetForm">초기화</button>
          <button class="ghost" type="button" @click.stop="openWriterPromptEditor">프롬프트 설정</button>
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
          제목
          <input v-model="form.title" placeholder="가이드 KB 제목을 입력해주세요" />
        </label>

        <div class="field-block">
          <div class="label-head-inline">
            <span class="field-title">내용</span>
            <button class="ai-action-btn ai-action-soft" type="button" :disabled="refiningSolution" @click="refineSolutionWithAi">
              {{ refiningSolution ? '정리 중...' : 'AI로 내용 정리' }}
            </button>
          </div>
          <textarea v-model="form.content" rows="5" placeholder="가이드 KB 내용을 입력하세요" />
        </div>

        <div class="field-block">
          <div class="label-head-inline">
            <span class="field-title">키워드</span>
            <button class="ai-action-btn ai-action-soft" type="button" :disabled="generatingKeywords" @click="generateKeywords">
              {{ generatingKeywords ? '생성 중...' : '키워드 생성하기' }}
            </button>
          </div>
          <div class="similar-input-row">
            <input
              v-model="keywordInput"
              placeholder="예) 인증서, 로그인"
              @keydown.enter.prevent="onKeywordEnter"
              @compositionstart="isComposingKeyword = true"
              @compositionend="isComposingKeyword = false"
            />
            <button class="secondary" type="button" @click="addKeywordFromInput">추가</button>
          </div>
          <div class="chips" v-if="keywordDraft.length > 0">
            <span v-for="(item, idx) in keywordDraft" :key="`keyword-${item}-${idx}`" class="chip">
              #{{ item }}
              <button type="button" @click="removeKeyword(idx)">×</button>
            </span>
          </div>
        </div>

        <div class="field-block">
          <div class="label-head-inline">
            <span class="field-title">플랫폼</span>
            <button class="ghost" type="button" @click="openPlatformEditor">관리</button>
          </div>
          <div class="field-header">
            <select v-model="platformInput" @change="onPlatformSelectChanged">
              <option v-for="p in platformOptions" :key="p" :value="p">{{ p }}</option>
            </select>
          </div>
          <div class="chips" v-if="form.platforms.length > 0">
            <span v-for="(item, idx) in form.platforms" :key="`platform-${item}-${idx}`" class="chip">
              {{ item }}
              <button type="button" @click="removePlatformFromForm(idx)">×</button>
            </span>
          </div>
          <small class="hint">복수 선택 가능 (공통은 단독 선택 시에만 유지)</small>
        </div>

        <div class="field-block">
          <div class="label-head-inline">
            <span class="field-title">예상질문</span>
            <button class="ai-action-btn ai-action-soft" type="button" :disabled="generatingSimilar" @click="generateSimilarQuestions">
              {{ generatingSimilar ? '생성 중...' : '예상질문 생성하기' }}
            </button>
          </div>
          <div class="similar-input-row">
            <input
              v-model="form.expectedInput"
              placeholder="예) 인증서 경로가 어디에 있나요?"
              @keydown.enter.prevent="onSimilarEnter"
              @compositionstart="isComposingSimilar = true"
              @compositionend="isComposingSimilar = false"
            />
            <button class="secondary" @click="addSimilarFromInput">추가</button>
          </div>
          <small class="hint">예상질문은 최대 10개까지 등록 가능합니다.</small>
        </div>

        <div v-if="generatedCandidates.length > 0" class="generated-candidates">
          <div class="field-title">최근 생성된 예상질문</div>
          <div class="generated-list">
            <div v-for="(item, idx) in generatedCandidates" :key="`generated-${idx}-${item}`" class="generated-item">
              <span>{{ item }}</span>
              <div class="generated-actions">
                <button class="ghost" type="button" @click="addGeneratedAsSimilar(item)">예상질문 추가하기</button>
              </div>
            </div>
          </div>
        </div>

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
        <input v-model="keyword" placeholder="제목/내용/예상질문/키워드 검색" />
      </div>

      <div v-if="error" class="error">{{ error }}</div>
      <div v-else-if="loading" class="empty">불러오는 중...</div>
      <div v-else-if="kbList.length === 0" class="empty">등록된 KB가 없습니다.</div>

      <div v-else class="kb-list">
        <article v-for="kb in kbList" :key="kb.id" class="kb-item">
          <div class="kb-top">
            <div class="badges">
              <span class="scope" :class="kb.visibility">
                {{ kb.visibility === 'user' ? '사용자 공개' : '관리자 전용' }}
              </span>
              <span class="scope platform">{{ extractPlatforms(kb).join(', ') }}</span>
            </div>
            <div class="kb-item-actions">
              <button class="secondary" @click="startEdit(kb)">수정</button>
              <button class="danger" @click="deleteKb(kb)">삭제</button>
            </div>
          </div>

          <div class="kb-body">
            <div class="kb-row">
              <div class="kb-label">제목</div>
              <div class="kb-value kb-title">{{ kb.title || '-' }}</div>
            </div>
            <div class="kb-row">
              <div class="kb-label">내용</div>
              <div class="kb-value kb-question-row">
                <span>{{ kb.content || kb.solution || '-' }}</span>
                <button class="q-btn" @click="toggleExpanded(kb.id)">
                  {{ expandedId === kb.id ? '예상질문 접기' : '예상질문 보기' }}
                </button>
              </div>
            </div>
          </div>

          <div class="meta">
            <span class="meta-chip" v-if="kb.keywords">키워드: {{ kb.keywords }}</span>
            <span class="meta-chip">참조수: {{ kb.viewCount }}</span>
            <span class="meta-chip">등록: {{ kb.createdBy || '-' }} · {{ formatDateTime(kb.createdAt) }}</span>
            <span v-if="hasUpdateHistory(kb)" class="meta-chip">수정: {{ kb.updatedBy || '-' }} · {{ formatDateTime(kb.updatedAt) }}</span>
          </div>

          <div v-if="expandedId === kb.id" class="similar-box">
            <h4>예상질문</h4>
            <ul v-if="kb.expectedQuestions && kb.expectedQuestions.length > 0">
              <li v-for="item in kb.expectedQuestions" :key="item.id">{{ item.question }}</li>
            </ul>
            <p v-else>등록된 예상질문이 없습니다.</p>
          </div>

        </article>
      </div>

      <div v-if="!loading && kbTotal > 0" class="pager">
        <button class="ghost" :disabled="kbPage <= 1" @click="goKbPage(kbPage - 1)">이전</button>
        <span>{{ kbPage }} / {{ kbTotalPages }} (총 {{ kbTotal }}건)</span>
        <button class="ghost" :disabled="kbPage >= kbTotalPages" @click="goKbPage(kbPage + 1)">다음</button>
      </div>
    </div>

    <div v-if="showSimilarityGuide" class="modal-overlay" @click="showSimilarityGuide = false">
      <div class="modal info-modal" @click.stop>
        <div class="modal-head">
          <h4>가이드KB 작성 방법</h4>
          <button class="ghost" type="button" @click="showSimilarityGuide = false">닫기</button>
        </div>

        <div class="similarity-guide">
          <p>
            가이드KB는 질문과의 의미 유사도를 기준으로 검색되며,
            키워드 기반 후보 리콜과 AI 재정렬을 함께 사용합니다. 아래 기준대로 작성하면 검색 정확도를 높이는 데 도움이 됩니다.
          </p>

          <div class="guide-card">
            <strong>1) 점수는 이렇게 계산됩니다</strong>
            <p>
              질문 임베딩과 가이드 KB(제목+내용+예상질문 합본 임베딩) 유사도를 계산합니다.
            </p>
            <p>유사도 점수는 벡터 유사도만 사용하고, 키워드는 후보 리콜(top10) 용도로만 사용합니다.</p>
            <p class="guide-formula">최종 후보 = 벡터 top15(임계치 필터) + 키워드 top10 → AI rerank top5</p>
          </div>

          <div class="guide-card">
            <strong>2) 제목/내용을 이렇게 쓰면 유리합니다</strong>
            <p>
              가이드 KB 제목은 주제를 명확히, 내용은 절차/조건/예외를 구체적으로 작성하세요.
            </p>
            <p>환경(웹/앱), 대상(고객/관리자), 증상(오류코드/현상)을 함께 넣으면 매칭이 안정적입니다.</p>
          </div>

          <div class="guide-card">
            <strong>3) 예상질문은 표현 다양화가 핵심입니다</strong>
            <p>
              같은 의미를 다른 표현으로 최대 10개 등록하세요.
              예: "로그인 실패", "인증서 고른 뒤 접속 불가", "서명 후 메인 화면 안 넘어감"
            </p>
            <p>오탈자/축약어(예: 공인인증서, 인증서, cert)도 자주 들어오면 함께 넣어두세요.</p>
          </div>

          <div class="guide-card">
            <strong>4) 키워드는 짧고 구체적으로 넣으세요</strong>
            <ul>
              <li>권장: 원인/기능/대상 기준 키워드 (예: 인증서, 결제실패, 환불지연, 관리자승인)</li>
              <li>비권장: 너무 포괄적인 단어만 입력 (예: 오류, 문제, 문의)</li>
              <li>키워드와 제목/내용 용어를 맞추면 상위 노출 가능성이 높아집니다.</li>
            </ul>
          </div>

          <div class="guide-card">
            <strong>5) 저유사도 문의가 쌓일 때 점검 순서</strong>
            <ul>
              <li>저유사도 문의의 원문을 보고, 유사한 표현을 예상질문에 추가</li>
              <li>기존 키워드가 실제 사용자 표현과 맞는지 수정</li>
              <li>해결안이 다른 케이스와 섞이지 않게 가이드 KB를 분리 작성</li>
            </ul>
          </div>
        </div>
      </div>
    </div>

    <div v-if="showWriterPromptEditor" class="modal-overlay" @click="showWriterPromptEditor = false">
      <div class="modal info-modal" @click.stop>
        <div class="modal-head">
          <h4>KB 작성 AI 프롬프트 설정</h4>
          <button class="ghost" type="button" @click="showWriterPromptEditor = false">닫기</button>
        </div>

        <div class="form-grid">
          <label>
            키워드 생성 시스템 프롬프트
            <textarea v-model="writerPromptTemplateForm.keywordSystemPrompt" rows="3" />
          </label>
          <label>
            키워드 생성 규칙 프롬프트
            <textarea v-model="writerPromptTemplateForm.keywordRulesPrompt" rows="4" />
          </label>
          <label>
            답변 정리 시스템 프롬프트
            <textarea v-model="writerPromptTemplateForm.answerRefineSystemPrompt" rows="3" />
          </label>
          <label>
            답변 정리 규칙 프롬프트
            <textarea v-model="writerPromptTemplateForm.answerRefineRulesPrompt" rows="4" />
          </label>
        </div>

        <div class="actions">
          <button class="primary" type="button" :disabled="savingWriterPromptTemplate" @click="saveWriterPromptTemplate">
            {{ savingWriterPromptTemplate ? '저장 중...' : '저장' }}
          </button>
        </div>
      </div>
    </div>

    <div v-if="showRefinePreview" class="modal-overlay" @click="cancelRefinedSolution">
      <div class="modal info-modal" @click.stop>
        <div class="modal-head">
          <h4>AI 답변 정리 미리보기</h4>
          <button class="ghost" type="button" @click="cancelRefinedSolution">닫기</button>
        </div>

        <div class="form-grid">
          <label>
            현재 내용
            <textarea :value="form.content" rows="5" readonly />
          </label>
          <label>
            정리된 내용
            <textarea :value="refinedSolutionPreview" rows="7" readonly />
          </label>
        </div>

        <div class="actions">
          <button class="ghost" type="button" @click="cancelRefinedSolution">적용 안함</button>
          <button class="primary" type="button" @click="applyRefinedSolution">적용하기</button>
        </div>
      </div>
    </div>
    </template><!-- /tab:kb -->

    <!-- ── 공유 모달: 플랫폼 관리 (탭 공통) ── -->
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
            @compositionstart="isComposingPlatformName = true"
            @compositionend="isComposingPlatformName = false"
            @keydown.enter.prevent="() => { if (!isComposingPlatformName) addPlatform() }"
          />
          <button class="secondary" :disabled="addingPlatform" @click="addPlatform">추가</button>
        </div>

        <div class="platform-items" v-if="platformOptions.length > 0">
          <div v-for="p in platformOptions" :key="p" class="platform-item">
            <template v-if="renamingPlatform === p">
              <input v-model="renamingPlatformName"
                @compositionstart="isComposingRenamePlatform = true"
                @compositionend="isComposingRenamePlatform = false"
                @keydown.enter.prevent="() => { if (!isComposingRenamePlatform) submitRenamePlatform() }" />
              <button class="secondary compact-btn" :disabled="savingRename" @click="submitRenamePlatform">저장</button>
              <button class="ghost compact-btn" :disabled="savingRename" @click="cancelRenamePlatform">취소</button>
            </template>
            <template v-else>
              <span class="platform-name">{{ p }}</span>
              <span v-if="p === '공통'" class="scope platform">기본(삭제불가)</span>
              <button v-else class="secondary compact-btn" @click="startRenamePlatform(p)">수정</button>
              <button v-if="p !== '공통'" class="danger compact-btn" :disabled="deletingPlatform === p" @click="removePlatform(p)">삭제</button>
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

.tab-bar {
  display: flex;
  gap: 4px;
  border-bottom: 2px solid #dee2e6;
  padding-bottom: 0;
  margin-bottom: 4px;
}

.tab-btn {
  padding: 7px 20px;
  border: 1px solid transparent;
  border-bottom: none;
  border-radius: 8px 8px 0 0;
  background: #f8f9fa;
  color: #6c757d;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: background 0.15s, color 0.15s;
}

.tab-btn.active {
  background: #ffffff;
  color: #1f7a6d;
  border-color: #dee2e6;
  border-bottom-color: #ffffff;
  margin-bottom: -2px;
}

.doc-upload-form {
  display: grid;
  grid-template-columns: minmax(180px, 1.1fr) minmax(140px, 0.8fr) minmax(160px, 0.9fr) minmax(220px, 1.4fr) auto;
  gap: 12px;
  align-items: end;
  margin-top: 8px;
}

.upload-btn-row {
  display: flex;
  align-items: center;
  gap: 8px;
  justify-self: end;
  white-space: nowrap;
}

.selected-file-name {
  grid-column: 1 / -1;
  margin-top: -4px;
}

.doc-file-picker {
  width: 100%;
  max-width: 360px;
  border: 1px solid #cfd8e3;
  border-radius: 10px;
  padding: 4px;
  background: #fff;
  color: #334155;
}

.doc-file-picker::file-selector-button {
  border: 1px solid #0d6efd;
  border-radius: 8px;
  background: linear-gradient(180deg, #f7fbff 0%, #e8f2ff 100%);
  color: #0d6efd;
  font-weight: 700;
  padding: 6px 12px;
  margin-right: 10px;
  cursor: pointer;
  transition: background 0.15s ease, transform 0.1s ease;
}

.doc-file-picker:hover::file-selector-button {
  background: linear-gradient(180deg, #edf5ff 0%, #dcecff 100%);
}

.doc-file-picker:active::file-selector-button {
  transform: translateY(1px);
}

.doc-file-inline-row {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.kw-chip {
  display: inline-block;
  background: #e9f5f3;
  color: #1f7a6d;
  border-radius: 6px;
  padding: 1px 7px;
  font-size: 0.8rem;
  margin: 1px 3px 1px 0;
}

.hint {
  font-size: 0.75rem;
  color: #868e96;
  font-weight: 400;
}

.panel {
  border: 1px solid #dee2e6;
  border-radius: 12px;
  background: #ffffff;
  box-shadow: 0 0.45rem 1rem rgba(0, 0, 0, 0.06);
  padding: 18px;
}

.list-panel {
  background: #ffffff;
}

.panel-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 12px;
}

.writer-toggle-head {
  cursor: pointer;
  user-select: none;
}

.writer-toggle-indicator {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 20px;
  height: 20px;
  border-radius: 999px;
  border: 1px solid #dbe4f2;
  background: #f8fbff;
  color: #4b5563;
  font-size: 11px;
  line-height: 1;
  font-weight: 700;
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
  grid-template-columns: 1fr;
  gap: 8px;
  align-items: center;
}

.label-head-inline {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  width: fit-content;
  max-width: 100%;
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

.ai-action-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: auto;
  flex: 0 0 auto;
  border-color: #5b8ff0;
  color: #ffffff;
  background: #3b82f6;
}

.ai-action-soft {
  border: 1px solid #9dbcf6;
  border-radius: 10px;
  padding: 4px 9px;
  font-size: 12px;
  line-height: 1.2;
  font-weight: 700;
  cursor: pointer;
  color: #ffffff;
  background: #78a8f9;
}

.ai-action-btn:disabled {
  opacity: 0.6;
}

.ai-action-btn:hover:not(:disabled),
.ai-action-btn:focus-visible:not(:disabled),
.ai-action-btn:active:not(:disabled) {
  border-color: #5b8ff0;
  color: #ffffff;
  background: #3b82f6;
}

.ai-action-soft:hover:not(:disabled),
.ai-action-soft:focus-visible:not(:disabled),
.ai-action-soft:active:not(:disabled) {
  border-color: #9dbcf6;
  color: #ffffff;
  background: #78a8f9;
  box-shadow: none;
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

.generated-candidates {
  display: grid;
  gap: 6px;
}

.generated-list {
  display: grid;
  gap: 6px;
}

.generated-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  border: 1px solid #dbe6f2;
  background: #f8fbff;
  border-radius: 10px;
  padding: 8px 10px;
  color: #334155;
}

.generated-item > span {
  min-width: 0;
  overflow-wrap: anywhere;
  word-break: break-word;
}

.generated-actions {
  display: flex;
  align-items: center;
  gap: 6px;
  flex: 0 0 auto;
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

.compact-btn {
  padding: 4px 8px;
  border-radius: 8px;
  font-size: 12px;
}

.filters {
  display: grid;
  grid-template-columns: 170px 170px 1fr;
  gap: 8px;
  margin-bottom: 12px;
}

.kb-list {
  display: grid;
  gap: 12px;
}

.kb-item {
  border: 1px solid #dfe6ee;
  border-radius: 12px;
  padding: 15px;
  background: #ffffff;
  box-shadow: 0 4px 12px rgba(15, 23, 42, 0.05);
  transition: border-color 0.16s ease, box-shadow 0.16s ease;
}

.kb-item:hover {
  border-color: #c8d5e6;
  box-shadow: 0 8px 18px rgba(15, 23, 42, 0.08);
}

.kb-top {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  align-items: flex-start;
  margin-bottom: 10px;
}

.kb-item-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-left: auto;
}

.badges {
  display: flex;
  align-items: center;
  gap: 6px;
}

.q-btn {
  border: 1px solid #d2deec;
  background: #f8fbff;
  font-weight: 700;
  text-align: center;
  cursor: pointer;
  color: #36506d;
  padding: 6px 10px;
  line-height: 1.35;
  border-radius: 999px;
  white-space: nowrap;
  flex: 0 0 auto;
}

.q-btn:hover {
  background: #eef5ff;
  border-color: #b9cbe0;
}

.kb-body {
  display: grid;
  gap: 8px;
  margin-bottom: 10px;
}

.kb-row {
  display: grid;
  grid-template-columns: 52px 1fr;
  gap: 10px;
  align-items: start;
}

.kb-label {
  color: #6b7280;
  font-size: 12px;
  font-weight: 700;
  line-height: 1.6;
}

.kb-value {
  color: #1f2937;
  font-size: 14px;
  line-height: 1.6;
  min-width: 0;
  overflow-wrap: anywhere;
  word-break: break-word;
}

.file-link-btn {
  border: none;
  background: transparent;
  padding: 0;
  margin: 0;
  color: #1d4ed8;
  text-decoration: underline;
  font-size: 14px;
  line-height: 1.6;
  cursor: pointer;
  text-align: left;
}

.file-link-btn:hover {
  color: #1e40af;
}

.kb-question-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.kb-question-row > span {
  min-width: 0;
  overflow-wrap: anywhere;
  word-break: break-word;
}

.kb-title {
  font-weight: 700;
  color: #1f2f45;
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

.kb-answer {
  margin: 0;
  white-space: pre-wrap;
  font-family: inherit;
  color: #374151;
  line-height: 1.62;
  background: #f9fbfd;
  border: 1px solid #e4ebf3;
  border-radius: 10px;
  padding: 10px 12px;
  min-width: 0;
  overflow-wrap: anywhere;
  word-break: break-word;
}

.meta {
  display: flex;
  gap: 8px;
  color: #516174;
  font-size: 12px;
  flex-wrap: wrap;
  margin-top: 2px;
}

.meta-chip {
  display: inline-flex;
  align-items: center;
  border: 1px solid #d7e2f2;
  background: #f8fbff;
  border-radius: 999px;
  padding: 4px 9px;
}

.similar-box {
  margin-top: 12px;
  border-top: 1px dashed #d8e1ea;
  padding-top: 12px;
  background: #fcfdff;
  border-radius: 10px;
  padding-left: 10px;
  padding-right: 10px;
  padding-bottom: 10px;
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
  .doc-upload-form,
  .filters {
    grid-template-columns: 1fr;
  }

  .upload-btn-row {
    justify-self: stretch;
    justify-content: flex-start;
    white-space: normal;
    flex-wrap: wrap;
  }

  .kb-row {
    grid-template-columns: 1fr;
    gap: 2px;
  }

  .kb-top {
    align-items: stretch;
    flex-direction: column;
  }

  .kb-item-actions {
    margin-left: 0;
    justify-content: flex-end;
  }

  .kb-question-row {
    align-items: stretch;
    flex-direction: column;
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
