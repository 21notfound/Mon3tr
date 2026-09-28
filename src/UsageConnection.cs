using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mon3trDesktop {
    // One background account connection, reused across clicks. No per-click process startup.
    sealed class UsageConnection : IDisposable {
        readonly SemaphoreSlim connectGate=new SemaphoreSlim(1,1);
        readonly object sync=new object();
        readonly Dictionary<int,TaskCompletionSource<Dictionary<string,object>>> pending=new Dictionary<int,TaskCompletionSource<Dictionary<string,object>>>();
        Process process;
        TaskCompletionSource<bool> ready;
        int nextId=1;
        bool disposed;
        public async Task Warm(){await EnsureConnected();}
        async Task EnsureConnected(){
            await connectGate.WaitAsync().ConfigureAwait(false);
            try{
                if(disposed)throw new ObjectDisposedException("UsageConnection");
                if(process!=null&&!process.HasExited&&ready!=null&&ready.Task.Status==TaskStatus.RanToCompletion)return;
                StopProcess();
                ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var localReady=ready;
                var start=new ProcessStartInfo(UsageClient.FindCodex(),"app-server --stdio"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Program.Root};
                var own=new Process{StartInfo=start,EnableRaisingEvents=true};process=own;
                own.OutputDataReceived+=(s,e)=>{
                    if(string.IsNullOrWhiteSpace(e.Data))return;
                    try{
                        var m=Json.Parse(e.Data);int id=(int)Json.Number(m,"id",-1);if(id<0)return;
                        if(id==0){
                            if(Json.Obj(m,"error")!=null){localReady.TrySetException(new IOException("额度连接失败，请确认 Codex 已登录。"));return;}
                            lock(sync){own.StandardInput.WriteLine("{\"method\":\"initialized\"}");own.StandardInput.Flush();}
                            localReady.TrySetResult(true);return;
                        }
                        TaskCompletionSource<Dictionary<string,object>> completion;
                        lock(sync){if(!pending.TryGetValue(id,out completion))return;pending.Remove(id);}
                        if(Json.Obj(m,"error")!=null)completion.TrySetException(new IOException("额度读取失败，请确认账号登录和网络。"));
                        else{var result=Json.Obj(m,"result");if(result==null)completion.TrySetException(new IOException("账号未返回额度数据。"));else completion.TrySetResult(result);}
                    }catch{localReady.TrySetException(new IOException("额度连接已中断，再次点击人物可重试。"));}
                };
                own.ErrorDataReceived+=(s,e)=>{};
                own.Exited+=(s,e)=>{localReady.TrySetException(new IOException("请先打开 Codex 并登录账号。"));lock(sync){if(!object.ReferenceEquals(process,own))return;foreach(var request in pending.Values)request.TrySetException(new IOException("额度连接已断开，请再次点击重试。"));pending.Clear();}};
                lock(sync){if(disposed)throw new ObjectDisposedException("UsageConnection");own.Start();own.BeginOutputReadLine();own.BeginErrorReadLine();}
                lock(sync){own.StandardInput.WriteLine("{\"id\":0,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"mon3tr_desktop_pet\",\"title\":\"Mon3tr desktop pet\",\"version\":\"2.0.0\"}}}");own.StandardInput.Flush();}
                if(await Task.WhenAny(localReady.Task,Task.Delay(15000)).ConfigureAwait(false)!=localReady.Task)throw new TimeoutException("额度连接超时，再次点击人物可重试。");
                await localReady.Task.ConfigureAwait(false);
            }catch{StopProcess();throw;}finally{connectGate.Release();}
        }
        public async Task<string> Read(){
            await EnsureConnected().ConfigureAwait(false);
            var completion=new TaskCompletionSource<Dictionary<string,object>>(TaskCreationOptions.RunContinuationsAsynchronously);
            int id;
            lock(sync){
                if(disposed||process==null)throw new IOException("额度服务已关闭。");
                id=nextId++;pending.Add(id,completion);
                try{process.StandardInput.WriteLine("{\"id\":"+id+",\"method\":\"account/rateLimits/read\"}");process.StandardInput.Flush();}catch{pending.Remove(id);throw;}
            }
            try{
                if(await Task.WhenAny(completion.Task,Task.Delay(15000)).ConfigureAwait(false)!=completion.Task)throw new TimeoutException("额度读取超时，再次点击人物可重试。");
                return UsageClient.Format(await completion.Task.ConfigureAwait(false));
            }finally{lock(sync)pending.Remove(id);}
        }
        void StopProcess(){var own=process;process=null;if(own==null)return;try{if(!own.HasExited)own.Kill();}catch{}try{own.Dispose();}catch{}}
        public void Dispose(){lock(sync){disposed=true;StopProcess();foreach(var item in pending.Values)item.TrySetCanceled();pending.Clear();}}
    }
}
