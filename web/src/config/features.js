/**
 * 前端功能开关
 *
 * ENABLE_PLUGIN_FEATURES —— 插件相关功能（插件管理 / 插件商城 / 插件详情 /
 * 服务器依赖插件列表 / 插件统计 / 自动更新插件）
 *
 * 背景：插件商城依赖的远端接口（110.42.70.32:13423 的 /api/fantnel/plugin/*、
 * /api/fantnel/plugin/get/download、/api/fantnel/dependence）已在服务端下线，
 * 全部返回 500「No static resource ...」。因此先把插件相关的入口统一隐藏。
 *
 * 这里只是「隐藏」不是「删除」——远端接口恢复后，把下面的值改回 true 即可全部还原。
 */
export const ENABLE_PLUGIN_FEATURES = false
