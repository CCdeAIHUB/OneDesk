package cc.onedesk.mobile

import java.io.File
import java.nio.file.AtomicMoveNotSupportedException
import java.nio.file.Files
import java.nio.file.StandardCopyOption

/** 将完整下载的临时资源一次性提交到正式缓存路径。 */
internal object SchemeAssetFileStore {
    fun commit(temporary: File, destination: File) {
        destination.parentFile?.mkdirs()
        try {
            Files.move(
                temporary.toPath(),
                destination.toPath(),
                StandardCopyOption.ATOMIC_MOVE,
                StandardCopyOption.REPLACE_EXISTING,
            )
        } catch (_: AtomicMoveNotSupportedException) {
            // 部分 Android 文件系统不提供原子移动，退回到同目录覆盖移动仍可避免半写入文件。
            Files.move(temporary.toPath(), destination.toPath(), StandardCopyOption.REPLACE_EXISTING)
        }
    }
}
