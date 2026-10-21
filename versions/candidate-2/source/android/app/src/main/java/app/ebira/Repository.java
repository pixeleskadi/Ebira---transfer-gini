package app.ebira;

import android.content.Context;
import org.json.*;

final class Repository {
    private static LocalState instance;
    static volatile String status = "Ready. Paste the invitation from Windows to pair.";
    static volatile String jobs = "[]";
    static synchronized LocalState get(Context c) throws Exception { if (instance == null) instance = new LocalState(c.getApplicationContext()); return instance; }
    static JSONObject snapshot(Context c) throws Exception { LocalState s = get(c); synchronized (s) { return new JSONObject(s.data.toString()); } }
    static Api api(LocalState s,Context context) throws Exception { Context app=context.getApplicationContext(); synchronized (s) { return new Api(s.data.getString("host"), s.data.getString("pin"), s.data.optString("token"), url -> {
        android.net.ConnectivityManager cm=app.getSystemService(android.net.ConnectivityManager.class);
        for(android.net.Network network:cm.getAllNetworks()){
            android.net.NetworkCapabilities caps=cm.getNetworkCapabilities(network);
            if(caps!=null&&caps.hasTransport(android.net.NetworkCapabilities.TRANSPORT_WIFI))return network.openConnection(url);
        }
        return url.openConnection();
    }); } }
}
