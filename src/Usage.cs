using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Mon3trDesktop {
    static class Json {
        public static Dictionary<string,object> Parse(string text){return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text);}
        public static string Stringify(object value){return new JavaScriptSerializer().Serialize(value);}
        public static object Get(Dictionary<string,object> d,string k){object value;return d!=null&&d.TryGetValue(k,out value)?value:null;}
        public static Dictionary<string,object> Obj(Dictionary<string,object> d,string k){return Get(d,k) as Dictionary<string,object>;}
        public static string Str(Dictionary<string,object> d,string k,string fallback=""){return Convert.ToString(Get(d,k)??fallback,CultureInfo.InvariantCulture);}
        public static double Number(Dictionary<string,object>d,string k,double fallback=0){var v=Get(d,k);double x;return v!=null&&double.TryParse(Convert.ToString(v,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out x)&&!double.IsNaN(x)&&!double.IsInfinity(x)?x:fallback;}
        public static bool Bool(Dictionary<string,object>d,string k,bool fallback){var v=Get(d,k);return v is bool?(bool)v:fallback;}
    }

    static class UsageClient {
        public static string FindCodex(){
            var configured=Environment.GetEnvironmentVariable("MON3TR_CODEX_PATH");if(!string.IsNullOrEmpty(configured)&&File.Exists(configured))return configured;
            var bin=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
            if(Directory.Exists(bin)){var exe=Directory.GetFiles(bin,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();if(exe!=null)return exe;}
            foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')){try{var p=System.IO.Path.Combine(dir,"codex.exe");if(File.Exists(p))return p;}catch{}}
            throw new IOException("未找到 Codex。请先安装并登录 Codex，或设置 MON3TR_CODEX_PATH。桌宠和动作仍可使用。");
        }
        public static async Task<string> Read(){ using(var connection=new UsageConnection()) return await connection.Read(); }
        public static string Format(Dictionary<string,object> data){
            var buckets=Json.Obj(data,"rateLimitsByLimitId");
            if(buckets==null||buckets.Count==0){buckets=new Dictionary<string,object>();var single=Json.Obj(data,"rateLimits");if(single!=null)buckets[Json.Str(single,"limitId","codex")]=single;}
            var lines=new List<string>();
            foreach(var pair in buckets){
                var bucket=pair.Value as Dictionary<string,object>;if(bucket==null)continue;
                string name=Json.Str(bucket,"limitName");if(string.IsNullOrWhiteSpace(name))name=pair.Key.Equals("codex",StringComparison.OrdinalIgnoreCase)?"Codex":pair.Key;
                string plan=Json.Str(bucket,"planType");lines.Add(name+(string.IsNullOrEmpty(plan)?"":" · "+plan.ToUpperInvariant()));
                bool found=false;
                foreach(var key in new[]{"primary","secondary"}){
                    var window=Json.Obj(bucket,key);if(window==null)continue;found=true;
                    var duration=Json.Number(window,"windowDurationMins",-1);string label=duration==10080?"本周":duration==300?"5 小时":duration>0?(duration%1440==0?(duration/1440)+" 天":duration%60==0?(duration/60)+" 小时":duration+" 分钟"):"当前周期";
                    if(Json.Get(window,"usedPercent")==null)lines.Add(label+"：额度暂未提供");else{double used=Layout.Clamp(Json.Number(window,"usedPercent"),0,100);lines.Add(label+"剩余 "+(100-used).ToString("0.#")+"%  ·  已用 "+used.ToString("0.#")+"%");}
                    var reset=Json.Number(window,"resetsAt",-1);if(reset>=0){try{var date=new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(reset).ToLocalTime();lines.Add("重置："+date.ToString("MM月dd日 HH:mm"));}catch{}}
                }
                if(!found)lines.Add("当前账号未提供周期额度");lines.Add("");
            }
            if(lines.Count==0)lines.Add("当前账号未提供可读取的额度。\n");
            return string.Join("\n",lines).Trim();
        }
    }

}
