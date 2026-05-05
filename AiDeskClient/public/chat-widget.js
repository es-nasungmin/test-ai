(function () {
  var STYLE_ID = 'crm-chat-widget-style';

  function ensureStyle() {
    if (document.getElementById(STYLE_ID)) return;

    var style = document.createElement('style');
    style.id = STYLE_ID;
    style.textContent = ""
      + ".crm-chat-fab{position:fixed;width:56px;height:56px;border-radius:50%;border:none;color:#fff;font-size:14px;font-weight:700;cursor:pointer;box-shadow:0 4px 16px rgba(0,0,0,.3);z-index:9999;transition:transform .2s,box-shadow .2s;display:flex;align-items:center;justify-content:center;}"
      + ".crm-chat-fab:hover{transform:scale(1.05);box-shadow:0 6px 24px rgba(0,0,0,.35);}"
      + ".crm-chat-popup{position:fixed;width:360px;height:520px;background:#fff;border-radius:16px;box-shadow:0 8px 40px rgba(0,0,0,.2);z-index:9998;display:flex;flex-direction:column;overflow:hidden;}"
      + ".crm-chat-header{color:#fff;padding:12px 16px;display:flex;align-items:center;justify-content:space-between;flex-shrink:0;}"
      + ".crm-chat-title{font-weight:700;font-size:14px;}"
      + ".crm-chat-sub{margin-top:2px;font-size:11px;opacity:.9;}"
      + ".crm-chat-actions{display:flex;align-items:center;gap:6px;}"
      + ".crm-chat-platform{border:1px solid rgba(255,255,255,.5);border-radius:8px;padding:4px 8px;background:rgba(255,255,255,.18);color:#fff;font-size:12px;min-width:96px;}"
      + ".crm-chat-platform option{color:#111827;}"
      + ".crm-chat-close{background:rgba(255,255,255,.2);border:none;border-radius:6px;color:#fff;font-size:14px;padding:4px 8px;cursor:pointer;}"
      + ".crm-chat-messages{flex:1;overflow-y:auto;padding:14px 16px;background:#f8f9fb;display:flex;flex-direction:column;gap:12px;}"
      + ".crm-chat-row{display:flex;align-items:flex-start;gap:8px;}"
      + ".crm-chat-row.user{flex-direction:row-reverse;}"
      + ".crm-chat-avatar{width:32px;height:32px;border-radius:50%;display:flex;align-items:center;justify-content:center;color:#fff;font-size:11px;font-weight:800;letter-spacing:.3px;flex-shrink:0;}"
      + ".crm-chat-content{display:flex;flex-direction:column;gap:4px;max-width:78%;}"
      + ".crm-chat-bubble{background:#fff;padding:10px 14px;border-radius:14px 14px 14px 4px;font-size:14px;line-height:1.45;box-shadow:0 2px 6px rgba(0,0,0,.07);white-space:pre-wrap;word-break:break-word;}"
      + ".crm-chat-row.user .crm-chat-bubble{color:#fff;border-radius:14px 14px 4px 14px;}"
      + ".crm-chat-time{font-size:11px;color:#a0aec0;padding:0 2px;}"
      + ".crm-chat-row.user .crm-chat-time{text-align:right;}"
      + ".crm-chat-input{display:flex;gap:8px;padding:10px 14px;border-top:1px solid #e2e8f0;background:#fff;flex-shrink:0;}"
      + ".crm-chat-textarea{flex:1;padding:8px 12px;border:2px solid #e2e8f0;border-radius:10px;font-size:14px;resize:none;font-family:inherit;line-height:1.4;outline:none;}"
      + ".crm-chat-send{padding:0 14px;color:#fff;border:none;border-radius:10px;font-size:18px;cursor:pointer;transition:opacity .2s;}"
      + ".crm-chat-send:disabled{opacity:.4;cursor:not-allowed;}"
      + ".crm-chat-loading{display:flex;gap:5px;align-items:center;padding:12px 16px;}"
      + ".crm-chat-dot{width:7px;height:7px;border-radius:50%;animation:crm-chat-bounce 1.2s infinite ease-in-out;}"
      + ".crm-chat-dot:nth-child(2){animation-delay:.2s;}"
      + ".crm-chat-dot:nth-child(3){animation-delay:.4s;}"
      + "@keyframes crm-chat-bounce{0%,80%,100%{transform:scale(.6);opacity:.4;}40%{transform:scale(1);opacity:1;}}"
      + "@media (max-width:480px){.crm-chat-popup{width:calc(100vw - 16px);height:calc(100vh - 120px);}.crm-chat-fab{display:none!important;}.crm-chat-popup{display:none!important;}}";

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
      buttonLabel: 'CHAT',
      hideButton: false,
      themeColor: '#1f7a6d',
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
      messages: [
        { role: 'bot', text: defaultWelcome(isAdmin, resolveGreetingPlatform(opts, opts.platform || opts.defaultPlatform)), time: now() }
      ]
    };

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
    var subEl = createEl('div', 'crm-chat-sub', headerSubtitle(isAdmin));
    titleWrap.appendChild(titleEl);
    titleWrap.appendChild(subEl);

    var actions = createEl('div', 'crm-chat-actions');
    var platformSelect = createEl('select', 'crm-chat-platform');
    if (!opts.showPlatformSelector) {
      platformSelect.style.display = 'none';
    }

    var closeBtn = createEl('button', 'crm-chat-close', '✕');

    actions.appendChild(platformSelect);
    actions.appendChild(closeBtn);
    header.appendChild(titleWrap);
    header.appendChild(actions);

    var messagesEl = createEl('div', 'crm-chat-messages');
    messagesEl.style.background = theme.surface;

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
    popup.appendChild(messagesEl);
    popup.appendChild(inputWrap);

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
      if (open) {
        if (!state.platformsFetched) {
          refreshPlatforms().catch(function () {});
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
        var bubble = createEl('div', 'crm-chat-bubble');
        bubble.innerHTML = linkify(msg.text).replace(/\n/g, '<br>');
        if (msg.role === 'user') {
          bubble.style.background = theme.headerGradient;
        } else {
          bubble.style.background = theme.botBubble;
        }

        var time = createEl('div', 'crm-chat-time', msg.time || now());

        content.appendChild(bubble);
        content.appendChild(time);

        if (msg.role === 'user') {
          row.appendChild(content);
          row.appendChild(avatar);
        } else {
          row.appendChild(avatar);
          row.appendChild(content);
        }

        messagesEl.appendChild(row);
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
    }

    async function send() {
      var q = (textarea.value || '').trim();
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
          userLoginId: userContext.userLoginId || null
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
        renderMessages();
      }
    }

    platformSelect.addEventListener('change', function () {
      state.selectedPlatform = platformSelect.value;
    });

    closeBtn.addEventListener('click', function () {
      state.sessionId = null;
      state.messages = [{ role: 'bot', text: defaultWelcome(isAdmin, resolveGreetingPlatform(opts, state.selectedPlatform)), time: now() }];
      setOpen(false);
    });

    fab.addEventListener('click', function () {
      var opening = !state.isOpen;
      if (!opening) {
        state.sessionId = null;
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
