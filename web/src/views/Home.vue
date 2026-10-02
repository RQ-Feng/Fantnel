<template>
  <div class="home">
    <div class="home-header">
      <h1>欢迎使用 Fantnel 管理系统</h1>
      <button class="refresh-btn" :disabled="loading" @click="loadAll">
        {{ loading ? '刷新中...' : '刷新' }}
      </button>
    </div>
    <p class="intro-line">账号、服务器与皮肤的一站式管理面板。</p>

    <div class="card-grid">
      <!-- 当前游戏账号 -->
      <section class="card">
        <header class="card-title">
          <h2>当前游戏账号</h2>
          <router-link to="/game-accounts" class="card-link">管理</router-link>
        </header>

        <div v-if="currentAccount" class="account-body">
          <p class="account-name">{{ currentAccount.name || currentAccount.account || '未命名账号' }}</p>
          <ul class="kv">
            <li><span>类型</span><b>{{ typeText(currentAccount.type) }}</b></li>
            <li><span>账号</span><b>{{ currentAccount.account || '—' }}</b></li>
            <li><span>用户 ID</span><b>{{ currentAccount.userId || '—' }}</b></li>
            <li><span>状态</span><b class="tag-ok">已登录</b></li>
          </ul>
        </div>

        <div v-else class="account-empty">
          <p class="empty-title">当前没有已登录的游戏账号</p>
          <p class="empty-tip">
            <template v-if="accounts.length">共 {{ accounts.length }} 个账号，但都还没登录。</template>
            <template v-else>还没有添加任何账号。</template>
          </p>
          <router-link to="/game-accounts" class="btn-primary">去游戏账号页</router-link>
        </div>

        <div v-if="switchableAccounts.length" class="quick-switch">
          <p class="quick-switch-title">切换优先账号</p>
          <div class="chip-list">
            <button
              v-for="acc in switchableAccounts"
              :key="acc.id"
              class="chip"
              :class="{ active: currentAccount && acc.id === currentAccount.id }"
              :disabled="!!currentAccount && acc.id === currentAccount.id"
              @click="askSwitch(acc)">
              {{ acc.name || acc.account }}
            </button>
          </div>
        </div>
      </section>

      <!-- 运行状态 -->
      <section class="card">
        <header class="card-title">
          <h2>运行状态</h2>
        </header>
        <ul class="stat-list">
          <li><span>账号</span><b>{{ availableAccounts.length }} / {{ accounts.length }}</b><em>已登录 / 总数</em></li>
          <li><span>白端游戏</span><b>{{ launchers.length }}</b><em>运行中</em></li>
          <li><span>代理</span><b>{{ proxies.length }}</b><em>运行中</em></li>
          <li v-if="ENABLE_PLUGIN_FEATURES"><span>插件</span><b>{{ enabledPluginCount }} / {{ plugins.length }}</b><em>启用 / 总数</em></li>
        </ul>
      </section>

      <!-- 上次代理 -->
      <section class="card">
        <header class="card-title">
          <h2>上次代理</h2>
          <router-link to="/servers" class="card-link">去网络游戏</router-link>
        </header>

        <div v-if="proxyHistory.length" class="history-list">
          <button v-for="item in proxyHistory.slice(0, 3)" :key="item.mode + item.id"
            class="history-item" @click="openHistory(item)">
            <span class="history-main">
              <span class="history-name">{{ item.name }}</span>
              <span class="history-tag">{{ item.mode === 'rental' ? '租赁服' : '网络游戏' }}</span>
            </span>
            <span class="history-meta">
              <span v-if="item.version">版本 {{ item.version }}</span>
              <span v-if="item.player">角色 {{ item.player }}</span>
              <span class="history-time">{{ timeAgo(item.time) }}</span>
            </span>
          </button>
          <p v-if="proxyHistory.length > 3" class="history-more">
            共 {{ proxyHistory.length }} 条记录，仅显示最近 3 条
          </p>
        </div>

        <div v-else class="account-empty">
          <p class="empty-title">还没有代理记录</p>
          <p class="empty-tip">在网络游戏详情页点「启动代理」后，会记录在这里，方便下次快速回到同一个服务器。</p>
          <router-link to="/servers" class="btn-primary">去网络游戏页</router-link>
        </div>
      </section>
    </div>

    <!-- 快捷入口 -->
    <section class="card shortcuts-card">
      <header class="card-title">
        <h2>快捷入口</h2>
      </header>
      <div class="shortcuts">
        <router-link v-for="item in shortcuts" :key="item.path" :to="item.path" class="shortcut">
          <span class="shortcut-name">{{ item.name }}</span>
          <span class="shortcut-desc">{{ item.desc }}</span>
        </router-link>
      </div>
    </section>

    <p class="version-line">Fantnel {{ versionText }}</p>

    <Alert :show="showSwitchConfirm" title="切换优先账号" :message="switchMessage" :showCancel="true"
      okText="确认切换" cancelText="取消" @ok="doSwitch" @cancel="cancelSwitch" />
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { Message } from '../utils/message.js'
import {
  getGameAccount,
  getAccounts,
  getAvailableAccounts,
  getGameLaunchInfo,
  getProxyServerInfo,
  getPlugins,
  getProxyHistory,
  getVersion,
  switchAccount
} from '../utils/Tools.js'
import { ENABLE_PLUGIN_FEATURES } from '../config/features.js'

