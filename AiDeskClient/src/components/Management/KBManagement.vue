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
      user?.loginId,
      user?.LoginId,
      user?.name,
      user?.Name,
      user?.username,
      user?.Username
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
const bulkImportResult = ref(null)
const csvFileInput = ref(null)
const bulkImporting = ref(false)
const showBulkModal = ref(false)
const bulkSelectedFile = ref(null)
const bulkTotalCount = ref(0)
const writerPromptTemplateForm = ref({
  answerRefineSystemPrompt: '',
  answerRefineRulesPrompt: '',
  keywordSystemPrompt: '',
  keywordRulesPrompt: '',
  similarQuestionSystemPrompt: '',
  similarQuestionRulesPrompt: ''
})
const MAX_EXPECTED_QUESTIONS = 10

const kbTotalPages = computed(() => Math.max(1, Math.ceil(kbTotal.value / kbPageSize)))
const kbPageNumbers = computed(() => {
  const total = kbTotalPages.value
  const cur = kbPage.value
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1)
  const pages = []
  pages.push(1)
  if (cur > 3) pages.push('...')
  const start = Math.max(2, cur - 2)
  const end = Math.min(total - 1, cur + 2)
  for (let i = start; i <= end; i++) pages.push(i)
  if (cur < total - 2) pages.push('...')
  pages.push(total)
  return pages
})
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
    const isEdit = !!form.value.id
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

    alert(isEdit ? 'KB가 수정되었습니다.' : 'KB가 저장되었습니다.')
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
    answerRefineSystemPrompt: normalizeLineBreaks(response.data.answerRefineSystemPrompt),
    answerRefineRulesPrompt: normalizeLineBreaks(response.data.answerRefineRulesPrompt),
    keywordSystemPrompt: normalizeLineBreaks(response.data.keywordSystemPrompt),
    keywordRulesPrompt: normalizeLineBreaks(response.data.keywordRulesPrompt),
    similarQuestionSystemPrompt: normalizeLineBreaks(response.data.similarQuestionSystemPrompt),
    similarQuestionRulesPrompt: normalizeLineBreaks(response.data.similarQuestionRulesPrompt)
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
    !formData.answerRefineSystemPrompt.trim() ||
    !formData.answerRefineRulesPrompt.trim() ||
    !formData.keywordSystemPrompt.trim() ||
    !formData.keywordRulesPrompt.trim() ||
    !formData.similarQuestionSystemPrompt.trim() ||
    !formData.similarQuestionRulesPrompt.trim()
  ) {
    alert('KB 작성 프롬프트 항목을 모두 입력해주세요.')
    return
  }

  savingWriterPromptTemplate.value = true
  try {
    const response = await axios.put(`${API_URL}/knowledgebase/writer-prompt-template`, {
      answerRefineSystemPrompt: formData.answerRefineSystemPrompt,
      answerRefineRulesPrompt: formData.answerRefineRulesPrompt,
      keywordSystemPrompt: formData.keywordSystemPrompt,
      keywordRulesPrompt: formData.keywordRulesPrompt,
      similarQuestionSystemPrompt: formData.similarQuestionSystemPrompt,
      similarQuestionRulesPrompt: formData.similarQuestionRulesPrompt
    })

    writerPromptTemplateForm.value = {
      answerRefineSystemPrompt: normalizeLineBreaks(response.data.answerRefineSystemPrompt),
      answerRefineRulesPrompt: normalizeLineBreaks(response.data.answerRefineRulesPrompt),
      keywordSystemPrompt: normalizeLineBreaks(response.data.keywordSystemPrompt),
      keywordRulesPrompt: normalizeLineBreaks(response.data.keywordRulesPrompt),
      similarQuestionSystemPrompt: normalizeLineBreaks(response.data.similarQuestionSystemPrompt),
      similarQuestionRulesPrompt: normalizeLineBreaks(response.data.similarQuestionRulesPrompt)
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
    alert('KB가 삭제되었습니다.')
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
})

