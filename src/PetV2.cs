using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace Mon3trDesktop {
    static class Program {
        public static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
        [STAThread] public static int Main(string[] args){
            if(args.Contains("--test"))return Tests.Run();
            if(args.Contains("--usage-test")){try{Directory.CreateDirectory(System.IO.Path.Combine(Root,"qa"));File.WriteAllText(System.IO.Path.Combine(Root,"qa/live-usage.txt"),UsageClient.Read().GetAwaiter().GetResult());return 0;}catch(Exception e){File.WriteAllText(System.IO.Path.Combine(Root,"qa/live-usage.txt"),e.Message);return 1;}}
            bool first;using(var mutex=new Mutex(true,"Local\\Mon3trDesktopPetV1",out first)){
                if(!first)return 0;
                var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
                PetWindow pet=null;
                app.SessionEnding+=(s,e)=>{if(pet!=null)pet.AllowShutdown=true;};
                app.DispatcherUnhandledException+=(s,e)=>{if(pet!=null)pet.AllowShutdown=true;MessageBox.Show("Mon3tr 遇到了问题：\n"+e.Exception.Message,"Mon3tr");e.Handled=true;app.Shutdown(1);};
                try{pet=new PetWindow(args.Contains("--preview"));app.Run(pet);}catch(Exception e){MessageBox.Show("无法启动 Mon3tr：\n"+e.Message);return 1;}
            }return 0;
        }
    }
    enum Edge {None,Left,Right,Top,Bottom}
    static class BeijingClock {
        public static DateTimeOffset At(DateTimeOffset instant){return instant.ToOffset(TimeSpan.FromHours(8));}
        public static DateTimeOffset Now{get{return At(DateTimeOffset.UtcNow);}}
        public static string Greeting(DateTimeOffset instant){int hour=At(instant).Hour;return hour>=5&&hour<12?"早上好":hour>=12&&hour<18?"下午好":"晚上好";}
    }
    static class Layout {
        public static double Clamp(double v,double min,double max){return Math.Max(min,Math.Min(v,Math.Max(min,max)));}
        public static Rect Bubble(Rect pet,Size size,Rect work,out bool left){
            double x=pet.Left-size.Width+12;left=x>=work.Left+6;if(!left)x=pet.Right-12;
            return new Rect(Clamp(x,work.Left+6,work.Right-size.Width-6),Clamp(pet.Top-size.Height+20,work.Top+6,work.Bottom-size.Height-6),size.Width,size.Height);
        }
        public static Point ClampPet(Point window,Rect body,double scale,Rect work){
            return new Point(Clamp(window.X,work.Left-body.Left*scale,work.Right-body.Right*scale),Clamp(window.Y,work.Top-body.Top*scale,work.Bottom-body.Bottom*scale));
        }
        public static Rect Body(Point window,Rect body,double scale){return new Rect(window.X+body.X*scale,window.Y+body.Y*scale,body.Width*scale,body.Height*scale);}
        public static Edge Touching(Rect pet,Rect work){
            var gaps=new[]{pet.Left-work.Left,work.Right-pet.Right,pet.Top-work.Top,work.Bottom-pet.Bottom};
            int index=Array.IndexOf(gaps,gaps.Min());return gaps[index]<=6?(Edge)(index+1):Edge.None;
        }
    }
    sealed class Assets {
        public int Width,Height;public Rect Body;public byte[] Idle;public byte[][] Frames;public double[] Durations;
        public static Assets Load(){
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("animation.bin.gz"))
            using(var gzip=new GZipStream(stream,CompressionMode.Decompress))
            using(var reader=new BinaryReader(gzip)){
                if(new string(reader.ReadChars(4))!="M3T2")throw new IOException("动画素材无效。");
                var a=new Assets();a.Width=reader.ReadInt32();a.Height=reader.ReadInt32();int n=reader.ReadInt32();
                a.Body=new Rect(reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32());
                a.Durations=new double[n];for(int i=0;i<n;i++)a.Durations[i]=reader.ReadDouble();
                int length=a.Width*a.Height*4;a.Idle=reader.ReadBytes(length);a.Frames=new byte[n][];
                for(int i=0;i<n;i++){a.Frames[i]=reader.ReadBytes(length);if(a.Frames[i].Length!=length)throw new IOException("动画素材不完整。");}return a;
            }
        }
        public bool Hit(byte[] frame,double x,double y,double scale){int px=(int)Math.Floor(x/scale),py=(int)Math.Floor(y/scale);return px>=0&&py>=0&&px<Width&&py<Height&&frame[(py*Width+px)*4+3]>16;}
    }
    // Sequential presentation: never jump over source frames after a slow render tick.
    sealed class FrameStepper {
        readonly double[] durations;double due;bool begun;
        public int Index{get;private set;}public bool Finished{get;private set;}
        public FrameStepper(double[] timing){durations=timing;Reset();}
        public void Reset(){Index=-1;due=0;begun=false;Finished=false;}
        public bool Advance(double now){
            if(Finished)return false;
            if(!begun){begun=true;Index=0;due=now+durations[0];return true;}
            if(now+.0025<due)return false;
            Index++;if(Index>=durations.Length){Finished=true;return true;}
            due+=durations[Index];if(now-due>.050)due=now+durations[Index];return true;
        }
    }
    sealed class VoiceBank:IDisposable {
        readonly List<SoundPlayer> players=new List<SoundPlayer>();readonly List<Stream> streams=new List<Stream>();readonly Random random=new Random();
        public int LastIndex=-1;
        public VoiceBank(){foreach(var name in new[]{"voice-deploy.wav","voice-greeting.wav","voice-tap.wav"}){var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name);var player=new SoundPlayer(stream);player.Load();streams.Add(stream);players.Add(player);}}
        public void Play(){foreach(var p in players)p.Stop();int roll=random.Next(100);LastIndex=roll<45?0:roll<90?1:2;players[LastIndex].Play();}
        public void Stop(){foreach(var p in players)p.Stop();}
        public void Dispose(){foreach(var p in players){p.Stop();p.Dispose();}foreach(var s in streams)s.Dispose();}
    }
    sealed class PetWindow:Window {
        readonly Assets assets=Assets.Load();readonly Image picture=new Image{Stretch=Stretch.Fill,Cursor=Cursors.Hand};
        readonly WriteableBitmap bitmap;readonly Int32Rect frameRect;readonly FrameStepper player;readonly Stopwatch clock=new Stopwatch();
        readonly BubbleWindow bubble=new BubbleWindow();readonly EdgeWindow edgeTab=new EdgeWindow();readonly VoiceBank voices=new VoiceBank();
        readonly UsageConnection usage=new UsageConnection();readonly Forms.NotifyIcon tray;readonly System.Drawing.Icon trayIcon;readonly bool preview;
        readonly List<double> frameTimes=new List<double>();
        byte[] current;double scale=.605;Point press,origin;bool down,moved,closing,playing,trayHidden,initialized;Edge dock=Edge.None;
        public bool AllowShutdown;
        int clickGeneration,usageSuccesses;string usageText;DateTime lastUsage;
        string SettingsPath{get{return System.IO.Path.Combine(Program.Root,"settings.json");}}
        public PetWindow(bool previewMode){
            preview=previewMode;Title="Mon3tr 桌宠";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;
            bitmap=new WriteableBitmap(assets.Width,assets.Height,96,96,PixelFormats.Pbgra32,null);frameRect=new Int32Rect(0,0,assets.Width,assets.Height);player=new FrameStepper(assets.Durations);
            picture.Source=bitmap;RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.LowQuality);Content=picture;ApplySize();SetFrame(assets.Idle);
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("mon3tr.ico")){using(var icon=new System.Drawing.Icon(stream,32,32))trayIcon=(System.Drawing.Icon)icon.Clone();}
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("mon3tr.ico")){Icon=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);Icon.Freeze();}
            SourceInitialized+=(s,e)=>((HwndSource)PresentationSource.FromVisual(this)).AddHook(WindowHook);
            Loaded+=(s,e)=>{if(initialized)return;initialized=true;LoadSettings();if(preview){Directory.CreateDirectory(System.IO.Path.Combine(Program.Root,"qa"));RunPreview();}else Task.Run(async()=>{try{await usage.Warm();}catch{}});};
            MouseLeftButtonDown+=OnDown;MouseMove+=OnMove;MouseLeftButtonUp+=OnUp;LostMouseCapture+=(s,e)=>down=false;
            MouseRightButtonUp+=(s,e)=>{ShowMenu();e.Handled=true;};LocationChanged+=(s,e)=>PlaceBubble();
            edgeTab.RevealRequested+=Reveal;
            tray=new Forms.NotifyIcon{Icon=trayIcon,Text="Mon3tr · 双击头像显示桌宠",Visible=true};var menu=new Forms.ContextMenuStrip();
            menu.Items.Add("显示桌宠",null,(s,e)=>Dispatcher.Invoke(new Action(Reveal)));
            menu.Items.Add("找回到桌面",null,(s,e)=>Dispatcher.Invoke(new Action(ResetPosition)));
            menu.Items.Add("收起到托盘",null,(s,e)=>Dispatcher.Invoke(new Action(HideToTray)));tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>Dispatcher.Invoke(new Action(Reveal));
            Closing+=(s,e)=>{if(!AllowShutdown){e.Cancel=true;HideToTray();}};
            StateChanged+=(s,e)=>{if(WindowState==WindowState.Minimized){WindowState=WindowState.Normal;HideToTray();}};
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;
            Closed+=(s,e)=>{closing=true;StopAnimation();SaveSettings();bubble.Shutdown();edgeTab.Shutdown();tray.Dispose();trayIcon.Dispose();voices.Dispose();usage.Dispose();Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged;};
        }
        void ApplySize(){Width=assets.Width*scale;Height=assets.Height*scale;}
        void SetFrame(byte[] frame){current=frame;bitmap.WritePixels(frameRect,frame,assets.Width*4,0);}
        void StartAnimation(){StopAnimation();frameTimes.Clear();player.Reset();clock.Restart();playing=true;CompositionTarget.Rendering+=RenderFrame;}
        void StopAnimation(){CompositionTarget.Rendering-=RenderFrame;playing=false;clock.Stop();SetFrame(assets.Idle);}
        void RenderFrame(object sender,EventArgs e){
            double now=clock.Elapsed.TotalSeconds;if(!player.Advance(now))return;
            if(player.Finished){StopAnimation();if(preview)SavePerformance();return;}
            SetFrame(assets.Frames[player.Index]);if(preview)frameTimes.Add(now);
        }
        Point ToLogical(Point point){var src=PresentationSource.FromVisual(this);return src==null?point:src.CompositionTarget.TransformFromDevice.Transform(point);}
        Point CursorPosition(){var p=Forms.Cursor.Position;return ToLogical(new Point(p.X,p.Y));}
        Rect WorkArea(bool cursor=false){
            var body=BodyRect();var center=new Point(body.Left+body.Width/2,body.Top+body.Height/2);var src=PresentationSource.FromVisual(this);if(src!=null)center=src.CompositionTarget.TransformToDevice.Transform(center);
            var screen=cursor?Forms.Screen.FromPoint(Forms.Cursor.Position):Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X,(int)center.Y));var a=screen.WorkingArea;
            return new Rect(ToLogical(new Point(a.Left,a.Top)),ToLogical(new Point(a.Right,a.Bottom)));
        }
        Rect BodyRect(){return Layout.Body(new Point(Left,Top),assets.Body,scale);}
        void ClampPosition(Rect work){var p=Layout.ClampPet(new Point(Left,Top),assets.Body,scale,work);Left=p.X;Top=p.Y;}
        void LoadSettings(){
            var work=SystemParameters.WorkArea;Left=work.Right-assets.Body.Right*scale-24;Top=work.Bottom-assets.Body.Bottom*scale-12;
            try{if(File.Exists(SettingsPath)){
                var d=Json.Parse(File.ReadAllText(SettingsPath));scale=Layout.Clamp(Json.Number(d,"scale",.605),.30,1.2);ApplySize();Left=Json.Number(d,"left",Left);Top=Json.Number(d,"top",Top);Topmost=Json.Bool(d,"topmost",true);trayHidden=Json.Bool(d,"trayHidden",false);
                if(Json.Number(d,"version",1)<2){double center=Left+300*scale,foot=Top+544*scale;Left=center-(assets.Body.Left+assets.Body.Width/2)*scale;Top=foot-assets.Body.Bottom*scale;}
                dock=(Edge)(int)Layout.Clamp(Json.Number(d,"dock",0),0,4);
            }}catch{}
            ClampPosition(WorkArea());if(trayHidden)HideToTray();else if(dock!=Edge.None)Dock(dock,WorkArea());SaveSettings();
        }
        void SaveSettings(){if(preview)return;try{File.WriteAllText(SettingsPath,Json.Stringify(new{version=2,left=Left,top=Top,scale=scale,topmost=Topmost,dock=(int)dock,trayHidden=trayHidden}));}catch{}}
        void HideToTray(){trayHidden=true;dock=Edge.None;StopAnimation();voices.Stop();bubble.Dismiss(true);edgeTab.Hide();Hide();SaveSettings();}
        void ResetPosition(){trayHidden=false;dock=Edge.None;edgeTab.Hide();Show();var work=SystemParameters.WorkArea;Left=work.Right-assets.Body.Right*scale-24;Top=work.Bottom-assets.Body.Bottom*scale-12;SaveSettings();}
        void DisplayChanged(object s,EventArgs e){Dispatcher.BeginInvoke(new Action(()=>{if(dock!=Edge.None)Dock(dock,WorkArea());else ClampPosition(WorkArea());PlaceBubble();}));}
        void OnDown(object s,MouseButtonEventArgs e){var p=e.GetPosition(picture);if(!assets.Hit(current,p.X,p.Y,scale))return;down=true;moved=false;press=CursorPosition();origin=new Point(Left,Top);CaptureMouse();e.Handled=true;}
        void OnMove(object s,MouseEventArgs e){if(!down||e.LeftButton!=MouseButtonState.Pressed)return;var delta=CursorPosition()-press;if(delta.Length>4)moved=true;if(moved){Left=origin.X+delta.X;Top=origin.Y+delta.Y;}}
        void OnUp(object s,MouseButtonEventArgs e){
            if(!down)return;bool click=!moved;down=false;ReleaseMouseCapture();var work=WorkArea(true);ClampPosition(work);
            if(click)Interact();else{var touched=Layout.Touching(BodyRect(),work);if(touched!=Edge.None)Dock(touched,work);}SaveSettings();e.Handled=true;
        }
        void Dock(Edge side,Rect work){
            trayHidden=false;dock=side;StopAnimation();bubble.Dismiss(true);ClampPosition(work);
            if(side==Edge.Left)Left=work.Left-assets.Body.Left*scale;if(side==Edge.Right)Left=work.Right-assets.Body.Right*scale;
            if(side==Edge.Top)Top=work.Top-assets.Body.Top*scale;if(side==Edge.Bottom)Top=work.Bottom-assets.Body.Bottom*scale;
            edgeTab.Topmost=Topmost;edgeTab.Place(side,BodyRect(),work);Hide();edgeTab.ShowDock();SaveSettings();
        }
        void Reveal(){if(closing)return;trayHidden=false;dock=Edge.None;edgeTab.Hide();Show();ClampPosition(WorkArea());SaveSettings();}
        void Interact(){
            if(dock!=Edge.None)Reveal();StartAnimation();voices.Play();
            int request=++clickGeneration;bubble.Topmost=Topmost;bubble.SetText(usageText??"正在读取额度…",usageText==null?"连接已登录账号":"正在更新…");PlaceBubble();bubble.Appear();PlaceBubble();RefreshUsage(request);
        }
        async void RefreshUsage(int generation){
            try{string value=await Task.Run(()=>usage.Read());if(closing)return;usageSuccesses++;if(generation!=clickGeneration)return;usageText=value;lastUsage=DateTime.Now;bubble.SetText(value,"更新于 "+lastUsage.ToString("HH:mm:ss"));}
            catch{if(closing||generation!=clickGeneration)return;if(usageText==null)bubble.SetText("暂时无法读取额度","请检查 Codex 登录和网络");else bubble.SetText(usageText,"刷新失败 · 上次 "+lastUsage.ToString("HH:mm:ss"));}
            // Data may update a hidden card, but must never reopen it or extend its 5-second lifetime.
        }
        void PlaceBubble(){if(double.IsNaN(Left)||double.IsNaN(Top))return;bool left;var r=Layout.Bubble(BodyRect(),new Size(bubble.Width,bubble.Height),WorkArea(),out left);bubble.Left=r.Left;bubble.Top=r.Top;bubble.SetSide(left);}
        void ShowMenu(){var m=new ContextMenu();Add(m,"点击互动",Interact);Add(m,"缩小",()=>ResizePet(-.1));Add(m,"放大",()=>ResizePet(.1));Add(m,"恢复默认大小",()=>ResizePet(.605-scale));var top=new MenuItem{Header="始终置顶",IsCheckable=true,IsChecked=Topmost};top.Click+=(s,e)=>{Topmost=top.IsChecked;bubble.Topmost=Topmost;edgeTab.Topmost=Topmost;SaveSettings();};m.Items.Add(top);Add(m,"隐藏到最近边缘",()=>{var w=WorkArea();var p=BodyRect();var gaps=new[]{p.Left-w.Left,w.Right-p.Right,p.Top-w.Top,w.Bottom-p.Bottom};Dock((Edge)(Array.IndexOf(gaps,gaps.Min())+1),w);});m.Items.Add(new Separator());Add(m,"收起到托盘",HideToTray);m.IsOpen=true;}
        static void Add(ContextMenu m,string title,Action action){var item=new MenuItem{Header=title};item.Click+=(s,e)=>action();m.Items.Add(item);}
        void ResizePet(double amount){double foot=BodyRect().Bottom;scale=Layout.Clamp(scale+amount,.30,1.2);ApplySize();Top=foot-assets.Body.Bottom*scale;ClampPosition(WorkArea());PlaceBubble();SaveSettings();}
        IntPtr WindowHook(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled){if(msg==0x0084&&!down){long v=l.ToInt64();var p=PointFromScreen(new Point((short)(v&65535),(short)((v>>16)&65535)));if(!assets.Hit(current,p.X,p.Y,scale)){handled=true;return new IntPtr(-1);}}return IntPtr.Zero;}
        void RunPreview(){
            ResetPosition();Interact();string firstClock=bubble.ClockText;bool clockAdvanced=false;var action=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};action.Tick+=(s,e)=>{action.Stop();clockAdvanced=bubble.ClockText!=firstClock;SavePreview("v2-preview-action.png");};action.Start();
            var report=new DispatcherTimer{Interval=TimeSpan.FromSeconds(6)};report.Tick+=(s,e)=>{
                report.Stop();SavePreview("v2-preview.png");var original=new Point(Left,Top);var work=WorkArea();var checks=new List<string>();
                checks.Add("Bubble auto-closed: "+!bubble.IsVisible);checks.Add("Clock advanced while visible: "+clockAdvanced);checks.Add("Animation returned to idle: "+!playing);checks.Add("Live usage received: "+(usageSuccesses>0));checks.Add("Random voice selected: "+voices.LastIndex);
                foreach(Edge side in new[]{Edge.Left,Edge.Right,Edge.Top,Edge.Bottom}){Dock(side,work);checks.Add(side+" dock hidden: "+(!IsVisible&&edgeTab.IsVisible));Reveal();checks.Add(side+" restore: "+(IsVisible&&!edgeTab.IsVisible));Left=original.X;Top=original.Y;}
                Close();checks.Add("Normal close keeps process/tray and hides pet: "+(!IsVisible&&tray.Visible&&!closing));Reveal();checks.Add("Restore after normal close: "+IsVisible);
                WindowState=WindowState.Minimized;checks.Add("Minimize hides to tray: "+(!IsVisible&&tray.Visible));Reveal();checks.Add("Beijing greeting: "+BeijingClock.Greeting(BeijingClock.Now));checks.Add("Clock shown: "+bubble.ClockText);
                File.WriteAllLines(System.IO.Path.Combine(Program.Root,"qa/v3-interaction.txt"),checks);AllowShutdown=true;Close();
            };report.Start();
        }
        void SavePerformance(){var intervals=frameTimes.Zip(frameTimes.Skip(1),(a,b)=>b-a).ToArray();File.WriteAllText(System.IO.Path.Combine(Program.Root,"qa/v2-playback.json"),Json.Stringify(new{sourceFrames=assets.Frames.Length,presentedFrames=frameTimes.Count,sequenceComplete=frameTimes.Count==assets.Frames.Length,durationSeconds=clock.Elapsed.TotalSeconds,maximumFrameIntervalMs=intervals.Length>0?intervals.Max()*1000:0,meanFrameIntervalMs=intervals.Length>0?intervals.Average()*1000:0}));}
        void SavePreview(string name){
            var canvas=new Canvas{Width=580,Height=340,Background=new SolidColorBrush(Color.FromRgb(226,234,223))};var title=new TextBlock{Text="MON3TR / 小尺寸 · 60 FPS",FontSize=16,Foreground=Brushes.DarkSlateGray};Canvas.SetLeft(title,24);Canvas.SetTop(title,20);canvas.Children.Add(title);
            var image=new Image{Source=bitmap,Width=Width,Height=Height};Canvas.SetLeft(image,310);Canvas.SetTop(image,150);canvas.Children.Add(image);var card=bubble.CreateCard();Canvas.SetLeft(card,82);Canvas.SetTop(card,86);canvas.Children.Add(card);
            canvas.Measure(new Size(580,340));canvas.Arrange(new Rect(0,0,580,340));canvas.UpdateLayout();var target=new RenderTargetBitmap(580,340,96,96,PixelFormats.Pbgra32);target.Render(canvas);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(target));using(var stream=File.Create(System.IO.Path.Combine(Program.Root,"qa",name)))png.Save(stream);
        }
    }
    sealed class EdgeWindow:Window {
        bool armed,allowClose;public event Action RevealRequested;
        public EdgeWindow(){Title="Mon3tr 贴边标签";WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;ResizeMode=ResizeMode.NoResize;Topmost=true;Cursor=Cursors.Hand;ToolTip="Mon3tr 在这里 · 移入或点击恢复";Content=new Border{Background=new SolidColorBrush(Color.FromRgb(151,197,80)),CornerRadius=new CornerRadius(3),BorderBrush=new SolidColorBrush(Color.FromRgb(51,74,47)),BorderThickness=new Thickness(1)};MouseEnter+=(s,e)=>{if(armed&&RevealRequested!=null)RevealRequested();};MouseLeave+=(s,e)=>armed=true;MouseLeftButtonUp+=(s,e)=>{if(RevealRequested!=null)RevealRequested();};}
        public void Place(Edge side,Rect pet,Rect work){bool vertical=side==Edge.Left||side==Edge.Right;Width=vertical?7:34;Height=vertical?34:7;Left=vertical?(side==Edge.Left?work.Left:work.Right-Width):Layout.Clamp(pet.Left+pet.Width/2-Width/2,work.Left,work.Right-Width);Top=vertical?Layout.Clamp(pet.Top+pet.Height/2-Height/2,work.Top,work.Bottom-Height):(side==Edge.Top?work.Top:work.Bottom-Height);}
        public void ShowDock(){armed=false;Show();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(400)};timer.Tick+=(s,e)=>{timer.Stop();if(!IsMouseOver)armed=true;};timer.Start();}
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e){if(!allowClose){e.Cancel=true;Hide();}base.OnClosing(e);}
        public void Shutdown(){allowClose=true;Close();}
    }
    sealed class BubbleWindow:Window {
        readonly TextBlock body=new TextBlock(),status=new TextBlock(),greeting=new TextBlock(),beijingTime=new TextBlock();readonly Grid root=new Grid();readonly ScaleTransform scale=new ScaleTransform(1,1);readonly DispatcherTimer timeout=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};readonly DispatcherTimer timeTick=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};readonly Polygon tail=new Polygon();
        string text="正在读取额度…",note="";int epoch;bool allowClose;
        public string ClockText{get{return beijingTime.Text;}}
        static readonly Brush Green=new SolidColorBrush(Color.FromRgb(182,224,102)),Ink=new SolidColorBrush(Color.FromRgb(225,237,222)),Bg=new SolidColorBrush(Color.FromRgb(30,40,36));
        public BubbleWindow(){Title="Mon3tr 额度提示";Width=236;Height=180;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;
            root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(8)});root.Children.Add(BuildCard(body,status,greeting,beijingTime));tail.Points=new PointCollection{new Point(0,0),new Point(14,0),new Point(10,8)};tail.Fill=Bg;tail.Width=14;tail.Height=8;Grid.SetRow(tail,1);root.Children.Add(tail);root.RenderTransform=scale;Content=root;timeout.Tick+=(s,e)=>Dismiss(false);SetSide(true);
            timeTick.Tick+=(s,e)=>UpdateTime();IsVisibleChanged+=(s,e)=>{if(IsVisible){UpdateTime();timeTick.Start();}else timeTick.Stop();};Closed+=(s,e)=>{timeTick.Stop();timeout.Stop();};UpdateTime();
        }
        void UpdateTime(){var now=BeijingClock.Now;greeting.Text=BeijingClock.Greeting(now);beijingTime.Text="北京时间  "+now.ToString("MM-dd HH:mm:ss");}
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e){if(!allowClose){e.Cancel=true;Dismiss(true);}base.OnClosing(e);}
        public void Shutdown(){allowClose=true;Close();}
        public void SetSide(bool left){tail.HorizontalAlignment=left?HorizontalAlignment.Right:HorizontalAlignment.Left;tail.Margin=left?new Thickness(0,0,18,0):new Thickness(18,0,0,0);root.RenderTransformOrigin=new Point(left?.88:.12,1);}
        public void SetText(string value,string statusText){text=value;note=statusText;body.Text=value;status.Text=statusText;}
        public void Appear(){epoch++;timeout.Stop();Show();var easing=new QuadraticEase{EasingMode=EasingMode.EaseOut};scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.65,1,TimeSpan.FromMilliseconds(170)){EasingFunction=easing});scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.65,1,TimeSpan.FromMilliseconds(170)){EasingFunction=easing});root.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(140)));timeout.Start();}
        public void Dismiss(bool immediate){timeout.Stop();int ticket=++epoch;if(immediate||!IsVisible){Hide();return;}var easing=new QuadraticEase{EasingMode=EasingMode.EaseIn};var close=new DoubleAnimation(1,.55,TimeSpan.FromMilliseconds(170)){EasingFunction=easing};close.Completed+=(s,e)=>{if(ticket==epoch)Hide();};scale.BeginAnimation(ScaleTransform.ScaleXProperty,close);scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,.55,TimeSpan.FromMilliseconds(170)){EasingFunction=easing});root.BeginAnimation(OpacityProperty,new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(170)));}
        public Border CreateCard(){UpdateTime();var b=new TextBlock{Text=text};var s=new TextBlock{Text=note};var card=BuildCard(b,s,new TextBlock{Text=greeting.Text},new TextBlock{Text=beijingTime.Text});card.Width=236;card.Height=172;return card;}
        Border BuildCard(TextBlock content,TextBlock footer,TextBlock greet,TextBlock time){
            var card=new Border{Background=Bg,BorderBrush=new SolidColorBrush(Color.FromRgb(90,113,68)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(13,9,13,9)};var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new DockPanel{Margin=new Thickness(0,0,0,4)};var close=new TextBlock{Text="×",Foreground=Ink,FontSize=15,Cursor=Cursors.Hand};close.MouseLeftButtonUp+=(s,e)=>Dismiss(false);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);header.Children.Add(new TextBlock{Text="MON3TR",Foreground=Green,FontSize=12,FontWeight=FontWeights.Bold});grid.Children.Add(header);
            greet.Foreground=Green;greet.FontSize=12;greet.Margin=new Thickness(0,0,0,2);Grid.SetRow(greet,1);grid.Children.Add(greet);time.Foreground=Ink;time.FontSize=10;time.Margin=new Thickness(0,0,0,7);Grid.SetRow(time,2);grid.Children.Add(time);
            content.FontFamily=new FontFamily("Microsoft YaHei UI");content.FontSize=11;content.LineHeight=18;content.Foreground=Ink;content.TextWrapping=TextWrapping.Wrap;var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,3);grid.Children.Add(scroll);footer.Foreground=new SolidColorBrush(Color.FromRgb(157,177,155));footer.FontSize=9;footer.Margin=new Thickness(0,4,0,0);footer.TextWrapping=TextWrapping.Wrap;Grid.SetRow(footer,4);grid.Children.Add(footer);card.Child=grid;return card;
        }
    }
}
