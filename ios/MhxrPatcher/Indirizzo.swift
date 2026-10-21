import Foundation

/// Indirizzo del server privato, lo stesso dell'app Android (Indirizzo.java) e del PC (Indirizzo.cs).
/// Codificato in Base64 solo per non lasciarlo leggibile a colpo d'occhio: chi decompila l'app lo vede comunque.
enum Indirizzo {
    static let server = String(data: Data(base64Encoded: "aHR0cDovLzU3LjEzMS4xOTMuMjQ0Lw==")!, encoding: .ascii)!

    /// Ultimi 4 caratteri veri, il resto pallini.
    static func mascherato(_ reale: String) -> String {
        let visibili = 4
        guard reale.count > visibili else { return reale }
        return String(repeating: "•", count: reale.count - visibili) + reale.suffix(visibili)
    }
}
