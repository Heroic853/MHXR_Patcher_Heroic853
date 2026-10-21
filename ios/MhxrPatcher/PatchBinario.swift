import Foundation

/// Scrive l'indirizzo del server nell'eseguibile del gioco. Stessa logica di PatchLibreria.java (Android) e
/// Patcher.cs (PC): si cercano le stringhe "http://" e "https://" e si sceglie lo slot giusto:
///   1. "mhxr-dispatch" (gioco originale: https://mhxr-dispatch.s3-ap-northeast-1.amazonaws.com/);
///   2. uno slot che contiene gia' l'indirizzo nuovo (gioco gia' patchato);
///   3. il piu' grande, escluso 203.191.249.158 (un indirizzo che il gioco non usa).
/// Su iOS l'eseguibile puo' essere "fat" (piu' architetture in un file): si patcha ogni fetta.
enum PatchBinario {
    struct Slot { let offset: Int; let url: String; let spazioMax: Int }

    static let daIgnorare = ["googlesource", "dl.mh-xr.jp", "web.mh-xr.jp", "localhost", "apple.com"]

    static func trovaUrl(_ d: [UInt8], in r: Range<Int>) -> [Slot] {
        var trovati: [Slot] = []
        var i = r.lowerBound
        while i + 12 < r.upperBound {
            defer { i += 1 }
            guard d[i] == 0x68, d[i + 1] == 0x74, d[i + 2] == 0x74, d[i + 3] == 0x70 else { continue } // "http"
            var dopo = i + 4
            if d[dopo] == 0x73 { dopo += 1 } // "s"
            guard d[dopo] == 0x3A, d[dopo + 1] == 0x2F, d[dopo + 2] == 0x2F else { continue } // "://"
            var fine = i
            while fine < r.upperBound && d[fine] != 0 { fine += 1 }
            let url = String(decoding: d[i..<fine], as: UTF8.self)
            var ignora = url.count < 10 || url.count > 80
            for x in daIgnorare where url.lowercased().contains(x) { ignora = true }
            if !ignora {
                var zeri = fine
                while zeri < r.upperBound && d[zeri] == 0 { zeri += 1 }
                // un byte resta sempre libero per il terminatore
                trovati.append(Slot(offset: i, url: url, spazioMax: zeri - i - 1))
            }
            i = fine
        }
        return trovati
    }

    static func scegli(_ c: [Slot], _ nuovo: String) -> Slot? {
        if let s = c.first(where: { $0.url.lowercased().contains("mhxr-dispatch") }) { return s }
        if let s = c.first(where: { $0.url == nuovo }) { return s }
        return c.filter { !$0.url.contains("203.191.249.158") }.max(by: { $0.spazioMax < $1.spazioMax })
    }

    /// Patcha una fetta. Restituisce lo spazio dello slot usato, o nil se non c'e' uno slot adatto.
    static func scrivi(_ d: inout [UInt8], in r: Range<Int>, _ nuovo: String) throws -> Int? {
        guard let slot = scegli(trovaUrl(d, in: r), nuovo) else { return nil }
        let b = Array(nuovo.utf8)
        guard b.count <= slot.spazioMax else { throw ErrorePatch.indirizzoLungo(slot.spazioMax) }
        // prima si azzera tutto lo slot, poi si scrive: niente avanzi del vecchio indirizzo
        for k in 0...slot.spazioMax { d[slot.offset + k] = 0 }
        for (k, x) in b.enumerated() { d[slot.offset + k] = x }
        return slot.spazioMax
    }

    // MARK: Mach-O

    /// Le fette dell'eseguibile: tutto il file se e' "thin", una per architettura se e' "fat".
    static func fette(_ d: [UInt8]) -> [Range<Int>] {
        func be32(_ o: Int) -> Int { Int(d[o]) << 24 | Int(d[o + 1]) << 16 | Int(d[o + 2]) << 8 | Int(d[o + 3]) }
        guard d.count > 8, be32(0) == 0xCAFEBABE else { return [0..<d.count] }
        let n = be32(4)
        return (0..<n).compactMap { k in
            let o = 8 + k * 20, inizio = be32(o + 8), dim = be32(o + 12)
            return inizio + dim <= d.count ? inizio..<(inizio + dim) : nil
        }
    }

    /// true se una fetta e' ancora cifrata da Apple (FairPlay, LC_ENCRYPTION_INFO con cryptid != 0): non si puo' patchare.
    static func cifrata(_ d: [UInt8], in r: Range<Int>) -> Bool {
        func le32(_ o: Int) -> Int { Int(d[o]) | Int(d[o + 1]) << 8 | Int(d[o + 2]) << 16 | Int(d[o + 3]) << 24 }
        let b = r.lowerBound
        let magic = le32(b)
        guard magic == 0xFEEDFACF || magic == 0xFEEDFACE else { return false }
        let a64 = magic == 0xFEEDFACF
        let ncmds = le32(b + 16)
        var o = b + (a64 ? 32 : 28)
        for _ in 0..<ncmds {
            guard o + 8 <= r.upperBound else { break }
            let cmd = le32(o), dim = le32(o + 4)
            if cmd == 0x21 || cmd == 0x2C { return le32(o + 16) != 0 } // LC_ENCRYPTION_INFO(_64): cryptid
            o += max(dim, 8)
        }
        return false
    }
}

enum ErrorePatch: LocalizedError {
    case nonIpa, eseguibile, cifrato, nessunIndirizzo, indirizzoLungo(Int)
    var errorDescription: String? {
        switch self {
        case .nonIpa: return "this is not an IPA (no Payload/*.app inside)"
        case .eseguibile: return "the game executable was not found in the IPA"
        case .cifrato: return "this IPA is still encrypted by Apple (FairPlay): use a decrypted IPA"
        case .nessunIndirizzo: return "no server address found in the game: is this the Japanese IPA?"
        case .indirizzoLungo(let n): return "address too long for the slot (\(n))"
        }
    }
}
