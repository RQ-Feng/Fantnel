<template>
  <div class="plugin-store">
    <h1>插件商城</h1>
    <p>插件商城页面，用于浏览和下载系统中的插件。</p>
    <div class="search-bar">
      <input type="text" v-model="searchQuery" placeholder="搜索插件..." :disabled="plugins.length === 0">
    </div>

    <p v-if="loading" class="store-hint">正在获取插件列表...</p>
    <p v-else-if="loadError" class="store-hint store-error">{{ loadError }}</p>
    <p v-else-if="plugins.length === 0" class="store-hint">插件商城暂无可用插件。</p>

    <div v-else class="plugin-list">
      <div v-for="plugin in filteredPlugins" :key="plugin.id" class="plugin-item" @click="navigateToPlugin(plugin.id)">
        <div class="plugin-info">
          <h3>{{ plugin.name }}</h3>
          <p class="plugin-description">{{ plugin.shortDescription }}</p>
          <div class="plugin-meta">
            <span class="publisher">发布者: {{ plugin.publisher }}</span>
            <span class="download-count">下载次数: {{ plugin.downloadCount }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { getPluginList } from '../../utils/Tools'

const searchQuery = ref('')
const plugins = ref([])
const loading = ref(true)
const loadError = ref('')

// 获取插件列表
// 注意：接口失败时后端返回的是错误对象（data 为调用栈数组），
// 不能直接塞进列表，否则 filteredPlugins 会抛 "Cannot read properties of undefined"。
onMounted(async () => {
  try {
    const res = await getPluginList()
    if (res?.code === 1 && Array.isArray(res.data)) {
      plugins.value = res.data
    } else {
      loadError.value = `插件商城获取失败：${res?.msg || '服务端返回异常'}`
      console.warn('[plugin-store] 获取插件列表失败:', res)
    }
  } catch (error) {
    loadError.value = '连接插件商城失败，请检查网络或服务端状态。'
    console.error('[plugin-store] 获取插件列表异常:', error)
  } finally {
    loading.value = false
  }
})

const filteredPlugins = computed(() => {
  const keyword = searchQuery.value.trim().toLowerCase()
  if (!keyword) return plugins.value
  return plugins.value.filter(plugin =>
    String(plugin?.name ?? '').toLowerCase().includes(keyword) ||
    String(plugin?.shortDescription ?? '').toLowerCase().includes(keyword) ||
    String(plugin?.publisher ?? '').toLowerCase().includes(keyword)
  )
})

function navigateToPlugin(id) {
  location.href = `/plugin/${id}`
}

</script>

<style scoped>
.plugin-store {
  padding: 20px;
}

.search-bar {
  margin-bottom: 20px;
  margin-top: 10px;
}

.search-bar input {
  width: 100%;
  padding: 10px;
  border: 1px solid var(--border-color);
  border-radius: 5px;
  font-size: 16px;
  background-color: var(--bg-color);
  color: var(--text-color);
}

.store-hint {
  padding: 20px 10px;
  color: var(--text-color);
  opacity: 0.7;
  font-size: 14px;
}

.store-error {
  color: #e57373;
  opacity: 1;
}

.plugin-list {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 20px;
  max-height: 600px;
  overflow-y: auto;
  padding: 10px;
}

.plugin-item {
  border: 1px solid var(--border-color);
  border-radius: 5px;
  overflow: hidden;
  cursor: pointer;
  transition: transform 0.2s;
  background-color: var(--bg-color);
}

.plugin-item:hover {
  transform: translateY(-5px);
  box-shadow: 0 5px 15px rgba(0, 0, 0, 0.1);
}

.plugin-info {
  padding: 15px;
}

.plugin-info h3 {
  margin: 0 0 10px 0;
  font-size: 18px;
  color: var(--text-color);
}

.plugin-description {
  margin: 0 0 12px 0;
  color: var(--text-color);
  opacity: 0.8;
  font-size: 14px;
  line-height: 1.4;
}

.plugin-meta {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 13px;
  color: var(--text-color);
  opacity: 0.6;
}

.publisher {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  margin-right: 10px;
}

.download-count {
  font-weight: 500;
  color: var(--text-color);
  opacity: 0.8;
}
</style>