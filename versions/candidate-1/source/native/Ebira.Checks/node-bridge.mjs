import https from 'node:https';import http from 'node:http';import readline from 'node:readline';
const input=readline.createInterface({input:process.stdin});let server;
input.once('line',line=>{
 const config=JSON.parse(line);
 server=https.createServer({pfx:Buffer.from(config.pfx,'base64'),minVersion:'TLSv1.2'},(req,res)=>{
  if(req.url.startsWith('/v1/transfers/'+'f'.repeat(32)+'/chunk')){res.writeHead(200,{'X-Ebira-SHA256':'0'.repeat(64)});res.end(Buffer.from([1,2,3]));return;}
  const next=http.request({host:'127.0.0.1',port:config.port,path:req.url,method:req.method,headers:req.headers},incoming=>{res.writeHead(incoming.statusCode,incoming.headers);incoming.pipe(res);});
  next.on('error',()=>{res.writeHead(502);res.end();});req.pipe(next);
 });
 server.listen(0,'127.0.0.1',()=>process.stdout.write(server.address().port+'\n'));
});
input.on('close',()=>{if(server)server.close(()=>process.exit());else process.exit();});
