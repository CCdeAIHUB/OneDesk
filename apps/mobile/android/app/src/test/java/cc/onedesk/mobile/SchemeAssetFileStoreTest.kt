package cc.onedesk.mobile

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test
import java.io.File
import java.nio.file.Files

class SchemeAssetFileStoreTest {
    @Test
    fun commitReplacesExistingAssetAndConsumesTemporaryFile() {
        val root = Files.createTempDirectory("onedesk-asset-store").toFile()
        try {
            val destination = File(root, "component-background.png").apply { writeText("old") }
            val temporary = File.createTempFile("component-background.", ".tmp", root).apply { writeText("new") }

            SchemeAssetFileStore.commit(temporary, destination)

            assertEquals("new", destination.readText())
            assertFalse(temporary.exists())
        } finally {
            root.deleteRecursively()
        }
    }
}
