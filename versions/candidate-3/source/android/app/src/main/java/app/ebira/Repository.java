package app.ebira;

import android.content.Context;
import org.json.*;

final class Repository {
    private static LocalState instance;
    private static Api cachedApi;
    private static boolean cachedOffline;
    static volatile String status = "Ready. Paste the invitation from Windows to pair.";
    static volatile String jobs = "[]";
    static synchronized LocalState get(Context c) throws Exception { if (instance == null) instance = new LocalState(c.getApplicationContext()); return instance; }
    static JSONObject snapshot(Context c) throws Exception { LocalState s = get(c); synchronized (s) { return new JSONObject(s.data.toString()); } }
    static synchronized Api api(LocalState s,Context context) throws Exception {
        synchronized(s){return api(s.data.getString("host"),s.data.getString("pin"),s.data.optString("token"),context);}
    }
    static synchronized Api api(String host,String pin,String token,Context context)throws Exception{
        if(cachedApi==null||cachedOffline!=OfflineService.active||!cachedApi.host.equals(host)||!cachedApi.pin.equalsIgnoreCase(pin)||!cachedApi.token.equals(token)){
            Context app=context.getApplicationContext();cachedApi=new Api(host,pin,token,url->LocalRouting.open(app,url));cachedOffline=OfflineService.active;
        }
        return cachedApi;
    }
}
