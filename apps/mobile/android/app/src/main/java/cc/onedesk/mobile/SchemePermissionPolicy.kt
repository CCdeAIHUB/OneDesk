package cc.onedesk.mobile

object SchemePermissionPolicy {
    fun isGranted(grants: Map<String, Set<String>>, sourceKey: String, capability: String): Boolean {
        if (sourceKey == "system") return true
        val sourceGrants = grants[sourceKey] ?: return false
        val category = capability.substringBefore('.')
        return capability in sourceGrants || "$category.*" in sourceGrants
    }
}
