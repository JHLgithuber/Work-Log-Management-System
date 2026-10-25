using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;
using WorkLogManagementSystem_UI.Models;
using WorkLogManagementSystem_UI.Services;
using WorkLogManagementSystem_UI.ViewModels;

static class RefreshReuseChecks
{
    public static async Task Run()
    {
        var child = new TaskDto { Id=3,ParentId=1,Title="Child",Entries=new(){new WorkEntryDto{Id=7,TaskId=3,EntryDate=new DateOnly(2099,1,10),PerformedContent="old"}}};
        var rows = new List<TaskDto> { new(){Id=1,Title="Parent",Children=new(){child}},new(){Id=2,Title="Sibling"} };
        var handler=new FakeHandler(req=>Task.FromResult(req.RequestUri!.AbsolutePath=="/tasks"
            ?new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(rows),System.Text.Encoding.UTF8,"application/json")}:FakeHandler.Default(req)));
        var model=new MainViewModel((url,token)=>new WorkLogApiClient(url,token,handler:handler),false) {
            SelectedDate=new DateTime(2099,1,10),ApiBaseUrl="https://test.invalid",AccessToken="synthetic",IsBackendConnected=true,IsAuthenticated=true};
        await model.LoadTasksCommand.ExecuteAsync(null);
        var collection=model.Tasks;var parent=model.Tasks[0];var sibling=model.Tasks[1];var oldChild=parent.Children[0];
        parent.IsExpanded=true;model.SelectedTask=oldChild;
        await model.LoadTasksCommand.ExecuteAsync(null);
        Check(ReferenceEquals(collection,model.Tasks)&&ReferenceEquals(parent,model.Tasks[0])&&ReferenceEquals(oldChild,parent.Children[0]),"Unchanged tree recreated");
        child.Entries[0].PerformedContent="new";
        await model.LoadTasksCommand.ExecuteAsync(null);
        Check(!ReferenceEquals(parent,model.Tasks[0])&&!ReferenceEquals(oldChild,model.Tasks[0].Children[0]),"Changed entry did not use full replacement fallback");
        Check(model.SelectedTask?.Id==3&&model.EditorPerformedContent=="new"&&model.Tasks[0].IsExpanded,"Entry,selection,or expansion stale");
        model.EditorPerformedContent="unsaved draft";child.Entries[0].PerformedContent="remote";
        await model.LoadTasksCommand.ExecuteAsync(null);
        Check(model.EditorPerformedContent=="unsaved draft"&&model.HasUnsavedTaskChanges,"Refresh lost draft");
        var beforeSort=model.Tasks;rows[1].Priority=1;await model.LoadTasksCommand.ExecuteAsync(null);
        Check(!ReferenceEquals(beforeSort,model.Tasks)&&model.Tasks[0].Id==2&&model.SelectedTask?.Id==3,
            "Reordering failed structural fallback or lost selection");
        rows[1].Priority=0;await model.LoadTasksCommand.ExecuteAsync(null);
        Check(model.Tasks[0].Id==1&&model.EditorPerformedContent=="unsaved draft", "Reordering lost draft");
        // Each persisted field participates in equality: a fresh object must carry its value.
        foreach(var field in new[]{"ParentId","Title","Content","Priority","StartAt","DueAt","ActualEndAt","CreatedAt","UpdatedAt"})
        {
            var target=rows[1];var old=model.Tasks.Single(x=>x.Id==2);var p=typeof(TaskDto).GetProperty(field)!;
            object value=p.PropertyType==typeof(string)?"changed-"+field:
                p.PropertyType==typeof(int)||p.PropertyType==typeof(int?)?42:
                new DateTimeOffset(2099,2,3,4,5,6,TimeSpan.Zero);
            p.SetValue(target,value);await model.LoadTasksCommand.ExecuteAsync(null);
            var fresh=model.Tasks.Single(x=>x.Id==2);
            Check(!ReferenceEquals(old,fresh)&&Equals(p.GetValue(fresh),value),"Ignored field "+field);
        }
        var before=model.Tasks;rows.Add(new TaskDto{Id=4,Title="Added"});await model.LoadTasksCommand.ExecuteAsync(null);
        Check(!ReferenceEquals(before,model.Tasks)&&model.Tasks.Any(t=>t.Id==4),"Addition did not use structural fallback");
        before=model.Tasks;rows.RemoveAt(0);await model.LoadTasksCommand.ExecuteAsync(null);
        Check(!ReferenceEquals(before,model.Tasks)&&model.Tasks.All(t=>t.Id!=1),"Deletion stale");
    }
    public static async Task DateContext()
    {
        var handler=new FakeHandler(req=>Task.FromResult(req.RequestUri!.AbsolutePath=="/tasks"
            ?new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("[{\"id\":1,\"title\":\"same\"}]",System.Text.Encoding.UTF8,"application/json")}:FakeHandler.Default(req)));
        var model=new MainViewModel((url,token)=>new WorkLogApiClient(url,token,handler:handler),false) {
            SelectedDate=new DateTime(2099,1,10),ApiBaseUrl="https://test.invalid",AccessToken="synthetic",IsBackendConnected=true,IsAuthenticated=true};
        await model.LoadTasksCommand.ExecuteAsync(null);var before=model.Tasks;
        model.SelectedDate=new DateTime(2099,1,11);await model.LoadTasksCommand.ExecuteAsync(null);
        Check(!ReferenceEquals(before,model.Tasks), "Different date reused old snapshot context");
    }
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
}
