using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WorkLogManagementSystem_UI.Models;
using WorkLogManagementSystem_UI.Services;
using WorkLogManagementSystem_UI.ViewModels;
using WorkLogManagementSystem_UI.Views;

static class LargeListChecks
{
    public static int Run()
    {
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        Application.Current!.Resources["AppFontFamily"] = FontFamily.Default;
        Application.Current.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        foreach (int count in new[] { 200, 1000 })
        {
            var data = Enumerable.Range(1,count).Select(i=>new TaskDto {Id=i,Title=$"Synthetic {i:0000}",Content="합성 본문",StartAt=DateTimeOffset.MinValue,DueAt=DateTimeOffset.MaxValue}).ToList();
            string payload=JsonSerializer.Serialize(data);
            var handler=new FakeHandler(req=>Task.FromResult(req.RequestUri!.AbsolutePath=="/tasks"
                ?new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(payload,System.Text.Encoding.UTF8,"application/json")}:FakeHandler.Default(req)));
            var model=new MainViewModel((url,token)=>new WorkLogApiClient(url,token,handler:handler),false) {
                SelectedDate=new DateTime(2099,1,10), ApiBaseUrl="https://test.invalid",AccessToken="synthetic",IsBackendConnected=true,IsAuthenticated=true};
            var view=new MainView{DataContext=model};var window=new Window{Content=view,Width=1180,Height=760};window.Show();Pump(window);
            var tree=view.GetVisualDescendants().OfType<TreeView>().Single();
            void Reload(string name)
            {
                var prior=tree.GetVisualDescendants().OfType<TreeViewItem>().ToHashSet();
                var before=GC.GetTotalAllocatedBytes(true);var sw=Stopwatch.StartNew();
                var work=model.LoadTasksCommand.ExecuteAsync(null);
                while(!work.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}work.GetAwaiter().GetResult();Pump(window);sw.Stop();
                var now=tree.GetVisualDescendants().OfType<TreeViewItem>().ToArray();
                Console.WriteLine(JsonSerializer.Serialize(new {count,scenario=name,ms=sw.Elapsed.TotalMilliseconds,allocated=GC.GetTotalAllocatedBytes(true)-before,realized=now.Length,reused=now.Count(prior.Contains),selected=model.SelectedTask?.Id}));
                if(model.Tasks.Count!=data.Count)throw new Exception("Task count mismatch");
            }
            Reload("initial"); model.SelectedTask=model.Tasks[0];Pump(window);
            for(int i=0;i<3;i++)Reload("unchanged-"+i);
            data[50].Content="변경된 합성 본문";payload=JsonSerializer.Serialize(data);Reload("one-change");
            if(model.Tasks.Single(t=>t.Id==51).Content!=data[50].Content)throw new Exception("Stale task content");
            data.Add(new TaskDto{Id=count+1,Title="New",StartAt=DateTimeOffset.MinValue,DueAt=DateTimeOffset.MaxValue});payload=JsonSerializer.Serialize(data);Reload("addition");
            var scroll=tree.GetVisualDescendants().OfType<ScrollViewer>().First();
            var sw2=Stopwatch.StartNew();for(int i=0;i<20;i++){scroll.Offset=new Vector(0,i*20);Pump(window);}sw2.Stop();
            Console.WriteLine(JsonSerializer.Serialize(new{count,scenario="20-offset-changes",ms=sw2.Elapsed.TotalMilliseconds}));window.Close();
        }
        return 0;
    }
    static void Pump(Window w){w.UpdateLayout();Dispatcher.UIThread.RunJobs();w.UpdateLayout();}
}
