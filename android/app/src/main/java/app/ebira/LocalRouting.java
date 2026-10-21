package app.ebira;

import android.content.Context;
import android.net.*;
import java.net.*;
import java.net.Proxy;

/** Local-only AP interfaces are not necessarily ConnectivityManager Networks. */
final class LocalRouting {
    static java.net.URLConnection open(Context context,URL url)throws java.io.IOException{
        // The OS local-network route reaches hotspot clients. Never force these
        // requests onto an unrelated station Wi-Fi (or the default cellular Network).
        if(OfflineService.active)return url.openConnection(Proxy.NO_PROXY);
        ConnectivityManager cm=context.getSystemService(ConnectivityManager.class);
        InetAddress target=InetAddress.getByName(url.getHost());
        for(Network network:cm.getAllNetworks()){
            NetworkCapabilities caps=cm.getNetworkCapabilities(network);LinkProperties links=cm.getLinkProperties(network);
            if(caps==null||!caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)||links==null)continue;
            for(LinkAddress address:links.getLinkAddresses())if(sameSubnet(address.getAddress().getAddress(),target.getAddress(),address.getPrefixLength()))return network.openConnection(url,Proxy.NO_PROXY);
        }
        return url.openConnection(Proxy.NO_PROXY);
    }
    static boolean sameSubnet(byte[] a,byte[] b,int prefix){
        if(a.length!=4||b.length!=4||prefix<1||prefix>32)return false;
        for(int i=0;i<4;i++){int bits=Math.min(8,Math.max(0,prefix-i*8));int mask=(0xff<<(8-bits))&0xff;if((a[i]&mask)!=(b[i]&mask))return false;}
        return true;
    }
}
