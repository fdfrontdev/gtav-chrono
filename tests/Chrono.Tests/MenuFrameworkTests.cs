using Chrono.Application;

namespace Chrono.Tests;

public class MenuFrameworkTests
{
    private static MenuScreen BuildScreen()
    {
        return new MenuScreen
        {
            Title = "TEST",
            Items = new[]
            {
                new MenuItem { Title = "A", OnActivate = () => { } },
                new MenuItem { Title = "B", OnActivate = () => { } },
                new MenuItem { Title = "C", OnActivate = () => { } }
            }
        };
    }

    [Fact]
    public void Open_ThenIsOpen_True()
    {
        var menu = new MenuFramework(new FakeRenderer());
        menu.Open(BuildScreen());
        Assert.True(menu.IsOpen);
    }

    [Fact]
    public void Close_ThenIsOpen_False()
    {
        var menu = new MenuFramework(new FakeRenderer());
        menu.Open(BuildScreen());
        menu.Close();
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void NavigateDown_WrapsAround()
    {
        var menu = new MenuFramework(new FakeRenderer());
        var screen = BuildScreen();
        menu.Open(screen);

        menu.NavigateDown();
        Assert.Equal(1, screen.SelectedIndex);
        menu.NavigateDown();
        Assert.Equal(2, screen.SelectedIndex);
        menu.NavigateDown(); // wrap to 0
        Assert.Equal(0, screen.SelectedIndex);
    }

    [Fact]
    public void NavigateUp_WrapsAround()
    {
        var menu = new MenuFramework(new FakeRenderer());
        var screen = BuildScreen();
        menu.Open(screen);

        menu.NavigateUp(); // wrap to last
        Assert.Equal(2, screen.SelectedIndex);
    }

    [Fact]
    public void Accept_FiresActivateOnce()
    {
        int fired = 0;
        var menu = new MenuFramework(new FakeRenderer());
        var screen = new MenuScreen
        {
            Title = "T",
            Items = new[] { new MenuItem { Title = "X", OnActivate = () => fired++ } }
        };
        menu.Open(screen);

        menu.Accept();
        menu.Accept(); // same item — but each Accept is a separate user press
        Assert.Equal(2, fired);
    }

    [Fact]
    public void Accept_WithSubmenu_PushesScreen()
    {
        var sub = new MenuScreen { Title = "SUB", Items = Array.Empty<MenuItem>() };
        var menu = new MenuFramework(new FakeRenderer());
        var root = new MenuScreen
        {
            Title = "ROOT",
            Items = new[] { new MenuItem { Title = "Go", Submenu = sub } }
        };
        menu.Open(root);

        menu.Accept();

        Assert.Equal("SUB", menu.CurrentScreen!.Title);
    }

    [Fact]
    public void NavigateBack_AtRoot_Closes()
    {
        var menu = new MenuFramework(new FakeRenderer());
        menu.Open(BuildScreen());
        menu.NavigateBack();
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void NavigateBack_InSubmenu_ReturnsToRoot()
    {
        var sub = new MenuScreen { Title = "SUB", Items = Array.Empty<MenuItem>() };
        var menu = new MenuFramework(new FakeRenderer());
        var root = new MenuScreen
        {
            Title = "ROOT",
            Items = new[] { new MenuItem { Title = "Go", Submenu = sub } }
        };
        menu.Open(root);
        menu.Accept(); // into SUB

        menu.NavigateBack();

        Assert.Equal("ROOT", menu.CurrentScreen!.Title);
    }

    [Fact]
    public void AdjustValue_InvokesOnAdjustWithDirection()
    {
        int? received = null;
        var menu = new MenuFramework(new FakeRenderer());
        var screen = new MenuScreen
        {
            Title = "T",
            Items = new[] { new MenuItem { Title = "Range", OnAdjust = d => received = d } }
        };
        menu.Open(screen);

        menu.AdjustValue(-1);

        Assert.Equal(-1, received);
    }

    [Fact]
    public void Render_DelegatesToRenderer()
    {
        var renderer = new FakeRenderer();
        var menu = new MenuFramework(renderer);
        menu.Open(BuildScreen());

        menu.Render();

        Assert.Equal(1, renderer.RenderCount);
    }

    [Fact]
    public void Render_WhenClosed_DoesNothing()
    {
        var renderer = new FakeRenderer();
        var menu = new MenuFramework(renderer);
        menu.Render();
        Assert.Equal(0, renderer.RenderCount);
    }
}