const loading = ref(false)
const currentAccount = ref(null)
const accounts = ref([])
const availableAccounts = ref([])
const launchers = ref([])
const proxies = ref([])
const plugins = ref([])
const version = ref(null)
const proxyHistory = ref([])

const showSwitchConfirm = ref(false)
const switchTarget = ref(null)

const shortcuts = [
  { name: '游戏账号', desc: '添加 / 登录 / 切换', path: '/game-accounts' },
  { name: '网络游戏', desc: '浏览并进入服务器', path: '/servers' },
  { name: '租赁服', desc: '租赁服角色管理', path: '/game-rental' },
  // 插件商城：远端接口已下线，由 ENABLE_PLUGIN_FEATURES 统一控制
  ...(ENABLE_PLUGIN_FEATURES ? [{ name: '插件商城', desc: '下载并安装插件', path: '/plugin-store' }] : []),
  { name: '设置', desc: '内存 / JVM / 启动项', path: '/settings' }
]

// 只有登录成功的账号才能被设为优先账号
const switchableAccounts = computed(() => availableAccounts.value)
const enabledPluginCount = computed(() => plugins.value.filter(p => p.status === '1').length)
const versionText = computed(() => version.value?.version || '—')
const switchMessage = computed(() => switchTarget.value
  ? `确定把「${switchTarget.value.name || switchTarget.value.account}」设为优先账号吗？`
  : '')

const typeText = (type) => {
  switch ((type || '').toLowerCase()) {
    case '4399': return '4399'
    case '4399com': return '4399Com'
    case '163email': return '163Email'
    case 'cookie': return 'Cookie / Auth'
    default: return type || '未知'
  }
}

// 相对时间：刚刚 / N 分钟前 / N 小时前 / N 天前
const timeAgo = (ms) => {
  if (!ms) return ''
  const diff = Date.now() - ms
  if (diff < 60 * 1000) return '刚刚'
  const min = Math.floor(diff / (60 * 1000))
  if (min < 60) return `${min} 分钟前`
  const hour = Math.floor(min / 60)
  if (hour < 24) return `${hour} 小时前`
  const day = Math.floor(hour / 24)
  if (day < 30) return `${day} 天前`
  return new Date(ms).toLocaleDateString()
}

// 点击历史记录：网络游戏进服务详情页，租赁服进租赁详情页
const openHistory = (item) => {
  location.href = item.mode === 'rental' ? `/game-rental/${item.id}` : `/server/${item.id}`
}

