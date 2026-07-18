import CryptoKit
import Foundation

struct SchemeCacheResult {
    let updated: Bool
    let hasScheme: Bool
    let version: String
    let hash: String
}

final class SchemeCacheService {
    private let gateway: MobileGatewayClient
    private let logs: MobileLogStore
    private let fileManager: FileManager
    private let root: URL

    init(gateway: MobileGatewayClient, logs: MobileLogStore, fileManager: FileManager = .default) throws {
        self.gateway = gateway
        self.logs = logs
        self.fileManager = fileManager
        let support = try fileManager.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true)
        root = support.appendingPathComponent("OneDesk", isDirectory: true)
        try fileManager.createDirectory(at: root, withIntermediateDirectories: true)
    }

    func get(desktopId: String) -> JSONObject? {
        let url = cacheURL(desktopId: desktopId)
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONSupport.object(from: data)
    }

    func downloadAndCache(
        desktop: KnownDesktop,
        credential: String,
        descriptor: JSONObject
    ) throws -> SchemeCacheResult {
        let version = descriptor.string("version").isEmpty ? "0" : descriptor.string("version")
        let hash = descriptor.string("hash")
        let hasScheme = descriptor.bool("hasScheme")
        if !hash.isEmpty, get(desktopId: desktop.desktopId)?.string("hash") == hash {
            return SchemeCacheResult(updated: false, hasScheme: hasScheme, version: version, hash: hash)
        }

        var payload: JSONObject = hasScheme
            ? try downloadSchemePayload(desktop: desktop, credential: credential, descriptor: descriptor)
            : [
                "activeSchemeId": NSNull(),
                "scheme": NSNull(),
                "pages": [],
                "components": [],
                "actions": [],
                "permissionGrants": [],
            ]
        let assetRoot = assetDirectory(desktopId: desktop.desktopId, hash: hash)
        do {
            try fileManager.createDirectory(at: assetRoot, withIntermediateDirectories: true)
            try materializeAssets(desktop: desktop, credential: credential, root: assetRoot, payload: &payload)
        } catch {
            try? fileManager.removeItem(at: assetRoot)
            throw error
        }

        let cache: JSONObject = [
            "desktopId": desktop.desktopId,
            "version": version,
            "hash": hash,
            "updatedAt": ISO8601DateFormatter().string(from: Date()),
            "activeSchemeId": payload["activeSchemeId"] ?? NSNull(),
            "scheme": payload["scheme"] ?? NSNull(),
            "pages": payload["pages"] ?? [],
            "components": payload["components"] ?? [],
            "actions": payload["actions"] ?? [],
            "permissionGrants": payload["permissionGrants"] ?? [],
        ]
        // Data.write(.atomic) 先写同目录临时文件再替换，进程中断不会破坏上一个可用缓存。
        try JSONSupport.data(cache).write(to: cacheURL(desktopId: desktop.desktopId), options: .atomic)
        removeStaleAssetDirectories(desktopId: desktop.desktopId, currentHash: hash)
        return SchemeCacheResult(updated: true, hasScheme: hasScheme, version: version, hash: hash)
    }

    private func downloadSchemePayload(
        desktop: KnownDesktop,
        credential: String,
        descriptor: JSONObject
    ) throws -> JSONObject {
        let totalBytes = descriptor.int64("totalBytes")
        let expectedHash = descriptor.string("hash")
        guard totalBytes > 0, totalBytes <= 32 * 1024 * 1024, !expectedHash.isEmpty else {
            throw OneDeskMobileError.integrity("方案快照大小或哈希无效")
        }
        var output = Data(capacity: Int(totalBytes))
        var offset: Int64 = 0
        while offset < totalBytes {
            var request = gateway.authorizedRequest(type: "scheme-chunk", credential: credential)
            request["hash"] = expectedHash
            request["offset"] = offset
            request["length"] = 24 * 1024
            let response = try gateway.request(
                host: desktop.host,
                port: desktop.port,
                payload: request,
                expectedFingerprint: desktop.gatewayFingerprint)
            guard response.bool("ok"), let chunk = response.object("payload"),
                  chunk.int64("offset", default: -1) == offset,
                  let bytes = Data(base64Encoded: chunk.string("data")), !bytes.isEmpty else {
                throw OneDeskMobileError.integrity(response.string("message").isEmpty
                    ? "方案分块顺序无效" : response.string("message"))
            }
            output.append(bytes)
            offset += Int64(bytes.count)
        }
        guard sha256(output) == expectedHash.lowercased() else {
            throw OneDeskMobileError.integrity("方案完整性校验失败")
        }
        return try JSONSupport.object(from: output)
    }

    private func materializeAssets(
        desktop: KnownDesktop,
        credential: String,
        root: URL,
        payload: inout JSONObject
    ) throws {
        var pages = payload["pages"] as? [JSONObject] ?? []
        for index in pages.indices {
            try replaceMediaSource(
                desktop: desktop,
                credential: credential,
                root: root,
                ownerKind: "page",
                ownerId: pages[index].string("id"),
                target: &pages[index],
                key: "backgroundMediaSource")
        }
        payload["pages"] = pages

        var components = payload["components"] as? [JSONObject] ?? []
        for index in components.indices {
            let ownerId = components[index].object("definition")?.string("id") ?? ""
            guard var visual = components[index].object("visualConfig") else { continue }
            if var background = visual.object("background") {
                try replaceMediaSource(
                    desktop: desktop,
                    credential: credential,
                    root: root,
                    ownerKind: "component",
                    ownerId: ownerId,
                    target: &background,
                    key: "mediaSource")
                visual["background"] = background
            }
            if var image = visual.object("image") {
                try replaceMediaSource(
                    desktop: desktop,
                    credential: credential,
                    root: root,
                    ownerKind: "component",
                    ownerId: ownerId,
                    target: &image,
                    key: "source")
                visual["image"] = image
            }
            components[index]["visualConfig"] = visual
        }
        payload["components"] = components
    }

    private func replaceMediaSource(
        desktop: KnownDesktop,
        credential: String,
        root: URL,
        ownerKind: String,
        ownerId: String,
        target: inout JSONObject,
        key: String
    ) throws {
        guard let source = target[key] as? String,
              !source.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              !ownerId.isEmpty else { return }
        let fileName = URL(string: source)?.lastPathComponent.isEmpty == false
            ? URL(string: source)!.lastPathComponent
            : URL(fileURLWithPath: source).lastPathComponent
        guard !fileName.isEmpty else { return }
        do {
            let local = try downloadAsset(
                desktop: desktop,
                credential: credential,
                root: root,
                ownerKind: ownerKind,
                ownerId: ownerId,
                fileName: fileName)
            target[key] = local.absoluteString
        } catch {
            logs.append("Error", "SchemeAsset", "\(ownerKind)/\(ownerId)/\(fileName)：\(error.localizedDescription)")
            throw error
        }
    }

    private func downloadAsset(
        desktop: KnownDesktop,
        credential: String,
        root: URL,
        ownerKind: String,
        ownerId: String,
        fileName: String
    ) throws -> URL {
        let safeName = sanitize("\(ownerKind)-\(ownerId)-\(URL(fileURLWithPath: fileName).lastPathComponent)")
        let destination = root.appendingPathComponent(safeName)
        if let size = try? destination.resourceValues(forKeys: [.fileSizeKey]).fileSize, size > 0 {
            return destination
        }
        let temporary = root.appendingPathComponent("\(safeName).tmp")
        fileManager.createFile(atPath: temporary.path, contents: nil)
        let output = try FileHandle(forWritingTo: temporary)
        defer { try? output.close() }
        var offset: Int64 = 0
        var total = Int64.max
        while offset < total {
            var request = gateway.authorizedRequest(type: "asset", credential: credential)
            request["ownerKind"] = ownerKind
            request["ownerId"] = ownerId
            request["fileName"] = fileName
            request["offset"] = offset
            request["length"] = 24 * 1024
            let response = try gateway.request(
                host: desktop.host,
                port: desktop.port,
                payload: request,
                timeoutMilliseconds: 12_000,
                expectedFingerprint: desktop.gatewayFingerprint)
            guard response.bool("ok"), let chunk = response.object("payload"),
                  chunk.int64("offset", default: -1) == offset,
                  let bytes = Data(base64Encoded: chunk.string("data")) else {
                throw OneDeskMobileError.integrity(response.string("message").isEmpty
                    ? "资源分块顺序无效" : response.string("message"))
            }
            total = chunk.int64("totalBytes")
            if bytes.isEmpty && offset < total {
                throw OneDeskMobileError.integrity("资源分块为空")
            }
            try output.write(contentsOf: bytes)
            offset += Int64(bytes.count)
            if chunk.bool("complete") { break }
        }
        if fileManager.fileExists(atPath: destination.path) { try fileManager.removeItem(at: destination) }
        try fileManager.moveItem(at: temporary, to: destination)
        return destination
    }

    private func cacheURL(desktopId: String) -> URL {
        root.appendingPathComponent("scheme-\(sanitize(desktopId)).json")
    }

    private func assetDirectory(desktopId: String, hash: String) -> URL {
        root.appendingPathComponent("scheme-assets", isDirectory: true)
            .appendingPathComponent(sanitize(desktopId), isDirectory: true)
            .appendingPathComponent(sanitize(hash.isEmpty ? "empty" : hash), isDirectory: true)
    }

    private func removeStaleAssetDirectories(desktopId: String, currentHash: String) {
        let desktopRoot = root.appendingPathComponent("scheme-assets", isDirectory: true)
            .appendingPathComponent(sanitize(desktopId), isDirectory: true)
        guard let directories = try? fileManager.contentsOfDirectory(
            at: desktopRoot,
            includingPropertiesForKeys: [.isDirectoryKey]) else { return }
        let keep = sanitize(currentHash.isEmpty ? "empty" : currentHash)
        for directory in directories where directory.lastPathComponent != keep {
            try? fileManager.removeItem(at: directory)
        }
    }

    private func sanitize(_ value: String) -> String {
        let allowed = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-")
        return value.unicodeScalars.map { allowed.contains($0) ? Character(String($0)) : "_" }.reduce("") { $0 + String($1) }
    }

    private func sha256(_ data: Data) -> String {
        SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }
}
