package app.ebira;

import org.json.*;
import java.net.*;
import java.io.*;
import java.security.*;
import java.security.cert.*;
import javax.net.ssl.*;

final class Api {
    static final int CHUNK = 8 * 1024 * 1024;
    static final long MAX_FILE = 60_000_000_000L;
    final String host, pin, token;
    final SSLSocketFactory sockets;
    private final HostnameVerifier verifier;
    interface ConnectionFactory { URLConnection open(URL url) throws IOException; }
    private final ConnectionFactory connections;
    Api(String host, String pin, String token) throws Exception { this(host,pin,token,URL::openConnection); }
    @android.annotation.SuppressLint("CustomX509TrustManager") // Exact SHA-256 leaf pin from the trusted Windows invitation; never accept an unpinned leaf.
    Api(String host, String pin, String token, ConnectionFactory connections) throws Exception {
        URI origin = new URI(host);
        if (!"https".equals(origin.getScheme()) || origin.getHost() == null || origin.getUserInfo() != null || origin.getQuery() != null || origin.getFragment() != null || (origin.getPath() != null && !origin.getPath().isEmpty()) || !pin.matches("[a-fA-F0-9]{64}")) throw new IOException("Invalid Ebira invitation");
        // v1 uses explicit local IPv4 addresses; never resolve a remote hostname or follow redirects.
        String ip = origin.getHost(); if (!ip.matches("[0-9.]+")) throw new IOException("Use the local IPv4 invitation shown in Windows");
        InetAddress address = InetAddress.getByName(ip);
        if (!address.isSiteLocalAddress() && !address.isLinkLocalAddress() && !address.isLoopbackAddress()) throw new IOException("Ebira requires a local network address");
        this.host = host; this.pin = pin.toLowerCase(java.util.Locale.ROOT); this.token = token; this.connections=connections;
        verifier=(name,session)->{try{return hash(session.getPeerCertificates()[0].getEncoded()).equals(this.pin);}catch(Exception e){return false;}};
        TrustManager[] trust = { new X509TrustManager() {
            public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }
            public void checkClientTrusted(X509Certificate[] chain, String auth) throws CertificateException { throw new CertificateException("Not a client trust manager"); }
            public void checkServerTrusted(X509Certificate[] chain, String auth) throws CertificateException {
                try { if (chain.length == 0 || !MessageDigest.isEqual(hash(chain[0].getEncoded()).getBytes(java.nio.charset.StandardCharsets.US_ASCII), Api.this.pin.getBytes(java.nio.charset.StandardCharsets.US_ASCII))) throw new CertificateException("Windows certificate does not match the invitation"); chain[0].checkValidity(); }
                catch (GeneralSecurityException e) { throw new CertificateException(e); }
            }
        } };
        SSLContext tls = SSLContext.getInstance("TLS"); tls.init(null, trust, new SecureRandom()); sockets = tls.getSocketFactory();
    }
    private HttpsURLConnection connection(String route, String method) throws Exception {
        HttpsURLConnection c = (HttpsURLConnection) connections.open(new URL(host + route));
        c.setSSLSocketFactory(sockets); c.setHostnameVerifier(verifier);
        c.setConnectTimeout(15000); c.setReadTimeout(120000); c.setInstanceFollowRedirects(false); c.setRequestMethod(method);
        if (token != null && !token.isEmpty()) c.setRequestProperty("Authorization", "Bearer " + token); return c;
    }
    static byte[] read(InputStream in, int max) throws IOException {
        try (InputStream input = in; ByteArrayOutputStream out = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[65536]; int n; while ((n = input.read(buffer)) != -1) { if (out.size() + n > max) throw new IOException("Response exceeds protocol limit"); out.write(buffer, 0, n); } return out.toByteArray();
        }
    }
    private byte[] response(HttpsURLConnection c, int limit) throws Exception {
        int status = c.getResponseCode();
        if (status < 200 || status >= 300) {
            String message = "Windows returned " + status;
            InputStream error = c.getErrorStream(); if (error != null) { try { message = new JSONObject(new String(read(error, 65536), java.nio.charset.StandardCharsets.UTF_8)).optString("error", message); } catch (JSONException ignored) { } }
            if (status == 401) throw new SessionEnded(message); throw new IOException(message);
        }
        return read(c.getInputStream(), limit);
    }
    Object json(String route, String method, JSONObject value) throws Exception {
        HttpsURLConnection c = connection(route, method);
        boolean consumed=false;
        try {
            if (value != null) { byte[] bytes = value.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8); c.setDoOutput(true); c.setFixedLengthStreamingMode(bytes.length); c.setRequestProperty("Content-Type", "application/json"); try (OutputStream out = c.getOutputStream()) { out.write(bytes); } }
            byte[] bytes = response(c, 16 * 1024 * 1024); consumed=true; if (bytes.length == 0) return new JSONObject(); return new JSONTokener(new String(bytes, java.nio.charset.StandardCharsets.UTF_8)).nextValue();
        } finally { if(!consumed)c.disconnect(); }
    }
    JSONObject get(String route) throws Exception { return (JSONObject) json(route, "GET", null); }
    JSONObject post(String route, JSONObject value) throws Exception { return (JSONObject) json(route, "POST", value); }
    long put(String id, long offset, byte[] bytes) throws Exception {
        HttpsURLConnection c = connection("/v1/transfers/" + id + "/chunk?offset=" + offset, "PUT");
        boolean consumed=false;
        try { c.setDoOutput(true); c.setFixedLengthStreamingMode(bytes.length); c.setRequestProperty("X-Ebira-SHA256", hash(bytes)); try (OutputStream out = c.getOutputStream()) { out.write(bytes); } byte[] reply=response(c,65536);consumed=true;return new JSONObject(new String(reply, java.nio.charset.StandardCharsets.UTF_8)).getLong("offset"); }
        finally { if(!consumed)c.disconnect(); }
    }
    byte[] chunk(String id, long offset) throws Exception {
        HttpsURLConnection c = connection("/v1/transfers/" + id + "/chunk?offset=" + offset, "GET");
        boolean consumed=false;
        try { byte[] bytes = response(c, CHUNK); if (!hash(bytes).equals(c.getHeaderField("X-Ebira-SHA256"))) throw new IOException("Chunk checksum mismatch");consumed=true;return bytes; }
        finally { if(!consumed)c.disconnect(); }
    }
    static String hash(byte[] bytes) throws NoSuchAlgorithmException { return hex(MessageDigest.getInstance("SHA-256").digest(bytes)); }
    static String hex(byte[] bytes) { StringBuilder s = new StringBuilder(bytes.length * 2); for (byte b : bytes) s.append(String.format(java.util.Locale.ROOT, "%02x", b & 255)); return s.toString(); }
    static final class SessionEnded extends IOException { SessionEnded(String message) { super(message); } }
}
