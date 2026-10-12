/* 原型主题切换器
 *
 * 用法：页面 <html> 上放 data-theme，右上角放 <select class="theme-switch">，
 * 然后引 <script src="theme.js"></script>。
 *
 * 优先级：URL hash（#theme=midautumn）> localStorage > 页面自带 data-theme。
 * hash 优先是为了截图与分享可复现：file://.../web/home.html#theme=midautumn
 * 切换只改 documentElement.dataset.theme，DOM 结构不动——布局不变的证据。
 */
(function () {
  'use strict'

  var THEMES = [
    { id: 'brand', label: '默认（品牌）' },
    { id: 'dark', label: '暗色' },
    { id: 'cny', label: '节日·春节' },
    { id: 'lantern', label: '节日·元宵' },
    { id: 'dragonboat', label: '节日·端午' },
    { id: 'midautumn', label: '节日·中秋' },
    { id: 'national', label: '节日·国庆' },
    { id: 'spring', label: '季节·春' },
    { id: 'summer', label: '季节·夏' },
    { id: 'autumn', label: '季节·秋' },
    { id: 'winter', label: '季节·冬' }
  ]

  var KEY = 'doggo.proto.theme'

  function readHash() {
    var m = /[#&]theme=([a-z]+)/.exec(location.hash)
    return m ? m[1] : null
  }

  function current() {
    return readHash() || localStorage.getItem(KEY) || document.documentElement.getAttribute('data-theme') || 'brand'
  }

  function apply(theme, persist) {
    document.documentElement.setAttribute('data-theme', theme)
    if (persist) localStorage.setItem(KEY, theme)
    var nodes = document.querySelectorAll('.theme-switch')
    for (var i = 0; i < nodes.length; i++) nodes[i].value = theme
    var marks = document.querySelectorAll('.theme-current')
    for (var j = 0; j < marks.length; j++) marks[j].textContent = labelOf(theme)
  }

  function labelOf(theme) {
    for (var i = 0; i < THEMES.length; i++) {
      if (THEMES[i].id === theme) return THEMES[i].label
    }
    return theme
  }

  function mount() {
    var nodes = document.querySelectorAll('.theme-switch')
    if (!nodes.length) {
      apply(current(), false)
      return
    }
    for (var i = 0; i < nodes.length; i++) {
      var sel = nodes[i]
      if (sel.options.length) continue
      for (var k = 0; k < THEMES.length; k++) {
        var opt = document.createElement('option')
        opt.value = THEMES[k].id
        opt.textContent = THEMES[k].label
        sel.appendChild(opt)
      }
      sel.addEventListener('change', function () {
        apply(this.value, true)
        var path = location.pathname.replace(/index\.html$/, '')
        history.replaceState(null, '', '#theme=' + this.value)
        if (path.indexOf('prototypes') !== -1) window.dispatchEvent(new CustomEvent('themechange', { detail: { theme: this.value } }))
      })
    }
    apply(current(), false)
    // 只读 hash，避免无谓污染历史
    window.addEventListener('hashchange', function () { apply(readHash(), false) })
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', mount)
  } else {
    mount()
  }
})()