function downloadCsvTemplate() {
  axios.get(`${API_URL}/knowledgebase/bulk-import/template`, {
    headers: getActorHeader(),
    responseType: 'blob'
  }).then((res) => {
    const url = URL.createObjectURL(new Blob([res.data], { type: 'text/csv;charset=utf-8;' }))
    const link = document.createElement('a')
    link.href = url
    link.download = 'kb-import-template.csv'
    link.click()
    URL.revokeObjectURL(url)
  }).catch(() => {
    alert('양식 다운로드에 실패했습니다.')
  })
}

function openBulkModal() {
  bulkSelectedFile.value = null
  bulkTotalCount.value = 0
  bulkImportResult.value = null
  showBulkModal.value = true
}

function closeBulkModal() {
  if (bulkImporting.value) return
  showBulkModal.value = false
  bulkImportResult.value = null
  bulkSelectedFile.value = null
  if (csvFileInput.value) csvFileInput.value.value = ''
}

function onBulkModalFileChange(event) {
  const file = event.target.files?.[0]
  if (!file) return
  if (!file.name.endsWith('.csv')) {
    alert('CSV 파일(.csv)만 업로드 가능합니다.')
    event.target.value = ''
    return
  }
  bulkSelectedFile.value = file
  // 클라이언트에서 CSV 행 수 카운트 (헤더 제외)
  const reader = new FileReader()
  reader.onload = (e) => {
    const text = e.target.result
    // 큰따옴표 안의 줄바꿈을 무시하고 실제 데이터 행 수 계산
    let count = 0
    let inQuotes = false
    for (const ch of text) {
      if (ch === '"') inQuotes = !inQuotes
      else if (ch === '\n' && !inQuotes) count++
    }
    bulkTotalCount.value = Math.max(0, count - 1) // 헤더 1행 제외
  }
  reader.readAsText(file, 'utf-8')
}

async function startBulkUpload() {
  if (!bulkSelectedFile.value) return
  bulkImporting.value = true
  bulkImportResult.value = null
  try {
    const formData = new FormData()
    formData.append('file', bulkSelectedFile.value)
    const res = await axios.post(`${API_URL}/knowledgebase/bulk-import`, formData, {
      headers: { ...getActorHeader(), 'Content-Type': 'multipart/form-data' }
    })
    bulkImportResult.value = res.data
  } catch (err) {
    alert('CSV 업로드에 실패했습니다: ' + (err.response?.data?.error || err.message))
    showBulkModal.value = false
  } finally {
    bulkImporting.value = false
    if (csvFileInput.value) csvFileInput.value.value = ''
  }
}

async function onCsvFileSelected(event) {
  onBulkModalFileChange(event)
}
</script>

