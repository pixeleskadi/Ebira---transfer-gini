package app.ebira;

import android.content.Context;
import android.net.*;
import android.net.wifi.WifiManager;
import org.json.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/** Short, user-initiated UDP discovery; never sends to public destinations. */
final class LanDiscovery {
    static final int PORT=8444;
    static final class Host {
        final String name,host,pin,code;
        Host(JSONObject value)throws JSONException{name=value.getString("name");host=value.getString("host");pin=value.getString("pin");code=value.getString("code");}
        String invitation(){return new Uri.Builder().scheme("ebira").authority("pair").appendQueryParameter("host",host).appendQueryParameter("pin",pin).appendQueryParameter("code",code).build().toString();}
        String certificateEnd(){return pin.substring(pin.length()-12).toUpperCase(Locale.ROOT);}
    }
    static boolean validReply(JSONObject reply,String nonce,InetAddress source)throws Exception{
        if(!"ebira-host-v1".equals(reply.optString("magic"))||!nonce.equals(reply.optString("nonce")))return false;
        String pin=reply.optString("pin"),code=reply.optString("code"),name=reply.optString("name");
        if(!pin.matches("[a-fA-F0-9]{64}")||!code.matches("[a-fA-F0-9]{12}")||name.isEmpty()||name.length()>80)return false;
        URI origin=new URI(reply.getString("host"));
        return source.isSiteLocalAddress()&&"https".equals(origin.getScheme())&&source.getHostAddress().equals(origin.getHost())&&origin.getPort()>0&&origin.getPort()<=65535&&origin.getUserInfo()==null&&origin.getQuery()==null&&origin.getFragment()==null&&(origin.getPath()==null||origin.getPath().isEmpty());
    }
    static List<Host> find(Context context)throws Exception{
        WifiManager manager=(WifiManager)context.getApplicationContext().getSystemService(Context.WIFI_SERVICE);
        WifiManager.MulticastLock lock=manager.createMulticastLock("Ebira:discovery");lock.acquire();
        List<DatagramSocket>sockets=new ArrayList<>();Map<String,Host>found=new LinkedHashMap<>();
        String nonce=UUID.randomUUID().toString().replace("-","");
        byte[] probe=new JSONObject().put("magic","ebira-discover-v1").put("nonce",nonce).toString().getBytes(StandardCharsets.UTF_8);
        try{
            ConnectivityManager cm=context.getSystemService(ConnectivityManager.class);
            Enumeration<NetworkInterface> interfaces=NetworkInterface.getNetworkInterfaces();
            while(interfaces.hasMoreElements()&&sockets.size()<8){
                NetworkInterface nic=interfaces.nextElement();if(!nic.isUp()||nic.isLoopback()||nic.isPointToPoint())continue;
                for(InterfaceAddress address:nic.getInterfaceAddresses()){
                    if(!(address.getAddress() instanceof Inet4Address)||!address.getAddress().isSiteLocalAddress()||address.getBroadcast()==null)continue;
                    DatagramSocket socket=new DatagramSocket(null);
                    try{
                        if(!OfflineService.active)for(Network network:cm.getAllNetworks()){
                            NetworkCapabilities caps=cm.getNetworkCapabilities(network);LinkProperties links=cm.getLinkProperties(network);
                            if(caps!=null&&caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)&&links!=null&&nic.getName().equals(links.getInterfaceName())){network.bindSocket(socket);break;}
                        }
                        socket.bind(new InetSocketAddress(address.getAddress(),0));socket.setBroadcast(true);socket.setSoTimeout(80);
                        for(int attempt=0;attempt<2;attempt++)socket.send(new DatagramPacket(probe,probe.length,address.getBroadcast(),PORT));
                        sockets.add(socket);
                    }catch(Exception ignored){socket.close();}
                }
            }
            if(sockets.isEmpty())throw new java.io.IOException("No private Wi-Fi address is ready. Connect Windows to the phone's hotspot first, then retry.");
            long end=android.os.SystemClock.elapsedRealtime()+5000;
            while(android.os.SystemClock.elapsedRealtime()<end&&!Thread.currentThread().isInterrupted())for(DatagramSocket socket:sockets){
                try{
                    DatagramPacket packet=new DatagramPacket(new byte[4096],4096);socket.receive(packet);
                    JSONObject reply=new JSONObject(new String(packet.getData(),0,packet.getLength(),StandardCharsets.UTF_8));
                    if(validReply(reply,nonce,packet.getAddress())){Host host=new Host(reply);found.put(host.host+host.pin,host);}
                }catch(SocketTimeoutException ignored){}catch(Exception ignored){}
            }
            return new ArrayList<>(found.values());
        }finally{for(DatagramSocket socket:sockets)socket.close();lock.release();}
    }
}