// 静默请求：网络异常或业务错误（如未登录的错误码 15）都返回 null，不影响页面渲染
const safe = async (fn) => {
  try {
    const data = await fn()
    return data && data.code === 1 ? data.data : null
  } catch (error) {
    console.warn('[home] 请求失败:', error?.message)
    return null
  }
}

const loadAll = async () => {
  if (loading.value) return
  loading.value = true
  try {
    const [cur, all, avail, launch, proxy, plugin, ver, history] = await Promise.all([
      safe(getGameAccount),
      safe(getAccounts),
      safe(getAvailableAccounts),
      safe(getGameLaunchInfo),
      safe(getProxyServerInfo),
      // 插件统计：插件功能隐藏时不再发起请求
      ENABLE_PLUGIN_FEATURES ? safe(getPlugins) : Promise.resolve(null),
      safe(getVersion),
      // 代理历史：主页「上次代理」
      safe(getProxyHistory)
    ])

    currentAccount.value = cur
    accounts.value = all || []
    availableAccounts.value = avail || []
    launchers.value = launch || []
    proxies.value = proxy?.proxies || []
    plugins.value = plugin || []
    version.value = ver
    proxyHistory.value = history || []
  } finally {
    loading.value = false
  }
}

const askSwitch = (acc) => {
  switchTarget.value = acc
  showSwitchConfirm.value = true
}

const cancelSwitch = () => {
  showSwitchConfirm.value = false
  switchTarget.value = null
}

const doSwitch = async () => {
  const target = switchTarget.value
  cancelSwitch()
  // 账号 Id 从 0 开始，不能用真假值判断
  if (target?.id == null) return
  try {
    const data = await switchAccount(target.id)
    if (data.code === 1) {
      Message.success('已切换优先账号')
      await loadAll()
    } else {
      Message.warning(data.msg || '切换失败')
    }
  } catch (error) {
    Message.error('切换失败，请检查网络连接')
  }
}

onMounted(loadAll)
</script>

<style scoped>
.home {
  max-width: 1200px;
  margin: 0 auto;
  padding: 20px;
}

h1 {
  color: var(--text-color);
  margin-bottom: 20px;
  font-size: 2rem;
}

.home-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.home-header h1 {
  margin-bottom: 0;
}

.refresh-btn {
  flex: none;
  background-color: var(--sidebar-bg);
  color: var(--text-color);
  border: 1px solid var(--border-color);
  padding: 6px 14px;
  font-size: 0.85rem;
}

.refresh-btn:disabled {
  opacity: 0.6;
  cursor: default;
}

.intro-line {
  color: var(--text-color);
  opacity: 0.65;
  font-size: 0.95rem;
  margin: 10px 0 24px;
}

/* 卡片 */
.card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 20px;
  margin-bottom: 20px;
}

.card {
  background-color: var(--sidebar-bg);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 18px;
}

.card-title {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 14px;
}

.card-title h2 {
  color: var(--text-color);
  font-size: 1.05rem;
}

.card-link {
  font-size: 0.85rem;
  color: var(--sidebar-active);
}

.account-name {
  color: var(--text-color);
  font-size: 1.25rem;
  font-weight: 600;
  margin-bottom: 12px;
}

.kv {
  list-style: none;
}

.kv li {
  display: flex;
  gap: 10px;
  padding: 6px 0;
  border-bottom: 1px dashed var(--border-color);
  font-size: 0.9rem;
}

.kv li:last-child {
  border-bottom: none;
}

.kv span {
  color: var(--text-color);
  opacity: 0.6;
  min-width: 72px;
}

.kv b {
  color: var(--text-color);
  font-weight: 600;
  word-break: break-all;
}

.tag-ok {
  color: #2e7d32;
}

.account-empty {
  padding: 4px 0;
}

.empty-title {
  color: var(--text-color);
  font-weight: 600;
  margin-bottom: 6px;
}

