#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <bcrypt.h>
#include <stdio.h>
#include <stdint.h>
#include <math.h>
#include "MinHook.h"
#include "damping.h"

typedef struct {float lower,upper,stiffness,damping,force,target,velocity; int type;} Drive;
typedef void (*Update)(void*,void*,void*);
typedef float (*Distance)(void*,void*);
typedef void (*DriveFn)(void*,Drive*);
static Update original,originalHingeUpdate;
typedef void (*Constructor)(void*,void*,void*,void*,void*,void*,void*);
static Constructor originalHingeCtor;
static const void* (*classMethod)(void*,const char*,int);
static int torsionReady=0,torsionFailed=0;
typedef struct {float x[3];int count;} ReducedSpace;
static void (*jointPosition)(void*,ReducedSpace*);
static Distance distanceFn;
static double (*fixedTimeFn)(void);
static void* (*resolveIcall)(const char*);
static unsigned char *game;
static char folder[MAX_PATH], configPath[MAX_PATH], statusPath[MAX_PATH], tempPath[MAX_PATH];
static int enabled=0, revision=0;
static float values[6][7]={{400,30,30,30,30,.5,.5},{1000,60,60,60,60,.5,.5},{4000,200,200,200,200,.5,.5},{5000,20,20,20,20,.5,.5},{20000,100,100,100,100,.5,.5},{50000,100,100,100,100,.5,.5}};
static DWORD lastRead, lastStatus;
static unsigned long long updates;
static char lastConfig[1024];
typedef struct {void *self,*body; float previous,speed; double previousTime; DWORD seen; int kind,id,compression; Drive drive;} Track;
static Track tracks[4096];
static int kindOf(int id){switch(id){case 59:case 141:case 142:return 0;case 58:case 139:case 140:return 1;case 60:case 143:case 144:return 2;case 66:return 3;case 65:return 4;case 67:return 5;default:return -1;}}
static void status(const char *state,const char *message){
 FILE *f=fopen(tempPath,"wb");if(!f)return;
 fprintf(f,"{\"state\":\"%s\",\"message\":\"%s\",\"protocol\":3,\"torsionReady\":%s,\"pid\":%lu,\"enabled\":%s,\"revision\":%d,\"updates\":%llu,\"samples\":[",state,message,torsionReady?"true":"false",GetCurrentProcessId(),enabled?"true":"false",revision,updates);
 int n=0;DWORD now=GetTickCount();
 for(int i=0;i<4096;i++){Track*t=&tracks[i];if(!t->self||now-t->seen>2000)continue;
 fprintf(f,"%s{\"id\":%d,\"kind\":%d,\"shaftSpeed\":%.6g,\"distance\":%.6g,\"stiffness\":%.6g,\"damping\":%.6g,\"compression\":%s}",n++?",":"",t->id,t->kind,t->speed,t->previous,t->drive.stiffness,t->drive.damping,t->compression?"true":"false");}
 fputs("]}\n",f);fclose(f);MoveFileExA(tempPath,statusPath,MOVEFILE_REPLACE_EXISTING);
}
static void readConfig(void){
 FILE*f=fopen(configPath,"rb");if(!f)return;char buf[1024]={0};size_t len=fread(buf,1,sizeof(buf)-1,f);int extra=fgetc(f);fclose(f);
 if(extra!=EOF||!len||!strcmp(buf,lastConfig))return;
 int version,on,n=0;float v[6][7];
 if(sscanf(buf,"%d %d %n",&version,&on,&n)!=2||version!=3||(on!=0&&on!=1))return;
 char *cursor=buf+n;
 for(int i=0;i<6;i++)for(int j=0;j<7;j++){
   n=0;if(sscanf(cursor,"%f %n",&v[i][j],&n)!=1||!n)return;cursor+=n;
   float max=j==0?100000:(j>=5?100:20000), min=j>=5?0.001f:0;
   if(!isfinite(v[i][j])||v[i][j]<min||v[i][j]>max)return;
 }
 if(*cursor)return;
 memcpy(values,v,sizeof(v));enabled=on;revision++;strcpy(lastConfig,buf);
}
static Track* track(void*self,void*body,DWORD now){
 unsigned start=((uintptr_t)self>>4)%4096;
 for(unsigned j=0;j<4096;j++){Track*t=&tracks[(start+j)%4096];if(t->self==self||!t->self||now-t->seen>10000){
 if(t->self!=self||t->body!=body||now-t->seen>250){memset(t,0,sizeof(*t));t->self=self;t->body=body;t->previous=NAN;}return t;}}
 return NULL;
}
static void tuneSpring(void*self){
 // Unity calls this on the physics/main thread. All Unity API access stays here.
 DWORD now=GetTickCount();if(now-lastRead>=250){lastRead=now;readConfig();}
 if(!fixedTimeFn)fixedTimeFn=(double(*)(void))resolveIcall("UnityEngine.Time::get_fixedTimeAsDouble()");
 if(!fixedTimeFn)return;
 double physicsTime=fixedTimeFn();
 int id=*(int*)((char*)self+0x10),kind=kindOf(id);
 if(kind>=0){
 void*body=*(void**)((char*)self+0x60);void*so=*(void**)((char*)self+0x18);
 DriveFn get=*(DriveFn*)(game+0x2564d88),set=*(DriveFn*)(game+0x2564d90);
 if(body&&so&&get&&set){
 Drive d;get(body,&d);
 Track*t=track(self,body,now);float dist;
 if(kind>=3){
   if(!jointPosition)jointPosition=(void(*)(void*,ReducedSpace*))resolveIcall("UnityEngine.ArticulationBody::get_jointPosition_Injected(UnityEngine.ArticulationReducedSpace&)");
   if(!jointPosition){torsionFailed=1;return;}
   ReducedSpace pos={{0,0,0},0};jointPosition(body,&pos);
   if(pos.count!=1||!isfinite(pos.x[0]))return;
   dist=pos.x[0];
 }else dist=distanceFn(self,NULL);
 if(t&&isfinite(dist)){
 t->speed=kind>=3?angular_velocity(dist,t->previous,physicsTime-t->previousTime):shaft_velocity(dist,t->previous,physicsTime-t->previousTime);
 if(kind>=3){
   t->compression=torsion_winding(dist,d.target,t->speed,t->compression);
 }else if(t->speed < -0.00001f)t->compression=1;else if(t->speed>0.00001f)t->compression=0;
 t->previousTime=physicsTime;
 t->previous=dist;t->seen=now;t->id=id;t->kind=kind;
 float k=enabled?values[kind][0]:*(float*)((char*)so+(kind>=3?0x100:0xd8));
 float damp=enabled?shaft_damping(values[kind],t->speed,t->compression):*(float*)((char*)so+(kind>=3?0x104:0xdc));
 // Keep at least the stock cap, so zero stiffness still permits damping.
 float cap=fmaxf(k,*(float*)((char*)so+(kind>=3?0x100:0xd8)));
 if(d.stiffness!=k||d.damping!=damp||d.force!=cap){d.stiffness=k;d.damping=damp;d.force=cap;set(body,&d);get(body,&d);}
 t->drive=d;updates++;
 }
 }
 }
 if(now-lastStatus>=1000){lastStatus=now;status(torsionFailed?"error":"hooked",torsionFailed?"Torsion hook validation failed":"");}
}
static void hookedUpdate(void*self,void*frame,void*method){tuneSpring(self);original(self,frame,method);}
static void hingeUpdate(void*self,void*frame,void*method){
 if(kindOf(*(int*)((char*)self+0x10))>=3)tuneSpring(self);
 originalHingeUpdate(self,frame,method);
}
static void hingeConstructor(void*self,void*a,void*b,void*c,void*d,void*e,void*method){
 originalHingeCtor(self,a,b,c,d,e,method);
 if(torsionReady||torsionFailed||kindOf(*(int*)((char*)self+0x10))<3)return;
 void*klass=*(void**)self;
 const void*fixedMethod=classMethod(klass,"FixedUpdate",1);
 // This build's Il2CppClass vtable starts at 0x138; FixedUpdate is slot 5.
 // Validate BOTH members of the slot; never hook the globally shared no-op.
 void**slot=(void**)((char*)klass+0x188);
 if(!fixedMethod||slot[0]!=game+0x303090||slot[1]!=fixedMethod){torsionFailed=1;status("error","Torsion FixedUpdate slot did not match");return;}
 DWORD oldProtect;
 if(!VirtualProtect(slot,sizeof(void*),PAGE_READWRITE,&oldProtect)){torsionFailed=1;status("error","Torsion vtable protection failed");return;}
 originalHingeUpdate=(Update)slot[0];
 InterlockedExchangePointer(slot,(void*)hingeUpdate);
 DWORD ignored;VirtualProtect(slot,sizeof(void*),oldProtect,&ignored);
 torsionReady=1;
}
static int validBuild(HMODULE module){
 char path[MAX_PATH];if(!GetModuleFileNameA(module,path,sizeof(path)))return 0;
 FILE*f=fopen(path,"rb");if(!f)return 0;
 BCRYPT_ALG_HANDLE alg=NULL;BCRYPT_HASH_HANDLE hash=NULL;unsigned char out[32],buf[65536];int ok=0;
 if(BCryptOpenAlgorithmProvider(&alg,BCRYPT_SHA256_ALGORITHM,NULL,0)<0)goto end;
 if(BCryptCreateHash(alg,&hash,NULL,0,NULL,0,0)<0)goto end;
 size_t n;while((n=fread(buf,1,sizeof(buf),f))>0)if(BCryptHashData(hash,buf,(ULONG)n,0)<0)goto end;
 if(ferror(f)||BCryptFinishHash(hash,out,32,0)<0)goto end;
 const unsigned char expected[32]={0xc6,0x87,0x61,0x8a,0x07,0xb8,0x73,0x00,0x8a,0x1b,0x04,0x5f,0x39,0xd1,0xd8,0x15,0x7d,0xe3,0x57,0x37,0xef,0xaa,0x71,0x38,0xcc,0xc5,0x86,0x02,0x62,0x00,0xfd,0xc8};
 ok=!memcmp(out,expected,32);
 end:if(hash)BCryptDestroyHash(hash);if(alg)BCryptCloseAlgorithmProvider(alg,0);fclose(f);return ok;
}
static DWORD WINAPI worker(void*unused){
 (void)unused;status("waiting","Waiting for GameAssembly");
 HMODULE h=NULL;for(int i=0;i<600&&!h;i++){h=GetModuleHandleA("GameAssembly.dll");if(!h)Sleep(100);}
 if(!h){status("error","GameAssembly did not load");return 0;}
 if(!validBuild(h)){status("error","Unsupported GameAssembly SHA256; mod not enabled");return 0;}
 resolveIcall=(void*(*)(const char*))GetProcAddress(h,"il2cpp_resolve_icall");
 if(!resolveIcall){status("error","Missing IL2CPP resolver");return 0;}
 classMethod=(const void*(*)(void*,const char*,int))GetProcAddress(h,"il2cpp_class_get_method_from_name");
 if(!classMethod){status("error","Missing IL2CPP method lookup");return 0;}
 game=(unsigned char*)h;distanceFn=(Distance)(game+0x59e900);
 const unsigned char prologue[]={0x40,0x53,0x48,0x83,0xec,0x20,0x33,0xd2,0x48,0x8b,0xd9};
 if(memcmp(game+0x59e690,prologue,sizeof(prologue))){status("error","Unexpected hook bytes; mod not enabled");return 0;}
 status("hooked","Waiting for active springs");
 if(MH_Initialize()!=MH_OK||MH_CreateHook(game+0x59e690,hookedUpdate,(void**)&original)!=MH_OK||MH_CreateHook(game+0x580a20,hingeConstructor,(void**)&originalHingeCtor)!=MH_OK||MH_EnableHook(MH_ALL_HOOKS)!=MH_OK){status("error","Unable to install physics hook");return 0;}
 // No thread reads/writes shared configuration after enabling the hook.
 return 0;
}
// Forward the version APIs imported by this Unity build to the Windows system DLL.
static HMODULE realVersion;static INIT_ONCE once=INIT_ONCE_STATIC_INIT;
static BOOL CALLBACK loadVersion(PINIT_ONCE a,PVOID b,PVOID*c){(void)a;(void)b;(void)c;char path[MAX_PATH];UINT n=GetSystemDirectoryA(path,MAX_PATH);if(n&&n<MAX_PATH-13){strcat(path,"\\version.dll");realVersion=LoadLibraryA(path);}return TRUE;}
static FARPROC versionProc(const char*name){InitOnceExecuteOnce(&once,loadVersion,NULL,NULL);return realVersion?GetProcAddress(realVersion,name):NULL;}
__declspec(dllexport) DWORD WINAPI ProxySizeA(LPCSTR p,LPDWORD h){typedef DWORD(WINAPI*F)(LPCSTR,LPDWORD);F f=(F)versionProc("GetFileVersionInfoSizeA");return f?f(p,h):0;}
__declspec(dllexport) BOOL WINAPI ProxyInfoA(LPCSTR p,DWORD h,DWORD l,LPVOID d){typedef BOOL(WINAPI*F)(LPCSTR,DWORD,DWORD,LPVOID);F f=(F)versionProc("GetFileVersionInfoA");return f?f(p,h,l,d):FALSE;}
__declspec(dllexport) BOOL WINAPI ProxyQueryA(LPCVOID p,LPCSTR s,LPVOID*b,PUINT l){typedef BOOL(WINAPI*F)(LPCVOID,LPCSTR,LPVOID*,PUINT);F f=(F)versionProc("VerQueryValueA");return f?f(p,s,b,l):FALSE;}
__declspec(dllexport) DWORD WINAPI ProxyGetFileVersionInfoSizeW(LPCWSTR p,LPDWORD h){typedef DWORD(WINAPI*F)(LPCWSTR p,LPDWORD h);F f=(F)versionProc("GetFileVersionInfoSizeW");return f?f(p,h):0;}
__declspec(dllexport) BOOL WINAPI ProxyGetFileVersionInfoW(LPCWSTR p,DWORD h,DWORD l,LPVOID d){typedef BOOL(WINAPI*F)(LPCWSTR p,DWORD h,DWORD l,LPVOID d);F f=(F)versionProc("GetFileVersionInfoW");return f?f(p,h,l,d):0;}
__declspec(dllexport) BOOL WINAPI ProxyVerQueryValueW(LPCVOID p,LPCWSTR s,LPVOID*b,PUINT l){typedef BOOL(WINAPI*F)(LPCVOID p,LPCWSTR s,LPVOID*b,PUINT l);F f=(F)versionProc("VerQueryValueW");return f?f(p,s,b,l):0;}
__declspec(dllexport) DWORD WINAPI ProxyGetFileVersionInfoSizeExA(DWORD flags,LPCSTR p,LPDWORD h){typedef DWORD(WINAPI*F)(DWORD flags,LPCSTR p,LPDWORD h);F f=(F)versionProc("GetFileVersionInfoSizeExA");return f?f(flags,p,h):0;}
__declspec(dllexport) BOOL WINAPI ProxyGetFileVersionInfoExA(DWORD flags,LPCSTR p,DWORD h,DWORD l,LPVOID d){typedef BOOL(WINAPI*F)(DWORD flags,LPCSTR p,DWORD h,DWORD l,LPVOID d);F f=(F)versionProc("GetFileVersionInfoExA");return f?f(flags,p,h,l,d):0;}
__declspec(dllexport) DWORD WINAPI ProxyVerLanguageNameA(DWORD lang,LPSTR text,DWORD size){typedef DWORD(WINAPI*F)(DWORD lang,LPSTR text,DWORD size);F f=(F)versionProc("VerLanguageNameA");return f?f(lang,text,size):0;}
__declspec(dllexport) DWORD WINAPI ProxyVerFindFileA(DWORD flags,LPCSTR file,LPCSTR win,LPCSTR app,LPSTR curr,PUINT currlen,LPSTR dest,PUINT destlen){typedef DWORD(WINAPI*F)(DWORD flags,LPCSTR file,LPCSTR win,LPCSTR app,LPSTR curr,PUINT currlen,LPSTR dest,PUINT destlen);F f=(F)versionProc("VerFindFileA");return f?f(flags,file,win,app,curr,currlen,dest,destlen):0;}
__declspec(dllexport) DWORD WINAPI ProxyVerInstallFileA(DWORD flags,LPCSTR src,LPCSTR dest,LPCSTR srcdir,LPCSTR destdir,LPCSTR currdir,LPSTR tmp,PUINT tmplen){typedef DWORD(WINAPI*F)(DWORD flags,LPCSTR src,LPCSTR dest,LPCSTR srcdir,LPCSTR destdir,LPCSTR currdir,LPSTR tmp,PUINT tmplen);F f=(F)versionProc("VerInstallFileA");return f?f(flags,src,dest,srcdir,destdir,currdir,tmp,tmplen):0;}
__declspec(dllexport) DWORD WINAPI ProxyGetFileVersionInfoSizeExW(DWORD flags,LPCWSTR p,LPDWORD h){typedef DWORD(WINAPI*F)(DWORD flags,LPCWSTR p,LPDWORD h);F f=(F)versionProc("GetFileVersionInfoSizeExW");return f?f(flags,p,h):0;}
__declspec(dllexport) BOOL WINAPI ProxyGetFileVersionInfoExW(DWORD flags,LPCWSTR p,DWORD h,DWORD l,LPVOID d){typedef BOOL(WINAPI*F)(DWORD flags,LPCWSTR p,DWORD h,DWORD l,LPVOID d);F f=(F)versionProc("GetFileVersionInfoExW");return f?f(flags,p,h,l,d):0;}
__declspec(dllexport) DWORD WINAPI ProxyVerLanguageNameW(DWORD lang,LPWSTR text,DWORD size){typedef DWORD(WINAPI*F)(DWORD lang,LPWSTR text,DWORD size);F f=(F)versionProc("VerLanguageNameW");return f?f(lang,text,size):0;}
__declspec(dllexport) DWORD WINAPI ProxyVerFindFileW(DWORD flags,LPCWSTR file,LPCWSTR win,LPCWSTR app,LPWSTR curr,PUINT currlen,LPWSTR dest,PUINT destlen){typedef DWORD(WINAPI*F)(DWORD flags,LPCWSTR file,LPCWSTR win,LPCWSTR app,LPWSTR curr,PUINT currlen,LPWSTR dest,PUINT destlen);F f=(F)versionProc("VerFindFileW");return f?f(flags,file,win,app,curr,currlen,dest,destlen):0;}
__declspec(dllexport) DWORD WINAPI ProxyVerInstallFileW(DWORD flags,LPCWSTR src,LPCWSTR dest,LPCWSTR srcdir,LPCWSTR destdir,LPCWSTR currdir,LPWSTR tmp,PUINT tmplen){typedef DWORD(WINAPI*F)(DWORD flags,LPCWSTR src,LPCWSTR dest,LPCWSTR srcdir,LPCWSTR destdir,LPCWSTR currdir,LPWSTR tmp,PUINT tmplen);F f=(F)versionProc("VerInstallFileW");return f?f(flags,src,dest,srcdir,destdir,currdir,tmp,tmplen):0;}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID reserved){(void)reserved;if(reason==DLL_PROCESS_ATTACH){
 DisableThreadLibraryCalls(module);
 // UnityCrashHandler also imports version.dll; only instrument the actual game.
 char exe[MAX_PATH];GetModuleFileNameA(NULL,exe,MAX_PATH);char*name=strrchr(exe,'\\');
 if(!name||_stricmp(name+1,"Mars First Logistics.exe"))return TRUE;
 GetModuleFileNameA(module,folder,MAX_PATH);char*p=strrchr(folder,'\\');if(!p)return TRUE;*p=0;
 snprintf(configPath,MAX_PATH,"%s\\spring-tuner\\tuning.txt",folder);snprintf(statusPath,MAX_PATH,"%s\\spring-tuner\\status.json",folder);snprintf(tempPath,MAX_PATH,"%s\\spring-tuner\\status.tmp",folder);
 HANDLE thread=CreateThread(NULL,0,worker,NULL,0,NULL);if(thread)CloseHandle(thread);
 }return TRUE;}
