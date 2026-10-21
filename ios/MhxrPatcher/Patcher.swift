import Foundation
import ZIPFoundation

/// Tutto il lavoro, senza interfaccia (come mhxr.patcher.core.Patcher su Android). Passi:
///   1. copia l'IPA togliendo le vecchie firme di Apple (_CodeSignature, embedded.mobileprovision, SC_Info...);
///   2. scrive l'indirizzo del server nell'eseguibile del gioco (CFBundleExecutable dell'Info.plist), in ogni fetta;
///   3. NON firma: su iPhone firma e installazione le fanno TrollStore o AltStore/SideStore con l'Apple ID dell'utente.
/// I file Mach-O (eseguibile, framework, dylib) restano eseguibili nello zip (permessi 755).
enum Patcher {
    static func patcha(ingresso: URL, uscita: URL, log: (String) -> Void) throws {
        let a = try Archive(url: ingresso, accessMode: .read)
        // L'app dentro Payload/ e il suo eseguibile
        guard let plist = a.first(where: { $0.path.range(of: #"^Payload/[^/]+\.app/Info\.plist$"#, options: .regularExpression) != nil }) else {
            throw ErrorePatch.nonIpa
        }
        let cartellaApp = String(plist.path.dropLast("Info.plist".count))
        let info = try dati(a, plist)
        guard let dizionario = try PropertyListSerialization.propertyList(from: info, format: nil) as? [String: Any],
              let nomeEseguibile = dizionario["CFBundleExecutable"] as? String else { throw ErrorePatch.eseguibile }
        let versione = dizionario["CFBundleShortVersionString"] as? String ?? "?"
        log("game: \(cartellaApp.dropFirst("Payload/".count).dropLast()) version \(versione)")
        let percorsoEseguibile = cartellaApp + nomeEseguibile

        try? FileManager.default.removeItem(at: uscita)
        let w = try Archive(url: uscita, accessMode: .create)
        var patchato = false
        for e in a {
            let p = e.path
            if scarta(p) { continue }
            if e.type == .directory {
                try w.addEntry(with: p, type: .directory, uncompressedSize: Int64(0), provider: { _, _ in Data() })
                continue
            }
            guard e.type == .file else { continue }
            var d = try dati(a, e)
            if p == percorsoEseguibile {
                var b = [UInt8](d)
                let fette = PatchBinario.fette(b)
                if fette.contains(where: { PatchBinario.cifrata(b, in: $0) }) { throw ErrorePatch.cifrato }
                var scritte = 0
                for (k, r) in fette.enumerated() {
                    if let spazio = try PatchBinario.scrivi(&b, in: r, Indirizzo.server) {
                        log("address patched in \(nomeEseguibile) (slice \(k + 1)/\(fette.count), room for \(spazio))")
                        scritte += 1
                    } else {
                        log("WARNING: no server address in slice \(k + 1)/\(fette.count)")
                    }
                }
                guard scritte > 0 else { throw ErrorePatch.nessunIndirizzo }
                d = Data(b)
                patchato = true
            }
            let machO = d.count >= 4 && [0xFEEDFACF, 0xFEEDFACE, 0xBEBAFECA] as [UInt32].contains(d.prefix(4).withUnsafeBytes { $0.load(as: UInt32.self) })
            let dimensione = d.count
            try w.addEntry(with: p, type: .file, uncompressedSize: Int64(dimensione), permissions: machO ? 0o755 : 0o644,
                           compressionMethod: .deflate, provider: { pos, n in d.subdata(in: Int(pos)..<min(Int(pos) + n, dimensione)) })
        }
        guard patchato else { throw ErrorePatch.eseguibile }
        log("IPA rebuilt without Apple's old signature")
    }

    /// Firme e dati dell'App Store: chi installa (TrollStore, AltStore, SideStore) rifirma da capo.
    static func scarta(_ p: String) -> Bool {
        p.contains("/_CodeSignature/") || p.hasSuffix("/_CodeSignature") || p.hasSuffix("/embedded.mobileprovision")
            || p.hasPrefix("META-INF/") || p == "iTunesMetadata.plist" || p == "iTunesArtwork" || p.contains("/SC_Info/")
    }

    static func dati(_ a: Archive, _ e: Entry) throws -> Data {
        var d = Data()
        _ = try a.extract(e, skipCRC32: false) { d.append($0) }
        return d
    }
}