.empty-tip {
  color: var(--text-color);
  opacity: 0.65;
  font-size: 0.875rem;
  margin-bottom: 14px;
}

.btn-primary {
  display: inline-block;
  background-color: var(--sidebar-active);
  color: #fff;
  padding: 8px 16px;
  border-radius: 6px;
  font-size: 0.9rem;
}

.btn-primary:hover {
  color: #fff;
  opacity: 0.9;
}

/* 上次代理 */
.history-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.history-item {
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 100%;
  text-align: left;
  background-color: transparent;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  padding: 10px 12px;
  cursor: pointer;
  transition: background-color 0.15s, border-color 0.15s;
}

.history-item:hover {
  background-color: rgba(128, 128, 128, 0.12);
  border-color: var(--sidebar-active);
}

.history-main {
  display: flex;
  align-items: center;
  gap: 8px;
}

.history-name {
  color: var(--text-color);
  font-weight: 600;
  font-size: 0.95rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.history-tag {
  flex: none;
  font-size: 0.7rem;
  padding: 1px 6px;
  border-radius: 3px;
  color: var(--sidebar-active);
  border: 1px solid var(--sidebar-active);
  opacity: 0.9;
}

.history-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  color: var(--text-color);
  opacity: 0.6;
  font-size: 0.8rem;
  /* 统一使用带中文字形的字体：Arial 没有汉字，中文会回落到雅黑，
     导致同一行里「数字」与「汉字」的字形高度/基线不一致 */
  font-family: "Microsoft YaHei", "Segoe UI", Arial, sans-serif;
  line-height: 1.2;
}

.history-meta > span {
  display: inline-flex;
  align-items: center;
  line-height: 1;
}

.history-time {
  margin-left: auto;
}

.history-more {
  color: var(--text-color);
  opacity: 0.5;
  font-size: 0.78rem;
  margin-top: 2px;
}

.quick-switch {
  margin-top: 16px;
  padding-top: 14px;
  border-top: 1px solid var(--border-color);
}

.quick-switch-title {
  color: var(--text-color);
  opacity: 0.6;
  font-size: 0.8rem;
  margin-bottom: 8px;
}

.chip-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.chip {
  background-color: transparent;
  color: var(--text-color);
  border: 1px solid var(--border-color);
  padding: 4px 12px;
  font-size: 0.85rem;
  border-radius: 999px;
}

.chip.active {
  border-color: var(--sidebar-active);
  color: var(--sidebar-active);
}

.chip:disabled {
  opacity: 0.55;
  cursor: default;
}

/* 运行状态 */
.stat-list {
  list-style: none;
}

.stat-list li {
  display: flex;
  align-items: baseline;
  gap: 10px;
  padding: 8px 0;
  border-bottom: 1px dashed var(--border-color);
  font-size: 0.9rem;
}

.stat-list li:last-child {
  border-bottom: none;
}

.stat-list span {
  color: var(--text-color);
  opacity: 0.6;
  min-width: 72px;
}

.stat-list b {
  color: var(--text-color);
  font-size: 1rem;
}

.stat-list em {
  color: var(--text-color);
  opacity: 0.5;
  font-size: 0.8rem;
  font-style: normal;
}

.stat-foot {
  margin-top: 10px;
  color: var(--text-color);
  opacity: 0.5;
  font-size: 0.8rem;
}

/* 快捷入口 */
.shortcuts-card {
  margin-bottom: 16px;
}

.shortcuts {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: 12px;
}

.shortcut {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 14px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  color: var(--text-color);
}

.shortcut:hover {
  border-color: var(--sidebar-active);
  color: var(--sidebar-active);
}

.shortcut-name {
  font-size: 0.95rem;
  font-weight: 600;
}

.shortcut-desc {
  font-size: 0.78rem;
  opacity: 0.6;
}

.version-line {
  color: var(--text-color);
  opacity: 0.45;
  font-size: 0.8rem;
}
</style>