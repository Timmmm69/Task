using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests;

public class MainWindowViewModelTests
{
    [Fact]
    public void Sections_ArePopulated_WithAtLeastEight()
    {
        var vm = new MainWindowViewModel();

        Assert.NotNull(vm.Sections);
        Assert.NotEmpty(vm.Sections);
        Assert.True(vm.Sections.Count >= 8, "The shell must expose at least eight navigation sections.");
    }

    [Fact]
    public void SelectedSection_DefaultsToFirst()
    {
        var vm = new MainWindowViewModel();

        Assert.NotNull(vm.Sections);
        Assert.Same(vm.Sections[0], vm.SelectedSection);
    }

    [Fact]
    public void ChangingSelectedSection_RaisesPropertyChanged()
    {
        var vm = new MainWindowViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var target = vm.Sections[^1];
        vm.SelectedSection = target;

        Assert.Contains(nameof(MainWindowViewModel.SelectedSection), raised);
        Assert.Same(target, vm.SelectedSection);
    }

    [Fact]
    public void EverySection_HasNonEmptyTitleAndPlaceholder()
    {
        var vm = new MainWindowViewModel();

        foreach (var section in vm.Sections)
        {
            Assert.False(string.IsNullOrWhiteSpace(section.Title),
                "Section title must be non-empty and Russian.");
            Assert.False(string.IsNullOrWhiteSpace(section.PlaceholderText),
                "Section placeholder must be non-empty.");
        }
    }

    [Fact]
    public void EverySection_HasUniqueNonEmptyRoute()
    {
        var vm = new MainWindowViewModel();
        var routes = vm.Sections.Select(section => section.Route).ToArray();

        Assert.All(routes, route => Assert.False(string.IsNullOrWhiteSpace(route)));
        Assert.Equal(routes.Length, routes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AssigningSameSelectedSection_DoesNotRaisePropertyChanged()
    {
        var vm = new MainWindowViewModel();
        var raisedCount = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedSection))
            {
                raisedCount++;
            }
        };

        vm.SelectedSection = vm.SelectedSection;

        Assert.Equal(0, raisedCount);
    }
}
