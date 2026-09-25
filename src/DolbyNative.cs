// Version-pinned local DAX RPC adapter. Used only inside the isolated worker process.
// Reads installed protocol metadata as data; never loads or distributes Dolby executable code.
using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace AudioSwitch { internal sealed class DolbyNative : IDolbyAccess, IDisposable {
 [DllImport("rpcrt4",CharSet=CharSet.Unicode)] static extern int RpcStringBindingCompose(string uuid,string protocol,string address,string endpoint,string options,out IntPtr text);
 [DllImport("rpcrt4",CharSet=CharSet.Unicode)] static extern int RpcBindingFromStringBinding(IntPtr text,out IntPtr binding);
 [DllImport("rpcrt4",CharSet=CharSet.Unicode)] static extern int RpcStringFree(ref IntPtr text);
 [DllImport("rpcrt4")] static extern int RpcBindingFree(ref IntPtr binding);
 [DllImport("rpcrt4")] static extern int RpcBindingSetOption(IntPtr binding,uint option,IntPtr value);
 [DllImport("rpcrt4",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr NdrClientCall2(IntPtr stub,IntPtr format,IntPtr a,IntPtr b,IntPtr c,IntPtr d,IntPtr e);
 [DllImport("oleaut32")] static extern uint SafeArrayGetDim(IntPtr array);
 [DllImport("oleaut32")] static extern int SafeArrayGetVartype(IntPtr array,out ushort type);
 [DllImport("oleaut32")] static extern int SafeArrayGetLBound(IntPtr array,uint dimension,out int bound);
 [DllImport("oleaut32")] static extern int SafeArrayGetUBound(IntPtr array,uint dimension,out int bound);
 [DllImport("oleaut32")] static extern int SafeArrayAccessData(IntPtr array,out IntPtr data);
 [DllImport("oleaut32")] static extern int SafeArrayUnaccessData(IntPtr array);
 [DllImport("oleaut32")] static extern int SafeArrayDestroy(IntPtr array);
 [DllImport("oleaut32")] static extern IntPtr SafeArrayCreateVector(ushort type,int lower,uint count);
 [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
 [DllImport("kernel32",CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module,string name);
 static List<IntPtr> allocations=new List<IntPtr>(); static IntPtr image,stub; static byte[] mapped;
 static IntPtr Alloc(int size) {var p=Marshal.AllocHGlobal(size);Marshal.Copy(new byte[size],0,p,size);allocations.Add(p);return p;}
 static void Check(int status,string name){if(status!=0)throw new System.ComponentModel.Win32Exception(status);}
 static void Call(int op,IntPtr a,IntPtr b,IntPtr c) {
  if(Array.IndexOf(new[]{0,1,4,5,6,7,8,9,10,11,12,13,14,17,18,38,39,42,43,45,46,47,48,49,50,73,74,92},op)<0) throw new InvalidOperationException("Dolby operation is not enabled");
  int offset=BitConverter.ToUInt16(mapped,0x16170+op*2);
  if(BitConverter.ToUInt16(mapped,0x168e2+offset+6)!=op)throw new Exception("Protocol metadata mismatch");
  
  NdrClientCall2(stub,IntPtr.Add(image,0x168e2+offset),a,b,c,IntPtr.Zero,IntPtr.Zero);
  
 }
 static void Setup(string path) {
  byte[] raw=File.ReadAllBytes(path); string hash;
  using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(raw)).Replace("-","");
  if(hash!="12BA6E99D2DED86929A247ABD0A7181E6956B395AADBCCED507F1E97AB582FB8")throw new Exception("当前 Dolby Access 版本尚未验证，已停止应用；原配置仍保留。");
  int pe=BitConverter.ToInt32(raw,60),optional=pe+24,sections=optional+BitConverter.ToUInt16(raw,pe+20);
  mapped=new byte[BitConverter.ToInt32(raw,optional+56)];
  for(int i=0;i<BitConverter.ToUInt16(raw,pe+6);i++) {int s=sections+40*i;Buffer.BlockCopy(raw,BitConverter.ToInt32(raw,s+20),mapped,BitConverter.ToInt32(raw,s+12),BitConverter.ToInt32(raw,s+16));}
  image=Alloc(mapped.Length);Marshal.Copy(mapped,0,image,mapped.Length);
  var iface=Alloc(96);Marshal.WriteInt32(iface,96);
  Marshal.Copy(new Guid("11c6212d-d5f3-4500-ab16-634dc63037be").ToByteArray(),0,IntPtr.Add(iface,4),16);Marshal.WriteInt16(iface,20,1);
  Marshal.Copy(new Guid("8a885d04-1ceb-11c9-9fe8-08002b104860").ToByteArray(),0,IntPtr.Add(iface,24),16);Marshal.WriteInt16(iface,40,2);
  var ole=LoadLibrary(Path.Combine(Environment.SystemDirectory,"ole32.dll"));
  var automation=LoadLibrary(Path.Combine(Environment.SystemDirectory,"oleaut32.dll"));
  var userMarshal=Alloc(64);string[] names={"BSTR_UserSize","BSTR_UserMarshal","BSTR_UserUnmarshal","BSTR_UserFree","LPSAFEARRAY_UserSize","LPSAFEARRAY_UserMarshal","LPSAFEARRAY_UserUnmarshal","LPSAFEARRAY_UserFree"};
  for(int i=0;i<names.Length;i++) {var p=GetProcAddress(automation,names[i]);if(p==IntPtr.Zero)throw new Exception("Missing "+names[i]);Marshal.WriteIntPtr(userMarshal,i*8,p);}
  stub=Alloc(152);Marshal.WriteIntPtr(stub,0,iface);
  Marshal.WriteIntPtr(stub,8,GetProcAddress(ole,"CoTaskMemAlloc"));Marshal.WriteIntPtr(stub,16,GetProcAddress(ole,"CoTaskMemFree"));
  Marshal.WriteIntPtr(stub,24,Alloc(8));Marshal.WriteIntPtr(stub,64,IntPtr.Add(image,0x15732));
  Marshal.WriteInt32(stub,72,1);Marshal.WriteInt32(stub,76,0x60001);Marshal.WriteInt32(stub,88,0x8010272);
  Marshal.WriteIntPtr(stub,104,userMarshal);
 }
 static int ReadInt(IntPtr context,int op) {var p=Alloc(8);Call(op,context,p,IntPtr.Zero);return op==5||op==34||op==40||op==73?Marshal.ReadInt16(p):Marshal.ReadInt32(p);}
 static int[] ReadEq(IntPtr context) {
  var holder=Alloc(8);Call(17,context,holder,IntPtr.Zero);var array=Marshal.ReadIntPtr(holder);
  if(array==IntPtr.Zero)throw new Exception("No EQ array");
  try {
   ushort type;Check(SafeArrayGetVartype(array,out type),"EQ type");if(type!=3 || SafeArrayGetDim(array)!=1)throw new Exception("Unexpected EQ array type");
   int lower,upper;Check(SafeArrayGetLBound(array,1,out lower),"EQ lower");Check(SafeArrayGetUBound(array,1,out upper),"EQ upper");
   if(upper<lower || upper-lower>127)throw new Exception("Unexpected EQ band count");
   var result=new int[upper-lower+1];IntPtr data;Check(SafeArrayAccessData(array,out data),"EQ access");
   try{Marshal.Copy(data,result,0,result.Length);}finally{SafeArrayUnaccessData(array);}
   return result;
  }finally{SafeArrayDestroy(array);}
 }
 static float ReadFloat(IntPtr context,int op){return BitConverter.ToSingle(BitConverter.GetBytes(ReadInt(context,op)),0);}
 static void WriteEq(IntPtr context,int[] values) {
  var array=SafeArrayCreateVector(3,0,(uint)values.Length);if(array==IntPtr.Zero)throw new OutOfMemoryException();
  try {IntPtr data;Check(SafeArrayAccessData(array,out data),"EQ write access");try{Marshal.Copy(values,0,data,values.Length);}finally{SafeArrayUnaccessData(array);}Call(18,context,array,IntPtr.Zero);}
  finally{SafeArrayDestroy(array);}
 }
 [DllImport("kernel32", CharSet=CharSet.Unicode)] static extern int GetPackagesByPackageFamily(string family,ref uint count,IntPtr names,ref uint length,IntPtr buffer);
 [DllImport("kernel32", CharSet=CharSet.Unicode)] static extern int GetPackagePathByFullName(string name,ref uint length,System.Text.StringBuilder path);
 IntPtr binding, text, storage, context;
 static string ComponentPath() {
  uint count=0,length=0;
  int result=GetPackagesByPackageFamily("DolbyLaboratories.DolbyAccess_rz1tebttyb220",ref count,IntPtr.Zero,ref length,IntPtr.Zero);
  if(result!=122 || count==0 || count>64 || length>65536)throw new InvalidOperationException("未找到当前用户安装的 Dolby Access。");
  var names=Alloc(checked((int)count*8));var buffer=Alloc(checked((int)length*2));
  Check(GetPackagesByPackageFamily("DolbyLaboratories.DolbyAccess_rz1tebttyb220",ref count,names,ref length,buffer),"Package");
  for(int i=0;i<count;i++) {
   string name=Marshal.PtrToStringUni(Marshal.ReadIntPtr(names,i*8)); uint size=0;
   if(GetPackagePathByFullName(name,ref size,null)!=122 || size>32768)continue;
   var path=new System.Text.StringBuilder((int)size);
   if(GetPackagePathByFullName(name,ref size,path)!=0)continue;
   string file=Path.Combine(path.ToString(),"DAXRPCClient.dll");
   if(File.Exists(file))return file;
  }
  throw new InvalidOperationException("未找到 Dolby 后台组件。");
 }
 internal DolbyNative(string deviceId) {
  try {
   string id=DolbyProfiles.EndpointGuid(deviceId);
   Setup(ComponentPath());
   Check(RpcStringBindingCompose(null,"ncalrpc",null,"DaxRpcEndpoint",null,out text),"Compose");
   Check(RpcBindingFromStringBinding(text,out binding),"Binding");
   Check(RpcBindingSetOption(binding,12,new IntPtr(1200)),"Timeout");
   storage=Alloc(8);Call(0,binding,storage,IntPtr.Zero);context=Marshal.ReadIntPtr(storage);
   if(context==IntPtr.Zero)throw new InvalidOperationException("Dolby 服务未建立连接。");
   var user=Marshal.StringToBSTR(Environment.UserName);var endpoint=Marshal.StringToBSTR(id);
   try {
    var output=Alloc(8);Call(4,context,user,output);
    if(Marshal.ReadInt16(output)!=-1)throw new InvalidOperationException("Dolby 服务初始化失败。");
    // InitializeEx accepts unsupported GUIDs, so the explicit membership check is mandatory.
    Call(92,context,endpoint,output);
    if(Marshal.ReadInt16(output)!=-1)throw new InvalidOperationException("此设备未被 Dolby 驱动识别，已保留当前音效。");
    int offset=BitConverter.ToUInt16(mapped,0x16170+91*2);
    if(BitConverter.ToUInt16(mapped,0x168e2+offset+6)!=91)throw new InvalidOperationException("Dolby 协议版本不匹配。");
    NdrClientCall2(stub,IntPtr.Add(image,0x168e2+offset),context,user,endpoint,output,IntPtr.Zero);
    if(Marshal.ReadInt16(output)!=-1)throw new InvalidOperationException("Dolby 无法绑定此设备。");
   }finally{Marshal.FreeBSTR(user);Marshal.FreeBSTR(endpoint);}
  }catch{Dispose();throw;}
 }
 public int Read(int op){return ReadInt(context,op);}
 public double ReadStrength(int op){return ReadFloat(context,op);}
 public int[] ReadEq(){return ReadEq(context);}
 public void Write(int op,int value){Call(op,context,new IntPtr(value),IntPtr.Zero);}
 public void WriteStrength(int op,double value){Call(op,context,new IntPtr(BitConverter.DoubleToInt64Bits(value)),IntPtr.Zero);}
 public void WriteEq(int[] value){WriteEq(context,value);}
 public void Dispose() {
  if(storage!=IntPtr.Zero && Marshal.ReadIntPtr(storage)!=IntPtr.Zero)try{Call(1,storage,IntPtr.Zero,IntPtr.Zero);}catch{}
  storage=IntPtr.Zero;context=IntPtr.Zero;
  if(binding!=IntPtr.Zero)RpcBindingFree(ref binding);if(text!=IntPtr.Zero)RpcStringFree(ref text);
  for(int i=allocations.Count-1;i>=0;i--)Marshal.FreeHGlobal(allocations[i]);
  allocations.Clear();
 }
}}