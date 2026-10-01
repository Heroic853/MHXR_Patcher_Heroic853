package mhxr.patcher.core;

import java.nio.charset.StandardCharsets;
import java.util.Base64;

/**
 * Indirizzo del server privato, lo stesso della versione PC (Indirizzo.cs).
 * Codificato in Base64 solo per non lasciarlo leggibile a colpo d'occhio: chi
 * decompila l'app lo vede comunque. IP fisso della VPS, non un dominio duckdns
 * (alcuni operatori telefonici filtrano i "dynamic DNS").
 */
public final class Indirizzo {
    private Indirizzo() {}

    public static final String SERVER = new String(
            Base64.getDecoder().decode("aHR0cDovLzU3LjEzMS4xOTMuMjQ0Lw=="), StandardCharsets.US_ASCII);

    /** Lo slot piu' piccolo fra le due architetture del gioco. */
    public static final int MAX_CARATTERI = 47;

    /** Ultimi 4 caratteri veri, il resto pallini. */
    public static String mascherato(String reale) {
        int visibili = 4;
        if (reale.length() <= visibili) return reale;
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < reale.length() - visibili; i++) sb.append('•');
        return sb.append(reale.substring(reale.length() - visibili)).toString();
    }
}
