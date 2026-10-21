import SwiftUI
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
    @State private var scegli = false
    @State private var condividi = false

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
                bottone("1.  Choose the Japanese IPA", attivo: !lavoro) { scegli = true }
                bottone("2.  Create the patched IPA", attivo: ingresso != nil && !lavoro) { crea() }
                bottone("3.  Install", attivo: uscita != nil && !lavoro) { condividi = true }
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
        .fileImporter(isPresented: $scegli, allowedContentTypes: [UTType(filenameExtension: "ipa") ?? .data, .data]) { r in
            if case .success(let u) = r { copia(u) } else if case .failure(let e) = r { scrivi("ERROR reading the file: \(e.localizedDescription)") }
        }
        .sheet(isPresented: $condividi) { if let u = uscita { Condividi(file: u) } }
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
                scrivi("IPA selected (\(mb) MB)")
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

/// Menu di condivisione: da qui l'IPA va a TrollStore, AltStore o SideStore ("Apri in...").
struct Condividi: UIViewControllerRepresentable {
    let file: URL
    func makeUIViewController(context: Context) -> UIActivityViewController { UIActivityViewController(activityItems: [file], applicationActivities: nil) }
    func updateUIViewController(_ c: UIActivityViewController, context: Context) {}
}