<template>
  <section class="kb-wrap">
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
          <button class="ghost btn-csv-upload" type="button" @click.stop="openBulkModal">CSV 업로드</button>
          <button class="ghost btn-prompt-setting" type="button" @click.stop="openWriterPromptEditor">프롬프트 설정</button>
          <input ref="csvFileInput" type="file" accept=".csv" style="display:none" @change="onBulkModalFileChange" />
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
              placeholder="예) 인증서 파일 경로 어떻게 찾아요?"
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

    <!-- CSV 업로드 통합 팝업 -->
    <div v-if="showBulkModal" class="modal-overlay" @click.self="closeBulkModal">
      <div class="modal-box bulk-modal" @click.stop>

        <!-- 헤더 -->
        <div class="bulk-modal-head">
          <h3>CSV 대량 등록</h3>
          <button class="icon-btn" type="button" :disabled="bulkImporting" @click="closeBulkModal">✕</button>
        </div>

        <!-- ① 파일 선택 단계 -->
        <template v-if="!bulkImporting && !bulkImportResult">
          <div class="bulk-step-section">
            <p class="bulk-step-label">1단계 — CSV 양식 다운로드</p>
            <button class="ghost" type="button" @click="downloadCsvTemplate">양식 다운로드</button>
          </div>
          <div class="bulk-step-section">
            <p class="bulk-step-label">2단계 — 파일 선택</p>
            <div class="bulk-file-area" @click="csvFileInput.click()">
              <span v-if="bulkSelectedFile" class="bulk-file-name">
                📄 {{ bulkSelectedFile.name }}
                <small>{{ bulkTotalCount }}건 감지됨</small>
              </span>
              <span v-else class="bulk-file-placeholder">클릭하여 CSV 파일 선택</span>
            </div>
          </div>
          <div class="modal-actions">
            <button class="primary" :disabled="!bulkSelectedFile" @click="startBulkUpload">업로드 시작</button>
            <button class="ghost" @click="closeBulkModal">취소</button>
          </div>
        </template>

        <!-- ② 업로드 중 -->
        <template v-if="bulkImporting">
          <div class="bulk-progress-wrap">
            <div class="bulk-spinner"></div>
            <p class="bulk-loading-title">KB 등록 중입니다…</p>
            <p class="bulk-loading-sub">총 <strong>{{ bulkTotalCount }}</strong>건 처리 중 &nbsp;·&nbsp; 임베딩 생성 중이니 잠시만 기다려 주세요.</p>
          </div>
        </template>

        <!-- ③ 결과 -->
        <template v-if="bulkImportResult && !bulkImporting">
          <div class="bulk-result-summary-bar">
            <span class="brs-total">전체 <strong>{{ bulkImportResult.total }}</strong>건</span>
            <span class="brs-ok">✅ 성공 <strong>{{ bulkImportResult.successCount }}</strong>건</span>
            <span class="brs-fail">❌ 실패 <strong>{{ bulkImportResult.failCount }}</strong>건</span>
          </div>
          <div class="bulk-result-list">
            <div v-for="r in bulkImportResult.results" :key="r.row"
                 :class="['bulk-row', r.status]">
              <span class="bulk-row-num">{{ r.row }}행</span>
              <span class="bulk-row-title">{{ r.title || '(제목 없음)' }}</span>
              <span class="bulk-row-status">{{ r.status === 'ok' ? '✅ 등록' : `❌ ${r.reason}` }}</span>
            </div>
          </div>
          <div class="modal-actions">
            <button class="primary" @click="closeBulkModal(); fetchKbs()">확인</button>
          </div>
        </template>

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
        <input v-model="keyword" placeholder="제목/내용/예상질문/키워드/KB번호 검색" />
      </div>

      <div v-if="error" class="error">{{ error }}</div>
      <div v-else-if="loading" class="empty">불러오는 중...</div>
      <div v-else-if="kbList.length === 0" class="empty">등록된 KB가 없습니다.</div>

      <div v-else class="kb-list">
        <article v-for="kb in kbList" :key="kb.id" class="kb-item">
          <div class="kb-top">
            <div class="badges">
              <span class="scope id-badge">#{{ kb.id }}</span>
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
        <button class="pager-btn nav" :disabled="kbPage <= 1" @click="goKbPage(1)" title="처음">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="11 17 6 12 11 7"/><polyline points="18 17 13 12 18 7"/></svg>
        </button>
        <button class="pager-btn nav" :disabled="kbPage <= 1" @click="goKbPage(kbPage - 1)" title="이전">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>
        </button>
        <template v-for="p in kbPageNumbers" :key="p">
          <span v-if="p === '...'" class="pager-ellipsis">…</span>
          <button v-else class="pager-btn" :class="{ active: p === kbPage }" @click="goKbPage(p)">{{ p }}</button>
        </template>
        <button class="pager-btn nav" :disabled="kbPage >= kbTotalPages" @click="goKbPage(kbPage + 1)" title="다음">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>
        </button>
        <button class="pager-btn nav" :disabled="kbPage >= kbTotalPages" @click="goKbPage(kbTotalPages)" title="마지막">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="13 17 18 12 13 7"/><polyline points="6 17 11 12 6 7"/></svg>
        </button>
        <span class="pager-info">총 {{ kbTotal }}건</span>
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
            키워드 매칭 점수 보정과 AI 재정렬을 함께 사용합니다. 아래 기준대로 작성하면 검색 정확도를 높이는 데 도움이 됩니다.
          </p>

          <div class="guide-card">
            <strong>1) 점수는 이렇게 계산됩니다</strong>
            <p>
              질문 임베딩으로 본문(document) 벡터와 예상질문(expected) 벡터를 함께 검색한 뒤,
              KB 단위로 병합 점수를 계산합니다.
            </p>
            <p>키워드는 강제 통과가 아니라 약한 보정치로만 반영되며, 최종 후보는 AI 재정렬을 거칩니다.</p>
            <p class="guide-formula">최종 후보 = document/expected 벡터 검색 → KB 병합 + 키워드 약보정 → AI rerank top5</p>
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
            <p>증상/상황/실패지점이 드러나도록 쓰고, 오탈자/축약어(예: 공인인증서, 인증서, cert)도 함께 넣어주세요.</p>
          </div>

          <div class="guide-card">
            <strong>4) 키워드는 짧고 구체적으로 넣으세요</strong>
            <ul>
              <li>권장: 원인/기능/대상 기준 키워드 (예: 인증서, 결제실패, 환불지연, 관리자승인)</li>
              <li>비권장: 너무 포괄적인 단어만 입력 (예: 오류, 문제, 문의)</li>
              <li>키워드는 보조 신호이므로, 제목/내용/예상질문의 표현 정합성이 더 중요합니다.</li>
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
            내용정리 시스템 프롬프트
            <textarea v-model="writerPromptTemplateForm.answerRefineSystemPrompt" rows="3" />
          </label>
          <label>
            내용정리 규칙 프롬프트
            <textarea v-model="writerPromptTemplateForm.answerRefineRulesPrompt" rows="4" />
          </label>
          <label>
            키워드 생성 시스템 프롬프트
            <textarea v-model="writerPromptTemplateForm.keywordSystemPrompt" rows="3" />
          </label>
          <label>
            키워드 생성 규칙 프롬프트
            <textarea v-model="writerPromptTemplateForm.keywordRulesPrompt" rows="4" />
          </label>
          <label>
            예상질문 생성 시스템 프롬프트
            <textarea v-model="writerPromptTemplateForm.similarQuestionSystemPrompt" rows="3" />
          </label>
          <label>
            예상질문 생성 규칙 프롬프트
            <textarea v-model="writerPromptTemplateForm.similarQuestionRulesPrompt" rows="4" />
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
    <!-- /tab:kb -->

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
  justify-content: center;
  gap: 4px;
  color: #495057;
  font-size: 13px;
}
.pager-btn {
  min-width: 32px;
  height: 32px;
  padding: 0 10px;
  border: 1px solid #dee2e6;
  border-radius: 6px;
  background: #fff;
  color: #495057;
  font-size: 13px;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  transition: background 0.15s, color 0.15s, border-color 0.15s;
  line-height: 1;
}
.pager-btn.nav {
  min-width: 32px;
  padding: 0;
  color: #6c757d;
}
.pager-btn:hover:not(:disabled) {
  background: #f1f3f5;
  border-color: #ced4da;
  color: #212529;
}
.pager-btn:disabled {
  opacity: 0.3;
  cursor: default;
}
.pager-btn.active {
  background: #3b82f6;
  color: #fff;
  border-color: #3b82f6;
  font-weight: 700;
}
.pager-ellipsis {
  padding: 0 4px;
  color: #adb5bd;
  user-select: none;
}
.pager-info {
  margin-left: 8px;
  color: #868e96;
  font-size: 12px;
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



/* bulk import result modal */
.modal-box {
  background: #fff;
  border-radius: 12px;
  padding: 24px;
  width: min(600px, 96vw);
  max-height: 80vh;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.modal-box h3 {
  margin: 0;
}

.bulk-loading-box {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  background: #fff;
  border-radius: 16px;
  padding: 40px 48px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.18);
  text-align: center;
}

.bulk-modal {
  width: min(560px, 96vw);
}

.bulk-modal-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.bulk-modal-head h3 { margin: 0; }

.icon-btn {
  background: none;
  border: none;
  cursor: pointer;
  font-size: 16px;
  color: #868e96;
  padding: 4px 6px;
  border-radius: 6px;
  line-height: 1;
}
.icon-btn:hover { background: #f1f3f5; color: #212529; }
.icon-btn:disabled { opacity: 0.4; cursor: not-allowed; }

.bulk-step-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.bulk-step-label {
  margin: 0;
  font-size: 13px;
  font-weight: 600;
  color: #495057;
}

.bulk-file-area {
  border: 2px dashed #ced4da;
  border-radius: 10px;
  padding: 20px;
  text-align: center;
  cursor: pointer;
  transition: border-color 0.2s, background 0.2s;
}
.bulk-file-area:hover { border-color: #4263eb; background: #f0f4ff; }

.bulk-file-name {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 14px;
  font-weight: 500;
  color: #212529;
}
.bulk-file-name small { font-size: 12px; color: #868e96; font-weight: 400; }

.bulk-file-placeholder { font-size: 14px; color: #adb5bd; }

.bulk-progress-wrap {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 14px;
  padding: 24px 0;
  text-align: center;
}

.bulk-result-summary-bar {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
  padding: 12px 16px;
  background: #f8f9fa;
  border-radius: 8px;
  font-size: 14px;
  align-items: center;
}
.brs-total { color: #495057; }
.brs-ok { color: #198754; font-weight: 500; }
.brs-fail { color: #dc3545; font-weight: 500; }

.bulk-summary {
  width: 48px;
  height: 48px;
  border: 5px solid #e9ecef;
  border-top-color: #4263eb;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

.bulk-loading-title {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
  color: #212529;
}

.bulk-loading-sub {
  margin: 0;
  font-size: 13px;
  color: #868e96;
}

.bulk-summary {
  margin: 0;
  font-size: 14px;
  color: #495057;
}

.bulk-summary .ok { color: #198754; }
.bulk-summary .fail { color: #dc3545; }

.bulk-result-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-height: 340px;
  overflow-y: auto;
  border: 1px solid #dee2e6;
  border-radius: 8px;
  padding: 10px;
  font-size: 13px;
}

.bulk-row {
  display: grid;
  grid-template-columns: 46px 1fr auto;
  gap: 8px;
  align-items: center;
  padding: 4px 2px;
  border-bottom: 1px solid #f1f3f5;
}

.bulk-row:last-child { border-bottom: none; }
.bulk-row.ok { background: #f0fdf4; }
.bulk-row.fail, .bulk-row.skip { background: #fff5f5; }

.bulk-row-num { color: #868e96; font-size: 12px; }
.bulk-row-title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.bulk-row-status { white-space: nowrap; font-size: 12px; }

.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 6px;
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

.scope.id-badge {
  background: #f1f3f5;
  color: #6c757d;
  font-family: monospace;
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

  .writer-toggle-head {
    flex-direction: row;
    align-items: center;
    flex-wrap: nowrap;
  }

  .panel-head-title {
    flex: 1;
    min-width: 0;
  }

  .panel-head-actions {
    flex-wrap: nowrap;
    gap: 6px;
  }

  .panel-head-actions .ghost {
    font-size: 13px;
  }

  .guide-trigger,
  .btn-csv-upload,
  .btn-prompt-setting {
    display: none;
  }
}
</style>
