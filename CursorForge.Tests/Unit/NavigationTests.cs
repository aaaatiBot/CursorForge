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
        var browseVM = new BrowseViewModel();
        var uploadVM = new UploadViewModel();
        var myCursorsVM = new MyCursorsViewModel();
        var accountVM = new AccountViewModel();

        _mainViewModel = new MainViewModel(
            _loggerMock.Object,
            _storageMock.Object,
            libraryVM,
            browseVM,
            uploadVM,
            myCursorsVM,
            accountVM);
    }

    [Fact]
    public void MainViewModel_InitializesWithLibraryView()
    {
        _mainViewModel.CurrentView.Should().BeOfType<LibraryViewModel>();
        _mainViewModel.IsBannerVisible.Should().BeFalse();
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
