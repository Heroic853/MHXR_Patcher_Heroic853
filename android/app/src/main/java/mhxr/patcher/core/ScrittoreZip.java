package mhxr.patcher.core;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.zip.CRC32;
import java.util.zip.Deflater;

/**
 * Scrive l'APK a mano, come ScrittoreZip in Patcher.cs (PC): le librerie .so e
 * resources.arsc restano non compresse e allineate (le .so a 4096 byte, come
 * "zipalign -p", resources.arsc e le altre voci non compresse a 4 byte). Con
 * ZipOutputStream normale finirebbero compresse o storte, e Android non riuscirebbe
 * a caricarle: crash muto all'avvio.
 */
final class ScrittoreZip implements AutoCloseable {
    private final OutputStream out;
    private long pos = 0;
    private final List<long[]> voci = new ArrayList<>(); // offset, crc, comp, uncomp, metodo
    private final List<byte[]> nomi = new ArrayList<>();

    ScrittoreZip(OutputStream out) { this.out = out; }

    private void bytes(byte[] b) throws IOException { out.write(b); pos += b.length; }
    private void u16(int v) throws IOException { bytes(new byte[]{(byte) v, (byte) (v >>> 8)}); }
    private void u32(long v) throws IOException { bytes(new byte[]{(byte) v, (byte) (v >>> 8), (byte) (v >>> 16), (byte) (v >>> 24)}); }

    void aggiungi(String nome, byte[] contenuto, boolean memorizza, int allineamento) throws IOException {
        CRC32 crc = new CRC32();
        crc.update(contenuto);
        byte[] dati;
        int metodo;
        if (memorizza) {
            dati = contenuto;
            metodo = 0;
        } else {
            Deflater def = new Deflater(Deflater.BEST_COMPRESSION, true);
            def.setInput(contenuto);
            def.finish();
            ByteArrayOutputStream mem = new ByteArrayOutputStream(Math.max(64, contenuto.length / 2));
            byte[] buf = new byte[65536];
            while (!def.finished()) mem.write(buf, 0, def.deflate(buf));
            def.end();
            dati = mem.toByteArray();
            metodo = 8;
        }
        byte[] nomeB = nome.getBytes(StandardCharsets.UTF_8);
        int extra = 0;
        if (memorizza && allineamento > 1) {
            long inizioDati = pos + 30 + nomeB.length;
            int resto = (int) ((allineamento - (inizioDati % allineamento)) % allineamento);
            // un extra field non vuoto e' lungo almeno 4 byte (id + lunghezza)
            extra = resto == 0 ? 0 : (resto < 4 ? resto + allineamento : resto);
        }
        long inizio = pos;
        u32(0x04034b50); u16(20); u16(0x0800); u16(metodo); u16(0); u16(0);
        u32(crc.getValue()); u32(dati.length); u32(contenuto.length);
        u16(nomeB.length); u16(extra);
        bytes(nomeB);
        if (extra > 0) { u16(0); u16(extra - 4); bytes(new byte[extra - 4]); }
        bytes(dati);
        voci.add(new long[]{inizio, crc.getValue(), dati.length, contenuto.length, metodo});
        nomi.add(nomeB);
    }

    @Override
    public void close() throws IOException {
        long inizioCentrale = pos;
        for (int i = 0; i < voci.size(); i++) {
            long[] v = voci.get(i);
            u32(0x02014b50); u16(20); u16(20); u16(0x0800); u16((int) v[4]); u16(0); u16(0);
            u32(v[1]); u32(v[2]); u32(v[3]);
            u16(nomi.get(i).length); u16(0); u16(0); u16(0); u16(0); u32(0); u32(v[0]);
            bytes(nomi.get(i));
        }
        long dimCentrale = pos - inizioCentrale;
        u32(0x06054b50); u16(0); u16(0); u16(voci.size()); u16(voci.size());
        u32(dimCentrale); u32(inizioCentrale); u16(0);
        out.flush();
    }
}
