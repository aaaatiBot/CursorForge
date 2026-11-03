using CursorForge.Client.Services;
using CursorForge.Client.ViewModels;
using FluentAssertions;
using Moq;

namespace CursorForge.Tests.Unit;

public class NavigationTests
{
    private readonly Mock<IStructuredLogger> _loggerMock;
    private readonly Mock<ILocalStorageService> _storageMock;
    private readonly MainViewModel _mainViewModel;

    public NavigationTests()
    {
        _loggerMock = new Mock<IStructuredLogger>();
        _storageMock = new Mock<ILocalStorageService>();

        var themeMgrMock = new Mock<IThemeManagerService>();
        themeMgrMock.Setup(m => m.LoadAllThemes()).Returns([]);
        var cursorServiceMock = new Mock<ISystemCursorService>();
        var libraryVM = new LibraryViewModel(themeMgrMock.Object, cursorServiceMock.Object, _loggerMock.Object);
        var supabaseMock = new Mock<CursorForge.Client.Services.Supabase.ISupabaseClientService>();
        supabaseMock.Setup(s => s.IsAuthenticated).Returns(true);
        var browseVM = new BrowseViewModel(supabaseMock.Object, _loggerMock.Object);
        var uploadVM = new UploadViewModel(supabaseMock.Object, _loggerMock.Object);
        var myCursorsVM = new MyCursorsViewModel(supabaseMock.Object, _loggerMock.Object);
        var accountVM = new AccountViewModel(supabaseMock.Object, _loggerMock.Object);

        _mainViewModel = new MainViewModel(
            _loggerMock.Object,
            _storageMock.Object,
            libraryVM,
            browseVM,
            uploadVM,
            myCursorsVM,
            accountVM,
            supabaseMock.Object);
    }

    [Theory]
    [InlineData(typeof(CursorForge.Client.Views.LibraryView))]
    [InlineData(typeof(CursorForge.Client.Views.BrowseView))]
    [InlineData(typeof(CursorForge.Client.Views.UploadView))]
    [InlineData(typeof(CursorForge.Client.Views.MyCursorsView))]
    [InlineData(typeof(CursorForge.Client.Views.AccountView))]
    public void AllViews_Instantiation_DoNotThrow(Type viewType)
    {
        Exception? caught = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    _ = new CursorForge.Client.App();
                }
                var view = Activator.CreateInstance(viewType);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (caught != null)
        {
            throw new Exception($"{viewType.Name} threw: " + caught.ToString(), caught);
        }
    }

    [Fact]
    public void MainViewModel_InitializesWithLibraryView()
    {
        _mainViewModel.CurrentView.Should().BeOfType<LibraryViewModel>();
        _mainViewModel.IsBannerVisible.Should().BeFalse();
    }

    [Fact]
    public void MainViewModel_WhenNotAuthenticated_InitializesWithAccountView()
    {
        var unauthedMock = new Mock<CursorForge.Client.Services.Supabase.ISupabaseClientService>();
        unauthedMock.Setup(s => s.IsAuthenticated).Returns(false);
        var vm = new MainViewModel(
            _loggerMock.Object,
            _storageMock.Object,
            _mainViewModel.LibraryVM,
            _mainViewModel.BrowseVM,
            _mainViewModel.UploadVM,
            _mainViewModel.MyCursorsVM,
            _mainViewModel.AccountVM,
            unauthedMock.Object);

        vm.CurrentView.Should().BeOfType<AccountViewModel>();
    }

    [Theory]
    [InlineData("Library", typeof(LibraryViewModel))]
    [InlineData("Browse", typeof(BrowseViewModel))]
    [InlineData("Upload", typeof(UploadViewModel))]
    [InlineData("MyCursors", typeof(MyCursorsViewModel))]
    [InlineData("Account", typeof(AccountViewModel))]
    public void NavigateCommand_SwitchesToExpectedViewModel(string viewName, Type expectedType)
    {
        _mainViewModel.Navigate(viewName);
        _mainViewModel.CurrentView.Should().BeOfType(expectedType);
    }

    [Fact]
    public void BannerManagement_ShowsAndHidesBannerCorrectly()
    {
        _mainViewModel.ShowBanner("Offline mode active");
        _mainViewModel.IsBannerVisible.Should().BeTrue();
        _mainViewModel.BannerText.Should().Be("Offline mode active");

        _mainViewModel.HideBanner();
        _mainViewModel.IsBannerVisible.Should().BeFalse();
        _mainViewModel.BannerText.Should().BeEmpty();
    }
}
