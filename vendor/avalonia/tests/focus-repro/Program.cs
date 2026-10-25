using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
partial class Program {
 internal static TextBox[] Editors=[];
 [JSExport] public static string Read() => string.Join("|",System.Array.ConvertAll(Editors,e=>(e.Text??"").Replace("\n","[LF]")));
 [JSExport] public static void Focus(int index) => Editors[index].Focus();
 [JSExport] public static void Seed(int index,string text,int start,int end) {var e=Editors[index];e.Focus();e.Text=text;e.SelectionStart=start;e.SelectionEnd=end;}
 [JSExport] public static void Flags(int index,bool readOnly,bool enabled) {Editors[index].IsReadOnly=readOnly;Editors[index].IsEnabled=enabled;}
 [JSExport] public static void Clear() {foreach(var e in Editors)e.Text="";}
 static Task Main(string[] args) => AppBuilder.Configure<TestApp>().StartBrowserAppAsync("out");
}
class TestApp:Application {
 public override void Initialize()=>Styles.Add(new FluentTheme());
 public override void OnFrameworkInitializationCompleted(){
  var a=new TextBox{Watermark="Synthetic username",Width=280};
  var b=new TextBox{Watermark="Synthetic password",PasswordChar='*',Width=280};
  var c=new TextBox{Watermark="General multiline",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Width=140,Height=130};
  Program.Editors=[a,b,c];
  if(ApplicationLifetime is ISingleViewApplicationLifetime s)s.MainView=new StackPanel{Margin=new Thickness(20),Spacing=20,Children={a,b,c}};
  base.OnFrameworkInitializationCompleted();
 }
}
