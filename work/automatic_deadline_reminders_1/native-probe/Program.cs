using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Npgsql;
using Task.Application;
using Task.Application.ProductData;
using Task.Application.Security;
using Task.Desktop.Notifications;
using Task.Desktop.Personal;
using Task.Desktop.TaskApi;
using Task.Desktop.Work;
using Task.Domain;
using Task.Infrastructure.Persistence;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var mode=args[0]; var directory=Path.GetFullPath(args[1]); Directory.CreateDirectory(directory);
        var app=new System.Windows.Application();
        var panel=new StackPanel { Margin=new Thickness(24) };
        var status=new TextBlock { Text="Проверка сохранения задачи → напоминания → центра Task → Windows", TextWrapping=TextWrapping.Wrap };
        var start=new Button { Content="Запустить проверку",Margin=new Thickness(0,16,0,16),Height=40 };
        panel.Children.Add(status); panel.Children.Add(start);
        var window=new Window { Title="Task — автонапоминание · "+mode, Width=620, Height=300, Content=panel };
        IDisposable? presenter=null;
        window.Closed+=(_,_)=>presenter?.Dispose();
        start.Click+=async (_,_)=>
        {
            start.IsEnabled=false;
            try
            {
                object evidence;
                if(mode=="personal")
                {
                    using var store=new PersonalTaskStore(new PersonalDataPaths(Path.Combine(directory,"data")));
                    var task=store.Create(new("Автоматическое напоминание · личный режим",DesktopTaskPriority.Normal,deadlineAtUtc:DateTimeOffset.UtcNow.AddMinutes(10)));
                    var count=store.ReconcileReminders(); var notification=store.ClaimPresentations().Single();
                    var tray=new PersonalWindowsNotifications(window,tip:"Task · тест автонапоминания"); presenter=tray;
                    var accepted=tray.Submit(notification,sound:false); store.CompletePresentation(notification.Id,accepted);
                    evidence=new { mode,taskId=task.Id,ruleCount=store.Reminders().Count,notificationCount=count,windowsRequestAccepted=accepted,visualBannerConfirmed=false };
                }
                else evidence=await Corporate(window,directory,p=>presenter=p);
                File.WriteAllText(Path.Combine(directory,"evidence.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions { WriteIndented=true }));
                status.Text=JsonSerializer.Serialize(evidence,new JsonSerializerOptions { WriteIndented=true });
            }
            catch(Exception e) { status.Text=e.ToString(); File.WriteAllText(Path.Combine(directory,"error.txt"),e.ToString()); }
        };
        app.Run(window);
    }
    private static async System.Threading.Tasks.Task<object> Corporate(Window window,string directory,Action<IDisposable> presenter)
    {
        var connection=Environment.GetEnvironmentVariable("TASK_POSTGRES_TEST_ADMIN_CONNECTION")!;
        var name="task_native_deadline_"+Guid.NewGuid().ToString("N");
        using var admin=NpgsqlDataSource.Create(connection); Sql(admin,"CREATE DATABASE "+name);
        try
        {
            var runtimeConnection=new NpgsqlConnectionStringBuilder(connection) { Database=name }.ConnectionString;
            using var runtime=new TaskPersistenceRuntime(runtimeConnection); using var db=NpgsqlDataSource.Create(runtimeConnection);
            await runtime.CreateMigrator().ApplyPendingAsync();
            var org=Guid.NewGuid(); var user=Guid.NewGuid();var profile=Guid.NewGuid();var device=Guid.NewGuid();var session=Guid.NewGuid();
            Sql(db,"INSERT INTO core.organizations(id,code,name,default_time_zone) VALUES($1,$2,'Native deadline fixture','UTC')",org,org.ToString("N"));
            Sql(db,"INSERT INTO core.objects(id,organization_id,object_type,created_at,created_by,updated_at,updated_by) VALUES($1,$4,'employee_profile',clock_timestamp(),$2,clock_timestamp(),$2),($2,$4,'user_account',clock_timestamp(),$2,clock_timestamp(),$2),($3,$4,'device',clock_timestamp(),$2,clock_timestamp(),$2)",profile,user,device,org);
            Sql(db,"INSERT INTO org.employee_profiles(id,organization_id,first_name,last_name,display_name,preferred_time_zone) VALUES($1,$2,'Deadline','Fixture','Deadline Fixture','UTC')",profile,org);
            Sql(db,"INSERT INTO iam.user_accounts(id,organization_id,employee_profile_id,login,password_hash,password_parameters) VALUES($1,$2,$3,$4,$5,'{}')",user,org,profile,user.ToString("N"),new string('h',64));
            Sql(db,"INSERT INTO iam.user_roles(user_account_id,role_id) SELECT $1,id FROM iam.roles WHERE organization_id=$2 AND code='system_employee'",user,org);
            Sql(db,"INSERT INTO iam.devices(id,organization_id,user_account_id,device_fingerprint_hash) VALUES($1,$2,$3,$4)",device,org,user,device.ToString("N"));
            Sql(db,"INSERT INTO iam.sessions(id,organization_id,user_account_id,device_id,credential_version,authorization_scope_version,idle_expires_at,absolute_expires_at) VALUES($1,$2,$3,$4,1,1,clock_timestamp()+interval '1 day',clock_timestamp()+interval '2 days')",session,org,user,device);
            var service=new TaskCreateCommandService(runtime.CreateTaskWriteCommandExecutor());
            var context=new AuthenticatedRequestContext(user,session,org,1,1,Guid.NewGuid().ToString(),"native-deadline");
            var command=service.CreateCommand(context,"native-deadline-create", "{\"title\":\"Native deadline\"}",new("Автоматическое напоминание · компания",TaskPriority.Normal,true,null,DateTimeOffset.UtcNow.AddMinutes(10)),
                t=>new(201,new Dictionary<string,string>(),JsonSerializer.Serialize(new { id=t.Metadata.Id,version=t.Metadata.Version }),t.Metadata.Id));
            await service.ExecuteAsync(command); await service.ExecuteAsync(command);
            var worker=runtime.CreateBackgroundDeliveryStore();await worker.MaterializeDueRemindersAsync(100);
            var delivery=(await worker.ClaimReminderDeliveriesAsync("native-probe",Guid.NewGuid(),TimeSpan.FromMinutes(1),100)).Single();await worker.DeliverReminderAsync(delivery);
            var route=ProductApiRoutes.All.First(r=>r.Resource=="notifications"&&r.Operation=="list");
            var response=runtime.CreateProductApiStore().Execute(new(org,user,session,Guid.NewGuid(),route,null,null,new JsonObject(),new Dictionary<string,string>(),null,null,"native-read",new HashSet<string> { "Notification.ReadOwn","Settings.ReadOwn" },RevalidatePermissions:true));
            var row=response.Body!["items"]!.AsArray().Single()!;
            var item=new DesktopNotification(Guid.Parse(row["id"]!.ToString()),row["version"]!.GetValue<long>(),"reminder",row["title"]!.ToString(),row["body"]!.ToString(),"info",row["status"]!.ToString(),command.TaskId,
                DateTimeOffset.Parse(row["notBefore"]!.ToString()),ReminderId:delivery.ReminderId,DeliveredAt:DateTimeOffset.Parse(row["deliveredAt"]!.ToString()));
            using var journal=new NotificationPresentationJournal(Path.Combine(directory,"journal"),"native-fixture"); journal.InitializeBaseline([]);
            var client=DispatchProxy.Create<IDesktopWorkApiClient,Api>();((Api)client).Item=item;
            var toast=new WindowsToastPresenter(window.Dispatcher,(_,_)=>{});presenter(toast);
            var tracked=new Tracked(toast);
            using var agent=new CorporateNotificationAgent(client,tracked,journal,()=>true,()=>true,_=>{},canSnooze:()=>true);
            await agent.RunPassAsync();await agent.RunPassAsync();
            return new { mode="corporate",taskId=command.TaskId,ruleCount=1,notificationCount=response.Body["items"]!.AsArray().Count,windowsRequestAccepted=tracked.Accepted,windowsSubmissionCount=tracked.Count,visualBannerConfirmed=false };
        }
        finally { Sql(admin,"DROP DATABASE "+name+" WITH (FORCE)"); }
    }
    private static void Sql(NpgsqlDataSource db,string sql,params object[] parameters)
    { using var c=db.CreateCommand(sql);foreach(var p in parameters)c.Parameters.AddWithValue(p);c.ExecuteNonQuery(); }
    public class Api:DispatchProxy
    {
        public DesktopNotification Item=null!;
        protected override object? Invoke(MethodInfo? m,object?[]? a)=>m!.Name switch
        {
            nameof(IDesktopWorkApiClient.GetNotificationsAsync)=>System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopNotification>>>(new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.Succeeded([Item])),
            nameof(IDesktopWorkApiClient.GetNotificationPreferencesAsync)=>System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopNotificationPreferences>>(new DesktopWorkResult<DesktopNotificationPreferences>.Succeeded(new(1,true,true,false,15,null,null,null))),
            _=>throw new InvalidOperationException(m.Name)
        };
    }
    private sealed class Tracked(IWindowsNotificationPresenter inner):IWindowsNotificationPresenter
    { public bool Accepted;public int Count;public bool Submit(WindowsNotification n) {Count++;return Accepted=inner.Submit(n);}public void Clear()=>inner.Clear();public void Reconcile(IReadOnlySet<Guid> ids)=>inner.Reconcile(ids); }
}
