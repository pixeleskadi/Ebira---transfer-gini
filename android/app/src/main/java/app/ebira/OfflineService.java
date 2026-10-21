package app.ebira;

import android.app.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.net.wifi.*;
import android.os.*;

/** Owns the OS reservation independently of Activity rotation and transfer pauses. */
public final class OfflineService extends Service {
    static volatile boolean active, starting;
    static volatile String ssid="", password="";
    static volatile String status="Create a private Wi-Fi connection. Mobile data and internet are not needed.";
    private WifiManager.LocalOnlyHotspotReservation reservation;
    private boolean destroyed;
    @Override public void onCreate(){
        super.onCreate();
        getSystemService(NotificationManager.class).createNotificationChannel(new NotificationChannel("offline","Offline Wi-Fi connection",NotificationManager.IMPORTANCE_LOW));
        Notification n=notification("Creating a private connection…");
        startForeground(2,n,ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE);
    }
    private Notification notification(String message){
        PendingIntent open=PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        PendingIntent stop=PendingIntent.getService(this,2,new Intent(this,OfflineService.class).setAction("stop"),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        return new Notification.Builder(this,"offline").setSmallIcon(android.R.drawable.stat_sys_upload).setContentTitle("Ebira · offline connection").setContentText(message).setContentIntent(open).setOngoing(true).addAction(new Notification.Action.Builder(null,"Stop connection",stop).build()).build();
    }
    @Override public int onStartCommand(Intent intent,int flags,int startId){
        if(intent!=null&&"stop".equals(intent.getAction())){stopSelf();return START_NOT_STICKY;}
        if(active||starting)return START_NOT_STICKY;
        starting=true;status="Creating offline Wi-Fi…";
        try{
            WifiManager manager=(WifiManager)getApplicationContext().getSystemService(WIFI_SERVICE);
            manager.startLocalOnlyHotspot(new WifiManager.LocalOnlyHotspotCallback(){
                @Override public void onStarted(WifiManager.LocalOnlyHotspotReservation value){
                    if(destroyed){value.close();return;}
                    reservation=value;
                    try{
                    if(Build.VERSION.SDK_INT>=30){SoftApConfiguration config=value.getSoftApConfiguration();ssid=config.getSsid();password=config.getPassphrase();}
                    else{WifiConfiguration config=value.getWifiConfiguration();if(config!=null){ssid=config.SSID;password=config.preSharedKey;}}
                    if(ssid==null||ssid.isEmpty()||password==null||password.isEmpty()){status="Android did not provide secured hotspot credentials. Try your phone's hotspot settings instead.";stopSelf();return;}
                    active=true;starting=false;
                    status="Offline Wi-Fi is ready. On Windows, join this network even if it says ‘No internet’. Then tap Find my laptop below.";
                    getSystemService(NotificationManager.class).notify(2,notification("Private Wi-Fi active · no internet required"));
                    }catch(Exception e){status="Could not read the private Wi-Fi settings: "+e.getMessage();stopSelf();}
                }
                @Override public void onStopped(){status="Android stopped the offline connection. Create it again, reconnect Windows and resume transfers.";stopSelf();}
                @Override public void onFailed(int reason){
                    switch(reason){
                        case ERROR_NO_CHANNEL:status="No Wi-Fi channel is available. Disconnect the phone from other Wi-Fi and try again.";break;
                        case ERROR_INCOMPATIBLE_MODE:status="Another hotspot or Wi-Fi sharing mode is active. Turn it off in Android Settings and try again.";break;
                        case ERROR_TETHERING_DISALLOWED:status="Android does not allow this hotspot. Check device restrictions or use another private Wi-Fi network.";break;
                        default:status="Android could not create the offline connection ("+reason+"). Check Wi-Fi and try again.";
                    }
                    stopSelf();
                }
            },new Handler(Looper.getMainLooper()));
        }catch(SecurityException e){status="Allow Nearby Wi-Fi (Android 13+) or precise Location (Android 10–12), then try again. On older Android, turn Location on too.";stopSelf();}
        catch(Exception e){status="Could not create offline Wi-Fi: "+e.getMessage();stopSelf();}
        return START_NOT_STICKY;
    }
    @Override public IBinder onBind(Intent intent){return null;}
    @Override public void onDestroy(){destroyed=true;active=false;starting=false;ssid="";password="";if(reservation!=null){reservation.close();reservation=null;}if(status.startsWith("Offline Wi-Fi")||status.startsWith("Creating"))status="Offline connection stopped. Recreate it and reconnect Windows to resume.";super.onDestroy();}
}
