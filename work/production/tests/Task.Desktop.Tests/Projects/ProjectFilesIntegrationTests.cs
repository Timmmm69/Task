using Task.Desktop.Projects;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Projects;

public sealed partial class ProjectsViewModelTests
{
    [Fact]
    public async AsyncTask FilesHost_LoadsOnOpen_PropagatesConfirmedVersionWithoutOverwritingEditorDraft()
    {
        var client = SectionClient(); var work = new ProjectFilesTests.Client();
        var files = ProjectFilesTests.Create(work, Project());
        using var vm = new ProjectsViewModel(client, User, ProjectFilesTests.Caps, files: files);
        vm.Activate(); await vm.CurrentDetailsOperation;
        Assert.Equal(0, work.ReadCalls);
        vm.SelectedItem!.SetPeopleNames("Владелец", "Руководитель");
        await vm.EditProjectCommand.ExecuteAsync(); var capturedVersion = vm.Editor!.Source!.Version;
        vm.Editor.Name = "Несохранённый черновик";
        await vm.OpenFilesSectionAsync(); await ProjectFilesTests.SelectTarget(files);
        work.Rows.Add(work.Link(ProjectId));
        work.Write = () => AsyncTask.FromResult<DesktopWorkResult<DesktopObjectLinks>>(new DesktopWorkResult<DesktopObjectLinks>.Succeeded(new(work.Rows.ToArray(), 19)));
        await files.AddCommand.ExecuteAsync();
        Assert.Equal("Владелец", vm.SelectedItem!.OwnerText);
        Assert.Equal(19, vm.SelectedItem!.Source.Version); Assert.Equal(capturedVersion, vm.Editor.Source.Version);
        Assert.Equal("Несохранённый черновик", vm.Editor.Name); Assert.Same(files.Section, vm.FilesSection);
    }

    [Theory]
    [InlineData("scope")][InlineData("session")][InlineData("offline")][InlineData("deactivate")][InlineData("close")][InlineData("switch")]
    public async AsyncTask FilesHost_RejectsLateResponsesOnHostContextChanges(string transition)
    {
        var client = SectionClient(); var work = new ProjectFilesTests.Client();
        var gate = new System.Threading.Tasks.TaskCompletionSource<DesktopWorkResult<DesktopObjectLinks>>();
        using var vm = new ProjectsViewModel(client, User, ProjectFilesTests.Caps, files: ProjectFilesTests.Create(work, Project()));
        vm.Activate(); await vm.CurrentDetailsOperation;
        work.Read = (_, _) => gate.Task;
        var loading = vm.OpenFilesSectionAsync();
        switch (transition)
        {
            case "scope": vm.UpdateCapabilities(ProjectFilesTests.Caps); break;
            case "session": vm.UpdateSessionState(false); break;
            case "offline": vm.UpdateConnectivity(false); break;
            case "deactivate": vm.Deactivate(); break;
            case "close": vm.SelectedItem = null; break;
            case "switch": vm.SelectedItem = new ProjectItemViewModel(Project() with { Id = Guid.NewGuid() }); break;
        }
        gate.SetResult(new DesktopWorkResult<DesktopObjectLinks>.Succeeded(new([work.Link(ProjectId)], 5)));
        await loading; Assert.Empty(vm.Files!.Items); Assert.Equal(0, work.MetadataCalls);
    }

    [Fact]
    public async AsyncTask FilesHost_ProjectForbiddenNeverReadsLinks()
    {
        var client = SectionClient(); client.ProjectHandler = (_, _) => AsyncTask.FromResult<DesktopProjectResult<DesktopProjectDto>>(new DesktopProjectResult<DesktopProjectDto>.Forbidden());
        var work = new ProjectFilesTests.Client();
        using var vm = new ProjectsViewModel(client, User, ProjectFilesTests.Caps, files: ProjectFilesTests.Create(work, Project()));
        vm.Activate(); await vm.CurrentDetailsOperation; await vm.OpenFilesSectionAsync();
        Assert.Null(vm.SelectedItem); Assert.Equal(0, work.ReadCalls);
    }
}
