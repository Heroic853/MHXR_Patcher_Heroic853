import SwiftUI
import UIKit
import UniformTypeIdentifiers

/// Patcher sul telefono, come l'app Android (MainActivity.java): si sceglie l'IPA giapponese originale,
/// si crea quello patchato e lo si passa a TrollStore o AltStore/SideStore per firmarlo e installarlo
/// (un'app iPhone non puo' installare altre app da sola). Stesso sfondo e stessa icona.
@main
struct MhxrPatcherApp: App {
    var body: some Scene { WindowGroup { Schermata() } }
}

struct Schermata: View {
    @State private var log = "Download the original Japanese IPA first (see the instructions on GitHub).\n"
    @State private var ingresso: URL?
    @State private var uscita: URL?
    @State private var lavoro = false

    var body: some View {
        ZStack {
            Color.white.ignoresSafeArea()
            Image("Sfondo").resizable().scaledToFit()
            VStack(alignment: .leading, spacing: 8) {
                Text("MHXR Patcher").font(.system(size: 22, weight: .bold)).foregroundColor(.black)
                Text("Japanese version only. The Taiwan build closes immediately after patching.")
                    .font(.system(size: 13)).foregroundColor(Color(red: 178 / 255, green: 34 / 255, blue: 34 / 255))
                Text("The game will connect to the private server (\(Indirizzo.mascherato(Indirizzo.server))). The English translation is downloaded from the server.")
                    .font(.system(size: 13)).foregroundColor(.gray)
                bottone("1.  Choose the Japanese IPA", attivo: !lavoro) { Selettore.condiviso.apri { copia($0) } }
                bottone("2.  Create the patched IPA", attivo: ingresso != nil && !lavoro) { crea() }
                bottone("3.  Install", attivo: uscita != nil && !lavoro) { if let u = uscita { condividi(u) } }
                Text("Install opens the share menu: choose TrollStore (permanent) or AltStore / SideStore (signed with your Apple ID). Before installing, set up the transfer code in the game you have now, then delete it.")
                    .font(.system(size: 12)).foregroundColor(.gray)
                ScrollView {
                    Text(log).font(.system(size: 11, design: .monospaced)).foregroundColor(.black)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                .background(Color.white.opacity(0.67))
            }
            .padding(18)
        }
        .onOpenURL { copia($0) } // "Apri con MHXR Patcher" dall'app File
    }

    private func bottone(_ t: String, attivo: Bool, _ azione: @escaping () -> Void) -> some View {
        Button(action: azione) { Text(t).frame(maxWidth: .infinity, alignment: .leading).padding(10) }
            .background(Color(white: attivo ? 0.88 : 0.95)).foregroundColor(attivo ? .black : .gray)
            .cornerRadius(4).disabled(!attivo)
    }

    private func scrivi(_ riga: String) { DispatchQueue.main.async { log += riga + "\n" } }

    private func copia(_ u: URL) {
        uscita = nil
        scrivi("copying the IPA...")
        lavoro = true
        DispatchQueue.global(qos: .userInitiated).async {
            defer { DispatchQueue.main.async { lavoro = false } }
            let accesso = u.startAccessingSecurityScopedResource()
            defer { if accesso { u.stopAccessingSecurityScopedResource() } }
            do {
                let f = FileManager.default.temporaryDirectory.appendingPathComponent("originale.ipa")
                try? FileManager.default.removeItem(at: f)
                try FileManager.default.copyItem(at: u, to: f)
                let mb = ((try? f.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0) / 1024 / 1024
                // Un IPA e' uno zip: inizia con "PK". Un download interrotto o una pagina web salvata no.
                let inizio = (try? FileHandle(forReadingFrom: f).readData(ofLength: 2)) ?? Data()
                guard inizio == Data([0x50, 0x4B]) else {
                    try? FileManager.default.removeItem(at: f)
                    scrivi("ERROR: \(u.lastPathComponent) (\(mb) MB) is not an IPA. Download it again (the original is about 68 MB).")
                    return
                }
                scrivi("IPA selected: \(u.lastPathComponent) (\(mb) MB)")
                DispatchQueue.main.async { ingresso = f }
            } catch { scrivi("ERROR reading the file: \(error.localizedDescription)") }
        }
    }

    private func crea() {
        guard let i = ingresso else { return }
        lavoro = true
        scrivi("--- start (it takes a minute) ---")
        DispatchQueue.global(qos: .userInitiated).async {
            defer { DispatchQueue.main.async { lavoro = false } }
            do {
                let cartella = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
                let o = cartella.appendingPathComponent("MHXR-patched.ipa")
                try Patcher.patcha(ingresso: i, uscita: o, log: scrivi)
                try? FileManager.default.removeItem(at: i)
                scrivi("--- done: MHXR-patched.ipa (also in Files > On My iPhone > MHXR Patcher) ---")
                DispatchQueue.main.async { ingresso = nil; uscita = o }
            } catch { scrivi("ERROR: \(error.localizedDescription)") }
        }
    }
}

/// La schermata in cima, da cui aprire selettore e menu di condivisione con UIKit. Con SwiftUI (.fileImporter e
/// .sheet sulla stessa vista) su iOS 15/16 il selettore a volte non si apriva o non lasciava scegliere l'IPA.
private func inCima() -> UIViewController? {
    let scene = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
    let scena = scene.first { $0.activationState == .foregroundActive } ?? scene.first
    var vc = scena?.windows.first { $0.isKeyWindow }?.rootViewController ?? scena?.windows.first?.rootViewController
    while let sopra = vc?.presentedViewController { vc = sopra }
    return vc
}

/// Selettore dei file: accetta qualsiasi file (.item: un .ipa scaricato da Safari puo' avere un tipo qualsiasi) e lo
/// copia dentro l'app (asCopy), cosi' funziona anche da Download, iCloud Drive o altre app.
final class Selettore: NSObject, UIDocumentPickerDelegate {
    static let condiviso = Selettore()
    private var scelto: ((URL) -> Void)?

    func apri(_ fatto: @escaping (URL) -> Void) {
        scelto = fatto
        let p = UIDocumentPickerViewController(forOpeningContentTypes: [.item], asCopy: true)
        p.delegate = self
        p.allowsMultipleSelection = false
        inCima()?.present(p, animated: true)
    }

    func documentPicker(_ controller: UIDocumentPickerViewController, didPickDocumentsAt urls: [URL]) {
        if let u = urls.first { scelto?(u) }
        scelto = nil
    }

    func documentPickerWasCancelled(_ controller: UIDocumentPickerViewController) { scelto = nil }
}

/// Menu di condivisione: da qui l'IPA va a TrollStore, AltStore o SideStore ("Apri in..."). Su iPad serve un punto
/// di partenza per il riquadro, se no l'app si chiude.
private func condividi(_ file: URL) {
    guard let vc = inCima() else { return }
    let a = UIActivityViewController(activityItems: [file], applicationActivities: nil)
    if let p = a.popoverPresentationController, let v = vc.view {
        p.sourceView = v
        p.sourceRect = CGRect(x: v.bounds.midX, y: v.bounds.midY, width: 0, height: 0)
        p.permittedArrowDirections = []
    }
    vc.present(a, animated: true)
}
