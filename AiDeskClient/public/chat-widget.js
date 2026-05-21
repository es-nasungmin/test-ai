(function () {
  var STYLE_ID = 'crm-chat-widget-style';

  function ensureStyle() {
    if (document.getElementById(STYLE_ID)) return;

    var style = document.createElement('style');
    style.id = STYLE_ID;
    style.textContent = ""
      + ".crm-chat-fab{position:fixed;width:64px;height:64px;border-radius:50%;border:none;color:#fff;font-size:15px;font-weight:700;cursor:pointer;box-shadow:0 6px 18px rgba(0,0,0,.28);z-index:9999;transition:transform .2s,box-shadow .2s;display:flex;align-items:center;justify-content:center;}"
      + ".crm-chat-fab:hover{transform:scale(1.05);box-shadow:0 6px 24px rgba(0,0,0,.35);}"
      + ".crm-chat-popup{position:fixed;width:430px;height:660px;background:#fff;border-radius:14px;box-shadow:0 16px 42px rgba(15,23,42,.22);z-index:9998;display:flex;flex-direction:column;overflow:hidden;}"
      + ".crm-chat-header{color:#fff;padding:16px 16px 14px;display:flex;align-items:center;justify-content:space-between;flex-shrink:0;}"
      + ".crm-chat-title{font-weight:900;font-size:17px;letter-spacing:-.1px;}"
      + ".crm-chat-sub{margin-top:4px;font-size:12px;opacity:.93;}"
      + ".crm-chat-actions{display:flex;align-items:center;gap:6px;}"
      + ".crm-chat-platform{border:1px solid rgba(255,255,255,.55);border-radius:9px;padding:5px 8px;background:rgba(255,255,255,.18);color:#fff;font-size:12px;min-width:108px;}"
      + ".crm-chat-platform option{color:#111827;}"
      + ".crm-chat-close{background:rgba(255,255,255,.18);border:none;border-radius:8px;color:#fff;font-size:14px;padding:5px 9px;cursor:pointer;}"
      + ".crm-chat-quick-menu{padding:14px 14px 12px;display:flex;flex-direction:column;gap:10px;flex-shrink:0;background:#f3f6fb;}"
      + ".crm-chat-quick-title{font-size:12px;font-weight:800;color:#1e3a66;letter-spacing:.1px;}"
      + ".crm-chat-category-row{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:6px;}"
      + ".crm-chat-category-select{border:1px solid #cbd5e1;border-radius:8px;padding:6px 7px;background:#fff;color:#1e293b;font-size:12px;}"
      + ".crm-chat-menu-head{display:flex;align-items:center;justify-content:space-between;gap:10px;padding:8px 10px;background:#ffffff;border:1px solid #d9e2ef;border-radius:10px;}"
      + ".crm-chat-menu-path{font-size:12px;color:#23426f;font-weight:800;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}"
      + ".crm-chat-menu-actions{display:flex;gap:6px;flex-wrap:wrap;}"
      + ".crm-chat-menu-action{border:1px solid #c7d5ea;background:#fff;border-radius:999px;padding:5px 11px;font-size:12px;color:#294b78;cursor:pointer;font-weight:700;}"
      + ".crm-chat-menu-list{display:grid;grid-template-columns:1fr;gap:7px;margin-top:10px;}"
      + ".crm-chat-menu-item{border:1px solid #ccd8ea;background:#fff;border-radius:10px;padding:10px 11px;font-size:12px;color:#1e293b;text-align:left;cursor:pointer;line-height:1.4;font-weight:700;}"
      + ".crm-chat-menu-item:hover,.crm-chat-menu-action:hover{border-color:#94a3b8;background:#f8fafc;}"
      + ".crm-chat-category-questions-title{font-size:12px;font-weight:800;color:#1f3f69;margin-top:14px;}"
      + ".crm-chat-category-questions{display:flex;gap:10px;flex-wrap:wrap;margin-top:10px;padding-top:2px;}"
      + ".crm-chat-category-question{border:1px solid #c8d8ee;background:#ffffff;color:#24456f;border-radius:999px;padding:7px 11px;font-size:12px;cursor:pointer;line-height:1.35;font-weight:700;}"
      + ".crm-chat-category-question:hover{border-color:#94a3b8;background:#f8fafc;}"
      + ".crm-chat-inline-choices{display:flex;gap:8px;flex-wrap:wrap;margin-top:8px;}"
      + ".crm-chat-inline-choice{border:1px solid #c8d8ee;background:#fff;color:#24456f;border-radius:999px;padding:7px 11px;font-size:12px;cursor:pointer;line-height:1.35;font-weight:700;}"
      + ".crm-chat-inline-choice:hover{border-color:#94a3b8;background:#f8fafc;}"
      + ".crm-chat-inline-choice:disabled{opacity:.45;cursor:not-allowed;}"
      + ".crm-chat-quick-categories{display:flex;gap:6px;overflow-x:auto;padding-bottom:2px;}"
      + ".crm-chat-quick-category{border:1px solid #cbd5e1;background:#fff;color:#334155;border-radius:999px;padding:5px 10px;font-size:12px;cursor:pointer;white-space:nowrap;flex-shrink:0;}"
      + ".crm-chat-quick-category.active{font-weight:700;}"
      + ".crm-chat-quick-questions{display:flex;gap:6px;overflow-x:auto;padding-bottom:2px;}"
      + ".crm-chat-quick-question{border:1px solid #cbd5e1;background:#fff;color:#334155;border-radius:8px;padding:6px 10px;font-size:12px;cursor:pointer;white-space:nowrap;flex-shrink:0;}"
      + ".crm-chat-quick-question:hover,.crm-chat-quick-category:hover{border-color:#94a3b8;background:#f8fafc;}"
      + ".crm-chat-messages{flex:1;overflow-y:auto;padding:16px;background:#f7f9fc;display:flex;flex-direction:column;gap:12px;}"
      + ".crm-chat-row{display:flex;align-items:flex-start;gap:8px;}"
      + ".crm-chat-row.user{flex-direction:row-reverse;}"
      + ".crm-chat-avatar{width:34px;height:34px;border-radius:50%;display:flex;align-items:center;justify-content:center;color:#fff;font-size:11px;font-weight:800;letter-spacing:.3px;flex-shrink:0;}"
      + ".crm-chat-content{display:flex;flex-direction:column;gap:4px;max-width:78%;}"
      + ".crm-chat-bubble{background:#fff;padding:11px 14px;border-radius:12px 12px 12px 4px;font-size:14px;line-height:1.58;box-shadow:0 2px 8px rgba(2,24,53,.08);white-space:pre-wrap;word-break:break-word;border:1px solid #e4ebf5;}"
      + ".crm-chat-row.user .crm-chat-bubble{color:#fff;border-radius:14px 14px 4px 14px;}"
      + ".crm-chat-time{font-size:11px;color:#a0aec0;padding:0 2px;}"
      + ".crm-chat-row.user .crm-chat-time{text-align:right;}"
      + ".crm-chat-input{display:flex;gap:8px;padding:11px 14px;border-top:1px solid #dde6f2;background:#ffffff;flex-shrink:0;}"
      + ".crm-chat-textarea{flex:1;padding:9px 12px;border:1px solid #ccd8ea;border-radius:10px;font-size:14px;resize:none;font-family:inherit;line-height:1.4;outline:none;background:#fbfdff;}"
      + ".crm-chat-send{padding:0 14px;color:#fff;border:none;border-radius:10px;font-size:18px;cursor:pointer;transition:opacity .2s;min-width:46px;}"
      + ".crm-chat-send:disabled{opacity:.4;cursor:not-allowed;}"
      + ".crm-chat-loading{display:flex;gap:5px;align-items:center;padding:12px 16px;}"
      + ".crm-chat-dot{width:7px;height:7px;border-radius:50%;animation:crm-chat-bounce 1.2s infinite ease-in-out;}"
      + ".crm-chat-dot:nth-child(2){animation-delay:.2s;}"
      + ".crm-chat-dot:nth-child(3){animation-delay:.4s;}"
      + "@keyframes crm-chat-bounce{0%,80%,100%{transform:scale(.6);opacity:.4;}40%{transform:scale(1);opacity:1;}}"
      + "@media (max-width:480px){.crm-chat-popup{width:calc(100vw - 16px);height:calc(100vh - 96px);}.crm-chat-fab{display:none!important;}.crm-chat-popup{display:none!important;}.crm-chat-category-row{grid-template-columns:1fr;}.crm-chat-menu-list{grid-template-columns:1fr;}}";

    document.head.appendChild(style);
  }

  function now() {
    return new Date().toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit' });
  }

  function linkify(text) {
    if (typeof text !== 'string' || !text) return text;
    var escapeHtml = function(s) {
      return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    };
    var parts = text.split(/(https?:\/\/[^\s]+)/);
    return parts.map(function(part, i) {
      if (i % 2 === 1) {
        return '<a href="' + part + '" target="_blank" rel="noopener noreferrer" style="color:#3b82f6;text-decoration:underline;cursor:pointer;word-break:break-all;">' + escapeHtml(part) + '</a>';
      }
      return escapeHtml(part);
    }).join('');
  }

  function platformLabel(platform) {
    var p = typeof platform === 'string' ? platform.trim() : '';
    if (!p || p === '전체 플랫폼') return '';
    return p;
  }

  function resolveGreetingPlatform(opts, selectedPlatform) {
    var configuredPlatformLabel = typeof opts.platformLabel === 'string' ? opts.platformLabel.trim() : '';
    var legacyPlatformName = typeof opts.platformName === 'string' ? opts.platformName.trim() : '';
    return platformLabel(configuredPlatformLabel || legacyPlatformName || selectedPlatform || opts.platform || opts.defaultPlatform);
  }

  function defaultWelcome(isAdmin, platform) {
    var p = platformLabel(platform);
    if (isAdmin) {
      return p
        ? '안녕하세요! ' + p + ' 상담 도우미입니다.\n관리자 문의를 도와드릴게요.'
        : '안녕하세요! 상담 도우미입니다.\n관리자 문의를 도와드릴게요.';
    }
    return p
      ? '안녕하세요! ' + p + ' 챗봇입니다.\n무엇을 도와드릴까요?'
      : '안녕하세요! 고객센터 챗봇입니다.\n무엇을 도와드릴까요?';
  }

  function defaultTreeWelcome(isAdmin, platform) {
    var p = platformLabel(platform);
    return '무엇을 도와드릴까요? 아래 메뉴를 선택하거나 궁금하신 내용을 질문해주세요.';
  }

  function headerSubtitle(isAdmin) {
    return isAdmin
      ? '학습된 운영 지식을 바탕으로 안내합니다'
      : '학습된 지식 내용을 바탕으로 안내합니다';
  }

  function normalizePlatforms(list) {
    var source = Array.isArray(list) ? list : [];
    var cleaned = source
      .map(function (x) { return typeof x === 'string' ? x.trim() : ''; })
      .filter(function (x) { return !!x && x !== '공통' && x !== '전체 플랫폼'; });
    var unique = Array.from(new Set(cleaned));
    return ['전체 플랫폼'].concat(unique);
  }

  function defaultQuickCategories() {
    return [
      {
        id: 'certificate',
        label: '인증서',
        questions: [
          { label: '인증서 조회가 안돼요', question: '인증서 조회가 안돼요' },
          { label: '인증서 등록 방법', question: '인증서 등록 방법 알려주세요' },
          { label: '인증서 갱신 방법', question: '인증서 갱신 방법 알려주세요' }
        ]
      },
      {
        id: 'loan',
        label: '대출/자금',
        questions: [
          { label: '정책자금 신청 절차', question: '정책자금 신청 절차 알려주세요' },
          { label: '대출 조회 방법', question: '대출 조회 방법 알려주세요' },
          { label: '융자한도 확인', question: '융자한도 확인 방법 알려주세요' }
        ]
      },
      {
        id: 'agreement',
        label: '전자약정',
        questions: [
          { label: '전자약정 진행 방법', question: '전자약정 진행 방법 알려주세요' },
          { label: '전자약정 오류', question: '전자약정 오류가 발생했어요' }
        ]
      },
      {
        id: 'followup',
        label: '사후관리',
        questions: [
          { label: '사후관리 제출서류', question: '사후관리 제출서류 알려주세요' },
          { label: '사후관리 점검 항목', question: '사후관리 점검 항목 알려주세요' }
        ]
      }
    ];
  }

  function normalizeQuickCategories(input) {
    var source = Array.isArray(input) ? input : defaultQuickCategories();
    var normalized = source
      .map(function (item, index) {
        var label = item && typeof item.label === 'string' ? item.label.trim() : '';
        var id = item && typeof item.id === 'string' ? item.id.trim() : '';
        var questions = Array.isArray(item && item.questions) ? item.questions : [];
        var mappedQuestions = questions
          .map(function (q) {
            if (!q || typeof q !== 'object') return null;
            var qLabel = typeof q.label === 'string' ? q.label.trim() : '';
            var question = typeof q.question === 'string' ? q.question.trim() : '';
            if (!question) return null;
            return {
              label: qLabel || question,
              question: question
            };
          })
          .filter(Boolean);

        if (!label || mappedQuestions.length === 0) return null;
        return {
          id: id || ('category-' + index),
          label: label,
          questions: mappedQuestions
        };
      })
      .filter(Boolean);

    return normalized;
  }

  function createEl(tag, className, text) {
    var el = document.createElement(tag);
    if (className) el.className = className;
    if (typeof text === 'string') el.textContent = text;
    return el;
  }

  function toSafeText(value, fallback) {
    var fb = fallback || '요청 처리 중 오류가 발생했습니다.';
    if (typeof value === 'string') {
      var t = value.trim();
      return t || fb;
    }
    if (value == null) return fb;
    if (typeof value === 'number' || typeof value === 'boolean') return String(value);
    if (Array.isArray(value)) {
      var joined = value.map(function (v) { return toSafeText(v, ''); }).filter(Boolean).join('\n');
      return joined || fb;
    }
    if (typeof value === 'object') {
      if (typeof value.message === 'string' && value.message.trim()) return value.message.trim();
      if (typeof value.error === 'string' && value.error.trim()) return value.error.trim();
      try {
        var json = JSON.stringify(value);
        return json && json !== '{}' ? json : fb;
      } catch (e) {
        return fb;
      }
    }
    return fb;
  }

  function clampChannel(n) {
    return Math.max(0, Math.min(255, n));
  }

  function parseHexColor(input) {
    if (typeof input !== 'string') return null;
    var hex = input.trim();
    if (!hex) return null;
    if (hex.charAt(0) === '#') hex = hex.slice(1);
    if (hex.length === 3) {
      hex = hex.split('').map(function (ch) { return ch + ch; }).join('');
    }
    if (!/^[0-9a-fA-F]{6}$/.test(hex)) return null;
    return {
      r: parseInt(hex.slice(0, 2), 16),
      g: parseInt(hex.slice(2, 4), 16),
      b: parseInt(hex.slice(4, 6), 16)
    };
  }

  function rgbToHex(rgb) {
    return '#'
      + clampChannel(rgb.r).toString(16).padStart(2, '0')
      + clampChannel(rgb.g).toString(16).padStart(2, '0')
      + clampChannel(rgb.b).toString(16).padStart(2, '0');
  }

  function mixHexColor(base, target, ratio) {
    var b = parseHexColor(base);
    var t = parseHexColor(target);
    if (!b || !t) return typeof base === 'string' && base.trim() ? base.trim() : '#1f7a6d';
    var p = Math.max(0, Math.min(1, Number(ratio) || 0));
    return rgbToHex({
      r: Math.round(b.r + (t.r - b.r) * p),
      g: Math.round(b.g + (t.g - b.g) * p),
      b: Math.round(b.b + (t.b - b.b) * p)
    });
  }

  function createThemePalette(themeColor) {
    var base = typeof themeColor === 'string' && themeColor.trim() ? themeColor.trim() : '#1f7a6d';
    var dark = mixHexColor(base, '#000000', 0.22);
    var light = mixHexColor(base, '#ffffff', 0.22);
    var lightStrong = mixHexColor(base, '#ffffff', 0.42);
    var surface = mixHexColor(base, '#ffffff', 0.95);
    var surfaceSoft = mixHexColor(base, '#ffffff', 0.9);
    var border = mixHexColor(base, '#ffffff', 0.72);
    var botBubble = mixHexColor(base, '#ffffff', 0.92);

    return {
      base: base,
      headerGradient: 'linear-gradient(135deg, ' + dark + ' 0%, ' + base + ' 55%, ' + light + ' 100%)',
      avatarBotGradient: 'linear-gradient(145deg, ' + dark + ' 0%, ' + base + ' 55%, ' + light + ' 100%)',
      avatarUserGradient: 'linear-gradient(145deg, ' + base + ' 0%, ' + light + ' 55%, ' + lightStrong + ' 100%)',
      surface: surface,
      surfaceSoft: surfaceSoft,
      border: border,
      botBubble: botBubble
    };
  }

  function createCrmChatWidget(options) {
    ensureStyle();

    var opts = Object.assign({
      apiBaseUrl: 'http://localhost:8080/api',
      role: 'user',
      userId: '',
      username: '',
      userLoginId: '',
      title: 'AI 상담 어시스턴트',
      platform: '전체 플랫폼',
      showPlatformSelector: false,
      enableQuickCategories: false,
      enableCategorySelector: true,
      enableCategoryTree: true,
      categoryTreeApiPath: '/chatbotcategory',
      categorySelectorTitle: '카테고리로 질문 찾기',
      quickMenuTitle: '자주 찾는 질문',
      quickCategories: null,
      buttonLabel: 'CHAT',
      hideButton: false,
      themeColor: '#0f4c81',
      initiallyOpen: false,
      buttonRight: '20px',
      buttonBottom: '20px',
      popupRight: '20px',
      popupBottom: '88px',
      mountTo: document.body
    }, options || {});

    var isAdmin = opts.role === 'admin';
    if (typeof opts.defaultPlatform === 'string' && !opts.platform) {
      opts.platform = opts.defaultPlatform;
    }
    var theme = createThemePalette(opts.themeColor);

    var state = {
      widgetVisible: true,
      isOpen: !!opts.initiallyOpen,
      loading: false,
      sessionId: null,
      selectedPlatform: opts.platform || opts.defaultPlatform || '전체 플랫폼',
      platformOptions: ['전체 플랫폼'],
      platformsFetched: false,
      categoryTreeLoaded: false,
      categoryTreeLoading: false,
      categoryTreeError: '',
      categoryTreeItems: [],
      categoryTreePath: [],
      categoryTreeMenuInitialized: false,
      categoryTreeMenuToggled: false,
      quickCategories: normalizeQuickCategories(opts.quickCategories),
      selectedQuickCategoryId: null,
      messages: [
        { role: 'bot', text: defaultWelcome(isAdmin, resolveGreetingPlatform(opts, opts.platform || opts.defaultPlatform)), time: now() }
      ]
    };

    if (state.quickCategories.length > 0) {
      state.selectedQuickCategoryId = state.quickCategories[0].id;
    }

    function getUserContext() {
      return {
        userId: typeof opts.userId === 'string' || typeof opts.userId === 'number'
          ? String(opts.userId).trim()
          : '',
        username: typeof opts.username === 'string'
          ? opts.username.trim()
          : '',
        userLoginId: typeof opts.userLoginId === 'string'
          ? opts.userLoginId.trim()
          : ''
      };
    }

    var mountRoot = typeof opts.mountTo === 'string'
      ? document.querySelector(opts.mountTo)
      : opts.mountTo;

    if (!mountRoot) {
      throw new Error('chat-widget mount target not found.');
    }

    var fab = createEl('button', 'crm-chat-fab');
    fab.style.right = opts.buttonRight || '20px';
    fab.style.bottom = opts.buttonBottom || '20px';
    fab.style.background = theme.base;

    var popup = createEl('div', 'crm-chat-popup');
    popup.style.right = opts.popupRight;
    popup.style.bottom = opts.popupBottom;
    popup.style.border = '1px solid ' + theme.border;

    var header = createEl('div', 'crm-chat-header');
    header.style.background = theme.headerGradient;

    var titleWrap = createEl('div');
    var titleEl = createEl('div', 'crm-chat-title', opts.title);
    titleWrap.appendChild(titleEl);

    var actions = createEl('div', 'crm-chat-actions');
    var platformSelect = createEl('select', 'crm-chat-platform');
    if (!opts.showPlatformSelector) {
      platformSelect.style.display = 'none';
    }

    var menuToggleBtn = createEl('button', 'crm-chat-close', '메뉴보기');
    menuToggleBtn.style.fontSize = '12px';
    menuToggleBtn.style.padding = '5px 8px';
    if (!opts.enableCategoryTree) {
      menuToggleBtn.style.display = 'none';
    }

    var closeBtn = createEl('button', 'crm-chat-close', '✕');

    actions.appendChild(platformSelect);
    actions.appendChild(menuToggleBtn);
    actions.appendChild(closeBtn);
    header.appendChild(titleWrap);
    header.appendChild(actions);

    var messagesEl = createEl('div', 'crm-chat-messages');
    messagesEl.style.background = theme.surface;

    var quickMenuEl = createEl('div', 'crm-chat-quick-menu');
    quickMenuEl.style.background = theme.surfaceSoft;
    quickMenuEl.style.borderBottom = '1px solid ' + theme.border;

    var inputWrap = createEl('div', 'crm-chat-input');
    inputWrap.style.background = theme.surfaceSoft;
    inputWrap.style.borderTopColor = theme.border;
    var isComposing = false;
    var textarea = createEl('textarea', 'crm-chat-textarea');
    textarea.style.borderColor = theme.border;
    textarea.rows = 2;
    textarea.placeholder = '질문 입력... (Shift+Enter 줄바꿈)';
    var sendBtn = createEl('button', 'crm-chat-send', '➤');
    sendBtn.style.background = theme.base;

    inputWrap.appendChild(textarea);
    inputWrap.appendChild(sendBtn);

    popup.appendChild(header);
    popup.appendChild(quickMenuEl);
    popup.appendChild(messagesEl);
    popup.appendChild(inputWrap);
    function activeQuickCategory() {
      if (!state.selectedQuickCategoryId) return null;
      return state.quickCategories.find(function (x) { return x.id === state.selectedQuickCategoryId; }) || null;
    }

    function resetQuickCategory() {
      state.selectedQuickCategoryId = state.quickCategories.length > 0 ? state.quickCategories[0].id : null;
    }

    function clearCategorySelection() {
      state.categoryTreeLoaded = false;
      state.categoryTreeItems = [];
      state.categoryTreePath = [];
      state.categoryTreeError = '';
      state.categoryTreeLoading = false;
      state.categoryTreeMenuInitialized = false;
    }

    function normalizedTreeApiPath() {
      var path = typeof opts.categoryTreeApiPath === 'string' ? opts.categoryTreeApiPath.trim() : '';
      if (!path) return '/chatbotcategory';
      return path.charAt(0) === '/' ? path : ('/' + path);
    }

    function currentTreePathText() {
      if (!Array.isArray(state.categoryTreePath) || state.categoryTreePath.length === 0) return '';
      return state.categoryTreePath.map(function (x) { return x && x.title ? x.title : ''; }).filter(Boolean).join(' > ');
    }

    function currentTreeParentId() {
      if (!Array.isArray(state.categoryTreePath) || state.categoryTreePath.length === 0) return null;
      var last = state.categoryTreePath[state.categoryTreePath.length - 1];
      return last && typeof last.categoryId === 'number' ? last.categoryId : null;
    }

    function normalizeTreeItems(list) {
      var source = Array.isArray(list) ? list : [];
      return source
        .map(function (x) {
          if (!x || typeof x !== 'object') return null;
          var categoryId = Number(x.categoryId);
          if (!Number.isFinite(categoryId)) return null;
          var title = typeof x.title === 'string' ? x.title.trim() : '';
          if (!title) return null;
          var type = typeof x.type === 'string' ? x.type.trim().toUpperCase() : 'MENU';
          if (type !== 'MENU' && type !== 'QUESTION' && type !== 'ANSWER') {
            type = 'MENU';
          }
          return {
            categoryId: categoryId,
            parentCategoryId: x.parentCategoryId == null ? null : Number(x.parentCategoryId),
            type: type,
            title: title,
            content: typeof x.content === 'string' ? x.content.trim() : '',
            sortOrder: Number(x.sortOrder) || 0,
            hasChildren: !!x.hasChildren
          };
        })
        .filter(Boolean);
    }

    function disableTreeChoicesInMessages() {
      state.messages = state.messages.map(function (msg) {
        if (!msg || !Array.isArray(msg.choices) || msg.choices.length === 0) return msg;
        return Object.assign({}, msg, { choicesActive: false });
      });
    }

    async function refreshCategoryTree(parentCategoryId) {
      if (!opts.enableCategorySelector || !opts.enableCategoryTree) return [];

      state.categoryTreeLoading = true;
      state.categoryTreeError = '';

      var params = new URLSearchParams();
      if (typeof parentCategoryId === 'number') {
        params.set('parentCategoryId', String(parentCategoryId));
      }

      try {
        var res = await fetch(opts.apiBaseUrl + normalizedTreeApiPath() + '/nodes?' + params.toString());
        if (!res.ok) {
          throw new Error('메뉴를 불러오지 못했습니다.');
        }
        var data = await res.json();
        state.categoryTreeItems = normalizeTreeItems(data && data.items);
        state.categoryTreeLoaded = true;
        return state.categoryTreeItems;
      } catch (e) {
        state.categoryTreeItems = [];
        state.categoryTreeError = toSafeText(e && (e.message || e), '메뉴를 불러오지 못했습니다.');
        return [];
      } finally {
        state.categoryTreeLoading = false;
      }
    }

    function appendTreeChoiceMessage(text, items) {
      var choices = normalizeTreeItems(items);
      if (choices.length === 0) return;

      var msg = {
        role: 'bot',
        choices: choices,
        choicesActive: true,
        time: now()
      };
      if (typeof text === 'string' && text.trim()) {
        msg.text = text.trim();
      }
      state.messages.push(msg);
    }

    async function openTreeRootMenu() {
      if (!opts.enableCategoryTree) return;

      var roots = await refreshCategoryTree(null);
      if (roots.length === 0) {
        var errorMsg = {
          role: 'bot',
          text: state.categoryTreeError || '현재 표시할 메뉴가 없습니다.',
          time: now()
        };
        
        if (!state.categoryTreeMenuInitialized) {
          state.messages = [errorMsg];
        } else {
          state.messages.push(errorMsg);
        }
        renderMessages();
        return;
      }

      var menuMsg = {
        role: 'bot',
        text: defaultTreeWelcome(isAdmin, state.selectedPlatform),
        choices: roots,
        choicesActive: true,
        time: now()
      };
      
      if (!state.categoryTreeMenuInitialized) {
        state.messages = [menuMsg];
      } else {
        state.messages.push(menuMsg);
      }
      
      state.categoryTreeMenuInitialized = true;
      renderMessages();
    }

    async function selectCategoryTreeNode(item) {
      if (!item || typeof item.categoryId !== 'number' || state.loading) return;

      disableTreeChoicesInMessages();
      state.messages.push({ role: 'user', text: item.title, time: now() });
      state.loading = true;
      renderMessages();

      state.categoryTreeLoading = true;
      state.categoryTreeError = '';

      try {
        var res = await fetch(opts.apiBaseUrl + normalizedTreeApiPath() + '/select', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ nodeId: item.categoryId })
        });

        if (!res.ok) {
          throw new Error('메뉴 선택 처리에 실패했습니다.');
        }

        var data = await res.json();
        var nodeType = data && data.node && typeof data.node.type === 'string'
          ? data.node.type.trim().toUpperCase()
          : item.type;
        var botMessage = data && typeof data.botMessage === 'string' ? data.botMessage.trim() : '';

        var children = normalizeTreeItems(data && data.children);
        var canExpand = nodeType === 'MENU' || nodeType === 'QUESTION';
        
        if (canExpand && children.length > 0) {
          state.categoryTreePath.push({
            categoryId: item.categoryId,
            title: item.title,
            type: nodeType
          });
          state.categoryTreeItems = children;
          state.categoryTreeMenuToggled = true;
          
          var botMsg = {
            role: 'bot',
            choices: children,
            choicesActive: true,
            time: now()
          };
          if (botMessage) {
            botMsg.text = botMessage;
          }
          state.messages.push(botMsg);
        } else {
          if (botMessage) {
            state.messages.push({ role: 'bot', text: botMessage, time: now() });
          }
          
          state.categoryTreePath = [];
          state.categoryTreeMenuToggled = false;
        }

        state.categoryTreeLoaded = true;
      } catch (e) {
        state.messages.push({
          role: 'bot',
          text: toSafeText(e && (e.message || e), '메뉴 선택 처리 중 오류가 발생했습니다.'),
          time: now()
        });
      } finally {
        state.categoryTreeLoading = false;
        state.loading = false;
        renderMessages();
        renderQuickMenu();
      }
    }

    function renderQuickMenu() {
      if (opts.enableCategoryTree) {
        quickMenuEl.innerHTML = '';
        quickMenuEl.style.display = 'none';
        return;
      }

      var enabled = (opts.enableCategorySelector) || (opts.enableQuickCategories && state.quickCategories.length > 0);
      quickMenuEl.innerHTML = '';
      quickMenuEl.style.display = enabled ? 'block' : 'none';
      if (!enabled) return;

      if (opts.enableCategorySelector) {
        var categoryTitle = createEl('div', 'crm-chat-quick-title', opts.categorySelectorTitle || '카테고리로 질문 찾기');
        quickMenuEl.appendChild(categoryTitle);

        if (opts.enableCategoryTree) {
          var treeMenuHead = createEl('div', 'crm-chat-menu-head');
          var treePathText = currentTreePathText();
          var treePathLabel = createEl('div', 'crm-chat-menu-path', treePathText || '메뉴를 선택해주세요');
          var treeActions = createEl('div', 'crm-chat-menu-actions');

          if (state.categoryTreePath.length > 0) {
            var treeHomeBtn = createEl('button', 'crm-chat-menu-action', '처음으로');
            treeHomeBtn.type = 'button';
            treeHomeBtn.addEventListener('click', function () {
              state.categoryTreePath = [];
              refreshCategoryTree(null);
            });
            treeActions.appendChild(treeHomeBtn);

            var treeBackBtn = createEl('button', 'crm-chat-menu-action', '이전');
            treeBackBtn.type = 'button';
            treeBackBtn.addEventListener('click', function () {
              if (state.categoryTreePath.length === 0) return;
              state.categoryTreePath.pop();
              refreshCategoryTree(currentTreeParentId());
            });
            treeActions.appendChild(treeBackBtn);
          }

          treeMenuHead.appendChild(treePathLabel);
          treeMenuHead.appendChild(treeActions);
          quickMenuEl.appendChild(treeMenuHead);

          if (state.categoryTreeLoading) {
            quickMenuEl.appendChild(createEl('div', 'crm-chat-category-questions-title', '메뉴를 불러오는 중...'));
          } else if (state.categoryTreeError) {
            quickMenuEl.appendChild(createEl('div', 'crm-chat-category-questions-title', state.categoryTreeError));
          } else {
            var treeMenuList = createEl('div', 'crm-chat-menu-list');
            state.categoryTreeItems.forEach(function (item) {
              var nodeLabel = item.type === 'QUESTION' ? '[질문] ' + item.title : item.title;
              var treeBtn = createEl('button', 'crm-chat-menu-item', nodeLabel);
              treeBtn.type = 'button';
              treeBtn.addEventListener('click', function () {
                selectCategoryTreeNode(item);
              });
              treeMenuList.appendChild(treeBtn);
            });

            if (treeMenuList.childNodes.length > 0) {
              quickMenuEl.appendChild(treeMenuList);
            }
          }

          if (!(opts.enableQuickCategories && state.quickCategories.length > 0)) {
            return;
          }
        }
      }

      if (!(opts.enableQuickCategories && state.quickCategories.length > 0)) {
        return;
      }

      var title = createEl('div', 'crm-chat-quick-title', opts.quickMenuTitle || '자주 찾는 질문');
      quickMenuEl.appendChild(title);

      var categoryRow = createEl('div', 'crm-chat-quick-categories');
      state.quickCategories.forEach(function (cat) {
        var btn = createEl('button', 'crm-chat-quick-category', cat.label);
        btn.type = 'button';
        if (cat.id === state.selectedQuickCategoryId) {
          btn.classList.add('active');
          btn.style.background = theme.base;
          btn.style.borderColor = theme.base;
          btn.style.color = '#ffffff';
        }
        btn.addEventListener('click', function () {
          if (state.selectedQuickCategoryId === cat.id) return;
          state.selectedQuickCategoryId = cat.id;
          renderQuickMenu();
        });
        categoryRow.appendChild(btn);
      });
      quickMenuEl.appendChild(categoryRow);

      var activeCategory = activeQuickCategory();
      if (!activeCategory) return;

      var questionRow = createEl('div', 'crm-chat-quick-questions');
      activeCategory.questions.forEach(function (item) {
        var qBtn = createEl('button', 'crm-chat-quick-question', item.label);
        qBtn.type = 'button';
        qBtn.addEventListener('click', function () {
          send(item.question);
        });
        questionRow.appendChild(qBtn);
      });
      quickMenuEl.appendChild(questionRow);
    }


    mountRoot.appendChild(fab);
    mountRoot.appendChild(popup);

    function isMobile() {
      return window.innerWidth <= 480;
    }

    function applyVisibility() {
      if (isMobile()) {
        fab.style.display = 'none';
        popup.style.display = 'none';
        return;
      }
      var noBtn = !!opts.hideButton;
      fab.style.display = (noBtn || !state.widgetVisible) ? 'none' : 'flex';
      var popupOn = noBtn ? state.isOpen : (state.widgetVisible && state.isOpen);
      popup.style.display = popupOn ? 'flex' : 'none';
    }

    function setOpen(open) {
      state.isOpen = open;
      fab.textContent = open ? '✕' : (opts.buttonLabel || 'CHAT');
      applyVisibility();
      renderQuickMenu();
      if (open) {
        if (!state.platformsFetched) {
          refreshPlatforms().catch(function () {});
        }
        if (opts.enableCategorySelector && opts.enableCategoryTree) {
          if (!state.categoryTreeMenuInitialized) {
            openTreeRootMenu().catch(function () {});
          }
        }
        setTimeout(function () { textarea.focus(); }, 50);
      }
    }

    function renderPlatformOptions() {
      platformSelect.innerHTML = '';
      state.platformOptions.forEach(function (p) {
        var option = createEl('option', '', p);
        option.value = p;
        platformSelect.appendChild(option);
      });
      if (!opts.showPlatformSelector) {
        state.selectedPlatform = opts.platform || opts.defaultPlatform || '전체 플랫폼';
      }
      if (!state.platformOptions.includes(state.selectedPlatform)) {
        state.selectedPlatform = '전체 플랫폼';
      }
      platformSelect.value = state.selectedPlatform;
    }

    function renderMessages() {
      messagesEl.innerHTML = '';
      state.messages.forEach(function (msg) {
        var row = createEl('div', 'crm-chat-row ' + msg.role);
        var avatar = createEl('div', 'crm-chat-avatar', msg.role === 'bot' ? (isAdmin ? 'M' : 'C') : (isAdmin ? 'A' : 'U'));
        avatar.style.background = msg.role === 'bot'
          ? theme.avatarBotGradient
          : theme.avatarUserGradient;

        var content = createEl('div', 'crm-chat-content');
        
        if (msg.text) {
          var bubble = createEl('div', 'crm-chat-bubble');
          bubble.innerHTML = linkify(msg.text).replace(/\n/g, '<br>');
          if (msg.role === 'user') {
            bubble.style.background = theme.headerGradient;
          } else {
            bubble.style.background = theme.botBubble;
          }
          content.appendChild(bubble);
        }

        var time = createEl('div', 'crm-chat-time', msg.time || now());

        if (Array.isArray(msg.choices) && msg.choices.length > 0) {
          var choicesWrap = createEl('div', 'crm-chat-inline-choices');
          msg.choices.forEach(function (choice) {
            var choiceLabel = choice.title;
            var choiceBtn = createEl('button', 'crm-chat-inline-choice', choiceLabel);
            choiceBtn.type = 'button';
            choiceBtn.disabled = state.loading || msg.choicesActive === false;
            choiceBtn.addEventListener('click', function () {
              selectCategoryTreeNode(choice);
            });
            choicesWrap.appendChild(choiceBtn);
          });
          content.appendChild(choicesWrap);
        }

        content.appendChild(time);

        if (msg.role === 'user') {
          row.appendChild(content);
          row.appendChild(avatar);
        } else {
          row.appendChild(avatar);
          if (msg.text || (Array.isArray(msg.choices) && msg.choices.length > 0)) {
            row.appendChild(content);
          }
        }

        if (row.childNodes.length > 1) {
          messagesEl.appendChild(row);
        }
      });

      if (state.loading) {
        var loadingRow = createEl('div', 'crm-chat-row bot');
        var loadingAvatar = createEl('div', 'crm-chat-avatar', isAdmin ? 'M' : 'C');
        loadingAvatar.style.background = theme.avatarBotGradient;
        var loadingBubble = createEl('div', 'crm-chat-bubble crm-chat-loading');

        for (var i = 0; i < 3; i += 1) {
          var dot = createEl('span', 'crm-chat-dot');
          dot.style.background = theme.base;
          loadingBubble.appendChild(dot);
        }

        loadingRow.appendChild(loadingAvatar);
        loadingRow.appendChild(loadingBubble);
        messagesEl.appendChild(loadingRow);
      }

      messagesEl.scrollTop = messagesEl.scrollHeight;
      sendBtn.disabled = state.loading;
    }

    async function refreshPlatforms() {
      try {
        var res = await fetch(opts.apiBaseUrl + '/knowledgebase/platforms');
        var data = await res.json();
        state.platformOptions = normalizePlatforms(data);
        state.platformsFetched = true;
      } catch (e) {
        state.platformOptions = ['전체 플랫폼'];
      }
      renderPlatformOptions();
      if (opts.enableCategorySelector && opts.enableCategoryTree) {
        if (state.isOpen && state.categoryTreeMenuToggled && !state.categoryTreeMenuInitialized) {
          openTreeRootMenu().catch(function () {});
        }
      }
    }

    async function send(preFilledQuestion) {
      var q = typeof preFilledQuestion === 'string'
        ? preFilledQuestion.trim()
        : (textarea.value || '').trim();
      if (!q || state.loading) return;

      state.messages.push({ role: 'user', text: q, time: now() });
      textarea.value = '';
      state.loading = true;
      renderMessages();

      try {
        var userContext = getUserContext();
        var requestBody = {
          question: q,
          role: opts.role,
          platform: opts.showPlatformSelector ? state.selectedPlatform : (opts.platform || opts.defaultPlatform || '전체 플랫폼'),
          sessionId: state.sessionId,
          createSession: state.sessionId === null,
          userId: userContext.userId || null,
          username: userContext.username || null,
          userLoginId: userContext.userLoginId || null,
        };

        var headers = { 'Content-Type': 'application/json' };
        if (userContext.userId) {
          headers['X-Actor-Id'] = userContext.userId;
        }

        var res = await fetch(opts.apiBaseUrl + '/knowledgebase/ask', {
          method: 'POST',
          headers: headers,
          body: JSON.stringify(requestBody)
        });

        if (!res.ok) {
          throw new Error('서버 응답 오류 (' + res.status + ')');
        }

        var data = await res.json();
        if (data.sessionId && !state.sessionId) {
          state.sessionId = data.sessionId;
        }

        state.messages.push({
          role: 'bot',
          text: toSafeText(data.answer, '답변을 생성하지 못했습니다.'),
          time: now()
        });
      } catch (err) {
        state.messages.push({
          role: 'bot',
          text: toSafeText(err && (err.message || err), '요청 처리 중 오류가 발생했습니다.'),
          time: now()
        });
      } finally {
        state.loading = false;
        state.categoryTreeMenuToggled = false;
        renderMessages();
        renderQuickMenu();
      }
    }

    platformSelect.addEventListener('change', function () {
      state.selectedPlatform = platformSelect.value;
      if (opts.enableCategorySelector && opts.enableCategoryTree) {
        clearCategorySelection();
        state.messages = [{ role: 'bot', text: defaultWelcome(isAdmin, resolveGreetingPlatform(opts, state.selectedPlatform)), time: now() }];
        if (state.isOpen) {
          openTreeRootMenu().catch(function () {});
        }
        renderMessages();
      }
    });

    menuToggleBtn.addEventListener('click', async function () {
      if (!opts.enableCategoryTree || state.loading) return;
      
      // 메뉴 보기: 항상 루트 메뉴를 표시
      state.categoryTreePath = [];
      state.categoryTreeMenuToggled = true;
      await openTreeRootMenu().catch(function () {});
      renderQuickMenu();
    });

    closeBtn.addEventListener('click', function () {
      state.sessionId = null;
      resetQuickCategory();
      clearCategorySelection();
      state.messages = [{ role: 'bot', text: defaultWelcome(isAdmin, resolveGreetingPlatform(opts, state.selectedPlatform)), time: now() }];
      setOpen(false);
    });

    fab.addEventListener('click', function () {
      var opening = !state.isOpen;
      if (!opening) {
        state.sessionId = null;
        resetQuickCategory();
        clearCategorySelection();
        state.messages = [{ role: 'bot', text: defaultWelcome(isAdmin, resolveGreetingPlatform(opts, state.selectedPlatform)), time: now() }];
      }
      setOpen(opening);
      if (opening) renderMessages();
    });

    textarea.addEventListener('compositionstart', function () {
      isComposing = true;
    });

    textarea.addEventListener('compositionend', function () {
      isComposing = false;
    });

    textarea.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && !e.shiftKey) {
        if (isComposing || e.isComposing || e.keyCode === 229) return;
        e.preventDefault();
        send();
      }
    });

    sendBtn.addEventListener('click', send);

    setOpen(!!opts.initiallyOpen);
    renderQuickMenu();
    renderMessages();

    window.addEventListener('resize', applyVisibility);

    return {
      showWidget: function () {
        state.widgetVisible = true;
        applyVisibility();
      },
      hideWidget: function () {
        state.widgetVisible = false;
        applyVisibility();
      },
      openChat: function () {
        state.widgetVisible = true;
        setOpen(true);
        renderQuickMenu();
      },
      closeChat: function () {
        setOpen(false);
      },
      setButtonRight: function (val) {
        fab.style.right = val;
      },
      setButtonBottom: function (val) {
        fab.style.bottom = val;
      },
      setPopupRight: function (val) {
        popup.style.right = val;
      },
      setPopupBottom: function (val) {
        popup.style.bottom = val;
      },
      setUserContext: function (context) {
        var next = context || {};
        if (Object.prototype.hasOwnProperty.call(next, 'userId')) {
          opts.userId = next.userId == null ? '' : String(next.userId);
        }
        if (Object.prototype.hasOwnProperty.call(next, 'username')) {
          opts.username = next.username == null ? '' : String(next.username);
        }
        if (Object.prototype.hasOwnProperty.call(next, 'userLoginId')) {
          opts.userLoginId = next.userLoginId == null ? '' : String(next.userLoginId);
        }
      },
      destroy: function () {
        fab.remove();
        popup.remove();
      }
    };
  }

  window.createCrmChatWidget = createCrmChatWidget;
})();
