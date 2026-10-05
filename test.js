'use strict';
const {test}=require('node:test'),assert=require('node:assert/strict'),https=require('node:https'),fs=require('node:fs'),fsp=fs.promises,path=require('node:path'),os=require('node:os');
const {createApp,category,safeName,credentials,listenAvailable}=require('./server');
test('busy port selects a free port without stopping the existing listener',async()=>{
 const net=require('node:net'),occupied=net.createServer(),next=https.createServer(credentials());
 try{await new Promise(r=>occupied.listen(0,'0.0.0.0',r));const original=occupied.address().port;const selected=await listenAvailable(next,original);assert.notEqual(selected,original);assert.ok(next.listening);assert.ok(occupied.listening);}finally{if(next.listening)await new Promise(r=>next.close(r));if(occupied.listening)await new Promise(r=>occupied.close(r));}
});
test('file categories and filename sanitization',()=>{assert.equal(category('photo.JPG'),'Pictures');assert.equal(category('movie.mp4'),'Videos');assert.equal(category('doc.pdf'),'Documents');assert.equal(category('unknown.zip'),'Other');assert.equal(safeName('../x\\bad:foo'),'.._x_bad_foo');});
test('encrypted sessions, two-way transfer, resume, limits and revocation',async()=>{
 const dir=await fsp.mkdtemp(path.join(os.tmpdir(),'ebira-test-')),receiveRoot=path.join(dir,'received');const app=createApp({base:path.join(dir,'data'),receiveRoot});
 const server=https.createServer({...credentials(),minVersion:'TLSv1.2'},app.handler);
 await new Promise(r=>server.listen(0,'127.0.0.1',r));const port=server.address().port;
 function request(route,token,method='GET',body,raw=false){return new Promise((resolve,reject)=>{const data=body===undefined?null:raw?body:Buffer.from(JSON.stringify(body));const headers={};if(token)headers.Authorization='Bearer '+token;if(data)headers['Content-Length']=data.length;const req=https.request({host:'127.0.0.1',port,path:route,method,headers,rejectUnauthorized:false},res=>{const chunks=[];res.on('data',c=>chunks.push(c));res.on('end',()=>{const b=Buffer.concat(chunks);resolve({status:res.statusCode,body:raw?b:JSON.parse(b.toString()),tls:res.socket?.encrypted});});});req.on('error',reject);req.end(data);});}
 try{
 assert.equal((await request('/api/join',null,'POST',{code:'bad'})).status,403);
 const device=(await request('/api/join',null,'POST',{code:app.code,name:'Test phone'})).body.token;
 assert.equal((await request('/api/files',device)).status,400);
 await request('/api/session',app.admin,'POST',{token:device,action:'approve'});
 const bytes=Buffer.from('Ebira encrypted test payload with \u0000 binary content');
 const id=(await request('/api/files',device,'POST',{name:'test.pdf',size:bytes.length})).body.id;
 assert.equal((await request(`/api/files/${id}/chunk?offset=0`,device,'PUT',bytes.subarray(0,8),true)).status,200);
 assert.equal((await request('/api/files/'+id,device)).body.offset,8);
 assert.equal((await request(`/api/files/${id}/chunk?offset=0`,device,'PUT',bytes,true)).status,409);
 assert.equal((await request(`/api/files/${id}/chunk?offset=8`,device,'PUT',bytes.subarray(8),true)).status,200);
 const finish=await request('/api/files/'+id+'/finish',device,'POST',{});assert.equal(finish.status,200);assert.equal(finish.body.sha256,require('node:crypto').createHash('sha256').update(bytes).digest('hex'));
 assert.deepEqual(await fsp.readFile(path.join(receiveRoot,'Documents','test.pdf')),bytes);
 assert.deepEqual((await request('/api/files/'+id+'/download',app.admin,'GET',undefined,true)).body,bytes);
 const outgoing=(await request('/api/files',app.admin,'POST',{name:'photo.png',size:bytes.length,session:device})).body.id;
 assert.equal((await request(`/api/files/${outgoing}/chunk?offset=0`,device,'PUT',bytes,true)).status,400);
 await request(`/api/files/${outgoing}/chunk?offset=0`,app.admin,'PUT',bytes,true);await request(`/api/files/${outgoing}/finish`,app.admin,'POST',{});
 assert.deepEqual((await request(`/api/files/${outgoing}/download`,device,'GET',undefined,true)).body,bytes);
 const stranger=(await request('/api/join',null,'POST',{code:app.code,name:'Other device'})).body.token;await request('/api/session',app.admin,'POST',{token:stranger,action:'approve'});
 assert.equal((await request('/api/files/'+outgoing,stranger)).status,404);
 assert.equal((await request('/api/files',device,'POST',{name:'huge',size:10*1024**3+1})).status,400);
 const big=await request('/api/files',device,'POST',{name:'10GiB.bin',size:10*1024**3});assert.equal(big.status,200);
 const zero=(await request('/api/files',device,'POST',{name:'empty.txt',size:0})).body.id;assert.equal((await request(`/api/files/${zero}/finish`,device,'POST',{})).status,200);
 await request('/api/session',device,'POST',{action:'end'});
 assert.equal((await request('/api/files',device)).status,400);
 assert.equal((await request('/api/files/'+outgoing+'/download',app.admin)).status,400);
 }finally{await new Promise(r=>server.close(r));await fsp.rm(dir,{recursive:true,force:true});}
});
