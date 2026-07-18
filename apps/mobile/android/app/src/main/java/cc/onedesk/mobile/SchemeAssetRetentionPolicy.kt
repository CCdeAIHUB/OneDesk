package cc.onedesk.mobile

data class SchemeAssetDirectory(val desktopId: String, val schemeHash: String)

object SchemeAssetRetentionPolicy {
    fun staleDirectories(
        desktopId: String,
        currentHash: String,
        directories: Collection<SchemeAssetDirectory>,
    ): List<SchemeAssetDirectory> {
        // 缓存属于“桌面端 + 方案哈希”，更新一个桌面端时绝不能清理其他桌面端的离线方案。
        return directories.filter { it.desktopId == desktopId && it.schemeHash != currentHash }
    }
}
